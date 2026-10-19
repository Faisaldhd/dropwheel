#Requires -Version 7.0
<#
.SYNOPSIS
    One-time rotation helper for the DropWheel update signing key.

.DESCRIPTION
    THIS REPLACES THE ONLY COPY OF THE UPDATE PRIVATE KEY. It exists for the single case where
    rotation is unavoidable (for example a leaked secret); see the rotation section of
    docs/signing-updates.md before running it. It refuses to do anything unless -Rotate is passed.

    When allowed, it:
      1. generates an ECDSA P-256 key pair (PKCS#8 private key, SubjectPublicKeyInfo public key);
      2. self-tests the pair (sign, verify, import the exported public key again);
      3. pipes the base64 PKCS#8 private key into `gh secret set UPDATE_SIGNING_KEY` on stdin;
      4. clears the private key bytes from memory and disposes the key;
      5. prints only public material: the public key, the matching UpdateKey.cs line, the exact
         gh command and the public key fingerprint.

    The private key is never written to a file, never printed and never put on a command line.
    No temporary files are created. There is deliberately no backup: after rotation the old key
    is gone and existing installations (which embed the old public key) cannot verify releases
    signed with the new key until they are updated manually.

    Prerequisites: GitHub CLI installed and `gh auth login` done with rights to set Actions
    secrets on the repository, and PowerShell 7 (`pwsh`).

.PARAMETER Rotate
    Required acknowledgement. Without it the script prints why it refused and exits non-zero.

.PARAMETER Repository
    owner/repository that stores the secret. Defaults to the app's repository (Faisaldhd/dropwheel).

.PARAMETER SecretName
    Actions secret name. The app's verifier and CI both expect UPDATE_SIGNING_KEY.

.PARAMETER Gh
    gh executable name or path. The secret is only ever sent over stdin, so this must resolve to
    gh.exe (a .cmd/.bat shim is rejected).

.EXAMPLE
    pwsh ./scripts/make-update-key.ps1 -Rotate
#>
[CmdletBinding()]
param(
    [switch] $Rotate,

    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string] $Repository = 'Faisaldhd/dropwheel',

    [ValidatePattern('^[A-Za-z_][A-Za-z0-9_]*$')]
    [string] $SecretName = 'UPDATE_SIGNING_KEY',

    [string] $Gh = 'gh'
)

$ErrorActionPreference = 'Stop'

if (-not $Rotate) {
    throw @'
Refusing to run: rotating the update signing key cannot be undone.

The old private key exists only in the GitHub secret this script would replace. Every installed
copy of DropWheel embeds the old public key, so after a rotation those installations reject
releases signed with the new key and must be updated manually.

Read the rotation section of docs/signing-updates.md, then run again with -Rotate.
'@
}

$script:P256SpkiPrefix = [byte[]] (
    0x30, 0x59, 0x30, 0x13, 0x06, 0x07, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x02, 0x01,
    0x06, 0x08, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x03, 0x01, 0x07, 0x03, 0x42, 0x00
)

function Test-BytesEqual {
    param([byte[]] $Left, [byte[]] $Right)

    if ($null -eq $Left -or $null -eq $Right) { return $false }
    if ($Left.Length -ne $Right.Length) { return $false }
    for ($i = 0; $i -lt $Left.Length; $i++) {
        if ($Left[$i] -ne $Right[$i]) { return $false }
    }
    return $true
}

function Assert-P256Spki {
    param(
        [Parameter(Mandatory = $true)][byte[]] $Spki,
        [Parameter(Mandatory = $true)][string] $Label
    )

    $prefix = [byte[]] ($Spki[0..25])
    if ($Spki.Length -ne 91 -or $Spki[26] -ne 0x04 -or
        -not (Test-BytesEqual -Left $prefix -Right $script:P256SpkiPrefix)) {
        throw "$Label is not an ECDSA P-256 SubjectPublicKeyInfo. Nothing was uploaded."
    }
}

function Test-KeyPair {
    param([Parameter(Mandatory = $true)][System.Security.Cryptography.ECDsa] $Key)

    $challenge = [byte[]]::new(32)
    $signature = $null
    $publicKey = $null
    $spki = $null
    $random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $random.GetBytes($challenge)
        $signature = $Key.SignData($challenge, [System.Security.Cryptography.HashAlgorithmName]::SHA256)
        if ($signature.Length -ne 64) {
            throw "the generated signature is $($signature.Length) bytes; a P-256 IEEE P1363 signature must be 64 bytes."
        }
        $spki = $Key.ExportSubjectPublicKeyInfo()

        $publicKey = [System.Security.Cryptography.ECDsa]::Create()
        $read = 0
        $publicKey.ImportSubjectPublicKeyInfo($spki, [ref] $read)
        if ($read -ne $spki.Length) {
            throw 'generated public key contains trailing data'
        }
        if (-not $publicKey.VerifyData($challenge, $signature, [System.Security.Cryptography.HashAlgorithmName]::SHA256)) {
            throw 'generated key pair failed its sign/verify self-test. Nothing was uploaded.'
        }
    } finally {
        $random.Dispose()
        if ($null -ne $publicKey) { $publicKey.Dispose() }
        if ($null -ne $signature) { [Array]::Clear($signature, 0, $signature.Length) }
        if ($null -ne $spki) { [Array]::Clear($spki, 0, $spki.Length) }
        [Array]::Clear($challenge, 0, $challenge.Length)
    }
}

