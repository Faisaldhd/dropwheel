# NOTES - signing (branch `2.2.0-signing`)

Status: done. Scripts and documentation written; the real update key was **not** generated,
rotated or copied anywhere.

## Files

| File | What it is |
| --- | --- |
| `scripts/make-update-key.ps1` | One-time **rotation** helper: generates P-256 in memory, pipes base64 PKCS#8 to `gh secret set UPDATE_SIGNING_KEY` over stdin, prints only public material, clears memory, refuses without `-Rotate`. |
| `scripts/sign-release.ps1` | Signs assets to `<asset>.sig` (P1363 `r||s`, 64 bytes, base64 ASCII), verifies each `.sig` against the app key, throws before/after a mismatch. `-SelfTest` exercises the same code path with an ephemeral in-memory key. |
| `scripts/verify-signature.ps1` | Verifies `<file> <sig>`; exit 1 with a clear message on any failure; `-PublicKey` for tests. |
| `docs/signing-updates.md` | Key history, secret location, exact `.sig`/`SHA256SUMS.txt` formats, CI signing, app verification snippet, rotation runbook, troubleshooting. |

Not changed: `ci.yml`, `Core/**`, `installer/**`, `UPDATE_SIGNING_KEY`, `UpdateKey.cs`.

## Key fingerprint

- Public key in `UpdateKey.cs` (unchanged):
  `MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEUCQv0in24CEgz0uiGoER/25EFZY5w/x0wvpl9TyRyG8VxFTqv3mKuGBxMwoknk9FBHrj2P9c56kCUU2POE5Glw==`
- 91-byte SPKI DER, curve `prime256v1` (P-256).
- **Fingerprint = SHA-256 of the SPKI DER bytes:**
  `9d6b35c5376d6decceced299829643456c52fee8c0662dd101d62d422f0bce02`

## Signature format (for update-core and release-clean)

- `.sig` is ASCII base64 of **ECDSA P-256 / SHA-256 over the complete file bytes**, encoded as
  **IEEE P1363 fixed-field concatenation `r || s` (32+32 = exactly 64 bytes)**, so the file is
  always **88 base64 characters**, one line, no newline.
- This is what `ECDsa.SignData(..., SHA256)` and `ECDsa.VerifyHash(hash, signature)` use **by
  default** in .NET, and what `UpdateVerifier.VerifyHash` expects. It is **not** an ASN.1/DER
  signature (DER is 70-72 bytes; `verify-signature.ps1` rejects that length with a clear message).
- The scripts both check the 64-byte length, so if a future runtime changed the default they
  would fail loudly instead of shipping an unverifiable `.sig`.

## Self-check evidence (no real secret used)

Local constraint: only Windows PowerShell 5.1 is installed (no `pwsh`), and the rules forbid
running `.ps1` files locally, so the scripts were **not executed here**. What was verified:

1. **Syntax/AST:** all three scripts parsed with
   `[System.Management.Automation.Language.Parser]::ParseFile` - 0 errors each,
   `#Requires -Version 7.0`, expected parameter blocks and functions.
2. **Same algorithm, inline (PS 5.1 CNG + Node/OpenSSL), no key on disk:**
   - ephemeral P-256 key generated in memory; PKCS#8 exported/imported, SPKI exported/imported;
   - signature was **64 bytes -> 88 base64 chars**, one line;
   - good signature accepted; tampered file rejected; wrong ephemeral key rejected; the
     **embedded app key** rejected (wrong-key case);
   - OpenSSL/Node (`dsaEncoding: 'ieee-p1363'`) verified the .NET signature and rejected a
     tampered file; .NET verified an OpenSSL P1363 signature and rejected tampering;
   - embedded key imported in Node as `ec` / `prime256v1`, fingerprint matched
     `9d6b35c5...bce02`.
3. **.NET 10 runtime check (`dotnet fsi`, SDK 10.0.402; no app build, no project):**
   - embedded SPKI imported (91 bytes, `KeySize=256`), fingerprint
     `9d6b35c5376d6decceced299829643456c52fee8c0662dd101d62d422f0bce02`;
   - ephemeral `ECDsa.Create(nistP256)`: SPKI 91 bytes, PKCS#8 138 bytes;
     `ImportPkcs8PrivateKey` read all 138 bytes;
   - `SignData(stream, SHA256)` produced **64 bytes / 88 base64 chars**;
   - `VerifyHash` accepted the good signature and rejected a tampered hash and the wrong
     (embedded) key;
   - confirms the .NET 10 default is IEEE P1363 and that the app's `VerifyHash` path matches the
     scripts. A 70-byte DER-like signature is 96 base64 chars, which the scripts reject.
