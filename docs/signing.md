# Code signing (optional, for the corporate review)

DropWheel currently ships **unsigned**. Windows SmartScreen may warn on first run; the company security
team can also allow-list the binary by hash.

## Applying to SignPath Foundation (free for open-source projects)

1. Create an account at <https://signpath.org> and apply with the repository URL
   <https://github.com/Faisaldhd/dropwheel> (MIT licence, public source).
2. Once approved, SignPath gives an organisation id, a project slug and a signing policy slug.
3. Add these repository secrets: `SIGNPATH_API_TOKEN`, `SIGNPATH_ORGANIZATION_ID`.
4. Add the SignPath GitHub Action to `.github/workflows/ci.yml` after the installer step, signing
   `DropWheel.exe`, every `DropWheel*.dll` and `DropWheel-Setup-*.exe`.
5. Keep the workflow on an unsigned build until the application is approved (current state).

No signing key is stored in this repository.