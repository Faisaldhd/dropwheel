#Requires -Version 7.0
<#
.SYNOPSIS
    Verifies one DropWheel release asset against its .sig file.

.DESCRIPTION
    Checks that <file>.sig is a valid ECDSA P-256 / SHA-256 signature over the complete bytes of
    <file>, using the public key embedded in the app
    (src/FileAction.Core/Updates/UpdateKey.cs). The same check the app performs before it runs a
    downloaded update.

    The signature encoding is IEEE P1363 fixed-field concatenation (raw r||s, exactly 64 bytes for
    P-256), which is what .NET's ECDsa.VerifyHash uses by default. It is not an ASN.1/DER
    signature; a DER .sig (70-72 bytes) is rejected with a clear message.

    Exit code 0 = the signature is valid. Exit code 1 = missing file, missing/empty/bad .sig,
    a signature made with another key, or any read/format error. The failure message says which.

    This script never needs the private key; it is safe to run on a downloaded release.

.PARAMETER File
    The release asset to check.

.PARAMETER Signature
    The .sig file. Defaults to <File>.sig next to the asset.

.PARAMETER PublicKey
    Testing only: base64 (or PEM) SPKI public key to use instead of the app key. Useful with an
    ephemeral key; production verification must use the embedded key.

.PARAMETER Quiet
    Suppress the success line; failures are still written to stderr and still exit 1.

.EXAMPLE
    ./scripts/verify-signature.ps1 release/DropWheel-2.2.0-Setup.exe

.EXAMPLE
    ./scripts/verify-signature.ps1 -File release/DropWheel-2.2.0-portable-win-x64.zip -Quiet
    if ($LASTEXITCODE -ne 0) { throw 'release asset failed verification' }
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string] $File,

    [Parameter(Position = 1)]
    [string] $Signature,

    [string] $PublicKey,

    [switch] $Quiet
)

$ErrorActionPreference = 'Stop'

# Keep in sync with scripts/sign-release.ps1 (same snapshot and DER prefix).
$script:DefaultPublicKeyBase64 =
    'MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEUCQv0in24CEgz0uiGoER/25EFZY5w/x0wvpl9TyRyG8VxFTqv3mKuGBxMwoknk9FBHrj2P9c56kCUU2POE5Glw=='

$script:P256SpkiPrefix = [byte[]] (
    0x30, 0x59, 0x30, 0x13, 0x06, 0x07, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x02, 0x01,
    0x06, 0x08, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x03, 0x01, 0x07, 0x03, 0x42, 0x00
)

function ConvertFrom-KeyText {
    param(
        [Parameter(Mandatory = $true)][string] $Text,
        [Parameter(Mandatory = $true)][string] $Label
    )

    $value = $Text.Trim()
    if ($value -match '-----BEGIN') {
        $value = (($value -split '\r?\n') | Where-Object { $_ -notmatch '-----' }) -join ''
    }
    $value = $value -replace '\s', ''
    if ($value.Length -eq 0) {
        throw "$Label is empty."
    }
    try {
        return ,([Convert]::FromBase64String($value))
    } catch {
        throw "$Label is not valid base64."
    }
}

function Test-BytesEqual {
    param([byte[]] $Left, [byte[]] $Right)

    if ($null -eq $Left -or $null -eq $Right) { return $false }
    if ($Left.Length -ne $Right.Length) { return $false }
    for ($i = 0; $i -lt $Left.Length; $i++) {
        if ($Left[$i] -ne $Right[$i]) { return $false }
    }
    return $true
}

function Assert-P256Key {
    param(
        [Parameter(Mandatory = $true)][System.Security.Cryptography.ECDsa] $Key,
        [Parameter(Mandatory = $true)][string] $Label
    )

    $spki = $Key.ExportSubjectPublicKeyInfo()
    try {
        $prefix = [byte[]] ($spki[0..25])
        if ($spki.Length -ne 91 -or $spki[26] -ne 0x04 -or
            -not (Test-BytesEqual -Left $prefix -Right $script:P256SpkiPrefix)) {
            throw "$Label is not an ECDSA P-256 key (expected a 91-byte P-256 SubjectPublicKeyInfo)."
        }
    } finally {
        [Array]::Clear($spki, 0, $spki.Length)
    }
}

function Import-PublicKey {
    param([Parameter(Mandatory = $true)][string] $Base64)

    $bytes = ConvertFrom-KeyText -Text $Base64 -Label 'update public key'
    $key = $null
    try {
        $key = [System.Security.Cryptography.ECDsa]::Create()
        $read = 0
        $key.ImportSubjectPublicKeyInfo($bytes, [ref] $read)
        if ($read -ne $bytes.Length) {
            throw 'The update public key contains data after the SubjectPublicKeyInfo.'
        }
        Assert-P256Key -Key $key -Label 'The update public key'
        return $key
    } catch {
        if ($null -ne $key) { $key.Dispose() }
        throw
    } finally {
        [Array]::Clear($bytes, 0, $bytes.Length)
    }
}

