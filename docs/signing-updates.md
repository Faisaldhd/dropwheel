# Signed updates: key, formats, CI and rotation

This document is the reference for the **update signing key** used by DropWheel's auto-updater.
It is separate from Authenticode/code signing (`docs/signing.md`): Authenticode proves who built
the EXE to Windows, while this key proves to a *running DropWheel* that a downloaded update came
from this project. The updater only runs an update whose `.sig` verifies against the public key
compiled into the app.

Files:

| File | Purpose |
| --- | --- |
| `scripts/make-update-key.ps1` | One-time **rotation** helper. Pipes a new PKCS#8 private key to the GitHub secret over stdin. Refuses to run without `-Rotate`. |
| `scripts/sign-release.ps1` | Signs every release asset, writes `<asset>.sig`, then verifies it against the app's public key. Also has `-SelfTest`. |
| `scripts/verify-signature.ps1` | Verifies one `<asset>` + `<asset>.sig` pair; exit code 1 on any failure. |
| `src/FileAction.Core/Updates/UpdateKey.cs` | The single source of truth for the embedded public key. |
| `src/FileAction.Core/Updates/UpdateVerifier.cs` | The app-side verifier that consumes `.sig`, the GitHub digest and `SHA256SUMS.txt`. |

Everything here requires **PowerShell 7** (`pwsh`). The scripts declare `#Requires -Version 7.0`
and use .NET's ECDSA PKCS#8/SPKI APIs.

## The current key

The 2.2.0 key was generated once with this procedure and has **not** been rotated. Do not run the
rotation helper again unless you are deliberately performing the runbook below.

```
Public key (base64 SubjectPublicKeyInfo, 91-byte DER):
MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEUCQv0in24CEgz0uiGoER/25EFZY5w/x0wvpl9TyRyG8VxFTqv3mKuGBxMwoknk9FBHrj2P9c56kCUU2POE5Glw==

Fingerprint (SHA-256 of the 91 DER bytes, lowercase hex):
9d6b35c5376d6decceced299829643456c52fee8c0662dd101d62d422f0bce02
```

The fingerprint is a digest of the **DER bytes**, not of the base64 text. Use it to compare keys
between the app, the secret and the release without pasting the whole key around.

## What the private key is and where it lives

- One ECDSA **P-256** key pair (`secp256r1` / `prime256v1`, OID `1.2.840.10045.3.1.7`).
- The private key is a **PKCS#8 DER** blob, base64-encoded, stored in exactly one place: the
  GitHub Actions secret **`UPDATE_SIGNING_KEY`** in `Faisaldhd/dropwheel`.
- It is never in the repository, never in a release asset, never in a workflow file, never in a
  build log and never on a developer's disk. `make-update-key.ps1` holds it only in process
  memory and writes it to `gh` over stdin.
- There is deliberately **no backup**: losing the secret means no future release can be signed
  for existing installations. Do not copy it to a file "just in case".
- The public key is not secret and is compiled into the app as `UpdateKey.PublicKeyBase64`.

PowerShell strings are immutable, so no script can guarantee that every copy of a secret is
zeroed. The helper minimises the window: the private key exists as a byte array and a `char[]`
that are explicitly cleared, never as a base64 `string`, and the key object is disposed. Run it
on a trusted machine and do not enable PowerShell transcript logging/`Start-Transcript` during
rotation.

## Exact on-disk formats

### `<asset>.sig`

```
ASCII text, one line, standard base64 (RFC 4648 alphabet, '=' padding)
no BOM, no PEM header/footer, no algorithm name, no trailing newline
```

The decoded bytes are the **IEEE P1363 fixed-field concatenation** of the two signature integers,
`r || s`, each left-padded to 32 bytes: exactly **64 bytes** for P-256. This is the format
`ECDsa.SignData(...)` and `ECDsa.VerifyHash(...)` use by default in .NET, and what
`UpdateVerifier.VerifyHash` expects. It is **not** an ASN.1/DER signature: a DER `.sig` is
70-72 bytes, fails the 64-byte check, and the app rejects it.

A 64-byte signature is always **88 base64 characters** (`=` padding included). The signature is
ECDSA P-256 with SHA-256 over the **complete file bytes**: .NET hashes the file and signs that
32-byte digest. Never sign or verify the hex digest *string* - that is a different message.

The app's exact check (the same code as `UpdateVerifier.VerifySignature`):

```csharp
using var key = ECDsa.Create();
key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(UpdateKey.PublicKeyBase64), out _);
byte[] hash = SHA256.HashData(File.ReadAllBytes(filePath));
byte[] signature = Convert.FromBase64String(File.ReadAllText(sigPath).Trim());
bool ok = key.VerifyHash(hash, signature);   // IEEE P1363 r||s, 64 bytes
```

`UpdateVerifier.VerifySignature` wraps this in a `try`/`catch` for `CryptographicException`,
`FormatException` and `ArgumentException` and returns `false` on any of them. A wrong length or a
DER signature never verifies.

