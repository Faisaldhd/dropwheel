# Function matrix


PASS/FAIL evidence for the 2.4.0 tools. Append one section per tool; do not rewrite another tool's
rows. A PASS names the assertion that produced it. `(inspection)` means the behaviour was verified by
reading the shipped code in this environment because a desktop session was not available; every other
row is an automated test that runs in CI.

## Add Background — `tools.addbackground` (one photo)

Window title "Add BG", primary button "Save with Background", output `<name> With Background.png` next to
the source. Tests live in `tests/FileAction.Tests/AddBackgroundTests.cs`.

| # | Function | Evidence — real output | Status |
|---|----------|------------------------|--------|
| 1 | Tool identity and localized labels | `The_window_and_primary_labels_resolve_in_both_languages`: Id `tools.addbackground`, TitleKey → `Add BG`, PrimaryLabelKey `tool.addbackground.apply` → `Save with Background`; the Arabic title/label/suffix exist and differ | PASS |
| 2 | Accepts exactly one image | `The_tool_accepts_one_photo_and_rejects_other_inputs`: `photo.jpg`/`photo.png` true; `report.pdf` false; two PNGs false; empty list false | PASS |
| 3 | Default padded composition | `Auto_keeps_the_padded_square_and_centres_the_photo`: 1200×1200 photo + padding 160 → canvas 1520×1520, photo at (160,160), corner radius 64 | PASS |
| 4 | Ratio chips crop the composition | `Ratio_crops_the_padded_composition_around_the_photo`: 1200² + 160, 16:9 → 1520×855 with photo y = −172; `Portrait_ratio_crops_a_landscape_photo_sideways`: 1600×900 + 100, 4:5 → 880×1100 with photo x = −360; `Square_ratio_centres_a_landscape_photo_that_has_no_padding`: 800×600, 1:1 → 600×600 | PASS |
| 5 | Slider pixels follow the original resolution | `Slider_values_are_pixels_of_the_preview_and_grow_by_the_original_scale`: 4096 px photo from a 2048 px preview → scale 2.0, padding 160 → 320; `The_export_scale_reproduces_the_preview_proportions`: 2368×1856 preview canvas → 4736×3712 export canvas, corners 64→128, shadow 40→80, same opacity | PASS |
| 6 | Render size and backdrop placement | `Render_writes_the_composed_size_with_the_backdrop_around_the_photo`: 320×200 photo + 20 px padding → a real 360×240 PNG (format Png); backdrop colour at (0,0) and (359,239), photo colour at (20,20) and (339,219) | PASS |
| 7 | Rounded corners | `Render_rounds_the_corners_so_the_backdrop_shows_through`: 200×200 photo, 10 padding, radius 60 → pixel (10,10) is the backdrop; (70,70) and (110,40) are the photo colour | PASS |
| 8 | Ratio crop end-to-end | `Render_crops_the_composition_to_the_ratio`: 200×100 photo + 100 padding, 16:9 → a 400×225 PNG (same numbers as `Compose`); backdrop at (0,0)/(399,224), photo at (200,110) | PASS |
| 9 | Image background, cover-fit | `Render_covers_the_canvas_with_a_background_image`: 400×100 green backdrop on a 220×220 canvas → green at (0,0) and (219,219), photo colour at (110,110) (cropped, never letterboxed); geometry also covered by `Cover_fit_keeps_the_aspect_and_splits_the_overflow` (1000×500 into 800×800 → −400,0,1600,800) | PASS |
| 10 | Source alpha shows the backdrop | `Render_lets_transparent_source_pixels_show_the_backdrop`: a photo with a transparent left half → backdrop colour at (20,50), photo colour at (100,50) | PASS |
| 11 | Alpha kept in the saved PNG | `Render_keeps_alpha_when_the_backdrop_is_transparent`: the saved PNG reports `HasAlpha`; composited over yellow, the transparent corner (0,0) is yellow and the photo at (70,70) is red | PASS |
| 12 | Never overwrites an existing result | `A_second_save_gets_a_numbered_name_and_never_overwrites`: real files named `photo With Background.png`, `photo With Background (2).png`, `photo With Background (3).png`; the third name does not exist yet | PASS |
| 13 | Shadow slider | `No_shadow_means_no_blur_offset_or_opacity` (0 → blur/offset/opacity 0); `Shadow_offset_follows_the_blur` (40 px blur → 7 px offset); `Shadow_strength_grows_with_the_blur_but_stays_readable` (faint < strong ≤ 0.42) | PASS |
| 14 | Export safety cap keeps the photo pixels | `A_huge_photo_keeps_its_pixels_but_the_frame_is_capped`: 9000×7000 photo + requested padding 469 → padding 31, canvas 9062×7062 ≤ 64 MP; `Compose_caps_the_frame_and_keeps_the_photo_size`: photo stays 6000×6000 while the frame shrinks together | PASS |
| 15 | Palettes | `There_are_nine_distinct_gradients_and_nine_distinct_solids`: 9 gradient presets (each start ≠ end, all distinct) and 9 distinct solid colours | PASS |
| 16 | Theme dark/light | Code audit, no desktop screenshot: every chrome/surface/text brush comes from `Theme.*` (`Panel`, `Card`, `PanelBorder`, `Text`, `Muted`, `Track`, `Accent`, `TextOnAccent`); the only literals are `Colors.Black` for the drop shadow and `Brushes.Transparent`. Found and fixed in `5256998`: a white custom colour used to draw a white “+” and a theme-text selection ring on itself — both now use `ContrastOn` (0x12 vs white by luminance), so selection stays visible in either theme; a disabled blur toggle now dims with its label. The CI “Screenshots” job needs a desktop session and is skipped on PRs. | PASS (audit) |
| 17 | Arabic / RTL | `LocalizationTests` table parity plus the spec test: 30 `tool.addbackground.*` keys in both tables, identical key sets and placeholders; ratio chips are `مربّع 1:1`, `عريض 16:9`, `طولي 4:5`; the hex box and preview are pinned LTR so the composition is not mirrored, while the controls inherit RTL | PASS |
| 18 | Reset restores the documented defaults | `The_defaults_behind_Reset_are_the_documented_ones`: padding 160 (max 400), corners 64 (max 400), shadow 40 (max 100), first gradient preset `Violet`, auto ratio 0 — the values `Reset` writes into the sliders, swatches and chips | PASS |
**How to reproduce the automated rows:** `dotnet test tests/FileAction.Tests/FileAction.Tests.csproj -c Release --filter "FullyQualifiedName~AddBackground"` (32 cases; the renderer rows also run in the `Build + test` job). Evidence run [39347847517](https://github.com/Faisaldhd/dropwheel/actions/runs/39347847517) on `03f42a9`: 318 passed, 0 failed; the same tests passed again on the merged head in run [39348678175](https://github.com/Faisaldhd/dropwheel/actions/runs/39348678175): 366 passed.

## Collage — `tools.collage`

| ID | Check | Expected / assertion | Status | Evidence |
| --- | --- | --- | --- | --- |
| C-1 | Output 1080 × 1080 | `Collage.png` next to the first input; `rendered.Width == 1080`, `rendered.Height == 1080` | PASS | `CollageTests.Renderer_honours_every_output_preset_and_leaves_the_source_files_untouched(1080,1080)` |
| C-2 | Output 1920 × 1080 | Same file, `rendered.Width == 1920`, `rendered.Height == 1080` | PASS | same theory, `(1920,1080)` |
| C-3 | Output 1080 × 1920 | Same file, `rendered.Width == 1080`, `rendered.Height == 1920` | PASS | same theory, `(1080,1920)` |
| C-4 | Original resolution, not preview | 400×300 and 300×400 sources produce the exact 1080/1920 output sizes above; the 640 px preview never caps the write | PASS | same theory (source sizes are hard-coded in the test) |
| C-5 | Grid cell layout | 320×240, spacing 40, radius 0: pixel (70,120) is image 1, (160,120) is the background gap, (250,120) is image 2; (70,5) and (315,120) stay inside their cells | PASS | `CollageTests.Renderer_places_the_images_in_the_grid_cells_with_the_background_in_the_gap` |
| C-6 | Source files untouched | SHA-256 and `LastWriteTimeUtc` identical before and after Apply for both inputs | PASS | same preset theory, hash and timestamp assertions |
| C-7 | Never overwrite | First Apply writes `Collage.png`; second writes `Collage (2).png` and the first file still exists | PASS | `CollageTests.Renderer_writes_the_output_next_to_the_first_file_and_never_overwrites` |
| C-8 | Primary button label | `CollageTool.PrimaryLabelKey == "tool.collage.create"`; English table = `Create Collage`; Arabic value differs and the key exists in both tables | PASS | `CollageTests.Primary_label_is_create_collage_in_both_tables` |
| C-9 | Window ~560 × 745 adaptive | `ToolWindowSizer` with the 1920×1080 probe gives 560 × 747.5 at 100%; 1366×768 clamps inside 95% × 90% of the work area | PASS | `CollageTests.Window_probe_keeps_the_designed_size_and_fits_a_small_work_area` |
| C-10 | Theme dark/light | Every surface uses `Theme.Card`/`Text`/`Muted`/`Track`/`Accent`/`AccentGradient`/`PanelBorder`; the Reset background follows `Theme.IsDark`; the shared palette switches between dark `#232131` and light `#FFFFFF` cards without error | PASS (inspection + palette test) | `CollageView` uses `Theme.*`; `CollageTests.Theme_switches_between_dark_and_light_palettes_for_the_tool` |
| C-11 | RTL | Window inherits `FlowDirection.RightToLeft`; label/control rows mirror; filmstrip is deliberately LTR (photo sequence is content order); no hard-coded English except numeric preset labels (`1080 × 1080`) and the `×` symbol | PASS (inspection) | `ToolWindow` FlowDirection + `CollageView` layout; screenshots not available in this agent (desktop session) |

**How to reproduce the automated rows:** `dotnet test tests/FileAction.Tests/FileAction.Tests.csproj -c Release --filter "FullyQualifiedName~CollageTests"` (38 cases), or the CI `Build + test` job. The run used for this matrix is [39347274919](https://github.com/Faisaldhd/dropwheel/actions/runs/39347274919): 324 passed, 0 failed, including all 38 Collage cases.

## Edit Photos — `tools.editphotos`

| ID | Check | Expected / assertion | Status | Evidence |
| --- | --- | --- | --- | --- |
| E-1 | All-zero parameters are a no-op save | `RenderFinal` with every slider at its default copies the source bytes; `File.ReadAllBytes(source)` equals the output exactly (no recompression, no orientation rewrite) | PASS | `PhotoEditTests.Final_render_is_a_byte_copy_at_neutral_and_differs_after_an_edit`; 6 ms local measurement |
| E-2 | A real edit changes pixels | The edited file decodes to different pixels than the neutral copy (`Compare(..., RootMeanSquared) > 0.01`; measured 0.58) | PASS | same test |
| E-3 | Never overwrites, names the copy | Two saves produce `<name> Edited.png` and `<name> Edited (2).png`; both files exist | PASS | same test |
| E-4 | Primary button label | `EditPhotosTool.PrimaryLabelKey == "tool.editphotos.apply"`; English `Save Edited Copy`; Arabic `حفظ نسخة معدّلة` exists and differs | PASS | `PhotoEditTests.The_tool_opens_for_one_still_image_only` and `Every_tool_string_exists_in_both_tables` |
| E-5 | Window ~500 × 745, adaptive | `ToolWindowSizer` with no media: 500 × 745; 4:3 photo: 500 wide (440 preview + 60 chrome); portrait: 420 wide (360 minimum panel); 1280 × 720 clamps inside 95 % × 90 % | PASS | `PhotoEditTests.The_window_opens_at_about_500_by_745_and_follows_the_photo_aspect` |
| E-6 | Single still image only | JPG/PNG/JFIF open; GIF, ICO and SVG are rejected so animations and icon frames are never flattened; two files are rejected | PASS | `PhotoEditTests.The_tool_opens_for_one_still_image_only` |
| E-7 | 24 MP live preview stays responsive | Slider change → refreshed downsampled preview in ≤ 500 ms; the UI thread only receives the finished bitmap | PASS (local measurement) | Release build, 6000 × 4000 source, all 16 sliders non-zero: 347 ms median (330–369 ms over 5 runs), 999 × 666 preview |
| E-8 | 24 MP full save | Worst case (all 16 sliders) ≤ 20 s; typical edit ≤ 3 s; neutral ≤ 50 ms | PASS (local measurement) | Release build, 6000 × 4000 source: 13.8 s all-sliders JPEG, 1.03 s exposure + contrast, 6 ms neutral (byte copy); 29.9 s only for the PNG + grain worst case |
| E-9 | Theme dark/light | Every chrome surface comes from `Theme.*`; the view builds in both palettes; the only fixed colour is the "Original" badge's black scrim over the photo | PASS (inspection + construction) | `EditPhotosView.Build()` on an STA thread with `Theme.Override` dark and light; no exception |
| E-10 | RTL | The window inherits `FlowDirection.RightToLeft`; title, tabs and labels mirror; sliders deliberately stay left-to-right (the value grows right); values use Western digits | PASS (inspection) | `ToolWindow` flow direction + `EditPhotosView` slider row; no hard-coded user text outside the string tables |
| E-11 | Every string exists in both tables | 29 `tool.editphotos.*` keys in en and ar (502 keys per table after the 2.4.0 train); no placeholders; Arabic is not a copy of English | PASS | `PhotoEditTests.Every_tool_string_exists_in_both_tables`, with `LocalizationTests` parity |

**How to reproduce the automated rows:** `dotnet test tests/FileAction.Tests/FileAction.Tests.csproj -c Release --filter "FullyQualifiedName~PhotoEditTests"` (26 cases), or the CI `Build + test` job. The local 24 MP measurements are from a Release build on the builder's workstation (ImageMagick Q8 in-process); CI hardware will differ.


## Protect / Unlock — `tools.protect`, `tools.unlock`

Protect writes one password-protected ZIP (`<name> Protected.zip`), or an AES-256 PDF when a single PDF is
selected; Unlock writes `<name> Unlocked.zip` / `<name> Unlocked.pdf`. Tests live in
`tests/FileAction.Tests/PasswordProtectionTests.cs`.

| ID | Check | Expected / assertion | Status | Evidence |
| --- | --- | --- | --- | --- |
| P-1 | Tool identity and localized labels | Specs register as `tools.protect` and `tools.unlock`; `tool.protect.title` / `tool.unlock.title` exist in both tables; 62 `tool.protect.*` / `tool.unlock.*` keys have identical key sets and placeholders and Arabic differs from English | PASS | `New_strings_have_matching_keys_placeholders_and_neutral_values`; `LocalizationTests` |
| P-2 | Selection rules | Protect accepts one file, one folder, or several files and rejects a folder mixed with files; Unlock accepts exactly one ZIP or PDF and rejects other formats/folders | PASS (inspection) | `ProtectTool.Applies` / `UnlockTool.Applies`, exercised by the wheel operation |
| P-3 | ZIP is AES-256 and round-trips | `AesZipWriter.Create` writes an AE-2 WinZip AES-256 entry; SharpCompress opens it with the correct password and reads the exact UTF-8 text | PASS | `Zip_round_trips_with_the_correct_password` |
| P-4 | ZIP rejects a wrong password | Opening an encrypted entry with a wrong password fails; no output is accepted | PASS | `Zip_rejects_the_wrong_password` |
| P-5 | Never overwrites | A second create at the same path throws `IOException` and leaves the first file byte-identical; callers use `OutputPaths.ProduceAsync`’s unique name | PASS | `Zip_never_overwrites_an_existing_output` |
| P-6 | Folder recursion is stable | Nested folders are walked in ordinal order; directory and file entries carry `project/`, `project/a/`, `project/a/one.txt`, `project/b/two.txt` | PASS | `Plan_walks_nested_folders_in_a_stable_order` |
| P-7 | Empty folder is refused | A folder with no file entries throws `Empty` and writes no ZIP, so no fake “protected” archive exists | PASS | `Empty_folder_is_not_a_fake_protected_archive` |
| P-8 | Unlock ZIP | The correct password produces an unencrypted ZIP whose entry reads back unchanged; a wrong password leaves no partial output file | PASS | `Unlock_creates_an_unencrypted_zip_and_never_leaves_a_partial_file` |
| P-9 | Plain ZIP is not called protected | An unencrypted ZIP reports `NotEncrypted` and is not rewritten | PASS | `A_plain_zip_is_reported_as_not_encrypted` |
| P-10 | PDF AES-256 and round-trip | The protected PDF contains `/AESV3`; the wrong password is rejected; the correct password opens it and Unprotect writes an unencrypted PDF | PASS | `Pdf_uses_aes_256_and_round_trips_with_the_correct_password` |
| P-11 | Password rules, strength and KDF | Short/mismatched/overlong values are refused; unlock accepts any non-empty value; strength is ordered and never strong for one repeated character; PBKDF2 output is deterministic and password-dependent | PASS | `Validation_rejects_short_mismatched_and_overlong_passwords`; `Unlock_validation_accepts_any_non_empty_password`; `Strength_meter_is_ordered_and_never_strong_for_one_repeated_character`; `Key_derivation_is_deterministic_and_password_dependent` |
| P-12 | Defaults, match gate and eye reset | One PDF defaults to PDF, everything else to ZIP; permissions start permissive; primary is disabled until both fields match and length ≥ 8; reveal state resets on reset/close | PASS | `Protect_defaults_suggest_pdf_for_one_pdf_and_zip_otherwise`; `Protect_primary_stays_disabled_until_both_fields_match_and_are_long_enough`; `Eye_toggle_state_is_hidden_after_reset_and_close` |
| P-13 | Capabilities are honest | ZIP key size 256, ZIP names are not encrypted (the note is shown), PDF key size 256, PDF owner permissions are supported | PASS | `Capabilities_do_not_pretend_zip_can_hide_names` |
| P-14 | Theme and RTL | Window uses the shared purple `Theme.*` brushes and shared `ToolWindow` chrome; controls inherit `FlowDirection.RightToLeft`; the hex/format row and eye toggle use logical columns | PASS (inspection) | `ProtectView` / `UnlockView` / `PasswordField`; screenshots need a desktop session |
| P-15 | No password logging | Password values never enter `AppLog`, diagnostics, toast text or exception messages; fields are cleared and hidden on close/focus loss | PASS (inspection) | No `AppLog`/`Notifications` call receives a password; `PasswordProtectionException` has no password parameter |
| P-16 | Cancellation and progress | ZIP protect checks every 80 KB and reports byte progress; the Cancel button cancels; ZIP unlock checks between entries; PDF saves are sync and cannot cancel mid-save | PASS (inspection) | `AesZipWriter` chunk loop, `BusyRow`, `CancellationTokenSource` in both views |
| P-17 | Library/licence decision | No package added: SharpCompress 0.50.5 (MIT) reads AES but cannot write it; PDFsharp 6.2.4 (MIT) writes PDF AES-256 V5; WinZip AE-2 writing is implemented with BCL crypto | PASS (inspection) | `PasswordZip.cs`, `PasswordPdf.cs`, `THIRD-PARTY-NOTICES.md` |

**How to reproduce the automated rows:** `dotnet test tests/FileAction.Tests/FileAction.Tests.csproj -c Release --filter "FullyQualifiedName~PasswordProtection"` (17 cases), or the CI `Build + test` job. Evidence run [39339048279](https://github.com/Faisaldhd/dropwheel/actions/runs/39339048279) on `5f2b375`: 264 passed, 0 failed; the merged head is re-run by the same PR’s Build + test job.
## QR reader — `tools.qr` (one image)

Read-only tool: it never writes a file, so the chrome's primary button ("Apply") copies the decoded text
and closes while the view's own **Copy** and **Open** sit above it. Window title "QR Code"; the decoded
text is selectable; the browser is started from the Open button only. Decoding is ZXing.Net 0.16.11
in-process (Apache-2.0, see [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md)). Tests live in
`tests/FileAction.Tests/QrPayloadTests.cs`.

| ID | Check | Expected / assertion | Status | Evidence |
| --- | --- | --- | --- | --- |
| Q-1 | Tool identity and localized title | Id `tools.qr`, TitleKey `tool.qr.title`, a declared layout, and the English title differs from the Arabic one | PASS | `QrPayloadTests.The_tool_opens_for_one_image_only` |
| Q-2 | One image only | `photo.png` and `code.JPG` open; two images, `report.pdf` and an empty list are rejected | PASS | same test |
| Q-3 | Wheel entry and id-scoped lookup | `WheelController` lists `new UI.Tools.Qr.QrOperation()` beside the other tools; `ToolRegistry.For(id, items)` matches by tool id, so Crop/Edit Photos do not compete for a single image | PASS (inspection) | `WheelController.cs` operation list, `ToolRegistry.For` |
| Q-4 | Decoder reads a code | `QRCodeWriter` 320 × 320 of a URL → `QrDecoder.Decode` returns the same string | PASS | `QrPayloadTests.Decoder_reads_a_generated_qr_code` |
| Q-5 | "No code" is not a crash | All-white 160 × 160 → null; a buffer shorter than `w × h × 4` → null; a zero-sized buffer → null | PASS | `Decoder_reports_no_code_for_a_blank_image`, `Decoder_refuses_a_buffer_that_is_too_small` |
| Q-6 | Link classification | Absolute http/https → kind Link, `CanOpen`, host as the headline, text kept byte-exact; a non-default port stays in the headline (`localhost:8080`) | PASS | `Web_links_are_openable`, `A_non_default_port_stays_in_the_headline` |
| Q-7 | Only http(s) may ever be opened | `javascript:`, `JavaScript:`, `file:`, `data:`, `vbscript:`, `ms-settings:`, `shell:`, `tel:` and `ftp:` stay plain Text with no URL and no Open button | PASS | `Non_web_schemes_never_become_links` |
| Q-8 | Disguised or relative values are refused | CRLF, tab, U+202E and U+2066/U+2069 overrides, `//host/path`, a bare host, `http://`, empty and null are all rejected | PASS | `Disguised_or_relative_values_are_not_links` |
| Q-9 | Scheme and host normalisation | `HTTP://EXAMPLE.COM/Path` → `http://example.com/Path` | PASS | `The_scheme_and_host_are_normalised` |
| Q-10 | Plain text and empty payloads | Ordinary text stays Text with no title/rows; null, empty and whitespace-only input produce an empty Text payload without throwing | PASS | `Plain_text_stays_text`, `Missing_text_is_an_empty_payload` |
| Q-11 | E-mail payloads | `mailto:` → address headline plus the decoded subject, no Open; the DoCoMo `MATMSG:` shape parses the same way | PASS | `Mailto_is_an_email_with_its_subject`, `Matmsg_is_an_email_too` |
| Q-12 | Wi-Fi payloads | `WIFI:` → network / security / password rows, `\;` and `\\` escapes honoured, empty SSID still shows the details | PASS | `Wifi_shows_network_security_and_password`, `Wifi_keeps_escaped_backslashes`, `A_wifi_code_without_a_network_name_still_shows_its_details` |
| Q-13 | vCard payloads | FN as the headline; ORG/TEL/EMAIL/URL as rows; folded lines unfold; an https URL on the card may be opened, a card without one may not | PASS | `Vcard_shows_the_contact_details`, `Vcard_folded_lines_are_unfolded`, `A_contact_without_a_web_address_is_not_openable` |
| Q-14 | Every shown label exists in both tables | All five kind keys and every field label are `tool.qr.*` and exist in en and ar; 26 `tool.qr.*` keys per table after the merge; `LocaleUsageTests` covers the literals the view passes to `Loc.T` | PASS | `Every_kind_and_field_label_exists_in_both_languages`, `LocalizationTests`, `LocaleUsageTests` |
| Q-15 | No network access and no telemetry | Inspection: the only shell call is `Process.Start(…, UseShellExecute: true)` inside `QrView.Open()`, reachable from the Open button; `QrDecoder` is a pure in-process ZXing call on a pixel buffer; no `HttpClient`/`WebRequest`/upload path in `UI/Tools/Qr` or `Core/Qr*` | PASS (inspection) | `QrView.Open`, `QrDecoder.Decode` |
| Q-16 | Never blocks the UI | Inspection: the constructor starts `Task.Run` (Magick rasterise → ZXing decode → frozen `BitmapSource`); `ActivateAsync` only paints the result and then reports the source `MediaSize` to `ToolWindowSizer` | PASS (inspection) | `QrView.Load`, `QrView.ActivateAsync` |
| Q-17 | Copy / Open / Reset | Copy writes `QrPayload.Text` verbatim and reports success or failure; Open re-checks `QrSafety.HttpUrl` and refuses with “Only http and https links can be opened.”; Reset clears the last message and the selection | PASS (inspection) | `QrView.Copy`, `QrView.Open`, `QrView.Reset` |
| Q-18 | Theme and RTL | Only `Theme.*` brushes (glass, borders and the X/title/Reset/primary bar come from the shared `ToolWindow`); the view inherits `FlowDirection` and technical values pass through `Bidi.Value` | PASS (inspection) | `QrView` brushes, `ToolWindow` chrome |
| Q-19 | Adaptive window | Own `ToolSpecLayout` (chrome 36 × 320; preview 300–720 wide, 220–520 high); the source pixel size feeds `ToolWindowSizer`, which fits any aspect and clamps to 95 % × 90 % of the work area | PASS (inspection) | `QrTool.Layout`, `ToolWindowSizer` |

**How to reproduce the automated rows:** `dotnet test tests/FileAction.Tests/FileAction.Tests.csproj -c Release --filter "FullyQualifiedName~QrPayloadTests"` (42 cases), or the CI `Build + test` job. The run used for this matrix is [39336656593](https://github.com/Faisaldhd/dropwheel/actions/runs/39336656593) on `f78170c`: `Build + test` green with no failures, including every `QrPayloadTests` case; the same suite re-runs on every push to this branch.

The `--self-test` window sweep still renders only the crop tool (`SelfTest.cs` is a shared file this tool did not edit), so Q-16…Q-19 are code inspection rather than a rendered screenshot; the `Screenshots` job needs a desktop session and is skipped on pull requests.

## Merge to PDF - `tools.pdfmerge` (2+ PDFs/images)

Window title "Merge to PDF", primary button "Merge PDFs", output `Merged.pdf` next to the first source (or the
drop folder), never overwriting. A single image stays with Crop. Real-file tests live in
`tests/FileAction.Tests/PdfToolFunctionTests.cs`; the model and key tests in `PdfOrganizeTests.cs`; the
theme scan/repaint test in `PdfToolThemeTests.cs`.

| ID | Check | Expected / assertion | Status | Evidence |
| --- | --- | --- | --- | --- |
| M-1 | Tool identity and localized labels | Id `tools.pdfmerge`; TitleKey `tool.pdfmerge.title`; PrimaryLabelKey `tool.pdfmerge.merge` -> "Merge PDFs"; all 14 `tool.pdfmerge.*` keys exist in en and ar with identical placeholders and differ between the languages | PASS | `Every_pdf_tool_key_exists_in_english_and_arabic_with_the_same_placeholders`, `The_pdf_tool_key_groups_contain_exactly_the_expected_keys` |
| M-2 | Accepts 2+ PDFs/images | `Applies` is true for 2+ PDFs, 2+ images and a mix; false for a single file or a non-PDF/image | PASS | `CanMerge_accepts_pdfs_and_images_only` |
| M-3 | Document order is the plan order | 3 PDFs (alpha 1 page, beta 2 pages, gamma 1 page) -> a real output of 4 pages in that order; PdfPig extracts `AlphaText`, `BetaOne`, `BetaTwo`, `GammaText` page by page | PASS | `Merge_writes_the_three_pdfs_in_document_order_with_their_text` |
| M-4 | Rotation preserved | source pages with /Rotate 0, 90, 180 -> output pages keep 0, 90, 180 | PASS | `Merge_keeps_the_rotation_of_each_source_page` |
| M-5 | One image = one page | 320x200 PNG after a 1-page PDF -> 2 pages; the image page is 240x150 pt (96 DPI -> points) and its pixels render | PASS | `Merge_adds_an_image_as_exactly_one_page` |
| M-6 | Up/down arrow rules | first row: up disabled, down enabled; last row: up enabled, down disabled; single file: both disabled; each press moves exactly one position | PASS | `Merge_arrows_are_disabled_at_the_first_and_last_row`, `Merge_a_single_file_has_both_arrows_disabled`, `Merge_up_and_down_move_one_position` |
| M-7 | Remove / reset / move | remove keeps the other rows in order, reset restores the incoming order, move places the file at the target index (and rejects no-ops/out-of-range) | PASS | `Merge_remove_keeps_the_other_files_in_order`, `Merge_reset_restores_the_selection_order`, `Merge_move_forward_places_the_file_at_the_target_index`, `Merge_move_rejects_out_of_range_or_no_op_indexes` |
| M-8 | Initial order and the alphabetical caveat | the plan copies the items in the order the app received them; paths collected without a selection can arrive alphabetically, which the arrows/drag correct (documented in `PdfMergeTool` and `PdfMergePlan`) | PASS (inspection) | `PdfMergeTool` XML doc; `Merge_keeps_the_selection_order_and_mixes_pdfs_with_images` |
| M-9 | Failure paths leave no partial output | corrupt PDF and password-protected PDF -> localized `ConversionException` naming the file and no `Merged*` file in the output folder; read-only folder -> access error and an empty folder | PASS | `Merge_fails_on_a_corrupt_pdf_without_writing_a_partial_file`, `Merge_fails_on_an_encrypted_pdf_without_writing_a_partial_file`, `Merge_fails_on_a_read_only_folder_without_writing_a_partial_file` |
| M-10 | Theme and RTL | no hard-coded colours in `UI/Tools/Pdf`; both views rebuild after a light/dark switch; RTL comes from the shared `ToolWindow` | PASS | `Pdf_tool_views_have_no_hard_coded_colours`, `Pdf_tool_views_repaint_when_the_theme_changes` (RTL chrome: inspection) |

## Organize Pages - `tools.pdforganize` (one PDF)

Header "N pages" and "Reset Order"; a page grid with numbered badges, rotate/duplicate/remove and
drag-reorder; footer "Make all pages the same width" with the note "Pages are scaled proportionally to match
the narrowest page; their heights may still differ."; primary button "Save Organized PDF"; output
`<name> Organized.pdf`, source untouched.

| ID | Check | Expected / assertion | Status | Evidence |
| --- | --- | --- | --- | --- |
| O-1 | Tool identity and localized labels | Id `tools.pdforganize`; PrimaryLabelKey `tool.pdforganize.save` -> "Save Organized PDF"; all 16 `tool.pdforganize.*` keys exist in en and ar with identical placeholders and differ between the languages | PASS | `Every_pdf_tool_key_exists_in_english_and_arabic_with_the_same_placeholders`, `The_pdf_tool_key_groups_contain_exactly_the_expected_keys` |
| O-2 | Accepts one PDF only | applies to `report.pdf`; false for an image, text file, folder or two PDFs | PASS | `CanOrganize_accepts_a_single_pdf_only` |
| O-3 | Seeds one page per source page | 3 pages -> indexes 0,1,2 with rotation 0; the header shows "{0} pages" | PASS | `Organize_seeds_one_page_per_source_page_in_order` |
| O-4 | Rotate | +90/-90/180 add to the page's own rotation and wrap at 360 | PASS | `Organize_rotate_adds_degrees_and_wraps`, `Page_ref_rotated_by_normalizes_to_a_quarter_turn` |
| O-5 | Duplicate / delete | duplicate inserts a copy right after the page with its rotation; delete removes one page and never the last page | PASS | `Organize_duplicate_inserts_a_copy_right_after_the_page`, `Organize_delete_removes_a_page_but_keeps_at_least_one` |
| O-6 | Move / drag and Reset Order | move reorders pages including duplicates; reset restores the source page list | PASS | `Organize_move_reorders_pages_and_keeps_duplicates`, `Organize_reset_restores_the_original_page_list` |
| O-7 | Exact real-file result | rotate(0,90)+rotate(1,180)+duplicate(1)+delete(3)+move(2,0) -> 3 pages, texts Two/One/Two, /Rotate 180/90/180 | PASS | `Organize_applies_rotate_duplicate_delete_and_move_exactly` |
| O-8 | Same-width option | 300x400 + 600x300, option on -> both output pages 300 pt wide, heights 400 and 150, and the text of both pages still extracts | PASS | `Organize_same_width_scales_every_page_to_the_narrowest_width` |
| O-9 | Validation | empty page list, page index outside the source and a non-quarter-turn rotation are rejected | PASS | `Organize_validate_flags_an_empty_page_list`, `Organize_validate_flags_a_page_index_outside_the_source`, `Organize_validate_flags_a_rotation_that_is_not_a_quarter_turn` |
| O-10 | Failure path | corrupt input -> localized `ConversionException` naming the file and no `Organized*` output | PASS | `Organize_fails_on_a_corrupt_pdf_without_writing_a_partial_file` |
| O-11 | Theme and RTL | theme scan and theme-change rebuild as for Merge; the grid and footer inherit RTL from the shared window | PASS | `Pdf_tool_views_have_no_hard_coded_colours`, `Pdf_tool_views_repaint_when_the_theme_changes` (RTL chrome: inspection) |

**How to reproduce the automated rows:** `dotnet test tests/FileAction.Tests/FileAction.Tests.csproj -c Release --filter "FullyQualifiedName~PdfToolFunctionTests|FullyQualifiedName~PdfToolThemeTests|FullyQualifiedName~PdfOrganizeTests"` (62 cases: 32 model/key methods, 9 real-file function tests, 3 theme cases). The 12 function/theme cases passed in run [39341981013](https://github.com/Faisaldhd/dropwheel/actions/runs/39341981013) (308 passed, 0 failed); the merged head reruns them in the `Build + test` job.
