#Requires -Version 7.0
<#
.SYNOPSIS
    Signs DropWheel release assets for the signed auto-updater.

.DESCRIPTION
    For every asset passed in -Path (or for the two default 2.2.0 assets below) this writes
    <asset>.sig next to the asset: ASCII base64 of an ECDSA P-256 / SHA-256 signature over the
    complete file bytes, encoded as IEEE P1363 fixed-field concatenation (raw r||s, 32+32 bytes,
    exactly 64 bytes for P-256). This is what .NET's ECDsa.SignData/VerifyHash use by default and
    what UpdateVerifier.VerifyHash expects; it is NOT an ASN.1/DER signature.

    Every .sig is read back and verified against the public key embedded in the app
    (src/FileAction.Core/Updates/UpdateKey.cs) before the script reports success, and the private
    key is checked against that public key first, so a release can never ship a signature the app
    would reject.

    The private key comes from the UPDATE_SIGNING_KEY environment variable (base64 PKCS#8). It is
    never written to disk, never printed, and never placed on a command line. In CI the variable
    is filled from the repository secret of the same name:

        env:
          UPDATE_SIGNING_KEY: ${{ secrets.UPDATE_SIGNING_KEY }}

    With no -Path, the version is read from src/FileAction.App/FileAction.App.csproj and these are
    signed (the names UpdateConfig.SetupAssetName / PortableAssetName produce):

        release/DropWheel-<version>-Setup.exe
        release/DropWheel-<version>-portable-win-x64.zip

    See docs/signing-updates.md for the key history, the exact format, CI wiring and rotation.

.PARAMETER Path
    One or more asset files to sign. Relative paths resolve against the current directory.

.PARAMETER Version
    x.y.z version used to build the default asset paths. Defaults to <Version> in
    src/FileAction.App/FileAction.App.csproj.

.PARAMETER KeyBase64
    Testing only: base64 PKCS#8 private key. Prefer UPDATE_SIGNING_KEY for every real run.
    Pass -PublicKey with the matching public key; production must not use this parameter.

.PARAMETER PublicKey
    Testing only: base64 SPKI public key used for the post-signing verification instead of the key
    from UpdateKey.cs. Pair it with -KeyBase64 when testing with an ephemeral key.

.PARAMETER RepositoryRoot
    Repository root used to find the default assets and UpdateKey.cs. Defaults to the parent of
    this script's folder.

.PARAMETER SelfTest
    Generates an ephemeral P-256 key in memory, signs and verifies a temporary file through the
    same functions as a real run, asserts that a tampered asset, a wrong key and a mismatched key
    pair are rejected, then exits. No key material is written to disk.

.EXAMPLE
    ./scripts/sign-release.ps1

.EXAMPLE
    ./scripts/sign-release.ps1 -Path release/DropWheel-2.2.0-Setup.exe, release/DropWheel-2.2.0-portable-win-x64.zip

.EXAMPLE
    ./scripts/sign-release.ps1 -SelfTest
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0, ValueFromRemainingArguments = $true)]
    [string[]] $Path,

    [string] $Version,
    [string] $KeyBase64,
    [string] $PublicKey,
    [string] $RepositoryRoot,
    [switch] $SelfTest
)

$ErrorActionPreference = 'Stop'

# Snapshot of UpdateKey.PublicKeyBase64. When the repository is present the live constant in
# src/FileAction.Core/Updates/UpdateKey.cs is preferred, so this copy only serves a standalone script.
$script:DefaultPublicKeyBase64 =
    'MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEUCQv0in24CEgz0uiGoER/25EFZY5w/x0wvpl9TyRyG8VxFTqv3mKuGBxMwoknk9FBHrj2P9c56kCUU2POE5Glw=='

# DER AlgorithmIdentifier prefix of an ECDSA P-256 SubjectPublicKeyInfo (26 bytes, before the 0x04 point).
$script:P256SpkiPrefix = [byte[]] (
    0x30, 0x59, 0x30, 0x13, 0x06, 0x07, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x02, 0x01,
    0x06, 0x08, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x03, 0x01, 0x07, 0x03, 0x42, 0x00
)