# Fail before generating anything if gh cannot be used for the upload.
$ghCommand = Get-Command $Gh -ErrorAction SilentlyContinue
if ($null -eq $ghCommand) {
    throw "GitHub CLI ('$Gh') was not found. Install it (winget install GitHub.cli), run 'gh auth login', then retry."
}
$ghPath = $ghCommand.Path
if ([string]::IsNullOrWhiteSpace($ghPath)) {
    $ghPath = $ghCommand.Source
}
if ([string]::IsNullOrWhiteSpace($ghPath)) {
    throw "Could not resolve the executable path of '$Gh'. Pass -Gh with the full path to gh.exe."
}
if ($ghPath -match '\.(cmd|bat)$') {
    throw "gh resolved to '$ghPath', a batch shim. Pass -Gh with the real gh.exe so the private key can be piped over stdin."
}

$authOutput = & $ghPath auth status 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) {
    throw "gh is not authenticated for $Repository. Run 'gh auth login' first.`n$($authOutput.Trim())"
}

Write-Warning @'
ROTATION IS ONE-WAY. The old private key exists only in the secret being replaced, so no future
release can be signed with it. Installed copies of DropWheel embed the old public key and will
reject releases signed with the new key; they must be updated manually. Read the rotation section
of docs/signing-updates.md before continuing.
'@

$key = $null
$privateBytes = $null
$publicBytes = $null
$base64Chars = $null
$process = $null

try {
    $curve = [System.Security.Cryptography.ECCurve]::CreateFromFriendlyName('nistP256')
    $key = [System.Security.Cryptography.ECDsa]::Create($curve)

    $publicBytes = $key.ExportSubjectPublicKeyInfo()
    Assert-P256Spki -Spki $publicBytes -Label 'The generated public key'
    Test-KeyPair -Key $key

    $publicKeyBase64 = [Convert]::ToBase64String($publicBytes)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { $fingerprint = (($sha.ComputeHash($publicBytes) | ForEach-Object { $_.ToString('x2') }) -join '') }
    finally { $sha.Dispose() }

    $privateBytes = $key.ExportPkcs8PrivateKey()

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $ghPath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in @('secret', 'set', $SecretName, '--repo', $Repository)) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::Start($startInfo)

    # Send the base64 straight to the child's stdin. No string, file, argument or environment
    # variable ever holds the private key, and the char buffer is cleared right after the write.
    $base64Chars = [Convert]::ToBase64CharArray($privateBytes, 0, $privateBytes.Length)
    try {
        $process.StandardInput.Write($base64Chars)
        $process.StandardInput.Flush()
    } finally {
        [Array]::Clear($base64Chars, 0, $base64Chars.Length)
        $base64Chars = $null
        [Array]::Clear($privateBytes, 0, $privateBytes.Length)
        $privateBytes = $null
    }
    $process.StandardInput.Close()

    if (-not $process.WaitForExit(30000)) {
        try { $process.Kill($true) } catch { }
        throw "gh secret set did not finish within 30 seconds. Verify the secret in the repository settings."
    }
    # Drain both pipes after the process exits; gh prints only a short confirmation line.
    $null = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    if ($process.ExitCode -ne 0) {
        throw "gh secret set failed (exit $($process.ExitCode)): $($stderr.Trim())"
    }

    $listOutput = & $ghPath secret list --repo $Repository 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "The secret was set, but 'gh secret list' failed, so it could not be confirmed."
    } elseif ($listOutput -notmatch "(?m)^$([regex]::Escape($SecretName))\s") {
        Write-Warning "gh reported success but '$SecretName' was not listed by 'gh secret list'. Check the repository settings."
    }

    Write-Host ''
    Write-Host 'Update signing key rotated. The private key was piped to gh on stdin; it was not written'
    Write-Host 'to disk and is not printed by this script.'
    Write-Host ''
    Write-Host 'Public key (base64 SubjectPublicKeyInfo):'
    Write-Host $publicKeyBase64
    Write-Host ''
    Write-Host 'Paste this into src/FileAction.Core/Updates/UpdateKey.cs:'
    Write-Host ''
    Write-Host '    public const string PublicKeyBase64 ='
    Write-Host ('        "{0}";' -f $publicKeyBase64)
    Write-Host ''
    Write-Host ('Secret {0} is now set for {1}. The exact command this script ran was:' -f $SecretName, $Repository)
    Write-Host ('    gh secret set {0} --repo {1}' -f $SecretName, $Repository)
    Write-Host ''
    Write-Host ("Fingerprint (SHA-256 of the SPKI DER): {0}" -f $fingerprint)
    Write-Host ''
    Write-Host 'Also update the key snapshot in scripts/sign-release.ps1, scripts/verify-signature.ps1'
    Write-Host 'and docs/signing-updates.md, then ship a build carrying the new public key.'
} finally {
    if ($null -ne $base64Chars) { [Array]::Clear($base64Chars, 0, $base64Chars.Length) }
    if ($null -ne $privateBytes) { [Array]::Clear($privateBytes, 0, $privateBytes.Length) }
    if ($null -ne $key) { $key.Dispose() }
    if ($null -ne $process) { $process.Dispose() }
    Remove-Variable base64Chars, privateBytes -ErrorAction SilentlyContinue
    [GC]::Collect()
}
