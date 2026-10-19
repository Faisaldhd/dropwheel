# NOTES — strings (2.2.0-strings)

## Requests from strings

None. No change is needed outside `src/FileAction.Core/Strings/en.json` and `ar.json`.

## String-table decisions for the lead

- Added `settings.updates.unavailable` (requested by the lead on behalf of update-ui) to both tables:
  - en: `Updates are not available in this build.`
  - ar: `التحديثات غير متوفرة في هذا الإصدار.`
  This key is not in `_local/AGENT_RULES.md`; it was added on the lead's explicit request.
- English version placeholders are plain `{0}` (matches the lead's confirmation and the existing
  `en.json` style: no bidi isolates in English). Arabic wraps version placeholders in
  U+2066…U+2069 per `docs/glossary-ar.md`, e.g. `لديك أحدث إصدار (⁦{0}⁩)`.
- `update.error.body` is `{0}` in English and `⁦{0}⁩` in Arabic. The Arabic isolate keeps the
  substituted message bidi-safe and also keeps the Arabic value distinct from English, which
  `LocalizationTests.Arabic_is_not_a_copy_of_english` requires (the key is not in `EqualByDesign`).
- `settings.updates.downloading` uses the task wording `Downloading update… {0}%`
  (not the shortened `Downloading… {0}%` from the lead's follow-up message).