If you check a signature with OpenSSL/Node instead of .NET, select the P1363 encoding explicitly
(for example Node's `crypto.verify(..., { dsaEncoding: 'ieee-p1363' })`); the default there is
DER and will not match this `.sig` format.

Signing is randomised: signing the same asset twice gives two different, equally valid `.sig`
files. Only verifiability matters.

### SHA256SUMS.txt

`sha256sum`-compatible text, one line per asset:

```
<64 lowercase hex characters><two spaces><file name>
```

CRLF line endings are fine, and the parser also accepts the `*` binary-mode marker that
`sha256sum -b` writes. The app reads the entry whose file name matches the downloaded asset and
requires it to equal its own SHA-256. The `.sig` is not listed in `SHA256SUMS.txt`; the signature
(and GitHub's per-asset `digest` field) are independent digest sources, and every supplied source
must agree.

### Public key

A base64 **SubjectPublicKeyInfo** (SPKI) DER blob. For P-256 this is exactly 91 bytes: a 26-byte
`AlgorithmIdentifier` prefix followed by the uncompressed point `0x04 || X(32) || Y(32)`. The
base64 text is 124 characters with padding. `UpdateKey.cs` stores it as one constant.

## How the key was generated once

The key was created once by the lead with the rotation helper's predecessor/equivalent procedure:

1. `gh auth login` as an account with admin rights on `Faisaldhd/dropwheel`.
2. Generate an ECDSA P-256 key in memory and export PKCS#8 (private) and SPKI (public).
3. Pipe the base64 PKCS#8 directly to `gh secret set UPDATE_SIGNING_KEY --repo Faisaldhd/dropwheel`
   over stdin. No temporary file, no command-line argument, no printed value.
4. Paste the printed public key into `UpdateKey.cs`.
5. Publish the build that embeds it, then sign that release (and later releases) with the secret.

`make-update-key.ps1 -Rotate` performs exactly these steps and additionally refuses a non-P-256
key, self-tests the pair, clears the private bytes and confirms the secret is listed. It is the
only supported way to reproduce the procedure, and it must only be used for a real rotation.

## How CI signs

The release job signs the two binary assets after downloading the package artifacts and before
`gh release create`. It needs `actions/checkout` so the scripts and `UpdateKey.cs` are present.
Only that job maps the secret; never add it to a workflow-level `env:` block.

```yaml
      - name: Sign release assets
        shell: pwsh
        env:
          UPDATE_SIGNING_KEY: ${{ secrets.UPDATE_SIGNING_KEY }}
        run: |
          if (-not $env:UPDATE_SIGNING_KEY) { throw "UPDATE_SIGNING_KEY is not available to this job" }
          $assets = @(Get-ChildItem dist -Recurse -File | Where-Object {
            $_.Name -like 'DropWheel-*-Setup.exe' -or
            $_.Name -like 'DropWheel-*-portable-win-x64.zip'
          } | Select-Object -ExpandProperty FullName)
          if ($assets.Count -ne 2) { throw "expected 2 release assets, found $($assets.Count)" }
          ./scripts/sign-release.ps1 -Path $assets
          $sigs = @(Get-ChildItem dist -Recurse -File -Filter '*.sig')
          if ($sigs.Count -ne 2) { throw "expected 2 .sig files, found $($sigs.Count)" }
```

Afterwards the release step must include `*.sig` in the assets it passes to `gh release create`
(and expect five files: setup, portable zip, their two signatures and `SHA256SUMS.txt`).

Rules the workflow must keep:

- A fork pull request never receives the secret; keep signing on `push` tags only.
- Do not echo, transform or re-encode the secret in logs. GitHub masking is best effort, not a
  guarantee (a transformed value can escape masking).
- If the secret is missing, fail loudly instead of publishing unsigned assets.
- The app only finds the setup asset under the exact name from `UpdateConfig.SetupAssetName`:
  `DropWheel-<version>-Setup.exe`. The Inno Setup output is currently
  `DropWheel-Setup-<version>.exe`; the release job must rename it (or `UpdateConfig` must change)
  before the updater can find it. See `NOTES.md`.

## How the app verifies

`UpdateVerifier.VerifySignature`:

1. imports `UpdateKey.PublicKeyBase64` with `ImportSubjectPublicKeyInfo`;
2. computes `SHA256.HashData(File.ReadAllBytes(filePath))`;
3. calls `ECDsa.VerifyHash(hash, signatureBytes)` where `signatureBytes` is
   `Convert.FromBase64String(<asset>.sig)`.

`VerifyHash` uses the IEEE P1363 `r||s` encoding by default, so the `.sig` must be exactly 64
bytes for P-256; the exact snippet is under "Exact on-disk formats" above. There is no DER
conversion anywhere in this path.

`UpdateVerifier.Verify` requires a valid signature first, then checks every other digest source
that was supplied: the GitHub asset `digest` (when present) and the matching
`SHA256SUMS.txt` entry. Any mismatch fails. `UpdateService.DownloadAsync` (owned by update-core)
deletes the downloaded asset and `.sig` on failure and surfaces `update.verify.failed`; an
unverified file is never executed.

Because the public key is compiled in, replacing the key requires shipping a new app build - see
rotation below.

## Verifying a release by hand

```powershell
# From a checkout (the script reads UpdateKey.cs when it is present):
pwsh ./scripts/verify-signature.ps1 .\DropWheel-2.2.0-Setup.exe
pwsh ./scripts/verify-signature.ps1 .\DropWheel-2.2.0-portable-win-x64.zip

# Exit code 0 = valid. Anything else = do not run the file.
```

The script prints the asset size, its SHA-256 and the `.sig` size on success. Compare the SHA-256
with `SHA256SUMS.txt` on the release page. A `.sig` copied from another version, a truncated
download or a modified asset all fail with exit code 1 and a `FAILED:` message on stderr.

To test the signing code path without the real secret:

```powershell
pwsh ./scripts/sign-release.ps1 -SelfTest
```

This generates an ephemeral P-256 key **in memory**, signs and verifies a temporary file through
the same functions as production, and asserts that a tampered asset, a wrong key and a mismatched
key pair are rejected. No key material is written to disk. `sign-release.ps1` also accepts
`-KeyBase64` plus `-PublicKey` for external ephemeral-key harnesses; production must never pass
either.

## Rotation

**Rotation is a one-way, breaking operation.** The app embeds a single public key. Replacing the
secret destroys the only copy of the old private key, so:

- every already-installed DropWheel rejects releases signed with the new key;
- those users cannot auto-update and must download and install the release manually;
- if the old secret leaked, an attacker can still sign malware for old installations, so a
  security notice and a manual reinstall are the safe response.

There is no supported way to stage a new key with the current single-key app. A safe transition
needs the app to trust a *list* of keys and a transition build that is signed with the old key and
carries the new key; that is tracked as a request in `NOTES.md` for update-core.

If rotation is unavoidable:

1. Freeze releases and read this section. Confirm the loss of automatic updates is acceptable.
2. Optionally publish a final release signed with the **old** key (the old secret is still in
   place) whose notes tell users to download the next version manually and keep it.
3. Run:

   ```powershell
   pwsh ./scripts/make-update-key.ps1 -Rotate
   ```

   This replaces the secret with a new P-256 key and prints the new public key, the exact
   `gh secret set UPDATE_SIGNING_KEY --repo Faisaldhd/dropwheel` command it ran, the matching
   `UpdateKey.cs` line, and the new fingerprint.
4. Paste the printed constant into `src/FileAction.Core/Updates/UpdateKey.cs`. Update the key
   snapshot constants in `scripts/sign-release.ps1` and `scripts/verify-signature.ps1` (used only
   when the repository is absent), and update this document's key section and fingerprint.
5. Open a PR; require CI green. Never tag or release from a machine that still has old scripts.
6. Tag and publish while CI signs with the new secret. Verify both `.sig` files with
   `verify-signature.ps1` against the new public key.
7. Tell users in the release notes that older installations cannot auto-update and must download
   manually. Do not re-use the old key name for anything.

If the secret is lost (not compromised), the same steps apply; there is no recovery path that
keeps automatic updates working.

## Troubleshooting

| Symptom | Meaning / fix |
| --- | --- |
| `UPDATE_SIGNING_KEY is not set` | The job did not map the secret, or you ran `sign-release.ps1` locally. Never set a real key locally; run the self-test or let CI sign. |
| `UPDATE_SIGNING_KEY does not match the update public key` | The secret and `UpdateKey.cs` disagree (rotation half-finished, wrong repository/secret, or wrong key format). Refusing is correct; fix the key or the constant. Do not bypass. |
| `is not an ECDSA P-256 key` | The secret holds an RSA key, another curve, or is truncated. If it is corrupt, restore the intended key (there is no backup by design) or follow the rotation runbook. |
| `signature verification failed for ...` | A file changed after signing, or the public key used for verification is not the one that signed. Regenerate the `.sig` from the final asset. |
| `verify-signature.ps1` exits 1 with `signature does not match` | Corrupt/partial download, a `.sig` from a different version, or the wrong public key. Delete both files and download again; do not run the asset. |
| `gh secret set failed` | `gh` is not authenticated or the account lacks admin on the repository. Run `gh auth login`; the helper checks this before generating anything. |
| `#Requires -Version 7.0` failure | Use `pwsh`, not Windows PowerShell 5.1. |
| CI signed assets but the updater says "Update could not be verified" | Check the release asset names, that both `.sig` files were uploaded, and that the release build embeds the key matching the secret (fingerprints above). |

## Command reference

```powershell
# Sign the two default assets for the version in FileAction.App.csproj (CI uses explicit paths):
pwsh ./scripts/sign-release.ps1

# Sign specific artifacts:
pwsh ./scripts/sign-release.ps1 -Path release/DropWheel-2.2.0-Setup.exe, release/DropWheel-2.2.0-portable-win-x64.zip

# Prove the code path without a secret:
pwsh ./scripts/sign-release.ps1 -SelfTest

# Verify a downloaded artifact:
pwsh ./scripts/verify-signature.ps1 .\DropWheel-2.2.0-Setup.exe

# Rotate (destructive; see above):
pwsh ./scripts/make-update-key.ps1 -Rotate
```