4. **Not covered locally:** actual execution of the `.ps1` files on pwsh 7 (no `pwsh` here; the
   rules forbid running `.ps1` locally). The underlying .NET 10 APIs above are verified, and the
   scripts are syntax-clean; the remaining risk is only PowerShell's method binding/execution.
   The test to run first in CI/on a pwsh 7 machine is:
   `pwsh ./scripts/sign-release.ps1 -SelfTest` (no secret needed; uses the same import, sign,
   write, read and verify functions, and asserts tamper/wrong-key failures).

## Requests from signing

### release-clean (owner of `.github/workflows/ci.yml`) - P0 for the first signed release

Neither step below is in `ci.yml` yet; release-clean is adding the signing step and the
no-secret self-test to the workflow.

1. **Add the signing step to the release job.** The release job currently has no
   `actions/checkout`, so it needs one before the script can read `scripts/` and `UpdateKey.cs`:

   ```yaml
         - uses: actions/checkout@v4

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

   The release step must then upload the two `.sig` files as well: extend the filter in
   "Create release" with `-like '*.sig'` and change the expected count from 3 to 5. Only the
   release job gets the secret; fork PRs never receive it.

2. **Add the no-secret self-test to `build-test`** (cheap, runs on every push, exercises the real
   pwsh 7 code path):

   ```yaml
         - name: Signing script self-test
           shell: pwsh
           run: ./scripts/sign-release.ps1 -SelfTest
   ```

3. **Asset naming mismatch (P1, blocks the updater).** `UpdateConfig.SetupAssetName` and the
   spec use `DropWheel-<version>-Setup.exe`, but `installer/DropWheel.iss` emits
   `DropWheel-Setup-<version>.exe` (and `release.cmd` prints that name). The app will not find
   the setup in the release until one side changes. Preferred fix (matches `UpdateConfig` and
   `docs/signing-updates.md`): change `OutputBaseFilename` in `installer/DropWheel.iss` to
   `DropWheel-{#AppVersion}-Setup` and update the ci.yml filters/artifact names. If the rename is
   done in the release job instead, **regenerate `SHA256SUMS.txt` after the rename**, otherwise
   the sums file names the old file and the app's digest check fails.

4. `scripts/sign-release.ps1` defaults to `release/DropWheel-<version>-Setup.exe` and
   `release/DropWheel-<version>-portable-win-x64.zip`; it accepts explicit `-Path` (the CI
   snippet passes `dist` paths), so naming only affects the default invocation.

### update-core - rotation safety (P2, not needed for 2.2.0 but needed before any rotation)

`UpdateKey` is a single constant. Because the private key exists only in the secret, rotating it
destroys the old key, and every installed copy (which embeds the old public key) permanently
loses automatic updates. `docs/signing-updates.md` documents this honestly. For a staged
rotation, `UpdateVerifier` would need to trust a list of public keys (old + new), so a transition
build signed with the old key can carry the new key. Suggested shape:

```csharp
public static IReadOnlyList<string> TrustedPublicKeyBase64 { get; } = new[] { Current, Next };
```

and `VerifySignature` accepts a signature that matches any listed key during the transition.
This is a design decision for the lead/update-core, not something the signing agent can change.
Otherwise: no rotation without accepting manual reinstall for existing users.

### update-core - verifier confirmation

`UpdateVerifier.VerifySignature` already matches these scripts: PKCS#8/SPKI import, SHA-256 over
the file bytes, `VerifyHash(hash, signature)` with the **P1363 64-byte** default. No change is
needed for the format; a unit test that signs with an ephemeral P-256 key and feeds a 64-byte
`r||s` signature would pin the format (tampered/wrong-key/missing cases are already in the spec).

## Exact CI step snippet (copy/paste)

See the YAML in "release-clean - P0" above. It is the canonical step: `actions/checkout@v4`,
then `scripts/sign-release.ps1 -Path $assets` with `UPDATE_SIGNING_KEY` mapped only on that job,
then a `.sig` count assertion. If `checkout` truly cannot be added, inline the exact functions
from `scripts/sign-release.ps1` rather than re-deriving the format; the invariants are
P-256, SHA-256 over the file bytes, IEEE P1363 `r||s` exactly 64 bytes, ASCII base64 (88 chars),
and a post-write verification against the app's public key.

## Notes / limitations

- `make-update-key.ps1` is destructive by design and refuses without `-Rotate`; it never writes
  the private key to disk or stdout. PowerShell strings cannot be provably zeroed, so it avoids
  ever creating a base64 *string* for the private key (uses `char[]` + `byte[]`, both cleared).
- `sign-release.ps1` and `verify-signature.ps1` prefer the live `PublicKeyBase64` constant from
  `src/FileAction.Core/Updates/UpdateKey.cs` when the repo is present; the built-in snapshot is
  only for a standalone copy. After a real rotation, update the snapshot constants and
  `docs/signing-updates.md` too.
- `docs/signing.md` (SignPath/Authenticode) is a different topic and was left untouched.
- No `ci.yml`, `Core/**` or `installer/**` changes were made from this branch.