function Get-PublicKeyFromSource {
    param([Parameter(Mandatory = $true)][string] $RepoRoot)

    $sourcePath = [System.IO.Path]::Combine(
        $RepoRoot, 'src', 'FileAction.Core', 'Updates', 'UpdateKey.cs')
    if (-not [System.IO.File]::Exists($sourcePath)) {
        return $null
    }
    $text = [System.IO.File]::ReadAllText($sourcePath)
    $match = [regex]::Match($text, 'PublicKeyBase64\s*=\s*"(?<key>[A-Za-z0-9+/=]+)"')
    if (-not $match.Success) {
        throw "could not read the PublicKeyBase64 constant from $sourcePath"
    }
    return $match.Groups['key'].Value
}

function Read-SignatureFile {
    param([Parameter(Mandatory = $true)][string] $LiteralPath)

    $info = [System.IO.FileInfo]::new($LiteralPath)
    if ($info.Length -gt 4096) {
        throw "signature file is too large to be a DropWheel .sig: $LiteralPath"
    }
    $text = [System.IO.File]::ReadAllText($LiteralPath, [System.Text.Encoding]::ASCII).Trim()
    if ($text.Length -eq 0) {
        throw "signature file is empty: $LiteralPath"
    }
    try {
        $decoded = [Convert]::FromBase64String($text)
    } catch {
        throw "signature file is not valid base64: $LiteralPath"
    }
    if ($decoded.Length -ne 64) {
        throw "signature file is $($decoded.Length) bytes; a P-256 IEEE P1363 signature must be exactly 64 bytes (DER is not accepted): $LiteralPath"
    }
    return ,$decoded
}

function Get-FileSha256Bytes {
    param([Parameter(Mandatory = $true)][string] $LiteralPath)

    $sha = [System.Security.Cryptography.SHA256]::Create()
    $stream = [System.IO.File]::OpenRead($LiteralPath)
    try { return ,($sha.ComputeHash($stream)) }
    finally {
        $stream.Dispose()
        $sha.Dispose()
    }
}

$failure = $null
$verified = $null

try {
    if (-not [System.IO.File]::Exists($File)) {
        throw "file not found: $File"
    }
    $filePath = [System.IO.Path]::GetFullPath($File)

    $sigPath = if ([string]::IsNullOrWhiteSpace($Signature)) {
        $filePath + '.sig'
    } elseif ([System.IO.Path]::IsPathRooted($Signature)) {
        [System.IO.Path]::GetFullPath($Signature)
    } else {
        [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $Signature))
    }
    if (-not [System.IO.File]::Exists($sigPath)) {
        throw "signature file not found: $sigPath (releases always ship <asset>.sig; download it again)"
    }

    $repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    $publicKeyBase64 = if (-not [string]::IsNullOrWhiteSpace($PublicKey)) {
        $PublicKey.Trim()
    } else {
        $fromSource = Get-PublicKeyFromSource -RepoRoot $repoRoot
        if (-not [string]::IsNullOrWhiteSpace($fromSource)) { $fromSource } else { $script:DefaultPublicKeyBase64 }
    }

    $key = Import-PublicKey -Base64 $publicKeyBase64
    $signature = $null
    $hashBytes = $null
    $sha256Hex = $null
    try {
        $signature = Read-SignatureFile -LiteralPath $sigPath
        $hashBytes = Get-FileSha256Bytes -LiteralPath $filePath
        # Same call as UpdateVerifier.VerifySignature: VerifyHash's default is IEEE P1363 (r||s).
        if (-not $key.VerifyHash($hashBytes, $signature)) {
            throw "signature does not match $([System.IO.Path]::GetFileName($filePath)); the file was modified or signed with another key. Do not run it."
        }
        $sha256Hex = (($hashBytes | ForEach-Object { $_.ToString('x2') }) -join '')
    } finally {
        if ($null -ne $signature) { [Array]::Clear($signature, 0, $signature.Length) }
        if ($null -ne $hashBytes) { [Array]::Clear($hashBytes, 0, $hashBytes.Length) }
        $key.Dispose()
    }

    $fileInfo = [System.IO.FileInfo]::new($filePath)
    $sigInfo = [System.IO.FileInfo]::new($sigPath)
    $verified = [pscustomobject] @{
        Name    = $fileInfo.Name
        Size    = $fileInfo.Length
        SigName = $sigInfo.Name
        SigSize = $sigInfo.Length
        Sha256  = $sha256Hex
    }
} catch {
    $failure = $_.Exception.Message
}

if ($null -ne $failure) {
    [Console]::Error.WriteLine("FAILED: $failure")
    exit 1
}

if (-not $Quiet) {
    Write-Host ("OK  {0}" -f $verified.Name)
    Write-Host ("    {0:N0} bytes  sha256 {1}" -f $verified.Size, $verified.Sha256)
    Write-Host ("    {0}  {1:N0} bytes  (ECDSA P-256 / SHA-256, IEEE P1363)" -f $verified.SigName, $verified.SigSize)
}
exit 0