function ConvertFrom-KeyText {
    param(
        [Parameter(Mandatory = $true)][string] $Text,
        [Parameter(Mandatory = $true)][string] $Label
    )

    # Accept the documented base64 of a PKCS#8/SPKI DER blob, and also a PEM block (trimmed here)
    # so a copied UpdateKey.cs value or a PEM file can be pasted into tests.
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

function Get-FingerprintFromBytes {
    param([byte[]] $Bytes)

    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { $hash = $sha.ComputeHash($Bytes) }
    finally { $sha.Dispose() }
    return (($hash | ForEach-Object { $_.ToString('x2') }) -join '')
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

function Import-PrivateKey {
    param([Parameter(Mandatory = $true)][string] $Base64)

    $bytes = ConvertFrom-KeyText -Text $Base64 -Label 'UPDATE_SIGNING_KEY'
    $key = $null
    try {
        $key = [System.Security.Cryptography.ECDsa]::Create()
        $read = 0
        try {
            $key.ImportPkcs8PrivateKey($bytes, [ref] $read)
        } catch {
            throw "UPDATE_SIGNING_KEY is not a valid base64 PKCS#8 P-256 private key: $($_.Exception.Message)"
        }
        if ($read -ne $bytes.Length) {
            throw 'UPDATE_SIGNING_KEY contains data after the PKCS#8 private key.'
        }
        Assert-P256Key -Key $key -Label 'UPDATE_SIGNING_KEY'
        return $key
    } catch {
        if ($null -ne $key) { $key.Dispose() }
        throw
    } finally {
        # The key was copied by ImportPkcs8PrivateKey; do not leave the DER bytes in the heap.
        [Array]::Clear($bytes, 0, $bytes.Length)
    }
}

function Import-PublicKey {
    param([Parameter(Mandatory = $true)][string] $Base64)

    $bytes = ConvertFrom-KeyText -Text $Base64 -Label 'update public key'
    $key = $null
    try {
        $key = [System.Security.Cryptography.ECDsa]::Create()
        $read = 0
        try {
            $key.ImportSubjectPublicKeyInfo($bytes, [ref] $read)
        } catch {
            throw "The update public key is not a valid base64 SubjectPublicKeyInfo: $($_.Exception.Message)"
        }
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

function New-P256Key {
    $curve = [System.Security.Cryptography.ECCurve]::CreateFromFriendlyName('nistP256')
    return [System.Security.Cryptography.ECDsa]::Create($curve)
}

function Test-KeyPair {
    param(
        [Parameter(Mandatory = $true)][System.Security.Cryptography.ECDsa] $PrivateKey,
        [Parameter(Mandatory = $true)][string] $PublicKeyBase64
    )

    $publicKey = Import-PublicKey -Base64 $PublicKeyBase64
    try {
        $fromPrivate = $PrivateKey.ExportSubjectPublicKeyInfo()
        $fromPublic = $publicKey.ExportSubjectPublicKeyInfo()
        try {
            return (Test-BytesEqual -Left $fromPrivate -Right $fromPublic)
        } finally {
            [Array]::Clear($fromPrivate, 0, $fromPrivate.Length)
            [Array]::Clear($fromPublic, 0, $fromPublic.Length)
        }
    } finally {
        $publicKey.Dispose()
    }
}

function Get-EcdsaSignature {
    param(
        [Parameter(Mandatory = $true)][string] $LiteralPath,
        [Parameter(Mandatory = $true)][System.Security.Cryptography.ECDsa] $Key
    )

    $stream = [System.IO.File]::Open(
        $LiteralPath,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::Read)
    try {
        $signature = $Key.SignData($stream, [System.Security.Cryptography.HashAlgorithmName]::SHA256)
    } finally {
        $stream.Dispose()
    }
    # The app verifies with VerifyHash's default, which is IEEE P1363 (r||s). A DER signature
    # would be 70-72 bytes and the app would reject it, so fail loudly rather than write it.
    if ($signature.Length -ne 64) {
        throw "the signature is $($signature.Length) bytes; a P-256 IEEE P1363 signature must be 64 bytes."
    }
    return ,$signature
}

function Test-EcdsaSignature {
    param(
        [Parameter(Mandatory = $true)][string] $LiteralPath,
        [Parameter(Mandatory = $true)][byte[]] $Signature,
        [Parameter(Mandatory = $true)][System.Security.Cryptography.ECDsa] $Key
    )

    # P1363 only: DER signatures are 70-72 bytes and are not what the app consumes.
    if ($Signature.Length -ne 64) { return $false }

    $stream = [System.IO.File]::Open(
        $LiteralPath,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::Read)
    try {
        return $Key.VerifyData($stream, $Signature, [System.Security.Cryptography.HashAlgorithmName]::SHA256)
    } finally {
        $stream.Dispose()
    }
}

function Write-SignatureFile {
    param(
        [Parameter(Mandatory = $true)][string] $LiteralPath,
        [Parameter(Mandatory = $true)][byte[]] $Signature
    )

    # ASCII, one line, standard padded base64, no newline: 64 signature bytes become 88 chars.
    # The app trims whitespace, but keeping the file minimal makes diffing and manual checking
    # unambiguous.
    $base64 = [Convert]::ToBase64String($Signature)
    [System.IO.File]::WriteAllText($LiteralPath, $base64, [System.Text.Encoding]::ASCII)
}

function Read-SignatureFile {
    param([Parameter(Mandatory = $true)][string] $LiteralPath)

    $text = [System.IO.File]::ReadAllText($LiteralPath, [System.Text.Encoding]::ASCII)
    $text = $text.Trim()
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

function Resolve-PublicKeyBase64 {
    param([string] $Override, [string] $RepoRoot)

    if (-not [string]::IsNullOrWhiteSpace($Override)) {
        return $Override.Trim()
    }
    $fromSource = Get-PublicKeyFromSource -RepoRoot $RepoRoot
    if (-not [string]::IsNullOrWhiteSpace($fromSource)) {
        return $fromSource
    }
    return $script:DefaultPublicKeyBase64
}

function Resolve-Assets {
    param([string[]] $Path, [string] $Version, [string] $RepoRoot)

    $items = @()
    if ($null -ne $Path -and $Path.Count -gt 0) {
        foreach ($item in $Path) {
            $full = if ([System.IO.Path]::IsPathRooted($item)) {
                [System.IO.Path]::GetFullPath($item)
            } else {
                [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $item))
            }
            if (-not [System.IO.File]::Exists($full)) {
                throw "asset not found: $full"
            }
            $items += [System.IO.FileInfo]::new($full)
        }
        return $items
    }

    if ([string]::IsNullOrWhiteSpace($Version)) {
        $csprojPath = [System.IO.Path]::Combine(
            $RepoRoot, 'src', 'FileAction.App', 'FileAction.App.csproj')
        if (-not [System.IO.File]::Exists($csprojPath)) {
            throw "no -Path given and $csprojPath was not found; pass -Version or -Path."
        }
        [xml] $csproj = [System.IO.File]::ReadAllText($csprojPath)
        $versionNode = @($csproj.Project.PropertyGroup | Where-Object { $_.Version }) | Select-Object -First 1
        if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace([string] $versionNode.Version)) {
            throw "no <Version> element in $csprojPath"
        }
        $Version = [string] $versionNode.Version
        Write-Verbose "version from csproj: $Version"
    }

    $Version = $Version.Trim()
    if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') {
        throw "version '$Version' is not x.y.z; release asset names use three numeric parts."
    }

    foreach ($name in @(
            "DropWheel-$Version-Setup.exe",
            "DropWheel-$Version-portable-win-x64.zip")) {
        $full = [System.IO.Path]::Combine($RepoRoot, 'release', $name)
        if (-not [System.IO.File]::Exists($full)) {
            throw "asset not found: $full (sign-release uses the asset names from UpdateConfig; check the package job output)."
        }
        $items += [System.IO.FileInfo]::new($full)
    }
    return $items
}

function Invoke-SigningSelfTest {
    $tempDir = [System.IO.Path]::Combine(
        [System.IO.Path]::GetTempPath(),
        'dropwheel-signing-selftest-' + [guid]::NewGuid().ToString('N'))
    [System.IO.Directory]::CreateDirectory($tempDir) | Out-Null
    $dataPath = [System.IO.Path]::Combine($tempDir, 'asset.bin')
    $sigPath = $dataPath + '.sig'
    $tamperedPath = [System.IO.Path]::Combine($tempDir, 'asset-tampered.bin')

    $privateKey = $null
    $publicKey = $null
    $otherKey = $null
    $privateBase64 = $null
    $publicBase64 = $null

    try {
        Write-Host 'Self-test: generating an ephemeral P-256 key in memory (not written to disk)'

        $bytes = [byte[]]::new(8192)
        $random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
        try { $random.GetBytes($bytes) } finally { $random.Dispose() }
        [System.IO.File]::WriteAllBytes($dataPath, $bytes)
        [System.IO.File]::WriteAllBytes($tamperedPath, $bytes)

        # Flip one byte of the copy that must be rejected.
        $stream = [System.IO.File]::Open($tamperedPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite)
        try {
            $stream.Position = 100
            $stream.WriteByte([byte] ($bytes[100] -bxor 0xFF))
        } finally {
            $stream.Dispose()
        }

        $ephemeral = New-P256Key
        try {
            $privateBase64 = [Convert]::ToBase64String($ephemeral.ExportPkcs8PrivateKey())
            $publicBase64 = [Convert]::ToBase64String($ephemeral.ExportSubjectPublicKeyInfo())
        } finally {
            $ephemeral.Dispose()
        }

        # Exercise the same import, pair-check, sign, write, read and verify functions as production.
        $privateKey = Import-PrivateKey -Base64 $privateBase64
        $publicKey = Import-PublicKey -Base64 $publicBase64

        if (-not (Test-KeyPair -PrivateKey $privateKey -PublicKeyBase64 $publicBase64)) {
            throw 'self-test: matching key pair was not recognised'
        }

        $signature = Get-EcdsaSignature -LiteralPath $dataPath -Key $privateKey
        try {
            Write-SignatureFile -LiteralPath $sigPath -Signature $signature
        } finally {
            [Array]::Clear($signature, 0, $signature.Length)
        }

        $sigText = [System.IO.File]::ReadAllText($sigPath, [System.Text.Encoding]::ASCII)
        if ($sigText -match '\s' -or $sigText.Length -ne 88) {
            throw 'self-test: .sig is not one line of 88 base64 characters (64-byte P-256 signature)'
        }
        Write-Host 'PASS  64-byte IEEE P1363 .sig is one base64 line'

        $fromFile = Read-SignatureFile -LiteralPath $sigPath
        try {
            if (-not (Test-EcdsaSignature -LiteralPath $dataPath -Signature $fromFile -Key $publicKey)) {
                throw 'self-test: a good signature was rejected'
            }
            Write-Host 'PASS  good signature verifies'

            if (Test-EcdsaSignature -LiteralPath $tamperedPath -Signature $fromFile -Key $publicKey) {
                throw 'self-test: a tampered asset was accepted'
            }
            Write-Host 'PASS  tampered asset is rejected'
        } finally {
            [Array]::Clear($fromFile, 0, $fromFile.Length)
        }

        $otherKey = New-P256Key
        $otherPublicBase64 = [Convert]::ToBase64String($otherKey.ExportSubjectPublicKeyInfo())
        $fromFile = Read-SignatureFile -LiteralPath $sigPath
        try {
            if (Test-EcdsaSignature -LiteralPath $dataPath -Signature $fromFile -Key $otherKey) {
                throw 'self-test: a signature was accepted under a wrong key'
            }
            Write-Host 'PASS  wrong key is rejected'
        } finally {
            [Array]::Clear($fromFile, 0, $fromFile.Length)
        }

        if (Test-KeyPair -PrivateKey $privateKey -PublicKeyBase64 $otherPublicBase64) {
            throw 'self-test: a mismatched key pair was accepted'
        }
        Write-Host 'PASS  mismatched key pair is rejected'
        Write-Host 'Self-test passed.'
    } finally {
        if ($null -ne $privateKey) { $privateKey.Dispose() }
        if ($null -ne $publicKey) { $publicKey.Dispose() }
        if ($null -ne $otherKey) { $otherKey.Dispose() }
        $privateBase64 = $null
        $publicBase64 = $null
        Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($SelfTest) {
    Invoke-SigningSelfTest
    exit 0
}

$repoRoot = if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
} else {
    [System.IO.Path]::GetFullPath($RepositoryRoot)
}

$publicKeyBase64 = Resolve-PublicKeyBase64 -Override $PublicKey -RepoRoot $repoRoot

$privateKeyText = if ([string]::IsNullOrWhiteSpace($KeyBase64)) {
    $env:UPDATE_SIGNING_KEY
} else {
    $KeyBase64
}
if ([string]::IsNullOrWhiteSpace($privateKeyText)) {
    throw @'
UPDATE_SIGNING_KEY is not set.

In CI it comes from the repository secret:
    env:
      UPDATE_SIGNING_KEY: ${{ secrets.UPDATE_SIGNING_KEY }}

Never put the private key in a file, a command line, or a log. See docs/signing-updates.md.
'@
}

$privateKey = $null
$publicKey = $null
$createdSignatures = [System.Collections.Generic.List[string]]::new()

try {
    $privateKey = Import-PrivateKey -Base64 $privateKeyText
    $publicKey = Import-PublicKey -Base64 $publicKeyBase64

    $privateSpki = $null
    $publicSpki = $null
    try {
        $privateSpki = $privateKey.ExportSubjectPublicKeyInfo()
        $publicSpki = $publicKey.ExportSubjectPublicKeyInfo()
        if (-not (Test-BytesEqual -Left $privateSpki -Right $publicSpki)) {
            $privateFingerprint = Get-FingerprintFromBytes -Bytes $privateSpki
            $publicFingerprint = Get-FingerprintFromBytes -Bytes $publicSpki
            throw @"
UPDATE_SIGNING_KEY does not match the update public key the app embeds.
    private key fingerprint: $privateFingerprint
    public key fingerprint:  $publicFingerprint
Refusing to sign: the app would reject every .sig this run produced.
"@
        }
    } finally {
        if ($null -ne $privateSpki) { [Array]::Clear($privateSpki, 0, $privateSpki.Length) }
        if ($null -ne $publicSpki) { [Array]::Clear($publicSpki, 0, $publicSpki.Length) }
    }

    $files = @(Resolve-Assets -Path $Path -Version $Version -RepoRoot $repoRoot)
    if ($files.Count -eq 0) {
        throw 'no assets to sign.'
    }

    foreach ($file in $files) {
        $signature = Get-EcdsaSignature -LiteralPath $file.FullName -Key $privateKey
        try {
            $sigPath = $file.FullName + '.sig'
            $createdSignatures.Add($sigPath)
            Write-SignatureFile -LiteralPath $sigPath -Signature $signature
        } finally {
            [Array]::Clear($signature, 0, $signature.Length)
        }
    }

    # Verify the bytes that will actually ship, not just the in-memory signature.
    foreach ($file in $files) {
        $sigPath = $file.FullName + '.sig'
        $signature = Read-SignatureFile -LiteralPath $sigPath
        try {
            if (-not (Test-EcdsaSignature -LiteralPath $file.FullName -Signature $signature -Key $publicKey)) {
                throw "signature verification failed for $($file.Name); the .sig does not match the update public key."
            }
        } finally {
            [Array]::Clear($signature, 0, $signature.Length)
        }
    }

    foreach ($file in $files) {
        $sig = [System.IO.FileInfo]::new($file.FullName + '.sig')
        Write-Host ("{0}  {1:N0} bytes  ->  {2}  {3:N0} bytes" -f $file.Name, $file.Length, $sig.Name, $sig.Length)
    }
} catch {
    # Never leave a .sig behind that was not verified.
    foreach ($sigPath in $createdSignatures) {
        Remove-Item -LiteralPath $sigPath -Force -ErrorAction SilentlyContinue
    }
    throw
} finally {
    # PowerShell strings are immutable; drop the reference as best effort.
    $privateKeyText = $null
    Remove-Variable privateKeyText -ErrorAction SilentlyContinue
    if ($null -ne $publicKey) { $publicKey.Dispose() }
    if ($null -ne $privateKey) { $privateKey.Dispose() }
}
