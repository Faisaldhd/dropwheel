# Verifies the Authenticode signature of a downloaded file and writes a small KEY=value report.
#
# Used by installer\DropWheel.iss before it runs the Microsoft .NET Desktop Runtime installer.
# Can also be run by hand:
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify-authenticode.ps1 `
#       -Path <file> -ReportPath <file> [-SignerMustContain 'Microsoft Corporation']
#
# Exit code 0 = signature is Valid and the signer subject contains the expected name (OK).
# Exit code 1 = anything else: file missing, not signed, invalid/untrusted signature, wrong signer.
#
# The report is written in ASCII with one "KEY=value" per line:
#   STATUS=<AuthenticodeStatus>
#   SIGNER=<signer certificate subject, empty when unsigned>
#   RESULT=OK|FAIL
#   ERROR=<reason, only on failure>

param(
    [Parameter(Mandatory = $true)][string] $Path,
    [Parameter(Mandatory = $true)][string] $ReportPath,
    [string] $SignerMustContain = 'Microsoft Corporation'
)

$ErrorActionPreference = 'Stop'

$status = 'Unavailable'
$signer = ''
$errorText = ''
$ok = $false

try {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "File not found: $Path"
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    $status = [string] $signature.Status
    if ($null -ne $signature.SignerCertificate) {
        $signer = [string] $signature.SignerCertificate.Subject
    }

    $ok = ($status -eq 'Valid') -and ($signer -like "*$SignerMustContain*")
} catch {
    $errorText = $_.Exception.Message
}

$result = 'FAIL'
if ($ok) { $result = 'OK' }

$lines = @(
    "STATUS=$status",
    "SIGNER=$signer",
    "RESULT=$result"
)
if ($errorText -ne '') {
    $lines += "ERROR=$errorText"
}

Set-Content -LiteralPath $ReportPath -Value $lines -Encoding ASCII

if ($ok) { exit 0 }
exit 1
