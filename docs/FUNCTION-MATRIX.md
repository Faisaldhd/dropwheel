# Function matrix


PASS/FAIL evidence for the 2.4.0 tools. Append one section per tool; do not rewrite another tool's
rows. A PASS names the assertion that produced it. `(inspection)` means the behaviour was verified by
reading the shipped code in this environment because a desktop session was not available; every other
row is an automated test that runs in CI.

## Add Background â€” `tools.addbackground` (one photo)

Window title "Add BG", primary button "Save with Background", output `<name> With Background.png` next to
the source. Tests live in `tests/FileAction.Tests/AddBackgroundTests.cs`.

| # | Function | Evidence â€” real output | Status |
|---|----------|------------------------|--------|
| 1 | Tool identity and localized labels | `The_window_and_primary_labels_resolve_in_both_languages`: Id `tools.addbackground`, TitleKey â†’ `Add BG`, PrimaryLabelKey `tool.addbackground.apply` â†’ `Save with Background`; the Arabic title/label/suffix exist and differ | PASS |
| 2 | Accepts exactly one image | `The_tool_accepts_one_photo_and_rejects_other_inputs`: `photo.jpg`/`photo.png` true; `report.pdf` false; two PNGs false; empty list false | PASS |
| 3 | Default padded composition | `Auto_keeps_the_padded_square_and_centres_the_photo`: 1200Ã—1200 photo + padding 160 â†’ canvas 1520Ã—1520, photo at (160,160), corner radius 64 | PASS |
| 4 | Ratio chips crop the composition | `Ratio_crops_the_padded_composition_around_the_photo`: 1200Â² + 160, 16:9 â†’ 1520Ã—855 with photo y = âˆ’172; `Portrait_ratio_crops_a_landscape_photo_sideways`: 1600Ã—900 + 100, 4:5 â†’ 880Ã—1100 with photo x = âˆ’360; `Square_ratio_centres_a_landscape_photo_that_has_no_padding`: 800Ã—600, 1:1 â†’ 600Ã—600 | PASS |
| 5 | Slider pixels follow the original resolution | `Slider_values_are_pixels_of_the_preview_and_grow_by_the_original_scale`: 4096 px photo from a 2048 px preview â†’ scale 2.0, padding 160 â†’ 320; `The_export_scale_reproduces_the_preview_proportions`: 2368Ã—1856 preview canvas â†’ 4736Ã—3712 export canvas, corners 64â†’128, shadow 40â†’80, same opacity | PASS |
| 6 | Render size and backdrop placement | `Render_writes_the_composed_size_with_the_backdrop_around_the_photo`: 320Ã—200 photo + 20 px padding â†’ a real 360Ã—240 PNG (format Png); backdrop colour at (0,0) and (359,239), photo colour at (20,20) and (339,219) | PASS |
| 7 | Rounded corners | `Render_rounds_the_corners_so_the_backdrop_shows_through`: 200Ã—200 photo, 10 padding, radius 60 â†’ pixel (10,10) is the backdrop; (70,70) and (110,40) are the photo colour | PASS |
| 8 | Ratio crop end-to-end | `Render_crops_the_composition_to_the_ratio`: 200Ã—100 photo + 100 padding, 16:9 â†’ a 400Ã—225 PNG (same numbers as `Compose`); backdrop at (0,0)/(399,224), photo at (200,110) | PASS |
| 9 | Image background, cover-fit | `Render_covers_the_canvas_with_a_background_image`: 400Ã—100 green backdrop on a 220Ã—220 canvas â†’ green at (0,0) and (219,219), photo colour at (110,110) (cropped, never letterboxed); geometry also covered by `Cover_fit_keeps_the_aspect_and_splits_the_overflow` (1000Ã—500 into 800Ã—800 â†’ âˆ’400,0,1600,800) | PASS |
| 10 | Source alpha shows the backdrop | `Render_lets_transparent_source_pixels_show_the_backdrop`: a photo with a transparent left half â†’ backdrop colour at (20,50), photo colour at (100,50) | PASS |
| 11 | Alpha kept in the saved PNG | `Render_keeps_alpha_when_the_backdrop_is_transparent`: the saved PNG reports `HasAlpha`; composited over yellow, the transparent corner (0,0) is yellow and the photo at (70,70) is red | PASS |
| 12 | Never overwrites an existing result | `A_second_save_gets_a_numbered_name_and_never_overwrites`: real files named `photo With Background.png`, `photo With Background (2).png`, `photo With Background (3).png`; the third name does not exist yet | PASS |
| 13 | Shadow slider | `No_shadow_means_no_blur_offset_or_opacity` (0 â†’ blur/offset/opacity 0); `Shadow_offset_follows_the_blur` (40 px blur â†’ 7 px offset); `Shadow_strength_grows_with_the_blur_but_stays_readable` (faint < strong â‰¤ 0.42) | PASS |
| 14 | Export safety cap keeps the photo pixels | `A_huge_photo_keeps_its_pixels_but_the_frame_is_capped`: 9000Ã—7000 photo + requested padding 469 â†’ padding 31, canvas 9062Ã—7062 â‰¤ 64 MP; `Compose_caps_the_frame_and_keeps_the_photo_size`: photo stays 6000Ã—6000 while the frame shrinks together | PASS |
| 15 | Palettes | `There_are_nine_distinct_gradients_and_nine_distinct_solids`: 9 gradient presets (each start â‰  end, all distinct) and 9 distinct solid colours | PASS |
| 16 | Theme dark/light | Code audit, no desktop screenshot: every chrome/surface/text brush comes from `Theme.*` (`Panel`, `Card`, `PanelBorder`, `Text`, `Muted`, `Track`, `Accent`, `TextOnAccent`); the only literals are `Colors.Black` for the drop shadow and `Brushes.Transparent`. Found and fixed in `5256998`: a white custom colour used to draw a white â€œ+â€ and a theme-text selection ring on itself â€” both now use `ContrastOn` (0x12 vs white by luminance), so selection stays visible in either theme; a disabled blur toggle now dims with its label. The CI â€œScreenshotsâ€ job needs a desktop session and is skipped on PRs. | PASS (audit) |
| 17 | Arabic / RTL | `LocalizationTests` table parity plus the spec test: 30 `tool.addbackground.*` keys in both tables, identical key sets and placeholders; ratio chips are `Ù…Ø±Ø¨Ù‘Ø¹ 1:1`, `Ø¹Ø±ÙŠØ¶ 16:9`, `Ø·ÙˆÙ„ÙŠ 4:5`; the hex box and preview are pinned LTR so the composition is not mirrored, while the controls inherit RTL | PASS |
| 18 | Reset restores the documented defaults | `The_defaults_behind_Reset_are_the_documented_ones`: padding 160 (max 400), corners 64 (max 400), shadow 40 (max 100), first gradient preset `Violet`, auto ratio 0 â€” the values `Reset` writes into the sliders, swatches and chips | PASS |
**How to reproduce the automated rows:** `dotnet test tests/FileAction.Tests/FileAction.Tests.csproj -c Release --filter "FullyQualifiedName~AddBackground"` (32 cases; the renderer rows also run in the `Build + test` job). Evidence run [39347847517](https://github.com/Faisaldhd/dropwheel/actions/runs/39347847517) on `03f42a9`: 318 passed, 0 failed; the same tests passed again on the merged head in run [39348678175](https://github.com/Faisaldhd/dropwheel/actions/runs/39348678175): 366 passed.

## Collage â€” `tools.collage`

| ID | Check | Expected / assertion | Status | Evidence |
| --- | --- | --- | --- | --- |
| C-1 | Output 1080 Ã— 1080 | `Collage.png` next to the first input; `rendered.Width == 1080`, `rendered.Height == 1080` | PASS | `CollageTests.Renderer_honours_every_output_preset_and_leaves_the_source_files_untouched(1080,1080)` |
| C-2 | Output 1920 Ã— 1080 | Same file, `rendered.Width == 1920`, `rendered.Height == 1080` | PASS | same theory, `(1920,1080)` |
| C-3 | Output 1080 Ã— 1920 | Same file, `rendered.Width == 1080`, `rendered.Height == 1920` | PASS | same theory, `(1080,1920)` |
| C-4 | Original resolution, not preview | 400Ã—300 and 300Ã—400 sources produce the exact 1080/1920 output sizes above; the 640 px preview never caps the write | PASS | same theory (source sizes are hard-coded in the test) |
| C-5 | Grid cell layout | 320Ã—240, spacing 40, radius 0: pixel (70,120) is image 1, (160,120) is the background gap, (250,120) is image 2; (70,5) and (315,120) stay inside their cells | PASS | `CollageTests.Renderer_places_the_images_in_the_grid_cells_with_the_background_in_the_gap` |
| C-6 | Source files untouched | SHA-256 and `LastWriteTimeUtc` identical before and after Apply for both inputs | PASS | same preset theory, hash and timestamp assertions |
| C-7 | Never overwrite | First Apply writes `Collage.png`; second writes `Collage (2).png` and the first file still exists | PASS | `CollageTests.Renderer_writes_the_output_next_to_the_first_file_and_never_overwrites` |
| C-8 | Primary button label | `CollageTool.PrimaryLabelKey == "tool.collage.create"`; English table = `Create Collage`; Arabic value differs and the key exists in both tables | PASS | `CollageTests.Primary_label_is_create_collage_in_both_tables` |
| C-9 | Window ~560 Ã— 745 adaptive | `ToolWindowSizer` with the 1920Ã—1080 probe gives 560 Ã— 747.5 at 100%; 1366Ã—768 clamps inside 95% Ã— 90% of the work area | PASS | `CollageTests.Window_probe_keeps_the_designed_size_and_fits_a_small_work_area` |
| C-10 | Theme dark/light | Every surface uses `Theme.Card`/`Text`/`Muted`/`Track`/`Accent`/`AccentGradient`/`PanelBorder`; the Reset background follows `Theme.IsDark`; the shared palette switches between dark `#232131` and light `#FFFFFF` cards without error | PASS (inspection + palette test) | `CollageView` uses `Theme.*`; `CollageTests.Theme_switches_between_dark_and_light_palettes_for_the_tool` |
| C-11 | RTL | Window inherits `FlowDirection.RightToLeft`; label/control rows mirror; filmstrip is deliberately LTR (photo sequence is content order); no hard-coded English except numeric preset labels (`1080 Ã— 1080`) and the `Ã—` symbol | PASS (inspection) | `ToolWindow` FlowDirection + `CollageView` layout; screenshots not available in this agent (desktop session) |
| C-12 | Detected drop folder | With a drop target folder, `Collage.png` is written there and the source folder receives no collage; the remembered folder is used when nothing is under the cursor; the source file is byte-identical afterwards | PASS | `CollageTests.Drop_target_folder_receives_the_collage_and_the_source_folder_stays_untouched` |

**How to reproduce the automated rows:** `dotnet test tests/FileAction.Tests/FileAction.Tests.csproj -c Release --filter "FullyQualifiedName~CollageTests"` (39 cases), or the CI `Build + test` job.
**How to reproduce the automated rows:** `dotnet test tests/FileAction.Tests/FileAction.Tests.csproj -c Release --filter "FullyQualifiedName~CollageTests"` (38 cases), or the CI `Build + test` job. The run used for this matrix is [39347274919](https://github.com/Faisaldhd/dropwheel/actions/runs/39347274919): 324 passed, 0 failed, including all 38 Collage cases.

## Edit Photos â€” `tools.editphotos`

| ID | Check | Expected / assertion | Status | Evidence |
| --- | --- | --- | --- | --- |
| E-1 | All-zero parameters are a no-op save | `RenderFinal` with every slider at its default copies the source bytes; `File.ReadAllBytes(source)` equals the output exactly (no recompression, no orientation rewrite) | PASS | `PhotoEditTests.Final_render_is_a_byte_copy_at_neutral_and_differs_after_an_edit`; 6 ms local measurement |
| E-2 | A real edit changes pixels | The edited file decodes to different pixels than the neutral copy (`Compare(..., RootMeanSquared) > 0.01`; measured 0.58) | PASS | same test |
| E-3 | Never overwrites, names the copy | Two saves produce `<name> Edited.png` and `<name> Edited (2).png`; both files exist | PASS | same test |
| E-4 | Primary button label | `EditPhotosTool.PrimaryLabelKey == "tool.editphotos.apply"`; English `Save Edited Copy`; Arabic `Ø­ÙØ¸ Ù†Ø³Ø®Ø© Ù…Ø¹Ø¯Ù‘Ù„Ø©` exists and differs | PASS | `PhotoEditTests.The_tool_opens_for_one_still_image_only` and `Every_tool_string_exists_in_both_tables` |
| E-5 | Window ~500 Ã— 745, adaptive | `ToolWindowSizer` with no media: 500 Ã— 745; 4:3 photo: 500 wide (440 preview + 60 chrome); portrait: 420 wide (360 minimum panel); 1280 Ã— 720 clamps inside 95 % Ã— 90 % | PASS | `PhotoEditTests.The_window_opens_at_about_500_by_745_and_follows_the_photo_aspect` |
| E-6 | Single still image only | JPG/PNG/JFIF open; GIF, ICO and SVG are rejected so animations and icon frames are never flattened; two files are rejected | PASS | `PhotoEditTests.The_tool_opens_for_one_still_image_only` |
| E-7 | 24 MP live preview stays responsive | Slider change â†’ refreshed downsampled preview in â‰¤ 500 ms; the UI thread only receives the finished bitmap | PASS (local measurement) | Release build, 6000 Ã— 4000 source, all 16 sliders non-zero: 347 ms median (330â€“369 ms over 5 runs), 999 Ã— 666 preview |
| E-8 | 24 MP full save | Worst case (all 16 sliders) â‰¤ 20 s; typical edit â‰¤ 3 s; neutral â‰¤ 50 ms | PASS (local measurement) | Release build, 6000 Ã— 4000 source: 13.8 s all-sliders JPEG, 1.03 s exposure + contrast, 6 ms neutral (byte copy); 29.9 s only for the PNG + grain worst case |
| E-9 | Theme dark/light | Every chrome surface comes from `Theme.*`; the view builds in both palettes; the only fixed colour is the "Original" badge's black scrim over the photo | PASS (inspection + construction) | `EditPhotosView.Build()` on an STA thread with `Theme.Override` dark and light; no exception |
| E-10 | RTL | The window inherits `FlowDirection.RightToLeft`; title, tabs and labels mirror; sliders deliberately stay left-to-right (the value grows right); values use Western digits | PASS (inspection) | `ToolWindow` flow direction + `EditPhotosView` slider row; no hard-coded user text outside the string tables |
| E-11 | Every string exists in both tables | 29 `tool.editphotos.*` keys in en and ar (502 keys per table after the 2.4.0 train); no placeholders; Arabic is not a copy of English | PASS | `PhotoEditTests.Every_tool_string_exists_in_both_tables`, with `LocalizationTests` parity |

**How to reproduce the automated rows:** `dotnet test tests/FileAction.Tests/FileAction.Tests.csproj -c Release --filter "FullyQualifiedName~PhotoEditTests"` (26 cases), or the CI `Build + test` job. The local 24 MP measurements are from a Release build on the builder's workstation (ImageMagick Q8 in-process); CI hardware will differ.


## Protect / Unlock â€” `tools.protect`, `tools.unlock`

Protect writes one password-protected ZIP (`<name> Protected.zip`), or an AES-256 PDF when a single PDF is
selected; Unlock writes `<name> Unlocked.zip` / `<name> Unlocked.pdf`. Tests live in
`tests/FileAction.Tests/PasswordProtectionTests.cs`.

| ID | Check | Expected / assertion | Status | Evidence |
| --- | --- | --- | --- | --- |
| P-1 | Tool identity and localized labels | Specs register as `tools.protect` and `tools.unlock`; `tool.protect.title` / `tool.unlock.title` exist in both tables; 62 `tool.protect.*` / `tool.unlock.*` keys have identical key sets and placeholders and Arabic differs from English | PASS | `New_strings_have_matching_keys_placeholders_and_neutral_values`; `LocalizationTests` |
| P-2 | Selection rules | Protect accepts one file, one folder, or several files and rejects a folder mixed with files; Unlock accepts exactly one ZIP or PDF and rejects other formats/folders | PASS (inspection) | `ProtectTool.Applies` / `UnlockTool.Applies`, exercised by the wheel operation |
| P-3 | ZIP is AES-256 and round-trips | `AesZipWriter.Create` writes an AE-2 WinZip AES-256 entry; SharpCompress opens it with the correct password and reads the exact UTF-8 text | PASS | `Zip_round_trips_with_the_correct_password` |
| P-4 | ZIP rejects a wrong password | Opening an encrypted entry with a wrong password fails; no output is accepted | PASS | `Zip_rejects_the_wrong_password` |
| P-5 | Never overwrites | A second create at the same path throws `IOException` and leaves the first file byte-identical; callers use `OutputPaths.ProduceAsync`â€™s unique name | PASS | `Zip_never_overwrites_an_existing_output` |
| P-6 | Folder recursion is stable | Nested folders are walked in ordinal order; directory and file entries carry `project/`, `project/a/`, `project/a/one.txt`, `project/b/two.txt` | PASS | `Plan_walks_nested_folders_in_a_stable_order` |
| P-7 | Empty folder is refused | A folder with no file entries throws `Empty` and writes no ZIP, so no fake â€œprotectedâ€ archive exists | PASS | `Empty_folder_is_not_a_fake_protected_archive` |
| P-8 | Unlock ZIP | The correct password produces an unencrypted ZIP whose entry reads back unchanged; a wrong password leaves no partial output file | PASS | `Unlock_creates_an_unencrypted_zip_and_never_leaves_a_partial_file` |
| P-9 | Plain ZIP is not called protected | An unencrypted ZIP reports `NotEncrypted` and is not rewritten | PASS | `A_plain_zip_is_reported_as_not_encrypted` |
| P-10 | PDF AES-256 and round-trip | The protected PDF contains `/AESV3`; the wrong password is rejected; the correct password opens it and Unprotect writes an unencrypted PDF | PASS | `Pdf_uses_aes_256_and_round_trips_with_the_correct_password` |
| P-11 | Password rules, strength and KDF | Short/mismatched/overlong values are refused; unlock accepts any non-empty value; strength is ordered and never strong for one repeated character; PBKDF2 output is deterministic and password-dependent | PASS | `Validation_rejects_short_mismatched_and_overlong_passwords`; `Unlock_validation_accepts_any_non_empty_password`; `Strength_meter_is_ordered_and_never_strong_for_one_repeated_character`; `Key_derivation_is_deterministic_and_password_dependent` |
| P-12 | Defaults, match gate and eye reset | One PDF defaults to PDF, everything else to ZIP; permissions start permissive; primary is disabled until both fields match and length â‰¥ 8; reveal state resets on reset/close | PASS | `Protect_defaults_suggest_pdf_for_one_pdf_and_zip_otherwise`; `Protect_primary_stays_disabled_until_both_fields_match_and_are_long_enough`; `Eye_toggle_state_is_hidden_after_reset_and_close` |
| P-13 | Capabilities are honest | ZIP key size 256, ZIP names are not encrypted (the note is shown), PDF key size 256, PDF owner permissions are supported | PASS | `Capabilities_do_not_pretend_zip_can_hide_names` |
| P-14 | Theme and RTL | Window uses the shared purple `Theme.*` brushes and shared `ToolWindow` chrome; controls inherit `FlowDirection.RightToLeft`; the hex/format row and eye toggle use logical columns | PASS (inspection) | `ProtectView` / `UnlockView` / `PasswordField`; screenshots need a desktop session |
| P-15 | No password logging | Password values never enter `AppLog`, diagnostics, toast text or exception messages; fields are cleared and hidden on close/focus loss | PASS (inspection) | No `AppLog`/`Notifications` call receives a password; `PasswordProtectionException` has no password parameter |
| P-16 | Cancellation and progress | ZIP protect checks every 80 KB and reports byte progress; the Cancel button cancels; ZIP unlock checks between entries; PDF saves are sync and cannot cancel mid-save | PASS (inspection) | `AesZipWriter` chunk loop, `BusyRow`, `CancellationTokenSource` in both views |
| P-17 | Library/licence decision | No package added: SharpCompress 0.50.5 (MIT) reads AES but cannot write it; PDFsharp 6.2.4 (MIT) writes PDF AES-256 V5; WinZip AE-2 writing is implemented with BCL crypto | PASS (inspection) | `PasswordZip.cs`, `PasswordPdf.cs`, `THIRD-PARTY-NOTICES.md` |

**How to reproduce the automated rows:** `dotnet test tests/FileAction.Tests/FileAction.Tests.csproj -c Release --filter "FullyQualifiedName~PasswordProtection"` (17 cases), or the CI `Build + test` job. Evidence run [39339048279](https://github.com/Faisaldhd/dropwheel/actions/runs/39339048279) on `5f2b375`: 264 passed, 0 failed; the merged head is re-run by the same PRâ€™s Build + test job.
## Compress â€” `tools.compress` (one image, audio, video or PDF)

Window title "Compress File", primary button "Compress", output `<name> Compressed.<ext>` next to the source;
the source is only read. Rules live in `tests/FileAction.Tests/CompressMetadataTests.cs`, real media in
`CompressMetadataFunctionTests.cs`.

| ID | Check | Expected / assertion | Status | Evidence |
| --- | --- | --- | --- | --- |
| C-1 | Tool identity and localized labels | Id `tools.compress`, TitleKey `tool.compress.title` â†’ "Compress File", PrimaryLabelKey `tool.compress.apply` â†’ "Compress"; Arabic "Ø¶ØºØ·" exists and differs | PASS | `CompressMetadataToolTests.Tool_identity_primary_labels_and_applicability` and `CompressMetadataStringTests.Addendum_keys_exist_in_both_languages_with_the_fixed_english` |
| C-2 | Accepts exactly one supported file | jpg/png/mp3/m4a/mp4/mkv/pdf open; docx, a folder, two files and an empty list are rejected | PASS | same identity test |
| C-3 | Defaults | Balanced preset (`CompressionPreset.Medium`), target-size and percentage off, 40 % reduction â†’ 60 % kept, image sizes 2560/1920/1280/1024/640 px, "keep original dimensions" = 0 | PASS | `CompressionPlanTests.Defaults_are_the_balanced_preset_with_no_size_goal`, `Reduction_maps_onto_the_kept_percentage` |
| C-4 | Image compress writes a smaller, resized copy | Generated 1200 Ã— 900 noisy JPEG: output < 80 % of the source, exactly 600 Ã— 450 (max long side), source SHA-256 identical afterwards, no `.part` files | PASS | `CompressMetadataFunctionTests.Compress_image_writes_a_smaller_resized_copy_and_never_touches_the_source` |
| C-5 | Image plans follow target and percentage | Target 1 MB downscales 4000 Ã— 3000 and estimates 0.9â€“1.1 MB; percentage 50 % keeps the best quality and scales the pixels to an estimate of 0.95â€“1.1 MB; never upscales | PASS | `CompressionPlanTests.Target_size_downscales_the_image_to_reach_the_target`, `Percentage_mode_keeps_the_quality_and_scales_the_pixels`, `Image_plan_never_upscales_and_honours_the_max_long_side` |
| C-6 | Video target size, duration and audio | Generated 8 s noisy testsrc + sine, target 419430 B: output â‰¤ target + 10 % and > 60 % of it, < 50 % of the source; duration within Â±0.25 s; the audio track is still there; source unchanged | PASS | `CompressMetadataFunctionTests.Compress_video_reaches_the_target_size_and_keeps_duration_and_audio`; with the maxrate cap a local noisy run produced 385438 B (92 % of the target), and CI held the â‰¤ +10 % ceiling |
| C-7 | Audio gets smaller and keeps duration | Generated 6 s 192 kbps MP3, target 0.08 MB: output < 80 % of the source, duration within Â±0.25 s, source unchanged | PASS | `CompressMetadataFunctionTests.Compress_audio_gets_smaller_and_keeps_its_duration` |
| C-8 | Corrupt input fails cleanly | A fake JPEG throws with a message; only the source remains; no `.part` file | PASS | `CompressMetadataFunctionTests.A_corrupt_image_fails_without_leaving_any_file` |
| C-9 | Unwritable output fails cleanly | A path whose parent is a file throws; the directory is never created; no partial file; source unchanged | PASS | `CompressMetadataFunctionTests.An_unwritable_output_folder_fails_without_partial_files` |
| C-10 | Missing FFmpeg fails clearly | `ConversionException` with a message and no partial output; on machines where FFmpeg is installed the case reports a skip instead (CI installs it) | PASS (conditional) | `CompressMetadataFunctionTests.A_missing_ffmpeg_reports_a_clear_error_and_writes_nothing`; `CompressOperation.IsAvailable` hides audio/video without FFmpeg |
| C-11 | Every string exists in both tables | 46 `tool.compress.*` keys, identical key sets in en and ar (823 keys per table after the merge train), same placeholders, Arabic is not a copy of English, fixed helper line verbatim | PASS | `CompressMetadataStringTests.Both_tables_have_the_same_tool_keys`, `Placeholders_match_for_every_tool_key`, `Addendum_keys_exist_in_both_languages_with_the_fixed_english` |
| C-12 | Theme only | The view uses `Theme.*` brushes plus `Brushes.Transparent`; no hex colour, `Color.From`, `Colors.` or cached brush; `ToolWindow` calls `Theme.Refresh()` before building | PASS | `ToolThemeTests.Tool_views_use_theme_brushes_only`, `The_shared_window_refreshes_the_theme_before_it_builds` |

## Edit metadata â€” `tools.metadata` (one image, audio, video or PDF)

Window title "Edit Metadata", primary button "Save", output `<name> Edited metadata.<ext>` next to the
source; the original is only read. Field groups, validation and the FFmpeg argument builder live in
`src/FileAction.Core/MetadataModel.cs`.

| ID | Check | Expected / assertion | Status | Evidence |
| --- | --- | --- | --- | --- |
| M-1 | Tool identity and labels | Id `tools.metadata`, TitleKey `tool.metadata.title` â†’ "Edit Metadata", PrimaryLabelKey `tool.metadata.save` â†’ "Save"; Arabic "Ø­ÙØ¸" exists and differs | PASS | `CompressMetadataToolTests.Tool_identity_primary_labels_and_applicability`, `CompressMetadataStringTests.Addendum_keys_exist_in_both_languages_with_the_fixed_english` |
| M-2 | Searchable sections in display order | File, Image, Video track, Audio track, ExifIFD, IPTC, XMP, PDF info, Chapters; query matches label/value/group; editable rows come first inside their section | PASS | `MetadataModelTests.The_field_list_is_searchable_and_grouped_in_display_order`, `Editable_rows_come_first_inside_their_group` |
| M-3 | Writable rows always exist | Media probe seeds title/artist/album plus the chapters row (existing chapters as `mm:ss Title`); image reader ensures Make/Model/Latitude/Longitude; PDF reader ensures the four info rows | PASS (inspection + probe test) | `The_probe_seeds_the_editable_rows_and_the_chapter_text`; `MetadataRunner.ReadImage`/`ReadPdf` `EnsureEditable` rows |
| M-4 | Camera and GPS write and re-read | Real 640 Ã— 480 JPEG: copy named `shot Edited metadata.jpg`; re-read EXIF has Make `TestCam`, Model `TestModel`, GPS refs `N`/`E` and both GPS values; source SHA-256 unchanged | PASS | `CompressMetadataFunctionTests.Metadata_edit_writes_camera_and_gps_and_remove_all_clears_them` |
| M-5 | Remove All clears camera and location | A second save `shot Edited metadata (2).jpg` re-reads with no Make and no GPS value; processing/other hidden data is stripped by `image.Strip()` | PASS | same test (`RemoveAll`) |
| M-6 | Per-field remove deletes that tag | Removed media tags are written as `-metadata title=`; removed chapters add `-map_chapters -1`; EXIF removals call `RemoveValue`; PDF removed fields are emptied; chapter removal drops the chapter track | PASS | `Removed_media_tags_are_written_as_empty_metadata`, `Removing_chapters_drops_the_chapter_track`, `Editable_keys_map_onto_the_edit`; EXIF/PDF removal in `MetadataRunner` |
| M-7 | PDF info dictionary round-trips | Copy named `doc Edited metadata.pdf`; re-read via PDFsharp has Title/Author/Subject/Keywords as written, 1 page, source unchanged | PASS | `CompressMetadataFunctionTests.Metadata_edit_updates_the_pdf_info_dictionary` |
| M-8 | Corrupt metadata input fails cleanly | A fake JPEG throws; no `.part` file; only the source remains | PASS | `CompressMetadataFunctionTests.A_corrupt_metadata_input_fails_without_writing_anything` |
| M-9 | Footer note, Remove All and Save labels | The photo note "For photos, Remove All also clears hidden camera and processing data. HDR and portrait effects may change." and the labels "Search metadata", "Remove All", "Save" exist verbatim in English and are translated in Arabic | PASS | `CompressMetadataStringTests.Addendum_keys_exist_in_both_languages_with_the_fixed_english` |
| M-10 | Every string exists in both tables | 50 `tool.metadata.*` keys with identical key sets and placeholders in en and ar; no key ends with a plural category (guards `Loc.PluralBaseKeys`) | PASS | `Both_tables_have_the_same_tool_keys`, `Placeholders_match_for_every_tool_key`, `No_tool_key_looks_like_a_plural_group` |
| M-11 | Theme only | Same rule as Compress: `Theme.*` + `Brushes.Transparent` only, no cached brushes; the window repaints with the current palette when it opens | PASS | `ToolThemeTests.Tool_views_use_theme_brushes_only`, `The_shared_window_refreshes_the_theme_before_it_builds` |

**How to reproduce the compress and metadata rows:** `dotnet test tests/FileAction.Tests/FileAction.Tests.csproj -c Release --filter "FullyQualifiedName~CompressMetadata|FullyQualifiedName~ToolTheme"`, or the CI `Build + test` job (which installs FFmpeg first, so C-6 and C-7 run for real).

## QR reader â€” `tools.qr` (one image)

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
| Q-4 | Decoder reads a code | `QRCodeWriter` 320 Ã— 320 of a URL â†’ `QrDecoder.Decode` returns the same string | PASS | `QrPayloadTests.Decoder_reads_a_generated_qr_code` |
| Q-5 | "No code" is not a crash | All-white 160 Ã— 160 â†’ null; a buffer shorter than `w Ã— h Ã— 4` â†’ null; a zero-sized buffer â†’ null | PASS | `Decoder_reports_no_code_for_a_blank_image`, `Decoder_refuses_a_buffer_that_is_too_small` |
| Q-6 | Link classification | Absolute http/https â†’ kind Link, `CanOpen`, host as the headline, text kept byte-exact; a non-default port stays in the headline (`localhost:8080`) | PASS | `Web_links_are_openable`, `A_non_default_port_stays_in_the_headline` |
| Q-7 | Only http(s) may ever be opened | `javascript:`, `JavaScript:`, `file:`, `data:`, `vbscript:`, `ms-settings:`, `shell:`, `tel:` and `ftp:` stay plain Text with no URL and no Open button | PASS | `Non_web_schemes_never_become_links` |
| Q-8 | Disguised or relative values are refused | CRLF, tab, U+202E and U+2066/U+2069 overrides, `//host/path`, a bare host, `http://`, empty and null are all rejected | PASS | `Disguised_or_relative_values_are_not_links` |
| Q-9 | Scheme and host normalisation | `HTTP://EXAMPLE.COM/Path` â†’ `http://example.com/Path` | PASS | `The_scheme_and_host_are_normalised` |
| Q-10 | Plain text and empty payloads | Ordinary text stays Text with no title/rows; null, empty and whitespace-only input produce an empty Text payload without throwing | PASS | `Plain_text_stays_text`, `Missing_text_is_an_empty_payload` |
| Q-11 | E-mail payloads | `mailto:` â†’ address headline plus the decoded subject, no Open; the DoCoMo `MATMSG:` shape parses the same way | PASS | `Mailto_is_an_email_with_its_subject`, `Matmsg_is_an_email_too` |
| Q-12 | Wi-Fi payloads | `WIFI:` â†’ network / security / password rows, `\;` and `\\` escapes honoured, empty SSID still shows the details | PASS | `Wifi_shows_network_security_and_password`, `Wifi_keeps_escaped_backslashes`, `A_wifi_code_without_a_network_name_still_shows_its_details` |
| Q-13 | vCard payloads | FN as the headline; ORG/TEL/EMAIL/URL as rows; folded lines unfold; an https URL on the card may be opened, a card without one may not | PASS | `Vcard_shows_the_contact_details`, `Vcard_folded_lines_are_unfolded`, `A_contact_without_a_web_address_is_not_openable` |
| Q-14 | Every shown label exists in both tables | All five kind keys and every field label are `tool.qr.*` and exist in en and ar; 26 `tool.qr.*` keys per table after the merge; `LocaleUsageTests` covers the literals the view passes to `Loc.T` | PASS | `Every_kind_and_field_label_exists_in_both_languages`, `LocalizationTests`, `LocaleUsageTests` |
| Q-15 | No network access and no telemetry | Inspection: the only shell call is `Process.Start(â€¦, UseShellExecute: true)` inside `QrView.Open()`, reachable from the Open button; `QrDecoder` is a pure in-process ZXing call on a pixel buffer; no `HttpClient`/`WebRequest`/upload path in `UI/Tools/Qr` or `Core/Qr*` | PASS (inspection) | `QrView.Open`, `QrDecoder.Decode` |
| Q-16 | Never blocks the UI | Inspection: the constructor starts `Task.Run` (Magick rasterise â†’ ZXing decode â†’ frozen `BitmapSource`); `ActivateAsync` only paints the result and then reports the source `MediaSize` to `ToolWindowSizer` | PASS (inspection) | `QrView.Load`, `QrView.ActivateAsync` |
| Q-17 | Copy / Open / Reset | Copy writes `QrPayload.Text` verbatim and reports success or failure; Open re-checks `QrSafety.HttpUrl` and refuses with â€œOnly http and https links can be opened.â€; Reset clears the last message and the selection | PASS (inspection) | `QrView.Copy`, `QrView.Open`, `QrView.Reset` |
| Q-18 | Theme and RTL | Only `Theme.*` brushes (glass, borders and the X/title/Reset/primary bar come from the shared `ToolWindow`); the view inherits `FlowDirection` and technical values pass through `Bidi.Value` | PASS (inspection) | `QrView` brushes, `ToolWindow` chrome |
| Q-19 | Adaptive window | Own `ToolSpecLayout` (chrome 36 Ã— 320; preview 300â€“720 wide, 220â€“520 high); the source pixel size feeds `ToolWindowSizer`, which fits any aspect and clamps to 95 % Ã— 90 % of the work area | PASS (inspection) | `QrTool.Layout`, `ToolWindowSizer` |

**How to reproduce the automated rows:** `dotnet test tests/FileAction.Tests/FileAction.Tests.csproj -c Release --filter "FullyQualifiedName~QrPayloadTests"` (42 cases), or the CI `Build + test` job. The run used for this matrix is [39336656593](https://github.com/Faisaldhd/dropwheel/actions/runs/39336656593) on `f78170c`: `Build + test` green with no failures, including every `QrPayloadTests` case; the same suite re-runs on every push to this branch.

The `--self-test` window sweep still renders only the crop tool (`SelfTest.cs` is a shared file this tool did not edit), so Q-16â€¦Q-19 are code inspection rather than a rendered screenshot; the `Screenshots` job needs a desktop session and is skipped on pull requests.

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
## Lock â€” `tools.lock` (Beta)

One real file or folder is transformed **in place** into one self-contained `.dwenc` / `.dwdir`: a
three-slot password/recovery envelope, authenticated header, chunked AES-256-GCM frames, verified
atomic replacement, a folder `desktop.ini` badge, auto-relock and crash recovery. Tests live in
`tests/FileAction.Tests/LockContainerTests.cs` and `LockSafetyTests.cs`.

| ID | What is checked | Expected result | Status | Evidence |
|---|---|---|---|---|
| L-1 | Single real file, one item | After lock there is exactly one `.dwenc` and no plaintext/temp; unlock restores the original name and bytes | PASS | `Service_roundtrip_creates_and_removes_exactly_one_file` |
| L-2 | 0 B, 1 B, chunkâˆ’1, chunk, chunk+1, multi-chunk | Each size round-trips to the same SHA-256; no partial files remain | PASS | same theory (6 sizes) |
| L-3 | Small folder | Nested file and `desktop.ini` round-trip; unlock restores the folder and byte-exact previous `desktop.ini` | PASS | `Folder_roundtrip_restores_content_and_desktop_ini_byte_for_byte` |
| L-4 | Folder failure rollback | A locked file inside the folder aborts the lock, restores the folder appearance and writes no container | PASS | `Folder_protect_rolls_back_when_a_file_is_locked` |
| L-5 | Tamper and truncation | Wrong tag/ciphertext/header/MAC and truncation are refused; nothing is restored | PASS | `Tampered_*`, `Truncated_file_is_refused` |
| L-6 | Corrupt input | A damaged container gives a clear error and leaves no restored/staging/temp output | PASS | `Corrupt_input_leaves_no_output_files` |
| L-7 | Read-only source | A read-only folder/file round-trips without partial output | PASS | `Read_only_folder_still_roundtrips_without_partial_output` |
| L-8 | Cancel mid-run | Source and bytes stay intact; no `.dwenc` and no `.tmp` remain | PASS | `Cancel_mid_run_leaves_the_source_and_no_output` |
| L-9 | Header-only rewrite atomicity | Failure injected at every file operation leaves the old secrets working; a verified replace is required | PASS | `Header_rewrite_is_atomic_under_a_failure_at_every_file_operation` |
| L-10 | Crash recovery | The journal re-encrypts edited plaintext into the same container; plaintext is kept until the replace verifies | PASS | `Crash_recovery_reencrypts_the_edited_plaintext_into_the_same_container` |
| L-11 | Auto-lock rules | Waits for the program, a tabbed Explorer view, a held file and the grace period; honors the idle fallback | PASS | `Auto_lock_waits_for_the_program_the_explorer_and_the_grace` |
| L-12 | No stored secrets or Recycle copies | No password/key under `%LOCALAPPDATA%\DropWheel`; `RecycleContainerAfterUnlock` defaults OFF and protect never recycles | PASS | `No_secret_is_written_under_the_app_data_folder`, `Recycle_bin_is_off_by_default_and_protection_never_recycles` |
| L-13 | Real system icons + purple padlock, cache, cleanup, HKCU only | Only `padlock.ico` is shipped (6 frames, 8.5 KB); displayed icons are composed at lock time from `IShellItemImageFactory` system PDF/JPG/folder icons with #8224D4/#B48AE0 padlock bottom-left; per-extension cache plus per-item copy; item cache deleted on unlock and all cache on uninstall; host notification after lock/unlock; HKCU-only registry | PASS (the `.dwenc` Explorer DefaultIcon is the composed system generic-file icon; a per-file Explorer icon would need a shell icon handler, which this tool deliberately does not install) | `Padlock_is_the_only_shipped_badge_and_uses_the_purple_tokens`, `Composition_keeps_the_real_shell_icon_and_adds_purple_pixels_bottom_left`, `Icon_cache_reuses_the_extension_cleans_items_and_builds_association_icons`, `Host_is_notified_after_lock_and_unlock_so_explorer_refreshes`, `Associations_are_per_user_and_removable` |

**How to reproduce the automated rows:** `dotnet test tests/FileAction.Tests/FileAction.Tests.csproj -c Release --filter "FullyQualifiedName~LockContainerTests|FullyQualifiedName~LockSafetyTests"`; the CI `Build + test` job runs them on every push.

---

The 2.4.0 evidence rows above are preserved verbatim from `origin/main`. The audit below is appended; its row counts cover the audit sections only.

# Adversarial audit (branch audit/afunc)

Audit branch `audit/afunc`; audited product tree `268a2d7` (`origin/main`, "Merge pull request #21 from
Faisaldhd/feat/2.4.0-lock"), with the upstream wheel wiring `4f23c9c` merged in so the drag appendix is
measured against the tree that ships. The local `main` ref is stale; this document audits the tree the CI
and release jobs build, not the older local branch. Four test-only commits (`dda1ba9`, `286d664`,
`13555b9`, `b126ae8`) and the `origin/main` merge were added to this branch while the audit ran; they add
`tests/FileAction.Tests/DragDropTests.cs` (25 cases, all green in CI) and are reviewed in §11.3 and the
appendix. No shipped source file changed except by that upstream merge.

Scope: every user-invocable operation on the three wheels — the 27 **Tools** operations documented
here (all 27 reachable from `BuildOperations` after `4f23c9c` wired Annotate and Redact in: 26 registered
windows plus OCR; image, video, audio, PDF, compress/metadata, password/lock, OCR), the **Convert** catalog
(image/audio/video/PDF/documents/slides/spreadsheets/subtitles/archives), the six **Actions**, and the
drag/key/repeat machinery that reaches them. For every tool the matrix covers: each supported input format, the relevant orientation/size
cases, the four output flows (same folder, cross-location drop folder, multi-select, folder input),
content verification (decode/probe, dimensions, duration, pages, text, metadata), source SHA-256
unchanged, unique names on repeat, and the corrupt/zero-byte/wrong-extension/read-only/in-use error
cases with the no-partial-file rule.

## 0. How to read this matrix

**Status is only PASS or FAIL.** A row is `PASS` when a named test on `origin/main` asserts it, or when
the exact shipped code path makes it deterministic and there is no conflicting path. It is `FAIL` when
the code contradicts the requirement, the assertion has no evidence at all where evidence is expected,
or the only possible evidence is blocked in this environment. A `FAIL` that is a coverage/blocked gap
rather than a demonstrated product break says so in the evidence column and maps to a fix task in §1.

**Evidence classes** (third column of the evidence cell):

- `[test]` — the named xUnit test exists on `origin/main` and runs in `.github/workflows/ci.yml`
  (`dotnet test ...`). It was **not re-executed in this session**: the task rules forbid running tests
  or any built exe locally; CI is the only test runner.
- `[code]` — verified by reading the shipped source in this worktree; the cited file:line is the exact
  path.
- `[blocked]` — the check needs a desktop session, an interactive drag, a helper program or a valid
  sample that this environment does not have. It is reported as FAIL, never silently upgraded.

**Execution limits recorded for this audit.** `dotnet build FileAction.sln -c Release` was executed
locally on 2026-10-20 08:31 (product tree `268a2d7`) and succeeded: 0 errors, 10 warnings (App 3,
Linux 4, Tests 3), 27.8 s. Two test-only commits landed during the audit: `dda1ba9` did not compile
(CS9135 in the new drag test); `286d664` fixed that, and `dotnet build tests/FileAction.Tests/... -c
Release` now succeeds. Nothing was **run**: no test assembly, no `DropWheel.exe`, no `--self-test`, no
helper download. There is no desktop session, so no drag/drop, wheel or tool-window interaction was
executed. The CI test job does not install LibreOffice (`.github/workflows/ci.yml:118-129` installs
FFmpeg and Pandoc only), so every LibreOffice-backed conversion is skipped by the test suite.
`_local/samples` now contains the supervisor's extracted sample pack (see §11.1): images, audio, video,
PDF, documents, archives, subtitles, hostile files and DragDrop_A/B. It is not wired into CI
(`TestData.ApplyFixtures` ignores it), no tool test consumes it, and `tools/make-samples.ps1` still does
not exist on any branch or in history; §11 lists what the pack covers and what is still missing.

**Counts:** the audit matrix has 488 scored rows — 442 PASS, 46 FAIL (four rows re-marked after `4f23c9c`:
W-01, Annotate T1, Redact T1 and FW-13; Collage T8 flipped and the `origin/main` C-12 row added when the train merged). The "Drag and drop" appendix at the
end (`tests/FileAction.Tests/DragDropTests.cs`) adds 33 executed rows (32 PASS, 1 FAIL): its 25 cases run
green in the `Build + test` job of CI run
[39376742559](https://github.com/Faisaldhd/dropwheel/actions/runs/39376742559) on `b126ae8`, so those rows
count as CI evidence (the single FAIL is the PDF window dispatch, F-02). The preserved 2.4.0 rows add 111
PASS rows. Counting every status row in the file, it contains 632 rows — 585 PASS, 47 FAIL. The finding
table, headers and sample-gap tables are not scored. Every FAIL maps to a fix task; F-10 was withdrawn during review
because the Lock OS-lock message embeds the OS reason, so the gap in the task numbers is intentional.

## 1. Findings and fix tasks

Every FAIL row in §2–§5 maps to one of these tasks. Severity is about user impact, not about how hard
the fix is. The "reproduction" column is the exact check that fails today or the check that must be
added; commands are given in the form CI will run them.

| ID | Sev | Finding (what is wrong) | Exact failing check / proposed command | Evidence | Fix task |
| --- | --- | --- | --- | --- | --- |
| F-01 | P1 | **Annotate and Redact are shipped but unreachable.** `WheelController.BuildOperations()` does not add `AnnotateOperation` or `RedactOperation`; only their own files and tests exist. No wheel can offer `tools.annotate`/`tools.redact`. | `dotnet test --filter "FullyQualifiedName~WheelDispatch"` — new test must assert `BuildOperations().Select(o => o.Id)` contains both ids and that `ToolRegistry.For("tools.annotate", [image])` returns the annotate spec. Today: 0 hits in the operation list. | [code] `src/FileAction.App/Wheel/WheelController.cs:88-118`; `src/FileAction.App/UI/Tools/Image/AnnotateTool.cs:49-54,58-79`; `src/FileAction.App/UI/Tools/Image/RedactTool.cs:67-72,91-96` | Add both operations to the list (Redact and Annotate open by identity, not by `ToolRegistry.For(items)`); add the dispatch test. **Fixed upstream in `4f23c9c`**: both operations are in `BuildOperations()`, and `DragDropTests.Every_registered_tool_window_is_reachable_from_the_wheel_by_its_own_id` asserts 26/26 registered windows reachable (CI run [39376742559](https://github.com/Faisaldhd/dropwheel/actions/runs/39376742559)) |
| F-02 | **P0** | **Both PDF tools can open another tool's window.** `PdfMergeOperation.Open` and `PdfOrganizeOperation.Open` resolve their spec with the first-match `ToolRegistry.For(items)`. For one PDF, Lock/Protect/Unlock/Compress/Metadata all accept it; for two images/PDFs, Collage/Protect/Lock also accept. Registration order comes from module initializers and is not part of the contract, so "Merge to PDF"/"Organize Pages" can open Lock, Protect, Collage or Compress. | `dotnet test --filter "FullyQualifiedName~PdfToolDispatch"` — new test must assert `PdfMergeOperation` resolves `tools.pdfmerge` for {2 images}, {2 PDFs}, {PDF+image} and `PdfOrganizeOperation` resolves `tools.pdforganize` for {1 PDF}, independent of `ToolRegistry` order. Today the code calls `ToolRegistry.For(items)`. | [code] `src/FileAction.App/UI/Tools/Pdf/PdfToolOperations.cs:35-39,59-63`; competing `src/FileAction.App/UI/Tools/Lock/LockTool.cs:44-52`, `.../Password/ProtectTool.cs:40-41`, `.../Collage/CollageTool.cs:44-46`; the identical bug is already documented for Crop in `.../Image/CropOperation.cs:24-28` | Open `PdfMergeTool.Instance` / `PdfOrganizeTool.Instance` directly (as Crop, Edit Photos, Lock, Protect, Unlock, Compress, Metadata, QR and the video/audio tools already do). |
| F-03 | P1 | **"Repeat the last action" never remembers an interactive tool.** `LastOperationId`/`LastParameters` are written only by `JobRunner.Remember`; `WheelController.RunOperation` opens a tool window and returns without remembering. The README advertises repeat with "the same trim times", but Trim/Crop/Compress/… are interactive, so tapping the same key again repeats an older job or shows "does not fit". | `dotnet test --filter "FullyQualifiedName~RepeatLastInteractive"` — new test must open an interactive operation through the controller's dispatch seam, assert `settings.LastOperationId == op.Id`, then call `LastFor` with the same items and assert the same op. Today `RunOperation` has no remember call. | [code] `src/FileAction.App/Wheel/WheelController.cs:244-252` (dispatch), `:151-158` (`LastFor`); the only remember call is `src/FileAction.App/Jobs/JobRunner.cs:206-211`; promise in `README.md:28` | Persist the interactive op id when its window opens (and update parameters on Apply if needed); add the repeat test. |
| F-04 | P1 | **Collage, Video Split and Video Join ignore the detected drop folder.** Collage always writes next to the first input (`CollageView` uses `Path.GetDirectoryName(_items[0].Path)`); the split and join runners hardcode `OutputTarget.SourceFolder(source)`/`clips[0]`. Dragging A→B and choosing these tools still writes into A. | `dotnet test --filter "FullyQualifiedName~ToolDropFolder"` — new theory must run Collage, Video Split and Video Join with `DropTargets(B, null)` and assert every output is under B. Today the three output paths are hardcoded. | [code] `src/FileAction.App/UI/Tools/Collage/CollageView.cs:472-492`, `.../Collage/CollageTool.cs:48-49`; `src/FileAction.App/UI/Tools/Video/VideoSplitRunners.cs:16-21`, `.../Video/VideoSplitTool.cs:47-48`; `src/FileAction.App/UI/Tools/Video/VideoSplitRunners.cs:119-131`, `.../Video/VideoJoinTool.cs:47-48` | Pass `DropTargets` into the three views and use `ToolOutputFolder.For`/the existing output helper; thread an output directory through both runners; add the theory. **Collage part fixed upstream on `origin/main`** (`CollageTool.Create` passes `DropTargets`, `CollageView.OutputDirectoryFor` resolves it, `CollageTests.Drop_target_folder_receives_the_collage_and_the_source_folder_stays_untouched`); Split and Join still hardcode the source folder. |
| F-05 | P1 | **The Settings "save results to folder" choice is ignored by every floating tool.** `ToolOutputFolder.For` (used by Compress/Metadata and copied by every other tool) considers only the drop folder, the remembered folder and the source folder; `AppSettings.OutputDirectory` is read only by `JobRunner`. A user who chose "to folder" still gets tool results next to the source. | `dotnet test --filter "FullyQualifiedName~ToolOutputFolder"` — new test must set a settings output folder, pass no drop target, and assert `ToolOutputFolder.For` returns it. Today it returns the source folder. | [code] `src/FileAction.App/UI/Tools/ToolOutputFolder.cs:13-25`; settings field `src/FileAction.App/AppSettings.cs:84-85`; UI promise `src/FileAction.App/UI/SettingsWindow.cs:300-327`; only honouring path `src/FileAction.App/Jobs/JobRunner.cs:111-115` | Make the tools fall back to `settings.OutputDirectory` after the drop target and before the source folder (or teach `ToolWindow` the settings); add the test. |
| F-06 | P2 | **Four image tools write the final path directly, so a failed/partial write can leave a damaged file.** Crop, Edit Photos, Annotate and Redact all call `OutputPaths.Unique` and then `image.Write(target)` / `File.Copy(..., target)` with no `.part` file and no rollback. `OutputPaths.ProduceAsync` is the pattern every other tool uses. | `dotnet test --filter "FullyQualifiedName~ImageToolPartialOutput"` — new test must force the writer to fail after it has created the target (e.g. a locked target file or an injected writer that throws mid-write) and assert the output folder contains no new file. Today the target can remain zero-length/partial. | [code] `src/FileAction.App/UI/Tools/Image/CropTool.cs:446-464`; `.../Image/EditPhotoRenderer.cs:58-74`; `.../Image/AnnotateRunner.cs:75-91`; `.../Image/RedactRunner.cs:53-68`; correct pattern `src/FileAction.Core/OutputPaths.cs:26-51` | Route all four writers through `OutputPaths.ProduceAsync` (or a temp+flush+move helper); add the failure-injection test. |
| F-07 | P2 | **Zero-page PDFs silently produce an empty result.** `pdf.to.jpg`/`pdf.to.png` and `pdf.split` read the page count, then create a unique `"<name> (Pages)"` folder and return it as success when the count is 0. The user gets an empty folder and a success toast; there is no clear error and no content. | `dotnet test --filter "FullyQualifiedName~ZeroPagePdf"` — build a valid 0-page PDF and assert `PdfBackend.RenderAsync` and `SplitAsync` throw a localized `ConversionException` and create no folder. Today they return the empty folder. | [code] `src/FileAction.Core/Backends/PdfBackend.cs:22-55` (`count == 1` else folder) and `:208-236` (`SplitAsync`); contrast the guarded organizer `src/FileAction.App/UI/Tools/Pdf/PdfOrganizeTool.cs:223-227` | Treat `count < 1` as a clear error before creating any folder; add the test. |
| F-08 | P2 | **A binary XLSX renamed `.csv`/`.tsv` is decoded as text and written out as a garbage CSV/TSV.** `SpreadsheetBackend.Read` selects the reader by extension; `ReadDelimited` reads any bytes as UTF-8 (fallback CP1250) and `ConvertAsync` writes the result. Wrong-extension input is not rejected, unlike the image/audio/video sniffing paths. | `dotnet test --filter "FullyQualifiedName~SpreadsheetWrongExtension"` — copy an XLSX's bytes to `report.csv`, run `sheet.to.csv`, assert a clear `ConversionException` and no output. Today it succeeds with mojibake. | [code] `src/FileAction.Core/Backends/SpreadsheetBackend.cs:20-29` (dispatch), `:80-99` (text read), `:246-267` (write); no test in `tests/FileAction.Tests/UnitTests.cs:94-111` | Reject binary/undecodable `.csv`/`.tsv` input (NUL/control-byte guard or byte-order check) before writing; add the test. |
| F-09 | P2 | **The metadata window reports raw pixel dimensions for EXIF-rotated photos.** `ReadImage` creates `MagickImage` without `AutoOrient`, so a portrait phone JPEG stored as 4032×3024 with Orientation=6 is shown as 4032×3024; `WriteImage` does auto-orient, so the saved copy is 3024×4032. The dimensions row and the output disagree. | `dotnet test --filter "FullyQualifiedName~MetadataOrientation"` — build a JPEG with Orientation=6 and assert the read document reports the displayed (oriented) width/height and the saved copy matches. Today read=raw, write=oriented. | [code] `src/FileAction.App/UI/Tools/Meta/MetadataRunner.cs:57-66` (read, no auto-orient) vs `:145-170` (write, `AutoOrient` at line 157). The sample `images/exif_rotated_6.jpg` is stored 1600×900 with EXIF Orientation=6 (probed 2026-10-20), so read reports 1600×900 while write produces 900×1600. | Auto-orient before reading the dimensions (or report both raw and displayed, explicitly); add the test. |

| F-11 | P2 | **Multi-frame formats are silently flattened by tools that claim to support them.** Edit Photos deliberately rejects GIF/ICO ("editing them would silently flatten an animation"), but Crop, Add Background, Annotate, Redact, Compress, Metadata and the image-edit catalog accept GIF and multi-page TIFF and keep only frame 0. A 2-frame GIF compressed/rotated/cropped becomes a 1-frame GIF. | `dotnet test --filter "FullyQualifiedName~AnimatedGif"` — process a 2-frame GIF through Crop/Compress/`image.rotate.right`, assert the output still has 2 frames (or the operation is refused with a clear message). Today the output has 1 frame. | [code] `src/FileAction.Core/Backends/ImageBackend.cs:15-19` ("Only the first frame"); `.../Tools/Image/EditPhotosTool.cs:15-18` states the rule the other tools break; `ImageBackend.cs:94-112` (edits) and `.../Compress/CompressRunner.cs:32-40` use a single `MagickImage`. The sample `images/animated.gif` has 8 frames (probed 2026-10-20), so a tool test can prove the flattening directly. | Either process all frames (MagickImageCollection) or reject GIF/animated WebP/multi-page TIFF in those tools the way Edit Photos does; add the frame-count test. |
| F-12 | P2 | **Lock loses files with the same name when several are dropped on one folder.** Lock protects each item to `LockPaths.TargetPath(source, isDirectory, outputDirectory)`; two `photo.jpg` from different folders with drop folder B both target `B\photo.dwenc`; the first succeeds, the second fails with "target exists". No unique suffix/plan exists for this case (Unlock has a collision suffix, Protect disambiguates archive entries). | `dotnet test --filter "FullyQualifiedName~LockDuplicateNames"` — two same-named files from different folders with drop folder B; assert both containers are produced with distinct names, or the operation refuses before changing anything. Today the first is locked and the second errors (partial batch). | [code] `src/FileAction.Core/Lock/LockService.cs:104-127`; target naming `src/FileAction.Core/Lock/LockPaths.cs`; loop in `src/FileAction.App/UI/Tools/Lock/LockTool.cs:384-401`; positive collision test only for Unlock `tests/FileAction.Tests/LockSafetyTests.cs` (`Unlock_uses_a_restored_suffix_on_collision`) | Plan all targets up front, disambiguate duplicate container names, and only then encrypt; add the test. |
| F-13 | P2 | **`pack` writes duplicate ZIP entries for same-named files.** `ArchiveBackend.PackAsync` names each file by `Path.GetFileName`; two `photo.jpg` from different folders become two `photo.jpg` entries in one ZIP. SharpCompress/`ZipArchive` accepts them, and extraction overwrites one, so one input is lost. Protect solved the same problem with `ProtectedZipPlanBuilder.Unique`. | `dotnet test --filter "FullyQualifiedName~PackDuplicateNames"` — pack two same-named files from different folders, reopen the archive, assert two distinct entries and both payloads. Today the entry names duplicate. | [code] `src/FileAction.Core/Backends/ArchiveBackend.cs:125-143`; comparison `src/FileAction.Core/PasswordProtection.cs:339-499`; extraction overwrite `ArchiveBackend.cs:13` | Disambiguate entry names exactly like `ProtectedZipPlanBuilder`; add the archive-round-trip test. |
| F-14 | P3 | **Document→Markdown media folder is not unique.** Pandoc writes extracted images into `<output dir>\<source> - media`; the Markdown file gets a unique name but the media folder does not. Two different sources with the same base name converted to the same folder share/overwrite the media folder; a repeated conversion after an edit can mix old images. | `dotnet test --filter "FullyQualifiedName~PandocMediaCollision"` — convert two `report.docx` from different folders to the same output folder; assert two distinct media folders and no overwritten image. | [code] `src/FileAction.Core/Backends/PandocBackend.cs:49-54` | Make the media folder unique per output (same suffix as the Markdown name, or a GUID); add the test. |
| F-15 | P2 | **OCR multi-select leaves earlier `.txt` files when a later item fails, and reports nothing about the ones it wrote.** The loop writes each item's text and only throws at the end; there is no per-item catch/partial result like `PerFileOperation`. A batch of [good, corrupt] creates a good `.txt` and shows only an error. | `dotnet test --filter "FullyQualifiedName~OcrBatch"` — OCR a text image and a corrupt image; assert one output plus a warning naming the failed file, or no output and a clear error (the chosen contract). Today: one output, exception, no result list. | [code] `src/FileAction.App/Ocr/OcrOperation.cs:63-101` | Process like `PerFileOperation`: collect successes, list failures in `OperationResult.Message`; add the test. |
| F-16 | P2 | **OCR has no automated test at all.** There is no test file or fact containing `Ocr`; the shipped OCR tool's only possible evidence is a Windows desktop engine. Image/PDF recognition, page join, no-text error, language absence and the clipboard note are unverified. | `dotnet test --filter "FullyQualifiedName~Ocr"` currently runs 0 tests; add tests for a generated text PNG, a text PDF, a multi-page PDF, a blank image (`error.ocr.noText`), corrupt input and a batch. | [code] no `Ocr` match under `tests/FileAction.Tests`; tool `src/FileAction.App/Ocr/OcrOperation.cs:24-141`; CI has a Windows runner and the OCR language pack is normally present | Add the OCR integration tests (skip only when `OcrEngine.AvailableRecognizerLanguages` is empty, and fail the skip path on the CI image). |
| F-17 | P2 | **Audio Channels, Bleep and Visualizer have no end-to-end file test.** Their filters/argument builders are unit-tested (`AudioEffectsTests`), but nothing renders a real file and probes channels/duration/audio. A broken filter graph would pass CI. | `dotnet test --filter "FullyQualifiedName~AudioChannels"`/`~AudioBleep`/`~AudioVisualizer` currently runs only unit facts, no function test; add function tests that write and probe the output. | [code] `tests/FileAction.Tests/AudioEffectsTests.cs` (filters only); `tests/FileAction.Tests/AudioFunctionTests.cs` covers Normalize/Trim only; writers `.../Audio/AudioChannelsTool.cs:261`, `AudioBleepTool.cs:495`, `AudioVisualizerTool.cs:285` | Add function tests: channels output channel count/volumes, bleep silence+tone, visualizer dimensions/duration/audio. |
| F-18 | P2 | **Crop has no end-to-end renderer test.** Only `CropMath` is tested; `CropRunner.Apply` (the method that opens the original, auto-orients, crops at original pixels and writes) is never executed by CI. The matrix therefore cannot claim the shipped file output is correct. | `dotnet test --filter "FullyQualifiedName~CropRunner"` currently runs 0 tests; add a test on a 400×300 PNG with a known rectangle: assert exact output size, pixel colour inside/outside, source hash unchanged and unique second name. | [code] `src/FileAction.App/UI/Tools/Image/CropTool.cs:446-464`; only `tests/FileAction.Tests/CropMathTests.cs` exists | Add the CropRunner function test and make it part of the Build + test job. |
| F-19 | P2 | **LibreOffice-backed conversions are never exercised in CI.** The test job installs FFmpeg and Pandoc only; `Every_operation_produces_output` skips every operation whose `Requires` includes LibreOffice. Slides, PDF→PPTX/XLSX, DOC/DOCX/PPTX/ODP→PDF and XLS/ODS→anything therefore have no executed test on `origin/main`. | `dotnet test --filter "FullyQualifiedName~Every_operation_produces_output"` on the CI image: LibreOffice is absent, so those operations are not in `Catalog.For`; add `choco install libreoffice-fresh` (or a dedicated job) and per-family samples. | [code] `.github/workflows/ci.yml:118-129` (no LibreOffice), `tests/FileAction.Tests/IntegrationTests.cs:200-237` (only offered operations run; missing tool = skipped); `Catalog.cs` `Requires = [ExternalTool.LibreOffice]` | Install LibreOffice in CI and add PPT/PPTX/ODP/XLSM/XLS/ODS fixtures with content probes. |
| F-20 | P2 | **Conversion tests assert only "file exists and is non-empty", not content.** `Every_operation_produces_output` checks existence and `Length > 0`; it never decodes the output, checks dimensions/pages/duration/text or source hash. A backend that writes a valid but wrong file (wrong format, one page, zero duration, recompressed garbage) passes. | Extend `IntegrationTests.Every_operation_produces_output` (or add `ConversionContentTests`) to probe each family: image dimensions/format/colour, audio duration/codec, video duration/streams, PDF pages/text, document text, spreadsheet cells, subtitle cues, archive tree. Today lines 211-219 only check existence/length. | [code] `tests/FileAction.Tests/IntegrationTests.cs:211-219`; no content assertions; `TestData.ApplyFixtures` maps only png/jpg/pdf/md/csv/srt/xlsx/docx/pptx/mp4/mp3/gif/zip/readonly (`tests/FileAction.Tests/TestData.cs:51-80`); the extracted `_local/samples` adds webp/bmp/tiff, flac/ogg/m4a, mov/webm, odt/rtf and PDF page-count fixtures but no test reads that pack | Add the family probes and the missing fixtures; make source SHA-256 part of the same loop. |
| F-21 | P2 | **The exact drag flows in the task cannot be executed in this environment, and CI has no substitute.** `_local/samples` now contains DragDrop_A/B, but `tools/make-samples.ps1` is absent from all history and no test drives a real OLE drag/key tap. Every "A→B / A→A" execution row is therefore blocked; only the wiring below is code-verified. | Add a Windows UI automation job (or a `--self-test` drag harness) that performs Shift/Ctrl/Alt taps and drops; the job must assert folder, names, content, source hashes and screenshots. | [blocked] no `tools/make-samples.ps1` in `git log --all`; sample pack exists but no test consumes it; CI `.github/workflows/ci.yml` has no drag test; `GlobalInput` requires low-level hooks | Add the UI job; until then drag rows are FAIL by evidence, not PASS by assumption. |
| F-22 | P3 | **The wheel's "save to" label can name a folder the drop will not use.** `SetDestination` updates only the displayed text/tooltip; the `DropTargets` record captured when the wheel opened is what `Dispatch` passes to the job/tool. A folder observed by `SampleDrag` after that point is shown but never applied. | `dotnet test --filter "FullyQualifiedName~WheelDestination"` — after `SetDestination(B)` and `Dispatch`, assert the execution receives B; today it receives the old record. | [code] `src/FileAction.App/Wheel/WheelWindow.cs:45,90,664-670,1038-1045`; `src/FileAction.App/Wheel/WheelController.cs:128-150` (label) vs `:250-252` (dispatch) | Update the targets used by `Dispatch` when `SetDestination` changes the effective folder (or make the label read-only); add the test. |
| F-23 | P2 | **Two tool content paths have no end-to-end test at all:** PDF compression (`CompressRunner.PdfAsync` → `PdfBackend.Optimize`) and audio/video metadata writing (`MetadataRunner.WriteMediaAsync` → `ffmpeg -c copy` with tags/chapters). Tool tests cover image/PDF metadata and image/video/audio compression, but not these two, so a regression there passes CI. | `dotnet test --filter "FullyQualifiedName~CompressPdf"` and `--filter "FullyQualifiedName~MetadataMedia"` currently run 0 tests; add a PDF→PDF compress test (page count/text still readable) and a media tag/chapter write-and-re-read test. | [code] `src/FileAction.App/UI/Tools/Compress/CompressRunner.cs:45-51`; `src/FileAction.App/UI/Tools/Meta/MetadataRunner.cs:263-292`; no matching tests in `tests/FileAction.Tests` | Add the two function tests and make them part of the Build + test job. |

## 2. Wheel dispatch and drag-and-drop

The wheel has three modes and two ways to open: the drag gesture (`WheelMode.Drag`, no focus) and the
hotkey/context-menu/click mode (`WheelMode.Click`). Shift=Convert, Ctrl=Tools, Alt=Actions is the
default mapping and can be reassigned in Settings (`AppSettings.ConvertKey`/`ToolsKey`/`ActionsKey`).

| ID | Check | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| W-01 | Operation inventory | Every operation class that ships is in `BuildOperations()` or `FileActions.Build()` | PASS | [test] `DragDropTests.Every_registered_tool_window_is_reachable_from_the_wheel_by_its_own_id` on `4f23c9c`: 26/26 registered windows offered by id; [code] `WheelController.cs:88-118` now lists `AnnotateOperation` and `RedactOperation` (F-01 fixed upstream) |
| W-02 | No duplicate operation ids | `Catalog.Build()` ids are unique; tool ids are unique by registration guard | PASS | [test] `UnitTests.Catalog_ids_are_unique`; [code] `ToolRegistry.Register` dedupes |
| W-03 | Shift tap while dragging opens Convert | `GlobalInput` raises `ModifierTapped(Shift)` only for a press+release with no other key and only while `_dragging`; `WheelFor(Shift)` → Convert | PASS | [code] `GlobalInput.cs:143-177`, `WheelController.cs:167-181`, `AppSettings.cs` key map; [blocked] no hook/desktop execution (F-21) |
| W-04 | Ctrl tap while dragging opens Tools | Same path, `WheelFor(Ctrl)` → Tools | PASS | [code] `GlobalInput.cs:114-120`, `WheelController.cs:167-181`; [blocked] execution (F-21) |
| W-05 | Alt tap while dragging opens Actions | Same path; bare Alt is neutralised so the menu bar does not activate | PASS | [code] `GlobalInput.cs:158-163`, `WheelController.cs:167-181`; [blocked] execution (F-21) |
| W-06 | A→B drop writes into B | The folder under the cursor/remembered when the wheel opened is resolved through `OutputTarget.Resolve`; a writable, different folder is used | PASS | [code] `WheelController.cs:202-224,250-252`; `OutputTarget.cs:49-77`; [test] `OutputTargetTests.Uses_a_writable_folder_that_differs_from_the_source_folder`; [blocked] real drag (F-21) |
| W-07 | A→A drop writes next to the file | Detected folder equal to the source folder is `SameAsSource` and falls back to the source folder; no self-overwrite | PASS | [test] `OutputTargetTests.Keeps_next_to_the_file_when_the_folder_is_the_source_folder`, `Never_writes_into_the_source_folder_even_when_it_is_detected_again`; [code] `OutputTarget.cs:64-69`; [blocked] real drag (F-21) |
| W-08 | Remembered folder after crossing | The last writable folder seen during the drag is used when the cursor is back over the source/wheel | PASS | [test] `DropFolderMemoryTests.Remembers_the_last_usable_folder_of_the_drag`, `Keeps_the_previous_folder_when_the_next_one_is_missing_or_read_only`; [code] `WheelController.cs:128-150`; [blocked] real drag (F-21) |
| W-09 | Drop on the hub cancels | `HitTest` returns no slice inside the inner radius; `OnDrop` finishes without executing and no file is written | PASS | [code] `WheelWindow.cs:556-583,683-700`; [blocked] real drag (F-21) |
| W-10 | Release outside the wheel cancels | `OnDragEnded` starts a 500 ms close timer; no drop → `Finish`, no execution | PASS | [code] `WheelWindow.cs:385-391`, `WheelController.cs:66-68`; [blocked] real drag (F-21) |
| W-11 | Switch wheel while open | Tapping another configured modifier calls `SwitchTo`; Tab cycles; Shift+Tab steps back; the repeat flag resets | PASS | [code] `WheelController.cs:167-181`, `WheelWindow.cs:265-290,617-629`; [blocked] real key execution (F-21) |
| W-12 | Repeat the last action (R / same key) | The last operation, with its saved parameters, runs again for the new items | FAIL | [code] repeat is wired (`WheelWindow.cs:276-290,630-632,664-670`) but interactive tools are never remembered (`JobRunner.cs:206-211` is the only writer; `WheelController.RunOperation` does not call it) → F-03 |
| W-13 | Drop target label matches the folder used | The "Save to …" text names the folder the job will write into | FAIL | [code] label updates only via `SetDestination` (`WheelWindow.cs:1038-1045`), dispatch uses the snapshot (`:664-670`) → F-22 |
| W-14 | Multi-select is handled per operation | Per-file conversions process each file; batch tools receive all items; a single-file tool is not offered for two files | PASS | [test] `PerFileOperation` behaviour covered by `IntegrationTests.Every_operation_produces_output`; [code] `PerFileOperation.AppliesTo/ExecuteAsync`, `BatchOperation.AllOf`; [blocked] real drop (F-21) |
| W-15 | Folder input is offered exactly where supported | `pack.zip` accepts folders; `Protect` accepts one folder; `Lock` accepts folders; every other tool refuses/does not offer a folder | PASS | [code] `Catalog.cs` `pack` `_ => true`, `PerFileOperation.AcceptsDirectories` default false; `ProtectTool.Applies`; `LockTool.Applies`; [blocked] real drop (F-21) |
| W-16 | Drag from `_local/samples` DropA to DropB / A→A, all three wheels | The exact task flow runs and leaves verified results in B / next to A | FAIL | [blocked] `_local/samples/DragDrop_A/B` exist, but `DragDropTests` uses the empty `_local/samples/afunc` path on a workstation and no desktop/UI automation exists; test-only commits `dda1ba9`/`286d664`/`13555b9`/`b126ae8` add `tests/FileAction.Tests/DragDropTests.cs`, which covers A→B copy/move, A→A, collisions, multi+folder pack, dispatch-by-shape, cancel and non-file drops in CI (25/25 green, run [39376742559](https://github.com/Faisaldhd/dropwheel/actions/runs/39376742559)), but it performs no real OLE drag or modifier-key tap (F-21) |
| W-17 | Actions wheel opens and runs its six actions | copy, move, copy path, terminal, float, share are offered and run | PASS | [code] `src/FileAction.App/Actions/FileActions.cs:15-28`; details in §4.7; [blocked] real drag (F-21) |

## 3. Tools — image

Rows are the same 14 checks for every tool so the matrix is comparable. `T2` enumerates every format the
tool accepts; a format that is handled by the same generic backend is `PASS` by code, and the evidence
cell says when no per-format fixture exists. `T8` is the detected-drop-folder flow; the Settings-folder
flow is the shared F-05 failure and is assessed once in §5.1, not repeated in every tool table.

### 3.1 Add Background — `tools.addbackground`

One still image; output `<name> With Background.png`; the source is only read.

| ID | Check | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| T1 | Identity / reachability | `tools.addbackground`, title "Add BG", primary "Save with Background"; offered on Tools for one supported image | PASS | [test] `AddBackgroundTests.The_window_and_primary_labels_resolve_in_both_languages`, `The_tool_accepts_one_photo_and_rejects_other_inputs`; [code] `AddBackgroundOperation.cs:12-29`, `AddBackgroundTool.cs:39-43` |
| T2 | Formats | jpg, png, webp, avif, tiff, bmp, ico, gif accepted; heic, svg, pdf, two images, empty list and folders rejected | PASS | [test] `The_tool_accepts_one_photo_and_rejects_other_inputs` (jpg/png/pdf/two/empty); [code] `AddBackgroundTool.cs:15-16`; webp/avif/tiff/bmp/ico/gif share the Magick read/write path — no per-format fixture in CI |
| T3 | Orientation / size | EXIF orientation applied before the composition; 1200² and 4096 px sources measured; export frame capped at 64 MP and 16000 px; photo pixels preserved | PASS | [test] `Auto_keeps_the_padded_square...`, `Slider_values_are_pixels...`, `The_export_scale_reproduces...`, `A_huge_photo_keeps_its_pixels_but_the_frame_is_capped`; [code] `AddBackgroundRenderer.cs:215-222` (`AutoOrient`), `AddBackground.cs:151-174` |
| T4 | Content verification | Real output PNG: exact 360×240/400×225 sizes, backdrop/photo pixels, rounded corner, alpha, image background cover-fit | PASS | [test] `Render_writes_the_composed_size...`, `Render_rounds_the_corners...`, `Render_crops_the_composition...`, `Render_covers_the_canvas...`, `Render_keeps_alpha...` |
| T5 | Source SHA-256 unchanged | The source is opened read-only and never written; source bytes/hash unchanged after save | PASS | [code] `AddBackgroundRenderer.cs:51` creates a new canvas, `:61` writes only `target`; no source write in the file; no explicit hash test |
| T6 | Unique names on repeat | First save `photo With Background.png`, second `photo With Background (2).png`, never overwrite | PASS | [test] `A_second_save_gets_a_numbered_name_and_never_overwrites`; [code] `AddBackgroundView.cs:588-592` uses `ProduceAsync` |
| T7 | Same-folder flow | No drop target → output next to the source | PASS | [code] `AddBackgroundView.cs:661-675`; `ToolOutputFolder` fallback |
| T8 | Cross-location drop A→B | Drop folder B is used | PASS | [code] `AddBackgroundView.cs:661-675` resolves `OutputTarget`; `AddBackgroundOperation` passes `DropTargets`; [blocked] no real drag (F-21) |
| T9 | Multi-select | Exactly one image; two images are not offered | PASS | [test] `The_tool_accepts_one_photo_and_rejects_other_inputs`; [code] `AddBackgroundTool.cs:39-40` |
| T10 | Folder input | A folder is not offered | PASS | [code] `AddBackgroundTool.Applies` requires `!IsDirectory` |
| T11 | Corrupt / zero-byte input | Clear Magick error; no output and no `.part` file | PASS | [code] `AddBackgroundRenderer.Load` throws before `ProduceAsync` creates the target; `OutputPaths.ProduceAsync` deletes the temp on failure; [test] `Produce_cleans_up_on_failure` |
| T12 | Wrong-extension input | Content is sniffed by Magick, so png bytes named `.jpg` still compose correctly | PASS | [code] `new MagickImage(path)` decodes by content; no extension switch in `AddBackgroundRenderer` |
| T13 | Read-only input / read-only output | Read-only source works; a read-only output folder fails with a clear error and no file | PASS | [code] `ProduceAsync` creates a temp in the output folder and deletes it in `finally`; `ToolWindow.ApplyAsync` shows the error; no partial target |
| T14 | In-use input / clear error | A file held exclusively fails with a readable error and no output | PASS | [code] Magick opens read-only; the exception reaches `ToolWindow.ApplyAsync`; no output path was created |

### 3.2 Collage — `tools.collage`

Two or more images; output `Collage.png`/`Collage.jpg`; source files only read.

| ID | Check | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| T1 | Identity / reachability | `tools.collage`, "Create Collage", offered for 2+ images | PASS | [test] `CollageTests.Primary_label_is_create_collage_in_both_tables`; [code] `CollageOperation.cs:15-33`, `WheelController.cs:95` |
| T2 | Formats | jpg, png, webp, heic, tiff, avif, bmp, gif, svg, ico accepted; one image, a folder or a non-image rejected | PASS | [code] `CollageTool.cs:44-46` uses `Formats.Images`; [test] `Renderer_*` uses png/jpg; heic/svg/gif share the Magick path but have no fixture |
| T3 | Orientation / size | Each source auto-oriented; output presets 1080², 1920×1080, 1080×1920, clamped 64..8192; partial grid row centred | PASS | [test] `Renderer_honours_every_output_preset...`, `Grid_uses_near_square_columns...`, `Presets_are_the_three_shipped_output_sizes`, `Output_size_is_clamped...`; [code] `CollageRenderer.cs:44-46` |
| T4 | Content verification | Grid cells/background gap pixels, layouts, cover/fit placements, corner radius, output dimensions | PASS | [test] `Renderer_places_the_images_in_the_grid_cells...`, `Renderer_honours_every_output_preset...`, `Every_layout_places_all_images_inside_the_canvas_without_overlap` |
| T5 | Source SHA-256 unchanged | Both sources keep SHA-256 and last-write time | PASS | [test] `Renderer_honours_every_output_preset...` lines 447-476; [code] `CollageRenderer.Write` only reads inputs |
| T6 | Unique names on repeat | First `Collage.png`, second `Collage (2).png`; first untouched | PASS | [test] `Renderer_writes_the_output_next_to_the_first_file_and_never_overwrites`; [code] `CollageRenderer.RenderAsync` uses `ProduceAsync` |
| T7 | Same-folder flow | No drop target → next to the first input | PASS | [code] `CollageView.cs:479-481`; documented as the batch fallback |
| T8 | Cross-location drop A→B | Drop folder B is used | PASS | [test] `CollageTests.Drop_target_folder_receives_the_collage_and_the_source_folder_stays_untouched`; [code] `CollageTool.Create` passes `DropTargets` and `CollageView.OutputDirectoryFor` resolves the drop folder (fixed upstream on `origin/main`) |
| T9 | Multi-select | 2+ images accepted; order is the drop order with drag reorder | PASS | [test] `Order_starts_in_drop_order_and_moves_positions`; [code] `CollageTool.Applies` |
| T10 | Folder input | A folder is not offered | PASS | [code] `CollageTool.Applies` requires every item not a directory |
| T11 | Corrupt / zero-byte input | A corrupt source fails before the output is moved; no partial collage | PASS | [code] `CollageRenderer.Write` throws inside `ProduceAsync`'s producer, temp deleted; [test] `Produce_cleans_up_on_failure` |
| T12 | Wrong-extension input | Magick content sniff; png bytes named `.jpg` still render | PASS | [code] `new MagickImage(paths[i])` decodes by content |
| T13 | Read-only input / output | Read-only sources read fine; a read-only destination fails via `ProduceAsync` with no file | PASS | [code] `OutputPaths.ProduceAsync` temp+finally; [test] `Produce_cleans_up_on_failure` |
| T14 | In-use input / clear error | An exclusively-held source raises a readable error; no output | PASS | [code] Magick load fails before the final move; `ProduceAsync` cleans the temp |


### 3.3 Edit Photos — `tools.editphotos`

One still image; output `<name> Edited.<ext>`; neutral settings produce a byte copy.

| ID | Check | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| T1 | Identity / reachability | `tools.editphotos`, "Save Edited Copy", offered for one still image | PASS | [test] `PhotoEditTests.The_tool_opens_for_one_still_image_only`, `Every_tool_string_exists_in_both_tables`; [code] `EditPhotosOperation.cs:11-31`, `WheelController.cs:94` |
| T2 | Formats | jpg, png, webp, heic, tiff, avif, bmp accepted; gif, ico, svg, two files and folders rejected | PASS | [test] `The_tool_opens_for_one_still_image_only`; [code] `EditPhotosTool.cs:15-18` |
| T3 | Orientation / size | EXIF orientation applied; 24 MP preview ≤500 ms and full save measured; portrait/landscape window sizing | PASS | [test] `The_window_opens_at_about_500_by_745...`; [code] `EditPhotoRenderer.cs:23-38,80-84`; existing measurements in the 2.4.0 evidence (6000×4000: 347 ms preview, 13.8 s all-slider JPEG) |
| T4 | Content verification | Neutral output is byte-identical to the source; a real edit changes pixels (RMSE > 0.01); unique copy | PASS | [test] `Final_render_is_a_byte_copy_at_neutral_and_differs_after_an_edit` |
| T5 | Source SHA-256 unchanged | The source is only read; neutral path copies bytes, edit path opens a new image | PASS | [code] `EditPhotoRenderer.RenderFinal` (`File.Copy`/new `MagickImage`); no write to `item.Path`; no explicit hash test |
| T6 | Unique names on repeat | `photo Edited.png`, then `photo Edited (2).png` | PASS | [test] `Final_render_is_a_byte_copy_at_neutral_and_differs_after_an_edit`; [code] `OutputPaths.Unique` |
| T7 | Same-folder flow | No drop target → source folder | PASS | [code] `EditPhotosView.cs:516,521-533` |
| T8 | Cross-location drop A→B | Drop folder B is used | PASS | [code] `EditPhotosView.cs:521-533`; `EditPhotosTool.Create` passes `targets`; [blocked] real drag (F-21) |
| T9 | Multi-select | Exactly one image; two images rejected | PASS | [test] `The_tool_opens_for_one_still_image_only` |
| T10 | Folder input | Not offered | PASS | [code] `EditPhotosTool.Applies` requires `!IsDirectory` |
| T11 | Corrupt / zero-byte input | Magick error before the target path is written; no partial file | PASS | [code] `RenderFinal` loads before choosing the target; `Unique` does not create the file |
| T12 | Wrong-extension input | Content sniff by Magick; extension only selects the output encoder | PASS | [code] `ImageBackend.ToMagickFormat(item.Format)`; `new MagickImage(item.Path)` |
| T13 | Read-only input / output | Read-only source works; a failed write to the final path can leave a partial file because there is no `.part`/rollback | FAIL | [code] `EditPhotoRenderer.cs:61-72` (`Unique` then `File.Copy`/`image.Write`); F-06 |
| T14 | In-use input / clear error | Exclusive holder raises a readable Magick/IO error before any write | PASS | [code] load precedes target creation; no partial file for a read failure |

### 3.4 Crop — `tools.crop`

One still image; output `<name> Cropped.<ext>` at the original resolution.

| ID | Check | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| T1 | Identity / reachability | `tools.crop`, offered for one supported image; opens by identity | PASS | [test] `CropMathTests.ToolLayout_clamps...`; [code] `CropOperation.cs:24-28`, `WheelController.cs:92` |
| T2 | Formats | jpg, png, webp, avif, tiff, bmp, ico, gif accepted; heic, svg, pdf, two images, folders rejected | PASS | [code] `CropTool.cs:26-36`; formats share the Magick path; only png/jpg are exercised by tests |
| T3 | Orientation / size | EXIF orientation applied for both preview (≤2048 px) and full-resolution crop; 8 handles and W/H boxes stay inside the image | PASS | [code] `CropTool.cs:418-443,449-458`; [test] `CropMathTests.SnapToRatio_*`, `Drag_*`, `FromPixels_is_clamped...` |
| T4 | Content verification | A real crop writes the exact requested pixel rectangle at the original resolution and leaves pixels outside it untouched | FAIL | [test] only `CropMathTests` (geometry) exists; `CropRunner.Apply` is never executed in CI; [code] `CropTool.cs:446-464` → F-18 |
| T5 | Source SHA-256 unchanged | The source is opened read-only; only the new target is written | PASS | [code] `CropRunner.Apply` reads `item.Path`, writes `target`; no source write |
| T6 | Unique names on repeat | `photo Cropped.png`, then `photo Cropped (2).png` | PASS | [code] `CropTool.cs:461` uses `OutputPaths.Unique`; no explicit test |
| T7 | Same-folder flow | No drop target → source folder | PASS | [code] `CropTool.cs:403-416` |
| T8 | Cross-location drop A→B | Drop folder B is used | PASS | [code] `CropTool.cs:403-416`; `targets` passed from `CropOperation`; [blocked] real drag (F-21) |
| T9 | Multi-select | Exactly one image; two images rejected | PASS | [code] `CropTool.Applies` |
| T10 | Folder input | Not offered | PASS | [code] `CropTool.Applies` requires `!IsDirectory` |
| T11 | Corrupt / zero-byte input | Magick load fails before the target is created; no output | PASS | [code] `CropRunner.Apply` line 451 `new MagickImage` precedes `Unique`/`Write` |
| T12 | Wrong-extension input | Content sniff; png bytes named `.jpg` crop correctly and are written as JPEG | PASS | [code] `new MagickImage(item.Path)`; extension only for the target name |
| T13 | Read-only input / output | Read-only source works; a failed final write leaves a partial file (no `.part`) | FAIL | [code] `CropTool.cs:461-462`; F-06 |
| T14 | In-use input / clear error | Exclusive holder fails at load before any output | PASS | [code] load precedes target; no partial file |

### 3.5 Annotate — `tools.annotate`

One still image; flattened copy at original resolution.

| ID | Check | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| T1 | Identity / reachability | `tools.annotate` is offered on Tools for one image | PASS | [test] `DragDropTests.Every_registered_tool_window_is_reachable_from_the_wheel_by_its_own_id` (26/26, CI run [39376742559](https://github.com/Faisaldhd/dropwheel/actions/runs/39376742559)); [code] `AnnotateTool.cs:49-79` registers, and `4f23c9c` added `AnnotateOperation` to `WheelController.BuildOperations` (F-01 fixed upstream) |
| T2 | Formats | jpg, png, webp, avif, tiff, bmp, ico, gif accepted; heic/svg and multiple images rejected | PASS | [code] `AnnotateTool.cs:33-43`; no per-format fixture |
| T3 | Orientation / size | EXIF orientation applied for preview and export; 1600 px preview / original-size export; pathological sizes reported | PASS | [code] `AnnotateRunner.cs:31-60` (`AutoOrient`), `AnnotationRenderer` export; [test] `AnnotationTests` geometry/flatten unit tests |
| T4 | Content verification | Marks render at original resolution; arrows/shapes/text/stamps flatten in z-order; source unchanged | FAIL | [test] `AnnotationTests` cover the pure document and renderer pieces, but no test writes a real annotated file; reachability no longer blocks it (`4f23c9c`), so the remaining gap is the missing renderer test → F-01 follow-up |
| T5 | Source SHA-256 unchanged | Only read | PASS | [code] `AnnotateRunner.Load`/`MagickImage`; target is separate |
| T6 | Unique names on repeat | `<name> Annotated.<ext>`, then `(2)` | PASS | [code] `AnnotateRunner.cs:88` uses `Unique`; no test |
| T7 | Same-folder flow | No drop target → source folder | PASS | [code] `AnnotateView.cs:605-620` |
| T8 | Cross-location drop A→B | Drop folder B is used | PASS | [code] `AnnotateView.cs:605-620`; `AnnotateOperation` passes targets (but unreachable) |
| T9 | Multi-select | Exactly one image | PASS | [code] `AnnotateTool.Applies` |
| T10 | Folder input | Not offered | PASS | [code] requires `!IsDirectory` |
| T11 | Corrupt / zero-byte input | Load fails before target; no output | PASS | [code] `Load` precedes `Unique`/`Write` |
| T12 | Wrong-extension input | Content sniff by Magick | PASS | [code] `AnnotateRunner.Load` |
| T13 | Read-only input / output | Read-only source works; a failed final write leaves a partial file | FAIL | [code] `AnnotateRunner.cs:88-89`; F-06 |
| T14 | In-use input / clear error | Exclusive holder fails at load before output | PASS | [code] load precedes target |

### 3.6 Redact — `tools.redact`

One still image; areas flattened, metadata stripped; output `<name> Redacted.<ext>`.

| ID | Check | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| T1 | Identity / reachability | `tools.redact` offered on Tools for one image | PASS | [test] `DragDropTests.Every_registered_tool_window_is_reachable_from_the_wheel_by_its_own_id` (26/26, CI run [39376742559](https://github.com/Faisaldhd/dropwheel/actions/runs/39376742559)); [code] `RedactTool.cs:73-88` registers, and `4f23c9c` added `RedactOperation` to `WheelController.BuildOperations` (F-01 fixed upstream) |
| T2 | Formats | jpg, png, webp, avif, tiff, bmp, ico, gif accepted; svg/heic and multiple images rejected | PASS | [code] `RedactTool.cs:25-32,56-57` |
| T3 | Orientation / size | EXIF orientation applied for preview and export; 2048 px preview; boxes clamp to the image | PASS | [code] `RedactRunner.cs:26-50,54-58`; [test] `RedactionTests` geometry/clamp |
| T4 | Content verification | Solid fill exact colour, blur/pixelate only inside the box, outside pixels identical, metadata stripped, output dimensions unchanged | PASS | [test] `RedactOutputTests.Redact_apply_fills...`, `...blurs_only...`, `...pixelates_only...`, `...strips_metadata...` |
| T5 | Source SHA-256 unchanged | Source byte-identical after Apply | PASS | [test] `RedactOutputTests.Redact_apply_leaves_the_source_byte_identical` |
| T6 | Unique names on repeat | Unique name; never overwrite | PASS | [test] `Redact_apply_writes_a_unique_name_and_never_overwrites` |
| T7 | Same-folder flow | No drop target → source folder | PASS | [code] `RedactView` output helper; `RedactRunner.Apply` receives directory |
| T8 | Cross-location drop A→B | Drop folder B is used | PASS | [code] `RedactTool.Create` passes targets; view resolves `OutputTarget` (but unreachable) |
| T9 | Multi-select | Exactly one image | PASS | [code] `RedactTool.Applies` |
| T10 | Folder input | Not offered | PASS | [code] requires `!IsDirectory` |
| T11 | Corrupt / zero-byte input | Load fails before target; no output | PASS | [code] `new MagickImage` before `Unique`/`Write` |
| T12 | Wrong-extension input | Content sniff by Magick | PASS | [code] `RedactRunner.Apply` |
| T13 | Read-only input / output | Read-only source works; failed final write leaves a partial file | FAIL | [code] `RedactRunner.cs:65-66`; F-06 |
| T14 | In-use input / clear error | Exclusive holder fails at load before output | PASS | [code] load precedes target |

### 3.7 QR reader — `tools.qr`

Read-only: one image, decoded text copied by Apply; no file is written.

| ID | Check | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| T1 | Identity / reachability | `tools.qr`, "QR Code"; offered for one image | PASS | [test] `QrPayloadTests.The_tool_opens_for_one_image_only`; [code] `QrOperation.cs:25-26`, `WheelController.cs:106` |
| T2 | Formats | Every `Formats.Images` format; two images, pdf, folder rejected | PASS | [test] `The_tool_opens_for_one_image_only`; [code] `QrTool.cs:32-33` |
| T3 | Orientation / size | EXIF orientation applied; preview decoded off the UI thread | PASS | [code] `QrView.cs:410-443` |
| T4 | Content verification | Generated QR decodes to the exact URL; blank/too-small buffers return null; payload classification and safety | PASS | [test] `Decoder_reads_a_generated_qr_code`, `Decoder_reports_no_code_for_a_blank_image`, `Decoder_refuses_a_buffer_that_is_too_small`, `Web_links_are_openable`, `Non_web_schemes_never_become_links`, `Disguised_or_relative_values_are_not_links`; the file→pixels load path itself has no dedicated test |
| T5 | Source SHA-256 unchanged | No write path in the tool | PASS | [code] `QrView` never opens a file for write; Apply only copies text |
| T6 | Unique names on repeat | Not applicable: no output file | PASS | [code] no write |
| T7 | Same-folder flow | Not applicable: no output file | PASS | [code] no write |
| T8 | Cross-location drop A→B | Not applicable: no output file | PASS | [code] no write |
| T9 | Multi-select | Exactly one image | PASS | [test] `The_tool_opens_for_one_image_only` |
| T10 | Folder input | Not offered | PASS | [code] `QrTool.Applies` |
| T11 | Corrupt / zero-byte input | Invalid image shown in the preview/status, no crash, no output | PASS | [code] `QrView.ActivateAsync` catches and calls `ShowInvalid`; no write |
| T12 | Wrong-extension input | Content sniff by Magick | PASS | [code] `QrView.Load` |
| T13 | Read-only input / output | Not applicable: no write | PASS | [code] no write |
| T14 | In-use input / clear error | Read failure shown as invalid; no output | PASS | [code] `ActivateAsync` catch |

## 4. Tools — video

All seven video tools require FFmpeg. CI installs it on a best-effort basis; when the install fails the
function tests skip, so a green run is not proof that FFmpeg ran. `T2` lists the 12 accepted containers;
CI-generated fixtures cover mp4, avi and mkv only. Odd/even dimensions, portrait/landscape and rotation
metadata are asserted where the tool transforms pixels; stream-copy paths preserve rotation metadata.

### 4.1 Save Frames — `tools.videoframes`

One video; every captured frame becomes a full-resolution PNG in `<name> Frames`.

| ID | Check | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| T1 | Identity / reachability | `tools.videoframes`; one video; primary label follows the capture count | PASS | [test] `VideoEditTests.The_three_video_specs_register_themselves_and_accept_one_video`; [code] `VideoFramesTool.cs:20-33,58-72`, `WheelController.cs:96` |
| T2 | Formats | mp4, mov, mkv, webm, avi, wmv, flv, mpg/mpeg, 3gp, ts, mts accepted; one video only | PASS | [code] `Formats.Video` + `VideoFramesTool.Applies`; mp4/avi/mkv exercised |
| T3 | Orientation / size | Full-resolution frames, not preview size; rotated source reported at display size; frame stepping clamps at both ends | PASS | [test] `Save_frames_exports_full_resolution_pngs_in_one_folder`, `Frame_stepping_stays_inside_the_file`, `Probe_swaps_a_rotated_video_to_its_display_size`; [code] `VideoCommon.cs:173-220` |
| T4 | Content verification | Requested frames are written as numbered PNGs in one unique folder; PNG dimensions equal the source | PASS | [test] `Save_frames_exports_full_resolution_pngs_in_one_folder`; `VideoFramesTests.Frame_numbers_are_five_padded_western_digits`, `Capture_names_and_folder_match_the_promise` |
| T5 | Source SHA-256 unchanged | The source is only read by FFmpeg | PASS | [test] `VideoToolsFunctionTests` asserts source unchanged in its frame/redaction tests; [code] `VideoRunner.ExportFramesAsync` only `-i item.Path` |
| T6 | Unique names on repeat | A new `<name> Frames (2)` folder per export; no overwrite | PASS | [code] `VideoCommon.cs:210` `UniqueDirectory`; [test] `Capture_names_and_folder_match_the_promise` |
| T7 | Same-folder flow | No drop target → source folder | PASS | [code] `VideoCommon.VideoOutput.Folder` (`:144-159`) |
| T8 | Cross-location drop A→B | Drop folder B is used | PASS | [code] `VideoFramesView` passes `targets`; `ExportFramesAsync` receives `outputDirectory`; [blocked] real drag (F-21) |
| T9 | Multi-select | Exactly one video | PASS | [test] `The_three_video_specs...`; [code] `Applies` |
| T10 | Folder input | Not offered | PASS | [code] `Applies` requires `!IsDirectory` |
| T11 | Corrupt / zero-byte input | Clear probe error; no frames folder | PASS | [test] `VideoToolsFunctionTests.Corrupt_input_reports_an_error_and_writes_nothing`; [code] `VideoFramesView` probe error path |
| T12 | Wrong-extension input | FFmpeg content sniff; a mov named `.mp4` still decodes | PASS | [code] FFmpeg `-i` decodes by content |
| T13 | Read-only input / output | Read-only source works; a read-only destination reports the write error and leaves no folder | PASS | [test] `Read_only_output_folder_reports_an_error_and_writes_nothing` |
| T14 | In-use input / clear error | Exclusive holder fails the probe with a readable error; cancellation leaves no partial folder | PASS | [test] `Cancelling_export_mid_run_leaves_no_partial_folder`; [code] temp folder + `TryDelete` |

### 4.2 Split — `tools.videosplit`

One video; `<name> Parts` folder with numbered parts; stream copy verified and re-encoded when needed.

| ID | Check | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| T1 | Identity / reachability | `tools.videosplit`; one video | PASS | [test] `The_three_video_specs...`; [code] `VideoSplitJoinOperations.cs:12-26`, `WheelController.cs:98` |
| T2 | Formats | All `Formats.Video`; one video | PASS | [code] `VideoSplitTool.Applies`; mp4/avi/mkv exercised |
| T3 | Orientation / size | Split by time, not size; a copied part seeks/stops inside the input and re-encodes when the keyframe copy is too long; legacy containers become mp4 | PASS | [test] `VideoSplitJoinTests.A_copied_part_seeks_and_stops_inside_the_input...`, `Legacy_containers_are_re_encoded_to_mp4...`, `A_plan_for_equal_parts_has_the_requested_number_of_names` |
| T4 | Content verification | Three parts in one new folder with the requested ranges; each part's length checked against its range | PASS | [test] `VideoSplitJoinFunctionTests.Split_writes_three_parts_into_one_new_folder_with_the_requested_ranges`; [code] `VideoSplitRunners.cs:57-99` |
| T5 | Source SHA-256 unchanged | Source only read; `AssertUnchangedAsync` in the function tests | PASS | [test] `VideoSplitJoinFunctionTests.Split_writes...` |
| T6 | Unique names on repeat | New `<name> Parts (2)` folder; part names padded and unique within the folder | PASS | [code] `VideoSplitRunners.cs:19-21`; [test] `A_plan_names_the_new_folder_after_the_source` |
| T7 | Same-folder flow | No drop target → source folder | PASS | [code] `VideoSplitRunners.cs:19` (hardcoded source folder, which is the same-folder rule) |
| T8 | Cross-location drop A→B | Drop folder B is used | FAIL | [code] `VideoSplitRunners.cs:19` hardcodes `OutputTarget.SourceFolder`; `VideoSplitTool.Create` discards `targets` (`:47-48`) → F-04 |
| T9 | Multi-select | Exactly one video | PASS | [test] `The_three_video_specs...` |
| T10 | Folder input | Not offered | PASS | [code] `Applies` |
| T11 | Corrupt / zero-byte input | Clear error; no part folder | PASS | [test] `A_corrupt_input_fails_with_an_error_and_leaves_no_part_folder` |
| T12 | Wrong-extension input | FFmpeg content sniff | PASS | [code] `-i` |
| T13 | Read-only input / output | Read-only folder reports the write error and leaves nothing; read-only source works | PASS | [test] `A_read_only_folder_fails_with_the_write_error_and_leaves_nothing` |
| T14 | In-use input / clear error | Missing FFmpeg / cancel / probe failure leaves no part folder | PASS | [test] `A_missing_ffmpeg_fails...`, `Cancelling_mid_run_removes_the_part_folder_it_created` |

### 4.3 Join — `tools.videojoin`

2+ videos; `Joined.mp4`; first clip defines canvas and frame rate; silent clips get silence.

| ID | Check | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| T1 | Identity / reachability | `tools.videojoin`; 2+ videos | PASS | [test] `The_three_video_specs...`; [code] `VideoSplitJoinOperations.cs:29-45`, `WheelController.cs:99` |
| T2 | Formats | All `Formats.Video`; 2+ | PASS | [code] `VideoJoinTool.Applies`; mp4/avi/mkv exercised |
| T3 | Orientation / size | First clip's canvas/frame rate; every other clip scaled/padded; odd dimensions rounded down; audio padded/trimmed to video length | PASS | [test] `The_join_filter_uses_the_first_clips_size_and_frame_rate`, `Odd_dimensions_are_rounded_down...`, `Audio_is_padded_and_trimmed...`, `A_clip_without_audio_gets_silence_of_its_own_length` |
| T4 | Content verification | Output has first clip's canvas, keeps a silent clip silent, every clip contributes video+audio | PASS | [test] `VideoSplitJoinFunctionTests.Join_uses_the_first_clip_canvas_and_keeps_a_silent_clip_silent` |
| T5 | Source SHA-256 unchanged | Sources only read | PASS | [test] `VideoSplitJoinFunctionTests` asserts source unchanged |
| T6 | Unique names on repeat | `Joined.mp4`, then `Joined (2).mp4` | PASS | [code] `VideoJoinRunner.cs:128-145` `ProduceAsync` |
| T7 | Same-folder flow | No drop target → next to the first clip | PASS | [code] `VideoJoinRunner.cs:119` |
| T8 | Cross-location drop A→B | Drop folder B is used | FAIL | [code] `VideoJoinRunner.cs:119` hardcodes the first clip's folder; `VideoJoinTool.Create` discards `targets` (`:47-48`) → F-04 |
| T9 | Multi-select | 2+ clips; reorder by drag; remove keeps order | PASS | [test] `The_initial_order_is_the_selection_order`, `Move_reorders_without_losing_a_clip`, `RemoveAt_removes_exactly_one_clip` |
| T10 | Folder input | Not offered | PASS | [code] `VideoJoinTool.Applies` requires every item not a directory |
| T11 | Corrupt / zero-byte input | A clip the probe cannot read blocks the join with a clear error; no output | PASS | [test] `A_clip_the_probe_could_not_read_blocks_the_join`, `Joining_fewer_than_two_clips_is_refused`; function tests cover corrupt input |
| T12 | Wrong-extension input | FFmpeg content sniff | PASS | [code] probe/`-i` |
| T13 | Read-only input / output | Read-only destination reports the write error and leaves nothing | PASS | [test] `A_join_into_a_read_only_folder_fails_with_the_write_error_and_leaves_nothing` |
| T14 | In-use input / clear error | Missing FFmpeg/cancel/probe failure leaves no partial output | PASS | [test] function tests `A_missing_ffmpeg...`, cancellation |

### 4.4 Trim — `tools.videotrim`

One video; `<name> Trimmed.<container>`; copy-safe containers use stream copy; otherwise H.264/AAC.

| ID | Check | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| T1 | Identity / reachability | `tools.videotrim`; one video | PASS | [test] `VideoEditTests.The_three_video_specs...`; [code] `VideoTrimTool.cs:27-37,51-65`, `WheelController.cs:100` |
| T2 | Formats | All `Formats.Video`; one | PASS | [code] `Applies`; mp4/avi/mkv exercised |
| T3 | Orientation / size | Requested range only; copy path keeps video+audio; re-encode uses H.264/AAC; webm uses VP9/Opus; rotation metadata preserved by copy/auto-rotate by re-encode | PASS | [test] `Trim_stream_copy_args_use_start_duration_and_copy`, `Trim_reencode_args_use_h264_and_aac`, `Trim_reencode_webm_uses_vp9_and_opus`, `Trim_outputs_the_requested_range...` |
| T4 | Content verification | Output duration/audio match the requested range; source untouched | PASS | [test] `VideoFunctionTests.Trim_outputs_the_requested_range_keeps_audio_and_leaves_the_source_untouched` |
| T5 | Source SHA-256 unchanged | Source only read | PASS | [test] same function test |
| T6 | Unique names on repeat | `<name> Trimmed.<ext>`, then `(2)` | PASS | [code] `FfmpegBackend.TransformAsync` → `ProduceAsync` |
| T7 | Same-folder flow | No drop target → source folder | PASS | [code] `VideoTrimView` targets → `OutputPaths` |
| T8 | Cross-location drop A→B | Drop folder B is used | PASS | [code] `VideoOutput.Folder`/targets in `VideoTrimView`; [blocked] real drag (F-21) |
| T9 | Multi-select | Exactly one video | PASS | [test] `The_three_video_specs...` |
| T10 | Folder input | Not offered | PASS | [code] `Applies` |
| T11 | Corrupt / zero-byte input | Clear error; no partial output | PASS | [test] `Corrupt_input_reports_a_clear_error_and_leaves_no_partial_file` |
| T12 | Wrong-extension input | FFmpeg content sniff | PASS | [code] `-i` |
| T13 | Read-only input / output | Read-only folder error with no partial file | PASS | [test] `Read_only_output_folder_reports_an_error_and_leaves_no_partial_file` |
| T14 | In-use input / clear error | Cancel/missing FFmpeg leave no partial file | PASS | [test] `Cancelling_mid_run...`, `Missing_ffmpeg...` |

### 4.5 Crop — `tools.videocrop`

One video; even-sized crop rectangle; audio copied or re-encoded.

| ID | Check | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| T1 | Identity / reachability | `tools.videocrop`; one video | PASS | [test] `The_three_video_specs...`; [code] `VideoCropTool.cs:23-33,43-58`, `WheelController.cs:101` |
| T2 | Formats | All `Formats.Video`; one | PASS | [code] `Applies`; mp4/avi/mkv exercised |
| T3 | Orientation / size | Crop rectangle rounded to even pixels inside the frame; audio copied when possible, encoded otherwise; silent video stays silent | PASS | [test] `EvenCrop_rounds_to_even_pixels...`, `EvenCrop_clamps...`, `Crop_args_contain_an_even_crop_filter_and_copy_the_audio`, `Crop_without_audio_is_silent` |
| T4 | Content verification | Exact even output size, audio kept | PASS | [test] `VideoFunctionTests.Crop_writes_the_exact_even_size_and_keeps_audio` |
| T5 | Source SHA-256 unchanged | Source only read | PASS | [test] same function test |
| T6 | Unique names on repeat | `<name> Cropped.mp4` (or safe container), then `(2)` | PASS | [code] `FfmpegBackend`/`VideoCommon` `ProduceAsync` |
| T7 | Same-folder flow | No drop target → source folder | PASS | [code] `VideoCropView` targets |
| T8 | Cross-location drop A→B | Drop folder B is used | PASS | [code] targets passed; [blocked] real drag (F-21) |
| T9 | Multi-select | Exactly one video | PASS | [test] `The_three_video_specs...` |
| T10 | Folder input | Not offered | PASS | [code] `Applies` |
| T11 | Corrupt / zero-byte input | Clear error; no partial output | PASS | [test] `VideoFunctionTests.Corrupt_input_reports_a_clear_error_and_leaves_no_partial_file` |
| T12 | Wrong-extension input | FFmpeg content sniff | PASS | [code] `-i` |
| T13 | Read-only input / output | Read-only folder error with no partial file | PASS | [test] `Read_only_output_folder...` |
| T14 | In-use input / clear error | Cancel/missing FFmpeg leave no partial file | PASS | [test] `Cancelling_mid_run...`, `Missing_ffmpeg...` |

### 4.6 Speed — `tools.videospeed`

One video; speed factor clamped 0.25–8; video setpts + chained atempo.

| ID | Check | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| T1 | Identity / reachability | `tools.videospeed`; one video | PASS | [test] `The_three_video_specs...`; [code] `VideoSpeedTool.cs:23-33,43-58`, `WheelController.cs:102` |
| T2 | Formats | All `Formats.Video`; one | PASS | [code] `Applies`; mp4/avi/mkv exercised |
| T3 | Orientation / size | Factor clamped/labelled; setpts changes duration; atempo keeps pitch; chained for extremes; silent video skips the audio filter; source frame rate preserved | PASS | [test] `Atempo_chain_multiplies_to_the_requested_speed`, `Speed_args_setpts...`, `Speed_args_use_the_source_frame_rate...`, `Speed_is_clamped_and_labelled` |
| T4 | Content verification | 2× halves duration, keeps audio and pitch | PASS | [test] `VideoFunctionTests.Speed_2x_halves_the_duration_keeps_audio_and_keeps_the_pitch` |
| T5 | Source SHA-256 unchanged | Source only read | PASS | [test] same function test |
| T6 | Unique names on repeat | `<name> 2×.mp4`, then `(2)` | PASS | [code] `FfmpegBackend.TransformAsync` `ProduceAsync` |
| T7 | Same-folder flow | No drop target → source folder | PASS | [code] `VideoSpeedView` targets |
| T8 | Cross-location drop A→B | Drop folder B is used | PASS | [code] targets passed; [blocked] real drag (F-21) |
| T9 | Multi-select | Exactly one video | PASS | [test] `The_three_video_specs...` |
| T10 | Folder input | Not offered | PASS | [code] `Applies` |
| T11 | Corrupt / zero-byte input | Clear error; no partial output | PASS | [test] `VideoFunctionTests.Corrupt_input...` |
| T12 | Wrong-extension input | FFmpeg content sniff | PASS | [code] `-i` |
| T13 | Read-only input / output | Read-only folder error with no partial file | PASS | [test] `Read_only_output_folder...` |
| T14 | In-use input / clear error | Cancel/missing FFmpeg leave no partial file | PASS | [test] `Cancelling_mid_run...`, `Missing_ffmpeg...` |

### 4.7 Redact video — `tools.videoredact`

One video; solid/blur boxes gated by time ranges; `<name> Redacted.<container>`.

| ID | Check | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| T1 | Identity / reachability | `tools.videoredact`; one video | PASS | [test] `The_three_video_specs...`; [code] `VideoRedactTool.cs:19-35,56-72`, `WheelController.cs:97` |
| T2 | Formats | All `Formats.Video`; one | PASS | [code] `Applies`; mp4/avi/mkv exercised |
| T3 | Orientation / size | Boxes/times clamped inside the frame/duration; solid vs blur graph; only the ranges are affected; audio kept | PASS | [test] `VideoFramesTests.One_solid_area_is_one_drawbox`, `One_blur_area_crops_blurs_and_overlays_it`, `Rectangles_are_clamped_inside_the_frame`, `Overlapping_time_ranges...` |
| T4 | Content verification | Redaction hides only inside the time ranges and keeps audio | PASS | [test] `VideoToolsFunctionTests.Redaction_hides_only_inside_the_time_ranges_and_keeps_audio` |
| T5 | Source SHA-256 unchanged | Source only read | PASS | [test] same function test |
| T6 | Unique names on repeat | `<name> Redacted.<ext>`, then `(2)` | PASS | [code] `VideoCommon.cs:237-275` `ProduceAsync` |
| T7 | Same-folder flow | No drop target → source folder | PASS | [code] `VideoOutput.Folder` |
| T8 | Cross-location drop A→B | Drop folder B is used | PASS | [code] targets passed; [blocked] real drag (F-21) |
| T9 | Multi-select | Exactly one video | PASS | [test] `The_three_video_specs...` |
| T10 | Folder input | Not offered | PASS | [code] `Applies` |
| T11 | Corrupt / zero-byte input | Clear error; no partial output | PASS | [test] `VideoToolsFunctionTests.Corrupt_input_reports_an_error_and_writes_nothing` |
| T12 | Wrong-extension input | FFmpeg content sniff | PASS | [code] `-i` |
| T13 | Read-only input / output | Read-only folder error with no partial file | PASS | [test] `Read_only_output_folder_reports_an_error_and_writes_nothing` |
| T14 | In-use input / clear error | Cancel leaves no partial file | PASS | [test] `Cancelling_redaction_mid_run_leaves_no_partial_file` |

## 5. Tools — audio

All five audio tools require FFmpeg. Accepted formats: mp3, m4a, aac, wav, flac, ogg, opus, aiff, wma.
Function tests exercise only the two generated fixtures (mp3, wav); the other seven share the FFmpeg
decode path but have no per-format fixture (`flac` exists in `_local/testdata` but is not mapped by
`TestData.ApplyFixtures`). Output containers go through `FfmpegBackend.SafeContainer` (wma→mp3,
legacy video containers→mp4) except where noted.

### 5.1 Normalize — `tools.audionormalize`

One audio file; `<name> Normalized.<ext>`; two-pass EBU R128 loudness/true-peak.

| ID | Check | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| T1 | Identity / reachability | `tools.audionormalize`; one audio file | PASS | [test] `AudioToolsTests.Audio_tool_windows_declare_their_specs_and_open_for_audio_only`; [code] `AudioNormalizeTool.cs:17-31`, `AudioNormalizeOperation.cs:12-16`, `WheelController.cs:110` |
| T2 | Formats | All nine audio formats; one file | PASS | [code] `Formats.Audio` + `Applies`; mp3/wav exercised, the rest share the FFmpeg path |
| T3 | Orientation / size | Loudness targets -30..-5 LUFS, LRA 1..50, true peak -9..0 dB; silence/no-measurement falls back to one pass; mono/stereo and any sample rate decoded by FFmpeg | PASS | [test] `Loudness_target_*`, `Loudness_parser_*`, `Apply_filter_falls_back_to_one_pass_for_silence_or_no_measurement`, `Normalize_reaches_the_requested_loudness_and_true_peak` |
| T4 | Content verification | Measured integrated loudness/true peak reach the target; duration preserved | PASS | [test] `AudioFunctionTests.Normalize_reaches_the_requested_loudness_and_true_peak` |
| T5 | Source SHA-256 unchanged | Source only read | PASS | [test] function tests call `AssertUnchangedAsync`; [code] `AudioRunner.NormalizeAsync` writes only `tmp` |
| T6 | Unique names on repeat | `<name> Normalized.<ext>`, then `(2)` | PASS | [code] `AudioRunner.cs:113-120` `ProduceAsync` |
| T7 | Same-folder flow | No drop target → source folder | PASS | [code] `AudioRunner.OutputDirectory` (`:144-156`) |
| T8 | Cross-location drop A→B | Drop folder B is used | PASS | [code] same helper; [blocked] real drag (F-21) |
| T9 | Multi-select | Exactly one audio file | PASS | [test] `Audio_tool_windows...` |
| T10 | Folder input | Not offered | PASS | [code] `Applies` |
| T11 | Corrupt / zero-byte input | Clear error; no output | PASS | [test] `AudioFunctionTests.Corrupt_input_fails_cleanly_without_output_files` |
| T12 | Wrong-extension input | FFmpeg content sniff | PASS | [code] FFmpeg `-i` decodes by content |
| T13 | Read-only input / output | Read-only source works; an output folder that cannot be created fails cleanly with no partial | PASS | [test] `AudioFunctionTests.An_output_folder_that_cannot_be_created_fails_cleanly` |
| T14 | In-use input / clear error | Cancel/missing FFmpeg leaves no partial output | PASS | [test] `Cancel_mid_run_leaves_no_partial_output`, `Missing_ffmpeg_reports_a_clear_error_and_writes_nothing`; [code] 400 ms delay before temp cleanup in `AudioRunner.cs:176-191` |

### 5.2 Trim — `tools.audiotrim`

One audio file; `<name> Trimmed.<ext>`; waveform selection or silence auto-trim.

| ID | Check | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| T1 | Identity / reachability | `tools.audiotrim`; one audio | PASS | [test] `Audio_tool_windows...`; [code] `AudioTrimTool.cs:17-31`, `AudioTrimOperation.cs`, `WheelController.cs:111` |
| T2 | Formats | All nine; one | PASS | [code] `Applies`; mp3/wav exercised |
| T3 | Orientation / size | Selection clamped to the file; minimum duration; silence spans at both ends; fully silent file kept; time parser accepts seconds/colons/Arabic digits | PASS | [test] `Trim_range_helpers_stay_inside_the_file`, `Trim_silence_trims_only_the_edges...`, `Trim_silence_of_a_fully_silent_file_keeps_the_file`, `Time_parser_accepts_seconds_colons_commas_and_arabic_digits` |
| T4 | Content verification | Output duration equals the selected range; silence trim removes only silent edges | PASS | [test] `AudioFunctionTests.Trim_writes_the_selected_duration`, `Trim_silence_removes_only_the_silent_ends` |
| T5 | Source SHA-256 unchanged | Source only read | PASS | [test] function tests `AssertUnchangedAsync` |
| T6 | Unique names on repeat | `<name> Trimmed.<ext>`, then `(2)` | PASS | [code] `AudioRunner.cs:124-138` `ProduceAsync` |
| T7 | Same-folder flow | No drop target → source folder | PASS | [code] `AudioRunner.OutputDirectory` |
| T8 | Cross-location drop A→B | Drop folder B is used | PASS | [code] same helper; [blocked] real drag (F-21) |
| T9 | Multi-select | Exactly one audio | PASS | [test] `Audio_tool_windows...` |
| T10 | Folder input | Not offered | PASS | [code] `Applies` |
| T11 | Corrupt / zero-byte input | Clear error; no output | PASS | [test] `Corrupt_input_fails_cleanly_without_output_files` |
| T12 | Wrong-extension input | FFmpeg content sniff | PASS | [code] `-i` |
| T13 | Read-only input / output | Output folder failure is clean; read-only source works | PASS | [test] `An_output_folder_that_cannot_be_created_fails_cleanly` |
| T14 | In-use input / clear error | Cancel/missing FFmpeg leave no partial | PASS | [test] `Cancel_mid_run_leaves_no_partial_output`, `Missing_ffmpeg...` |

### 5.3 Channels — `tools.audiochannels`

One audio; mono/stereo choice, two volume sliders and solo; `<name> Mono.ext` / `<name> Stereo.ext`.

| ID | Check | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| T1 | Identity / reachability | `tools.audiochannels`; one audio | PASS | [test] `Audio_tool_windows...`; [code] `AudioChannelsTool.cs:13-39`, `AudioOperations.cs:21-38`, `WheelController.cs:112` |
| T2 | Formats | All nine; one | PASS | [code] `Applies`; no per-format fixture |
| T3 | Orientation / size | Mono folds both channels at half gain; stereo keeps sides; volume 0–200 %; solo mutes the other side; output channel count follows the choice | PASS | [test] `AudioEffectsTests.Mono_folds_both_channels_at_half_gain`, `Stereo_keeps_the_two_sides_apart`, `Volume_sliders_become_pan_gains`, `Solo_mutes_the_other_side`, `Volumes_are_clamped...`, `Channel_arguments_add_the_output_channel_count` |
| T4 | Content verification | A real output file has the chosen channel count/levels and the source duration | FAIL | [code] writer `AudioChannelsTool.cs:241-275` uses `ProduceAsync`, but no function test opens the output; only filter arguments are asserted → F-17 |
| T5 | Source SHA-256 unchanged | Source only read | PASS | [code] `ProduceAsync` writes a temp; no source write |
| T6 | Unique names on repeat | `<name> Mono.ext`/`Stereo.ext`, then `(2)` | PASS | [code] `ProduceAsync` |
| T7 | Same-folder flow | No drop target → source folder | PASS | [code] `AudioToolOutput.DirectoryFor` |
| T8 | Cross-location drop A→B | Drop folder B is used | PASS | [code] same helper; [blocked] real drag (F-21) |
| T9 | Multi-select | Exactly one audio | PASS | [test] `Audio_tool_windows...` |
| T10 | Folder input | Not offered | PASS | [code] `Applies` |
| T11 | Corrupt / zero-byte input | FFmpeg probe fails, status error, no output | PASS | [code] probe + `ProduceAsync`; no dedicated test |
| T12 | Wrong-extension input | FFmpeg content sniff | PASS | [code] `-i` |
| T13 | Read-only input / output | Output write failure cleans the temp; no partial | PASS | [code] `ProduceAsync` |
| T14 | In-use input / clear error | Read failure surfaces in the status; no output | PASS | [code] probe error path |

### 5.4 Bleep — `tools.audiobleep`

One audio; silence sections replaced by a generated tone; `<name> Bleeped.ext`.

| ID | Check | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| T1 | Identity / reachability | `tools.audiobleep`; one audio | PASS | [test] `Audio_tool_windows...`; [code] `AudioBleepTool.cs:13-41`, `AudioOperations.cs:40-56`, `WheelController.cs:113` |
| T2 | Formats | All nine; one | PASS | [code] `Applies`; no per-format fixture |
| T3 | Orientation / size | Ranges sorted/clamped/merged; overlap nudged; tone settings clamped; bleep filter mutes ranges and adds the tone there | PASS | [test] `Ranges_are_sorted_clamped_and_trimmed`, `Overlapping_and_touching_ranges_are_merged`, `Bleep_filter_mutes_the_ranges_and_adds_the_tone_there`, `Bleep_filter_sorts_ranges_and_clamps_tone_settings` |
| T4 | Content verification | A real output has silence+tone only in the chosen ranges, source duration and audio outside | FAIL | [code] writer `AudioBleepTool.cs:475-520` uses `ProduceAsync`, but only the filter graph is tested; no output file probe → F-17 |
| T5 | Source SHA-256 unchanged | Source only read | PASS | [code] `ProduceAsync` |
| T6 | Unique names on repeat | `<name> Bleeped.ext`, then `(2)` | PASS | [code] `ProduceAsync` |
| T7 | Same-folder flow | No drop target → source folder | PASS | [code] `AudioToolOutput.DirectoryFor` |
| T8 | Cross-location drop A→B | Drop folder B is used | PASS | [code] same helper; [blocked] real drag (F-21) |
| T9 | Multi-select | Exactly one audio | PASS | [test] `Audio_tool_windows...` |
| T10 | Folder input | Not offered | PASS | [code] `Applies` |
| T11 | Corrupt / zero-byte input | FFmpeg probe fails, status error, no output | PASS | [code] probe + `ProduceAsync`; no dedicated test |
| T12 | Wrong-extension input | FFmpeg content sniff | PASS | [code] `-i` |
| T13 | Read-only input / output | Output write failure cleans the temp | PASS | [code] `ProduceAsync` |
| T14 | In-use input / clear error | Read failure surfaces in the status | PASS | [code] probe error path |

### 5.5 Visualizer — `tools.audiovisualizer`

One audio; MP4 with waveform/scope; landscape, portrait or square; black or image background.

| ID | Check | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| T1 | Identity / reachability | `tools.audiovisualizer`; one audio | PASS | [test] `Audio_tool_windows...`; [code] `AudioVisualizerTool.cs:19-42`, `AudioOperations.cs:59-75`, `WheelController.cs:114` |
| T2 | Formats | All nine; one | PASS | [code] `Applies`; no per-format fixture |
| T3 | Orientation / size | Dimensions follow the orientation; scope portrait size; waveform/scope args encode MP4; background image is input 0 | PASS | [test] `Visualizer_dimensions_follow_the_orientation`, `Visualizer_scope_arguments_use_the_portrait_size`, `Visualizer_waveform_arguments_encode_an_mp4`, `Visualizer_background_image_is_input_zero` |
| T4 | Content verification | A real MP4 exists, has the chosen dimensions, source duration and an audio track | FAIL | [code] writer `AudioVisualizerTool.cs:250-300` uses `ProduceAsync`, but only argument/dimension unit tests exist; no output probe → F-17 |
| T5 | Source SHA-256 unchanged | Source only read | PASS | [code] `ProduceAsync` |
| T6 | Unique names on repeat | `<name> Visualizer.mp4`, then `(2)` | PASS | [code] `ProduceAsync` |
| T7 | Same-folder flow | No drop target → source folder | PASS | [code] `AudioToolOutput.DirectoryFor` |
| T8 | Cross-location drop A→B | Drop folder B is used | PASS | [code] same helper; [blocked] real drag (F-21) |
| T9 | Multi-select | Exactly one audio | PASS | [test] `Audio_tool_windows...` |
| T10 | Folder input | Not offered | PASS | [code] `Applies` |
| T11 | Corrupt / zero-byte input | FFmpeg probe fails, status error, no output | PASS | [code] probe + `ProduceAsync` |
| T12 | Wrong-extension input | FFmpeg content sniff | PASS | [code] `-i` |
| T13 | Read-only input / output | Output write failure cleans the temp | PASS | [code] `ProduceAsync` |
| T14 | In-use input / clear error | Read failure surfaces in the status | PASS | [code] probe error path |

## 6. Tools — PDF, compression, metadata, OCR

### 6.1 Merge to PDF — `tools.pdfmerge`

2+ PDFs/images; `Merged.pdf`; page order is the plan order; image = one page.

| ID | Check | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| T1 | Identity / reachability | `tools.pdfmerge`; opens its own window for 2+ PDFs/images | FAIL | [code] `PdfToolOperations.cs:35-39` uses first-match `ToolRegistry.For(items)`; Lock/Protect/Collage also accept those selections → F-02 |
| T2 | Formats | 2+ PDFs, 2+ images, or a mix; one file or a non-PDF/image rejected | PASS | [test] `PdfOrganizeTests.CanMerge_accepts_pdfs_and_images_only`; [code] `PdfOrganize.CanMerge`, `PdfMergeTool.Applies` |
| T3 | Orientation / size | PDF /Rotate preserved; image page is 96 DPI→points; portrait/landscape/mixed pages kept as-is | PASS | [test] `Merge_keeps_the_rotation_of_each_source_page`, `Merge_adds_an_image_as_exactly_one_page`; [code] `PdfMergeTool.cs:448-504` (image `AutoOrient`) |
| T4 | Content verification | Page count/text/order and image page pixels | FAIL | [test] normal order/rotation/image tests pass, but a source with 0 pages is not rejected: `PdfMergeWriter.Write` saves a 0-page document and reports success (F-07) — no test covers it |
| T5 | Source SHA-256 unchanged | Sources are opened read-only (`PdfReader.Open(..., Import)` / Magick read) | PASS | [code] `PdfMergeWriter.cs:448-504`; no write to a source path |
| T6 | Unique names on repeat | `Merged.pdf`, then `Merged (2).pdf` | PASS | [code] `PdfMergeTool.cs:410-413` `ProduceAsync` |
| T7 | Same-folder flow | No drop target → next to the first source row | PASS | [code] `PdfToolOutput.DirectoryFor`; `PdfMergeTool.cs:411` |
| T8 | Cross-location drop A→B | Drop folder B is used | PASS | [code] `PdfToolOutput.DirectoryFor` (`PdfToolSupport.cs:75-91`); [blocked] real drag (F-21) |
| T9 | Multi-select | 2+ sources; arrows/drag reorder; remove/reset; duplicates allowed | PASS | [test] `Merge_arrows_are_disabled_at_the_first_and_last_row`, `Merge_remove_keeps...`, `Merge_up_and_down_move_one_position`, `Merge_reset_restores...` |
| T10 | Folder input | Not offered | PASS | [code] `CanMerge` requires `!IsDirectory` |
| T11 | Corrupt / zero-byte input | Corrupt or encrypted PDF: localized error, no `Merged*`; read-only output folder: error, empty folder | PASS | [test] `Merge_fails_on_a_corrupt_pdf_without_writing_a_partial_file`, `Merge_fails_on_an_encrypted_pdf...`, `Merge_fails_on_a_read_only_folder...` |
| T12 | Wrong-extension input | A non-PDF named `.pdf` fails in `AppendPdfPages` with a localized error | PASS | [code] `PdfMergeTool.cs:448-460` catch |
| T13 | Read-only input / output | Read-only source works; read-only destination handled with no partial | PASS | [test] `Merge_fails_on_a_read_only_folder_without_writing_a_partial_file` |
| T14 | In-use input / clear error | An exclusively-held PDF fails to open with a readable error; no partial | PASS | [code] `PdfReader.Open` throws, producer aborts, `ProduceAsync` cleans |

### 6.2 Organize Pages — `tools.pdforganize`

One PDF; page grid, rotate/duplicate/delete/move, optional same width; `<name> Organized.pdf`.

| ID | Check | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| T1 | Identity / reachability | `tools.pdforganize`; opens its own window for one PDF | FAIL | [code] `PdfToolOperations.cs:59-63` uses first-match `ToolRegistry.For(items)`; Lock/Protect/Unlock/Compress/Metadata also accept one PDF → F-02 |
| T2 | Formats | One PDF only; image/text/folder/two PDFs rejected | PASS | [test] `PdfOrganizeTests.CanOrganize_accepts_a_single_pdf_only` |
| T3 | Orientation / size | Rotate wraps at 360; duplicate keeps rotation; same-width scales all pages to the narrowest; heights may differ | PASS | [test] `Organize_rotate_adds_degrees_and_wraps`, `Organize_duplicate...`, `Organize_same_width_scales_every_page_to_the_narrowest_width`, `ScaleToWidth_scales_both_dimensions...` |
| T4 | Content verification | Real output has the planned page texts/rotation/order; 0-page input is refused with a clear status | PASS | [test] `Organize_applies_rotate_duplicate_delete_and_move_exactly`, `Organize_same_width_scales...`; [code] `PdfOrganizeTool.cs:223-227` (0-page error) |
| T5 | Source SHA-256 unchanged | Source opened read-only; result is a new file | PASS | [code] `PdfPageWriter.Write` opens import mode, saves to temp; no source write |
| T6 | Unique names on repeat | `<name> Organized.pdf`, then `(2)` | PASS | [code] `PdfOrganizeTool.cs:460-461` `ProduceAsync` |
| T7 | Same-folder flow | No drop target → source folder | PASS | [code] `PdfToolOutput.DirectoryFor` |
| T8 | Cross-location drop A→B | Drop folder B is used | PASS | [code] same helper; [blocked] real drag (F-21) |
| T9 | Multi-select | Exactly one PDF | PASS | [test] `CanOrganize_accepts_a_single_pdf_only` |
| T10 | Folder input | Not offered | PASS | [code] `CanOrganize` requires `!IsDirectory` |
| T11 | Corrupt / zero-byte input | Corrupt input: localized error naming the file, no `Organized*` | PASS | [test] `Organize_fails_on_a_corrupt_pdf_without_writing_a_partial_file` |
| T12 | Wrong-extension input | A non-PDF named `.pdf` is rejected by PDFsharp with a localized status | PASS | [code] `PdfOrganizeTool.ActivateAsync` catch, `PdfPageWriter` open catch |
| T13 | Read-only input / output | Read-only source works; read-only destination fails through `ProduceAsync` with no partial | PASS | [code] `ProduceAsync` temp+finally |
| T14 | In-use input / clear error | Exclusive holder fails to read with a readable status; no partial | PASS | [code] `File.ReadAllBytes`/`PdfReader.Open` failure → status; no write |

### 6.3 Compress — `tools.compress`

One image/audio/video/PDF; `<name> Compressed.<ext>`; image/video/audio plans plus a PDF optimize path.

| ID | Check | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| T1 | Identity / reachability | `tools.compress`; one supported media file; FFmpeg required for audio/video | PASS | [test] `CompressMetadataTests.Tool_identity_primary_labels_and_applicability`; [code] `CompressOperation.cs:11-34`, `WheelController.cs:107` |
| T2 | Formats | All images (including svg/heic/gif), all audio, all video, PDF; docx/folder/two files/empty rejected | PASS | [test] identity test; [code] `Compression.KindOf`; svg→png via `ImageBackend.OutputFormatFor` |
| T3 | Orientation / size | Image EXIF auto-orient; target/percentage presets resize to the exact plan; video target size two-pass with maxrate; lossless audio ignores bitrate; no upscale | PASS | [code] `CompressRunner.cs:32-38`; [test] `Image_plan_never_upscales...`, `Target_size_downscales...`, `Percentage_mode...`, `Video_target_size_uses_a_two_pass_bitrate`, `Lossless_audio_ignores_the_bitrate_target` |
| T4 | Content verification | Image smaller/resized; video ≤ target+10 %, duration/audio kept; audio smaller, duration kept; PDF remains a readable PDF | FAIL | [test] image/video/audio rows pass; the PDF branch (`CompressRunner.PdfAsync` → `PdfBackend.Optimize`) has no output test at all → F-20/F-23 |
| T5 | Source SHA-256 unchanged | Source hash unchanged after each path | PASS | [test] `Compress_image_writes_a_smaller_resized_copy_and_never_touches_the_source`, `Compress_video...`, `Compress_audio...` |
| T6 | Unique names on repeat | `<name> Compressed.<ext>`, then `(2)` | PASS | [code] `CompressRunner.cs:29,47,72` `ProduceAsync` |
| T7 | Same-folder flow | No drop target → source folder | PASS | [code] `CompressView.cs:464` `ToolOutputFolder` |
| T8 | Cross-location drop A→B | Drop folder B is used | PASS | [code] same helper; [blocked] real drag (F-21) |
| T9 | Multi-select | Exactly one file | PASS | [test] identity test |
| T10 | Folder input | Not offered | PASS | [code] `CompressTool.Applies` |
| T11 | Corrupt / zero-byte input | Corrupt image fails with no file; zero-byte/unsupported is refused before the write; unwritable folder leaves no partial | PASS | [test] `A_corrupt_image_fails_without_leaving_any_file`, `An_unwritable_output_folder_fails_without_partial_files`; [code] `CompressView.cs:452-461` |
| T12 | Wrong-extension input | Image content sniff; audio/video use the declared kind for the plan (a WAV named `.mp4` fails FFmpeg with a clear error) | PASS | [code] `MagickImage` sniff; `KindOf`/FFmpeg error path |
| T13 | Read-only input / output | Read-only source works; read-only destination cleans the temp | PASS | [test] unwritable test; [code] `ProduceAsync` |
| T14 | In-use input / clear error | Missing FFmpeg/corrupt source gives a clean error; no partial | PASS | [test] `A_missing_ffmpeg_reports_a_clear_error_and_writes_nothing` |

### 6.4 Edit Metadata — `tools.metadata`

One image/audio/video/PDF; `<name> Edited metadata.<ext>`; copy with EXIF/GPS/tags/PDF info.

| ID | Check | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| T1 | Identity / reachability | `tools.metadata`; one supported media file | PASS | [test] `CompressMetadataTests.Tool_identity_primary_labels_and_applicability`; [code] `MetadataOperation.cs:10-35`, `WheelController.cs:108` |
| T2 | Formats | All images/audio/video/PDF; folder/two files rejected | PASS | [test] identity test; [code] `MetadataTool.Applies` via `Compression.KindOf` |
| T3 | Orientation / size | The read document reports the same pixel dimensions the save produces for an EXIF-rotated photo | FAIL | [code] `MetadataRunner.cs:61-65` reads without `AutoOrient`; `:156-157` writes with it → F-09 |
| T4 | Content verification | Camera/GPS write and re-read; Remove All clears; PDF info round-trips; media tags/chapters write | FAIL | [test] image EXIF/GPS and PDF tests pass; media (audio/video) metadata has no end-to-end test (`EditArgs`/`TagArgs` unit only) → F-20/F-23 |
| T5 | Source SHA-256 unchanged | Source only read; output is a new file | PASS | [code] `MetadataRunner` opens source and writes `tmp`; [test] image/PDF function tests re-read the copy and keep the source |
| T6 | Unique names on repeat | `<name> Edited metadata.<ext>`, then `(2)` | PASS | [code] `MetadataRunner.cs:152-170,282-286,332` `ProduceAsync` |
| T7 | Same-folder flow | No drop target → source folder | PASS | [code] `MetadataView.cs:349` `ToolOutputFolder` |
| T8 | Cross-location drop A→B | Drop folder B is used | PASS | [code] same helper; [blocked] real drag (F-21) |
| T9 | Multi-select | Exactly one file | PASS | [test] identity test |
| T10 | Folder input | Not offered | PASS | [code] `MetadataTool.Applies` |
| T11 | Corrupt / zero-byte input | Corrupt image/PDF fails with no file; corrupt media probe reports the FFmpeg error | PASS | [test] `A_corrupt_metadata_input_fails_without_writing_anything`; [code] `ProduceAsync` |
| T12 | Wrong-extension input | Image sniff; PDF by header; media by FFmpeg | PASS | [code] `MagickImage`, `PdfReader.Open`, FFmpeg |
| T13 | Read-only input / output | Read-only source works; destination failure cleans the temp | PASS | [code] `ProduceAsync` |
| T14 | In-use input / clear error | Read failure is reported; no partial | PASS | [code] open/read precedes `ProduceAsync` |

### 6.5 Extract text (OCR) — `tools.ocr`

Images and PDFs; `<name> (text).txt`; text also copied to the clipboard.

| ID | Check | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| T1 | Identity / reachability | `tools.ocr`; offered for images/PDFs when a Windows OCR language exists | PASS | [code] `OcrOperation.cs:28-55`, `WheelController.cs:91`; [test] none (`Ocr` has no test file) |
| T2 | Formats | All `Formats.Images` and PDF; multi-select; folder rejected | PASS | [code] `OcrOperation.AppliesTo`; image converted to PNG before recognition |
| T3 | Orientation / size | EXIF orientation applied in the PNG conversion / WinRT decode; images above `MaxImageDimension` scaled; every PDF page recognized; page texts joined | PASS | [code] `OcrOperation.cs:104-139`, `ImageBackend.ConvertAsync` auto-orient; [test] none |
| T4 | Content verification | Recognized text equals the visible text; page join; no-text case is a clear error | FAIL | [code] implementation exists, but there is no test at all (F-16); no executed content evidence on `origin/main` |
| T5 | Source SHA-256 unchanged | Source only read; output is a new `.txt` | PASS | [code] `PagesOf` converts/read; `ProduceAsync` for txt |
| T6 | Unique names on repeat | `<name> (text).txt`, then `(2)` | PASS | [code] `OcrOperation.cs:88` `ProduceAsync` |
| T7 | Same-folder flow | No drop target → source folder | PASS | [code] `ctx.OutputDirFor` from `JobRunner` |
| T8 | Cross-location drop A→B | Drop folder B is used | PASS | [code] `JobRunner`/`OperationContext`; [blocked] real drag (F-21) |
| T9 | Multi-select | Each item gets a `.txt`; a failure lists the failed item and keeps the successful outputs | FAIL | [code] `OcrOperation.cs:63-101` throws on a later failure without a result message; earlier `.txt` files remain → F-15 |
| T10 | Folder input | Not offered | PASS | [code] `AppliesTo` requires `!IsDirectory` |
| T11 | Corrupt / zero-byte input | Magick/PDF error; no output for that item | PASS | [code] `PagesOf` throws before `ProduceAsync`; [test] none |
| T12 | Wrong-extension input | Image content sniff; PDF by declared format (a PDF named `.jpg` may fail in Magick with a clear error) | PASS | [code] `PagesOf` format branch |
| T13 | Read-only input / output | Read-only source works; destination failure cleans the temp | PASS | [code] `ProduceAsync` |
| T14 | In-use input / clear error | Read failure reported through `JobRunner.Friendly`; no partial | PASS | [code] `JobRunner.cs:213-220`; [test] none |

## 7. Tools — password and lock

### 7.1 Protect — `tools.protect`

One file, one folder, or several files; one AES-256 ZIP, or an AES-256 PDF for one PDF.

| ID | Check | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| T1 | Identity / reachability | `tools.protect`; opens its own window for one file/folder or several files | PASS | [test] `PasswordProtectionTests.New_strings_have_matching_keys...`; [code] `ProtectOperation.cs:55-69`, `WheelController.cs:105` |
| T2 | Formats | Any file; one folder; 2+ files; a folder mixed with files rejected; empty selection rejected | PASS | [code] `ProtectTool.Applies` (`:40-41`); multiple files are read as opaque bytes |
| T3 | Orientation / size | Not media-dependent: password/confirmation/strength, ZIP AES-256, PDF AES-256, owner print/copy permissions; empty folder refused | PASS | [test] `Validation_rejects_short_mismatched_and_overlong_passwords`, `Strength_meter_is_ordered...`, `Zip_uses_aes_256...`, `Pdf_uses_aes_256...`, `Empty_folder_is_not_a_fake_protected_archive`, `Capabilities_do_not_pretend_zip_can_hide_names` |
| T4 | Content verification | ZIP round-trips the exact UTF-8 bytes; PDF opens with the password and Unprotect writes an unencrypted PDF; plain ZIP is not called protected | PASS | [test] `Zip_round_trips_with_the_correct_password`, `Zip_rejects_the_wrong_password`, `Pdf_uses_aes_256_and_round_trips...`, `A_plain_zip_is_reported_as_not_encrypted` |
| T5 | Source SHA-256 unchanged | Originals are only read; output is a new archive/PDF | PASS | [code] `AesZipWriter`/`PdfPasswordProtection.Protect` read source and write `temp`; [test] plan tests do not modify sources |
| T6 | Unique names on repeat | `<stem> Protected.zip/pdf`, then `(2)` | PASS | [test] `Zip_never_overwrites_an_existing_output`; [code] `ProtectTool.cs:369-382` `ProduceAsync` |
| T7 | Same-folder flow | No drop target → next to the first source | PASS | [code] `ProtectTool.cs:451-463` |
| T8 | Cross-location drop A→B | Drop folder B is used | PASS | [code] same helper; [blocked] real drag (F-21) |
| T9 | Multi-select | Several files become one ZIP; duplicate names inside the archive are disambiguated; empty selection refused | PASS | [code] `ProtectedZipPlanBuilder.Unique` (`PasswordProtection.cs:481-496`); [test] `Plan_walks_nested_folders_in_a_stable_order`; no duplicate-name test but the code path is deterministic |
| T10 | Folder input | One folder accepted; recursive, stable order, symlinks skipped; mixed folder+files rejected | PASS | [test] `Plan_walks_nested_folders_in_a_stable_order`; [code] `Walk`/`Collect` |
| T11 | Corrupt / zero-byte input | Protect treats bytes as opaque, so a zero-byte file is archived (not an error); a locked/unreadable file fails with a clear error and no archive | PASS | [code] `AddFile` length check, `AesZipWriter` read; [test] empty-folder refusal |
| T12 | Wrong-extension input | Any file is valid input; extension does not select a parser | PASS | [code] opaque-byte path |
| T13 | Read-only input / output | Read-only source reads fine; read-only destination fails through `ProduceAsync` with no partial | PASS | [code] `ProduceAsync`; [test] `PasswordProtectionTests` failure paths |
| T14 | In-use input / clear error | A file held exclusively fails during archive/PDF read; the view shows a generic "could not be protected" message and no partial | PASS | [code] `ProtectView` catch (`:389-399`) + `ProduceAsync`; the message names the output folder but not the OS error — acceptable, not the Lock-specific message issue |

### 7.2 Unlock — `tools.unlock`

Exactly one encrypted ZIP or password-protected PDF; `<name> Unlocked.zip/pdf`.

| ID | Check | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| T1 | Identity / reachability | `tools.unlock`; opens its own window for one zip/pdf | PASS | [code] `UnlockOperation.cs:54-69`, `WheelController.cs:106` |
| T2 | Formats | Exactly one ZIP or PDF; other formats, folders and several files rejected | PASS | [code] `UnlockTool.Applies` (`:38-41`) |
| T3 | Orientation / size | Password field rules (non-empty, max 256); plain ZIP reports NotEncrypted; PDF owner permissions removed correctly | PASS | [test] `Unlock_validation_accepts_any_non_empty_password`, `A_plain_zip_is_reported_as_not_encrypted`, `Pdf_uses_aes_256_and_round_trips...` |
| T4 | Content verification | Correct password produces an unencrypted ZIP/PDF whose content reads back unchanged; wrong password leaves no output | PASS | [test] `Unlock_creates_an_unencrypted_zip_and_never_leaves_a_partial_file`, `Zip_rejects_the_wrong_password`, `Pdf_uses_aes_256...` |
| T5 | Source SHA-256 unchanged | Encrypted source only read | PASS | [code] `UnlockView`/`AesZipReader`/PDF open read; output `temp` |
| T6 | Unique names on repeat | `<name> Unlocked.zip/pdf`, then `(2)` | PASS | [code] `UnlockTool.cs:226-235` `ProduceAsync` |
| T7 | Same-folder flow | No drop target → source folder | PASS | [code] `UnlockTool.cs:292-306` |
| T8 | Cross-location drop A→B | Drop folder B is used | PASS | [code] same helper; [blocked] real drag (F-21) |
| T9 | Multi-select | Exactly one archive/PDF | PASS | [code] `Applies` |
| T10 | Folder input | Not offered | PASS | [code] `Applies` requires `!IsDirectory` |
| T11 | Corrupt / zero-byte input | Wrong password/corrupt archive/PDF: clear error, no partial output | PASS | [test] `Unlock_creates...never_leaves_a_partial_file`; wrong-password tests; [code] `ProduceAsync` |
| T12 | Wrong-extension input | A non-ZIP/PDF named `.zip`/`.pdf` fails in the container reader with a clear error | PASS | [code] reader exceptions mapped by `UnlockView` |
| T13 | Read-only input / output | Read-only source reads; read-only destination cleans the temp | PASS | [code] `ProduceAsync` |
| T14 | In-use input / clear error | Exclusive holder fails the read; no partial | PASS | [code] read precedes output; `ProduceAsync` |

### 7.3 Lock — `tools.lock` (Beta)

One or more files/folders transformed into `.dwenc`/`.dwdir`; three-slot password/recovery envelope.

| ID | Check | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| T1 | Identity / reachability | `tools.lock`; opens its own window; one protected container opens the unlock view | PASS | [code] `LockTool.cs:21-61,70-89`, `WheelController.cs:103`; [test] `LockSafetyTests` suite |
| T2 | Formats | Any file or folder; multiple items; already-protected single item opens unlock; mixed protected/unprotected multi is rejected | PASS | [code] `LockTool.Applies` (`:44-52`), `Create` (`:54-60`) |
| T3 | Orientation / size | Data-independent: 0 B, 1 B, chunk±1, multi-chunk, nested folders, folder `desktop.ini`; fixed 64 KB chunk framing | PASS | [test] `LockContainerTests.Roundtrip_at_every_frame_boundary`, `LockSafetyTests.Folder_roundtrip_restores_content_and_desktop_ini_byte_for_byte`; `File_can_be_moved_and_unlocked_there` |
| T4 | Content verification | Byte-exact round-trip for every size; three secrets open; tamper/truncation refused; corrupt input leaves no output | PASS | [test] `Roundtrip_at_every_frame_boundary`, `Every_one_of_the_three_secrets_opens_the_file`, `Tampered_*`, `Truncated_file_is_refused`, `Corrupt_input_leaves_no_output_files` |
| T5 | Source SHA-256 unchanged | Protect removes the plaintext only after the container verifies; a failed protect leaves the source intact | PASS | [test] `Protect_leaves_exactly_one_container_and_no_plaintext`, `Folder_protect_rolls_back_when_a_file_is_locked`; [code] `LockService.ProtectCore` verify→move→remove |
| T6 | Unique names on repeat | An existing target is refused (never overwritten); unlock collision uses a restored suffix | PASS | [test] `Protect_never_overwrites_an_existing_container`, `Unlock_uses_a_restored_suffix_on_collision` |
| T7 | Same-folder flow | No drop target → container in the source folder (in place) | PASS | [code] `LockOutput.DirectoryFor` fallback; `LockService.cs:104-127` |
| T8 | Cross-location drop A→B | Drop folder B is used for the container; the plaintext is then removed | PASS | [code] `LockTool.cs:388-391`, `LockService.Protect`; [blocked] real drag (F-21); note: this *moves* a locked item to B by design |
| T9 | Multi-select | Several unprotected items are protected one by one; a failure reports which items succeeded; duplicate container names fail on the second | FAIL | [code] loop `LockTool.cs:384-401`; duplicate target collision has no unique suffix → F-12 |
| T10 | Folder input | Folders accepted; folder appearance badge applied and restored byte-for-byte | PASS | [test] `Folder_roundtrip_restores_content_and_desktop_ini_byte_for_byte`, `Lock_icons_are_shipped...`, `Desktop_ini_is_restored_byte_for_byte_or_removed` |
| T11 | Corrupt / zero-byte input | A zero-byte file round-trips; a corrupt container is refused; a damaged container leaves no restored/staging/temp output | PASS | [test] `Roundtrip_at_every_frame_boundary` includes 0 B; `Corrupt_input_leaves_no_output_files`, `Tampered_*` |
| T12 | Wrong-extension input | Any file/folder is valid input; a `.dwenc`-named item opens unlock | PASS | [code] `LockFormat.IsProtectedName` / `LockTool.Create` |
| T13 | Read-only input / output | Read-only file/folder round-trips without partial output | PASS | [test] `Read_only_folder_still_roundtrips_without_partial_output` |
| T14 | In-use input / clear error | A file open in another program fails with the OS sharing-violation reason embedded in the localized message; nothing is written | PASS | [code] the OS sharing violation is a plain `IOException`; `LockRuntime.Describe` defaults to `tool.lock.error.generic` whose value embeds `{0}` = `ex.Message` (`LockRuntime.cs:161-172`, `Strings/en.json`), so the user sees "being used by another process"; temp/container cleanup is unchanged. The dedicated `tool.lock.error.fileInUse` key is only used for `LockFileInUseException` and is not reached by an OS lock — a wording/localization nit, not a functional failure. |

## 8. Actions wheel (`Alt` by default)

The six actions run through `AppAction`/`FileActions`, not through a tool window. They have no test file;
the evidence below is code unless noted, and none of the flows was executed without a desktop.

| ID | Action | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| A-1 | Copy to… | Folder picker; per item copy to `target\<name>`; same-path no-op; shell progress/conflict/undo; source unchanged; cancel leaves the already-copied items (shell semantics) | PASS | [code] `FileActions.cs:30-82`; `Microsoft.VisualBasic.FileIO.FileSystem.CopyFile/CopyDirectory`; [blocked] UI execution (F-21) |
| A-2 | Move to… | Same picker; source removed by the shell only after a successful move; cross-volume handled by the shell; folder and multi-select supported | PASS | [code] `FileActions.cs:65-79`; [blocked] UI execution (F-21) |
| A-3 | Copy path | Joined paths (newline) to the clipboard; five retries when another app holds the clipboard; success/failure toast | PASS | [code] `FileActions.cs:105-123`; no test |
| A-4 | Terminal | Opens Windows Terminal at the first item's folder (or the folder itself); falls back to PowerShell; trailing backslash quoted safely | PASS | [code] `FileActions.cs:125-143`; no test |
| A-5 | Float shelf | Always-on-top shelf holding the selection; drag out, double-click open, Delete removes, close; no file writes | PASS | [code] `ShelfWindow.cs:19-100`; no test |
| A-6 | Share | Files only (folders rejected); Windows share sheet with the selected files and title | FAIL | [code] `FileActions.cs:26-27`, `ShareHelper.cs:21-112`; the WinRT/COM share sheet cannot be exercised here and has no automated test; treat as unverified until a desktop smoke test exists (F-21) |

## 9. Convert wheel — family matrix

Every conversion is a `PerFileOperation` except the four batch operations (`image.merge.pdf`,
`pdf.merge`, `audio.concat`, `pack.*`). Inputs are recognized by extension (`Formats.KeyOf`); the
backends then decode by content. The CI-generated fixtures cover only png/jpg/svg/pdf/md/html/txt/
xlsx/csv/srt/zip/tgz/mp4/avi/mkv/wav/mp3/gif/docx/pptx; `TestData.ApplyFixtures` does not map the
owner's HEIC/RAR/7z/FLAC fixtures into any sample key. LibreOffice is not installed in CI (F-19).

### 9.1 Images — `image.to.*`, image edits, `image.merge.pdf`

| ID | Check | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| CV-I1 | Supported formats | In jpg/png/webp/heic/tiff/avif/bmp/gif/svg/ico; out jpg/png/webp/avif/tiff/ico/pdf; specific target hidden for the same format | FAIL | [test] `UnitTests.Convert_wheel_hides_same_format`; [code] `Formats.Images`/`ImageOutputs`, `Catalog.cs:44-55`; only png/jpg/svg/gif have fixtures — heic/avif/tiff/bmp/ico/webp are unexercised → F-20 |
| CV-I2 | Orientation / size | EXIF auto-orient; first frame only for animated/multi-page; 24 MP source; EXIF 1/3/6/8; huge-photo cap | PASS | [code] `ImageBackend.Load` (`:15-19`) auto-orients, first frame; [test] none for orientation; F-11 notes the frame loss in editing tools |
| CV-I3 | Content verification | Output decodes as the requested format with the source pixels/dimensions (or the documented resize) | FAIL | [code] no conversion test decodes the output; `IntegrationTests` only checks existence/length (`:211-219`) → F-20 |
| CV-I4 | Source SHA-256 unchanged | Every image operation opens the source read-only | PASS | [code] each backend only reads `item.Path`; `ProduceAsync` moves the temp |
| CV-I5 | Unique names on repeat | `<name>.<target>` then `(2)`; never overwrite | PASS | [code] `OutputPaths.ProduceAsync`; [test] `Unique_appends_counter`, `Produce_cleans_up_on_failure` |
| CV-I6 | Same folder / cross-location / multi-select / folder | No drop → source folder; drop → detected folder; each file separate; folders not offered | PASS | [code] `JobRunner.cs:106-115`, `OperationContext.OutputDirFor`; `PerFileOperation`; [blocked] real drag (F-21) |
| CV-I7 | Corrupt / zero-byte / wrong-extension | Magick error for corrupt/zero; content sniff for wrong extension; no partial output | PASS | [code] Magick load throws; `ProduceAsync` deletes temp; no extension switch for decode |
| CV-I8 | Read-only output / in-use input | Temp creation fails cleanly; read failure gives a clear error | PASS | [code] `ProduceAsync`; `JobRunner.Friendly` maps `UnauthorizedAccessException`/`MagickException` |

### 9.2 Audio — `audio.to.*`, compress, normalize, trim, concat

| ID | Check | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| CV-A1 | Supported formats | In mp3/m4a/aac/wav/flac/ogg/opus/aiff/wma; out mp3/m4a/wav/flac/ogg/opus/aiff | FAIL | [code] `Formats.Audio`/`AudioOutputs`, `Catalog.cs:84-135`; only mp3/wav have fixtures; the other seven share FFmpeg but are unexercised → F-20 |
| CV-A2 | Orientation / size | Mono/stereo, sample rate, 2 s and long files; trim/normalize preserve duration or apply the requested range; lossless bitrate targets ignored | PASS | [test] `AudioEffectsTests` mono/stereo; `AudioFunctionTests` duration/loudness; [code] streaming `AudioRunner.WaveformAsync` |
| CV-A3 | Content verification | Output duration/codec/audio match the requested target | FAIL | [code] conversion outputs are only checked for existence/non-empty; audio tool function tests cover normalize/trim, not `audio.to.*`/concat → F-20 |
| CV-A4 | Source SHA-256 unchanged | Read-only | PASS | [test] `AudioFunctionTests` `AssertUnchangedAsync`; [code] all writes go to `tmp` |
| CV-A5 | Unique names on repeat | `<name>.<target>` then `(2)` | PASS | [code] `ProduceAsync` |
| CV-A6 | Flows | Same/cross/multi; folders not offered; audio.concat is batch | PASS | [code] `PerFileOperation`/`BatchOperation`; [blocked] real drag (F-21) |
| CV-A7 | Corrupt / zero / wrong extension | FFmpeg non-zero → localized error with stderr tail; content sniff | PASS | [code] `ProcessRunner.cs:93-101`, `FfmpegBackend` |
| CV-A8 | Read-only output / in-use | `ProduceAsync` cleanup; read error surface | PASS | [code] `OutputPaths.ProduceAsync` |

### 9.3 Video — `video.to.*`, compress, 720p, mute, trim, rotate, speed, thumbnail, gif conversions

| ID | Check | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| CV-V1 | Supported formats | In mp4/mov/mkv/webm/avi/wmv/flv/mpg/mpeg/3gp/ts/mts; out mp4/mov/mkv/webm/gif/mp3; gif→mp4/webm | FAIL | [code] `Formats.Video`/`VideoOutputs`, `Catalog.cs:141-176`; fixtures cover mp4/avi/mkv/gif only; mov/webm/wmv/flv/mpg/3gp/ts/mts unexercised → F-20 |
| CV-V2 | Orientation / size | Portrait/landscape, 4K downscale, odd dimensions rounded, rotation metadata, no-audio video; frame/size preserved or target applied | PASS | [test] `VideoEditTests.Probe_swaps_a_rotated_video...`, `Odd_dimensions_are_rounded_down...`, `Video_without_audio_gives_clear_error`; [code] `H264` even scaling |
| CV-V3 | Content verification | Duration, streams (video/audio), dimensions and codec match the operation | FAIL | [code] `IntegrationTests` checks existence/length only; tool function tests cover trim/crop/speed/compress/join/split, not the Convert slices → F-20 |
| CV-V4 | Source SHA-256 unchanged | Read-only | PASS | [test] `VideoFunctionTests`/`VideoSplitJoinFunctionTests` source assertions for tool paths; [code] `-i` only |
| CV-V5 | Unique names on repeat | `<name>.<target>` then `(2)` | PASS | [code] `ProduceAsync` |
| CV-V6 | Flows | Same/cross/multi; folders not offered; video.to.* per file | PASS | [code] `PerFileOperation`; [blocked] real drag (F-21) |
| CV-V7 | Corrupt / zero / wrong extension | FFmpeg error with stderr tail; no partial; content sniff | PASS | [code] `ProcessRunner`; `ProduceAsync` |
| CV-V8 | Read-only output / in-use | Clean temp failure; read failure surface | PASS | [code] `ProduceAsync` |

### 9.4 PDF — `pdf.to.*`, merge, split, rotate, optimize

| ID | Check | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| CV-P1 | Supported outputs | docx, jpg, png, pptx, xlsx, txt, md, html, epub + merge/split/rotate/optimize; image inputs to merge | FAIL | [code] `Catalog.cs:178-230`; pptx/xlsx/docx/html/epub depend on LibreOffice/Pandoc; LibreOffice is absent in CI → F-19; only the image/text/merge paths have fixtures |
| CV-P2 | Orientation / size | Page /Rotate preserved by merge; render at 300 DPI; split one page per file; rotate 90; organize same width | PASS | [test] `Merge_keeps_the_rotation...`, `Merge_adds_an_image_as_exactly_one_page`, `Render...`; [code] `PdfBackend.RenderAsync` |
| CV-P3 | Content verification | Page count, text and rendered pixels verified for every output | FAIL | [code] tool merge/organize have real-file tests; `pdf.to.jpg/png/txt/md/html/docx` have only non-empty checks; zero-page `pdf.to.jpg` returns an empty folder (F-07) → F-20 |
| CV-P4 | Source SHA-256 unchanged | Read-only | PASS | [code] all readers open read-only; outputs temp |
| CV-P5 | Unique names on repeat | Single page `<name>.<ext>`, multi-page folder `<name> (Pages)`, then `(2)`; merge/rotate/optimize unique | PASS | [code] `ProduceAsync`/`UniqueDirectory` |
| CV-P6 | Flows | Same/cross/multi; folder not offered | PASS | [code] `JobRunner`/`OutputDirFor`; [blocked] real drag (F-21) |
| CV-P7 | Corrupt / zero / wrong extension | Corrupt/encrypted PDF errors with a clear message; no partial | PASS | [test] `Merge_fails_on_a_corrupt_pdf...`, `...encrypted...`; [code] `PdfBackend.Open`; `RenderAsync` relies on PDFium exceptions (clearer for merge/organize than render) |
| CV-P8 | Read-only output / in-use | Temp creation failure clean; read failure surface | PASS | [test] `Merge_fails_on_a_read_only_folder...`; [code] `ProduceAsync` |

### 9.5 Documents — `doc.to.*`

| ID | Check | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| CV-D1 | Supported formats | In docx/doc/odt/rtf/md/txt/html/epub; out docx/pdf/odt/md/html/txt/epub/rtf; route chosen by installed tools | FAIL | [code] `Catalog.cs:238-306`, `DocumentRoute`; Pandoc tests cover md/html/txt/docx; DOC/ODT/RTF/EPUB and LibreOffice routes have no fixtures and are skipped in CI (F-19) |
| CV-D2 | Orientation / size | Page size/layout preserved by LibreOffice routes; Pandoc text/paragraph order preserved; media extracted next to Markdown | PASS | [code] `OfficeBackend`/`PandocBackend`; [test] `Every_operation_produces_output` for Pandoc routes only |
| CV-D3 | Content verification | Text/paragraphs/media in the output match the source | FAIL | [code] no document output is decoded/compared; only existence/length → F-20; F-14 covers the non-unique media folder |
| CV-D4 | Source SHA-256 unchanged | Read-only | PASS | [code] Pandoc/Office read source and write temp |
| CV-D5 | Unique names on repeat | `<name>.<target>` then `(2)`; media folder unique | FAIL | [code] main file unique via `ProduceAsync`; extracted media folder not unique (`PandocBackend.cs:49-54`) → F-14 |
| CV-D6 | Flows | Same/cross/multi; folders not offered | PASS | [code] `PerFileOperation`; [blocked] real drag (F-21) |
| CV-D7 | Corrupt / zero / wrong extension | Pandoc/Office non-zero exit → clear error; no partial | PASS | [code] `ProcessRunner` throws with stderr tail; `ProduceAsync` cleanup; wrong extension can be decoded as the declared format (not validated) |
| CV-D8 | Read-only output / in-use | Clean temp failure; read error surface | PASS | [code] `ProduceAsync` |

### 9.6 Slides — `slides.to.*`

| ID | Check | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| CV-S1 | Supported formats | In pptx/ppt/odp; out pdf/png/jpg/pptx/odp | FAIL | [code] `Catalog.cs:309-339`; every slide operation requires LibreOffice, which CI does not install; no executed test → F-19 |
| CV-S2 | Orientation / size | Slide count/size/orientation preserved; PNG/JPG are one image per slide | FAIL | [code] only the route exists; no test executes `slides.to.*` → F-19/F-20 |
| CV-S3 | Content verification | PDF page count = slide count; image pages render; text preserved | FAIL | [code] no content test → F-20 |
| CV-S4 | Source SHA-256 unchanged | Read-only | PASS | [code] `OfficeBackend` reads source |
| CV-S5 | Unique names on repeat | Unique file/folder names | PASS | [code] `ProduceAsync`/`UniqueDirectory` |
| CV-S6 | Flows | Same/cross/multi; folders not offered | PASS | [code] `PerFileOperation`; [blocked] real drag (F-21) |
| CV-S7 | Corrupt / zero / wrong extension | LibreOffice failure → clear error; no partial | PASS | [code] `ProcessRunner`/`ProduceAsync` |
| CV-S8 | Read-only output / in-use | Clean failure; no partial | PASS | [code] `ProduceAsync` |

### 9.7 Spreadsheets — `sheet.to.*`

| ID | Check | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| CV-SH1 | Supported formats | In xlsx/xlsm/xls/ods/csv/tsv; out xlsx/csv/tsv/ods/pdf; native paths for xlsx/xlsm/xls/csv/tsv | FAIL | [code] `Catalog.cs:342-362`; xlsx/csv have fixtures, xlsm/xls/ods and the LibreOffice outputs do not (F-19) |
| CV-SH2 | Orientation / size | All sheets/rows/columns preserved; multiple sheets become multiple CSVs; values kept as text where leading zeros matter | PASS | [test] `Every_operation_produces_output` xlsx/csv; [code] `SpreadsheetBackend.Read/Write` |
| CV-SH3 | Content verification | Cell values/rows/sheets match | FAIL | [code] no content assertion; only existence/length → F-20 |
| CV-SH4 | Source SHA-256 unchanged | Read-only | PASS | [code] readers open read-only; output temp |
| CV-SH5 | Unique names on repeat | Unique per sheet/output; never overwrite | PASS | [code] `ProduceAsync` |
| CV-SH6 | Flows | Same/cross/multi; folders not offered | PASS | [code] `PerFileOperation`; [blocked] real drag (F-21) |
| CV-SH7 | Corrupt / zero / wrong extension | Corrupt xlsx/xls → clear error; binary bytes renamed `.csv` are silently decoded as text and written back | FAIL | [code] `SpreadsheetBackend.Read`/`ReadDelimited` (`:20-29,80-99`) → F-08 |
| CV-SH8 | Read-only output / in-use | Clean temp failure; read error surface | PASS | [code] `ProduceAsync` |

### 9.8 Subtitles — `subs.to.*`

| ID | Check | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| CV-SU1 | Supported formats | srt/vtt → srt/vtt/txt | PASS | [code] `Catalog.cs:365-374`; [test] `Srt_to_vtt_and_back`, `Vtt_without_hours_parses`, `Text_strips_tags` |
| CV-SU2 | Orientation / size | Timestamp formats with/without hours, comma/dot milliseconds, tags stripped for txt | PASS | [test] `Srt_to_vtt_and_back`, `Vtt_without_hours_parses`, `Text_strips_tags` |
| CV-SU3 | Content verification | Cue count/times/text round-trip | PASS | [test] same; `SubtitleBackend.Parse` + writers |
| CV-SU4 | Source SHA-256 unchanged | Read-only | PASS | [code] `File.ReadAllTextAsync` only |
| CV-SU5 | Unique names on repeat | Unique | PASS | [code] `ProduceAsync` |
| CV-SU6 | Flows | Same/cross/multi; folders not offered | PASS | [code] `PerFileOperation`; [blocked] real drag (F-21) |
| CV-SU7 | Corrupt / zero / wrong extension | No timings → `error.subs.none`, no output; SRT/VTT content parses by regex regardless of extension | PASS | [test] unit parse; [code] `SubtitleBackend.cs:88-101` |
| CV-SU8 | Read-only output / in-use | Clean temp failure; read error surface | PASS | [code] `ProduceAsync` |

### 9.9 Archives — `archive.extract`, `archive.to.*`, `pack.*`

| ID | Check | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| CV-AR1 | Supported formats | In zip/tar/tar.gz/gz/tar.bz2/bz2/tar.xz/xz/rar/7z; out zip/tar/tar.gz; extract | FAIL | [code] `Catalog.cs:377-397`; fixtures zip/tgz only; rar/7z/gz/bz2/xz/tar inputs are unexercised (`bundle.rar`/`bundle.7z` exist in `_local/testdata` but are not mapped) → F-20 |
| CV-AR2 | Orientation / size | Nested trees, single top-level item, GZ→TAR unwrap; traversal entries rejected; empty folders and large entries are supported by code but not exercised | PASS | [test] `Extract_restores_tree`, `Archive_rejects_path_traversal`; [code] `ArchiveBackend.ExtractAsync` / `PackAsync` |
| CV-AR3 | Content verification | Every extracted byte/tree matches; repack round-trips; `pack` includes exact file contents | FAIL | [test] `Extract_restores_tree` checks two files; [code] no hash comparison; duplicate names in `pack` lose an input (F-13) → F-20 |
| CV-AR4 | Source SHA-256 unchanged | Extract/repack read the archive; pack reads inputs | PASS | [code] only reads |
| CV-AR5 | Unique names on repeat | Folder/file names unique; second extraction gets `(2)` | PASS | [code] `UniqueDirectory`/`Unique` |
| CV-AR6 | Flows | Same/cross/multi; `pack` accepts folders, others do not | PASS | [code] `BatchOperation` `_ => true`, `PerFileOperation` no dirs; [blocked] real drag (F-21) |
| CV-AR7 | Corrupt / zero / wrong extension | Invalid archive → `error.archive.extract`; no partial folder; content sniff by SharpCompress | PASS | [code] `ExtractTo` catch; temp cleanup; wrong extension is detected by content |
| CV-AR8 | Read-only output / in-use | Temp directory creation failure clean; read failure surface | PASS | [code] temp folder + finally; `JobRunner.Friendly` |

## 10. Shared contracts

These rows apply to every tool; the per-tool tables point back here instead of repeating the same
evidence 28 times.

| ID | Check | Expected | Status | Evidence |
| --- | --- | --- | --- | --- |
| FW-1 | Settings "save results to folder" | The folder chosen in Settings is used when the drop did not resolve another folder, for tools as well as jobs | FAIL | [code] `ToolOutputFolder.cs:13-25` (tools ignore settings) vs `JobRunner.cs:111-115` (jobs honour it); field `AppSettings.cs:84-85`; UI `SettingsWindow.cs:300-327` → F-05 |
| FW-2 | Unique output names | `OutputPaths.Unique` appends ` (2)`, ` (3)` … and `ProduceAsync` moves a completed temp onto that name; a second run never overwrites | PASS | [test] `UnitTests.Unique_appends_counter`; [code] `OutputPaths.cs:7-51`; exceptions F-06 (direct writers) and F-12 (Lock duplicate targets) |
| FW-3 | Atomic output, no partial file | Every generated file is produced at a hidden temp path, checked for existence, then moved; a producer failure deletes the temp | PASS | [test] `UnitTests.Produce_cleans_up_on_failure`; [code] `OutputPaths.ProduceAsync`; exceptions F-06 (Crop/Edit/Annotate/Redact) |
| FW-4 | Source SHA-256 unchanged | Every operation except Lock and Move-to only reads its source; a test hashes before/after where a tool writes a copy | PASS | [test] `CollageTests...leaves_the_source_files_untouched`, `Redact_apply_leaves_the_source_byte_identical`, `Compress_*` source hashes, `AudioFunctionTests` `AssertUnchangedAsync`, `Video*FunctionTests`; [code] all writers target `temp`; Lock is in-place by design |
| FW-5 | Multi-select partial results | A per-file batch keeps successful outputs and reports `file: message` for failures in `OperationResult.Message`; the job card shows the warning in red and does not claim full success | PASS | [code] `PerFileOperation.cs:57-88`, `JobsWindow.cs:236-261`; [test] exercised by `Every_operation_produces_output` with one item only; multi-item partial not directly tested |
| FW-6 | Folder input | Only `pack`, `Protect` and `Lock` accept folders; every other operation is not offered for a directory | PASS | [code] `Catalog.cs` pack `_ => true`, `PerFileOperation.AcceptsDirectories` default false, `ProtectTool.Applies`, `LockTool.Applies`; no directory test for pack |
| FW-7 | Error mapping | Job failures map `ConversionException` to its localized text, `UnauthorizedAccessException` to "cannot write", `IOException` to the OS message, `MagickException` to the image error; tool windows show the exception message; no raw stack traces | PASS | [code] `JobRunner.cs:213-220`, `ToolWindow.cs:367-396`; Lock's OS-lock message embeds the raw OS reason rather than the dedicated localized key — readable, noted but not scored as a failure |
| FW-8 | Drop folder resolution | A detected folder is used only when it exists, is writable and differs from the source folder (case-insensitive, trailing separator/drive-root normalized); the remembered folder is tried next; otherwise the source folder or Settings folder | PASS | [test] `OutputTargetTests` (6 facts), `DropFolderMemoryTests`, `DropTargetLabelsTests`; [code] `OutputTarget.cs:36-90` |
| FW-9 | Build | The audited product tree builds in Release with 0 errors; the test project builds after `286d664` | PASS | local run 2026-10-20 08:31: `dotnet build FileAction.sln -c Release --nologo -v q` → 0 errors, 10 warnings, 27.8 s. Test-only `dda1ba9` failed to compile (2 × CS9135 in `DragDropTests.cs`); `286d664` fixed it and `dotnet build tests/FileAction.Tests/... -c Release` now succeeds. Build only; tests not run. |
| FW-10 | CI test gate | Build + test runs on every branch and PR; tests are the CI gate | PASS | [code] `.github/workflows/ci.yml:24-146`; caveat: LibreOffice missing (F-19) and FFmpeg/Pandoc install is `continue-on-error` (`:118-129`), so green ≠ tools exercised |
| FW-11 | Real drag execution | The task's DropA→DropB and A→A drags with Shift/Ctrl/Alt are executed and verified | FAIL | [blocked] `_local/samples/DragDrop_A/B` exist, but no desktop/UI automation and `tools/make-samples.ps1` is absent; `DragDropTests.cs` covers the file-system pipeline and is green in CI (25/25, run [39376742559](https://github.com/Faisaldhd/dropwheel/actions/runs/39376742559)) but performs no gesture or key tap → F-21 |
| FW-12 | Multi-frame preservation | A tool that accepts GIF/animated WebP/multi-page TIFF either preserves every frame or refuses the input with a clear message | FAIL | [code] `ImageBackend.Load` uses frame 0 only, while Edit Photos already refuses GIF/ICO for exactly this reason (`EditPhotosTool.cs:15-18`); the other image tools and image edits/compress/metadata flatten silently → F-11 |
| FW-13 | Reachability of every shipped operation | Every operation class is on at least one wheel | PASS | [test] `DragDropTests.Every_registered_tool_window_is_reachable_from_the_wheel_by_its_own_id` (26/26 registered windows, CI run [39376742559](https://github.com/Faisaldhd/dropwheel/actions/runs/39376742559)); [code] `4f23c9c` added Annotate/Redact, so every operation class is in `WheelController.BuildOperations`/`FileActions.Build` |
| FW-14 | No duplicate operation ids | `Catalog.Build()` ids unique; tool registration dedupes by id | PASS | [test] `UnitTests.Catalog_ids_are_unique`; [code] `ToolRegistry.Register` |

## 11. Coverage gaps, evidence paths and follow-up tests

### 11.1 Sample pack recheck — 2026-10-20 09:35

`_local/testdata/TestFiles` still has jpg, png, webp, a 43-byte heic, gif, mp3, wav, flac, mp4, mov,
mkv, pdf, a 50-page pdf, an Arabic-named pdf, docx, pptx, xlsx, csv, srt, vtt, md, zip, rar, 7z, a
folder and a read-only jpg. `TestData.ApplyFixtures` still maps only png, jpg, pdf, md, csv, srt, xlsx,
docx, pptx, mp4, mp3, gif, zip, readonly and the folder; it does not map webp, heic, mov, mkv, flac, rar
or 7z.

The supervisor's extracted `_local/samples` pack now exists and was probed locally (Pillow 12.3,
ffprobe 9.0.2, pdfinfo/poppler 25.07, Pandoc, openpyxl/python-docx/python-pptx; read-only, no app code
executed):
- **images (32 files):** jpg/png/webp at 32×32, 320×240, 1024², 600×3000, 1920×1080, 4000×800,
  4000×3000; bmp/tiff 640×480; gif 640×480 + animated 8-frame 320×240; transparent RGBA 800×600;
  `exif_rotated_6.jpg` is stored 1600×900 with EXIF Orientation=6 (displayed 900×1600); Arabic, spaces,
  long names.
- **audio (9):** wav mono/stereo 44.1/48 kHz, mp3, flac, ogg/vorbis, m4a/aac; all 10 s; quiet, clipped,
  silence-then-tone cases.
- **video (9):** mp4 1080p/720p, mov, mkv, webm/vp9+opus, avi/mpeg4; portrait 720×1280, square 640×640
  no-audio, 3 s GIF; durations 3–8 s.
- **PDF (6):** 1, 2, 3, 5 and 12 pages; A4 portrait, A4 landscape and letter; text extracts.
- **documents (9):** txt, md, html, docx, odt, rtf, csv, xlsx (one sheet, formulas), pptx (2 slides).
- **archives (2):** zip and tar.gz with docs/images/sheets trees; **subtitles (1):** SRT with English and
  Arabic cues; **hostile (8):** 0-byte jpg, truncated jpg, text `.png`, random-bytes `.mp4`, odd names;
  **DragDrop_A/B:** two 8-file mixed trees with same names for collision cases.

**Status deltas: 0 rows changed PASS/FAIL.** The pack closes several fixture gaps but does not execute
any product path: it is not consumed by `TestData`/CI, and no tool test reads it. The remaining gaps and
the sample-pack defect are:

| Still missing / defective | Why it matters | Proposed fixture/test |
| --- | --- | --- |
| HEIC, AVIF, ICO, SVG, animated WebP, multi-page TIFF | `Formats.Images` lists them; only bmp/tiff/webp/gif gained fixtures | Add one valid file per format; frame-count/dimension probes |
| 0-page, encrypted, 300-page portrait/landscape PDFs | PDF render/split/merge edge cases (F-07) | Add the files; assert clear errors and page counts |
| WMA, standalone AAC/OPUS/AIFF; WMV/FLV/MPG/3GP/TS/MTS | Audio/video format rows remain code-verified (F-20) | Add one valid file per format and probe codec/duration |
| DOC, EPUB, PPT, ODP, XLSM, XLS, ODS, TSV, TAR, BZ2, XZ, RAR, 7z | Document/sheet/archive rows remain code-verified; no LibreOffice in CI (F-19) | Add the files and the integration theory |
| Read-only source/output folder; in-use input | Only some families have an executed read-only case; in-use has none | Add read-only files/folders and an exclusive-open fixture |
| Real OLE drag / Shift-Ctrl-Alt taps | DragDrop_A/B exists but `DragDropTests` uses the empty `_local/samples/afunc` path and no UI automation exists (F-21) | Add the desktop/UI job |
| `hostile/truncated.pdf` | It is SHA-256-identical to `pdf/single.pdf` (both D3638F…), so it is not truncated | Replace with a genuinely truncated PDF before it is used as evidence |

### 11.2 Tests that are missing or too weak

| Area | Current evidence | Proposed test |
| --- | --- | --- |
| OCR (`tools.ocr`) | none | F-16: image/PDF/multi-page/no-text/corrupt/batch |
| Crop renderer | `CropMathTests` only | F-18: exact crop rectangle at original resolution + source hash + unique |
| Audio Channels/Bleep/Visualizer | unit argument tests | F-17: real output file, channel/duration/silence/dimensions probes |
| PDF compress, media metadata, QR file load | code only | F-23 for the first two; add a QR `QrView.Load` file→decode test |
| Conversion content | `IntegrationTests` existence/length only (F-20) | probe every output by family; assert dimensions/pages/duration/text/cells/cues/tree and source SHA-256 |
| Office conversions | skipped in CI, no LibreOffice | F-19: install LibreOffice and add PPT/PPTX/ODP/XLSM/XLS/ODS fixtures |
| Drag/key/repeat | `tests/FileAction.Tests/DragDropTests.cs` (commits `dda1ba9`…`b126ae8`, 25/25 green in CI run [39376742559](https://github.com/Faisaldhd/dropwheel/actions/runs/39376742559)) covers A→B/A→A file-system writes, collisions, multi+folder pack, shape dispatch, cancel and non-file drops | F-21/F-03/F-22: add the real OLE drag + Shift/Ctrl/Alt tap + repeat + label check (the file-system suite is green) |
| Multi-select partial failure | one-item integration only | add a [good, corrupt] theory per family and assert outputs+warning |
| Multi-frame formats | sample `images/animated.gif` has 8 frames; no test drives it through a tool | F-11: 8-frame GIF + a multi-page TIFF through Crop/Compress/Metadata |

### 11.3 `DragDropTests.cs` review (`dda1ba9`, `286d664`, `13555b9`, `b126ae8`; green in CI)

`tests/FileAction.Tests/DragDropTests.cs` is committed on this audit branch and runs in the `Build + test`
job. CI run [39376742559](https://github.com/Faisaldhd/dropwheel/actions/runs/39376742559) on `b126ae8`
(the merge of `origin/main` @ `4f23c9c`) reports **25 of 25 `DragDropTests` cases passed** and an assembly
total of **1011 passed, 0 failed** (`results.trx`). It covers the file-system half of the drag pipeline:
A→B copy-style with `image.rotate.right`, A→B move-style through the Lock service, A→A with
collision-free names and source hashes, multi-file + folder `pack.zip`, shape-based tool offering,
cancellation, and `DropContent` for file/text/link drops. It does **not** perform a real OLE drag, a
Shift/Ctrl/Alt tap, repeat or the wheel label check, so it does not close W-16/FW-11; those still need the
desktop/UI job requested in F-21. The review found one test defect, which is fixed:

- **Fixed in `13555b9`.** The link test asserted `Assert.NotNull(link.Prepare)` after
  `DropContent.Read(..., materialize: false)`. The shipped `Download(url, materialize: false)` returns a
  preview `DropContent` with **no** `Prepare` (`DropReader.cs:223-239`); `Prepare` is attached only for
  `materialize: true`, and that branch creates `Downloads\DropWheel` and needs the network, so the test
  deliberately does not execute it. The test now asserts exactly one `picture.png` item and
  `Prepare == null`, and it passes in the run above; appendix row D-04 is PASS.
- After the merge, the same file also asserts that all **26** registered windows are reachable by id
  (`Assert.Empty(missing)`), which passes on `4f23c9c`.

The test file is executed evidence now; its rows are the "Drag and drop - wheel and tools" appendix at the
end of this document.

### 11.4 Evidence index

- **Audited tree:** `268a2d7` on `origin/main`; the branch was rebased append-only onto `bc204c4`
  (Lock fixes #27 + the other merged audit lanes) so the product code in this worktree is `bc204c4`.
  Worktree: `C:\Users\FaisalSaeedMohammedD\Downloads\DropWheel_1\.worktrees\afunc`.
- **Build evidence:** local command `dotnet build FileAction.sln -c Release --nologo -v q`,
  2026-10-20 08:31 on `268a2d7`, exit code 0, 10 warnings, 0 errors, 27.8 s (session job
  `pwsh-0be04e2a40bf4356`). Test-only `dda1ba9` first failed with 2 × CS9135 in `DragDropTests.cs`;
  `286d664` fixed it. After the rebase onto `bc204c4`, `dotnet build FileAction.sln -c Release` at
  09:42 is **0 errors, 11 warnings** (the extra warning is `SelfTest.cs`'s obsolete `Language.Czech`).
  No file was written outside this document.
- **Test evidence:** the test files under `tests/FileAction.Tests/` named in each row; the CI job that
  runs them is `.github/workflows/ci.yml:131-146`. Tests were not re-executed locally by rule.
- **Code evidence:** the `src/...:line` citations in each row and in §1.
- **Sample-pack evidence:** `_local/samples/README.txt` and the local probes in §11.1 (Pillow,
  ffprobe, pdfinfo, Pandoc, openpyxl/python-docx/python-pptx); no application code was executed and the
  pack is not consumed by CI.
- **Missing evidence:** the `[blocked]` rows in §2/§10 and the sample gaps in §11.1; these are the
  residual unknowns of this audit. `tests/FileAction.Tests/DragDropTests.cs` covers the file-system drag
  pipeline and is green in CI (25/25, run
  [39376742559](https://github.com/Faisaldhd/dropwheel/actions/runs/39376742559)); it says nothing about a
  real OLE drag or a modifier-key tap.

### 11.5 How to reproduce the checks after a fix

```powershell
# build the audited tree (no exe is run)
dotnet build FileAction.sln -c Release --nologo

# the test gate (CI runs this; do not run a built exe locally)
dotnet test tests/FileAction.Tests/FileAction.Tests.csproj -c Release --no-build `
  --filter "FullyQualifiedName~WheelDispatch|FullyQualifiedName~PdfToolDispatch|FullyQualifiedName~RepeatLastInteractive|FullyQualifiedName~ToolDropFolder|FullyQualifiedName~ToolOutputFolder|FullyQualifiedName~ImageToolPartialOutput|FullyQualifiedName~ZeroPagePdf|FullyQualifiedName~SpreadsheetWrongExtension|FullyQualifiedName~MetadataOrientation|FullyQualifiedName~AnimatedGif|FullyQualifiedName~LockDuplicateNames|FullyQualifiedName~PackDuplicateNames|FullyQualifiedName~PandocMediaCollision|FullyQualifiedName~Ocr|FullyQualifiedName~CropRunner|FullyQualifiedName~AudioChannels|FullyQualifiedName~AudioBleep|FullyQualifiedName~AudioVisualizer|FullyQualifiedName~ConversionContent|FullyQualifiedName~CompressPdf|FullyQualifiedName~MetadataMedia"
```

The filters above include the not-yet-written tests (they currently match 0 tests); after a fix PR the
same command must run the new tests and fail on the old tree. For the real drag flows, the only valid
reproduction is the desktop/UI job requested in F-21; the test suite must not pretend to cover it.

## 12. Status summary

- Audit rows: 488 (442 PASS, 46 FAIL). Drag appendix: 33 rows (32 PASS, 1 FAIL), executed in CI run
  [39376742559](https://github.com/Faisaldhd/dropwheel/actions/runs/39376742559) (25/25 `DragDropTests`
  cases green); the one FAIL is the PDF window dispatch (F-02). The preserved 2.4.0 rows above add 111
  PASS rows. Whole file after the append-only rebase: 632 rows (585 PASS, 47 FAIL).
- Sample-pack recheck (§11.1): **0 rows changed PASS/FAIL**; the pack adds fixtures/evidence but no
  executed product coverage, and `hostile/truncated.pdf` is byte-identical to `pdf/single.pdf`.
- Fix tasks: 22 (F-01…F-09, F-11…F-23; F-10 withdrawn); P0: F-02; P1: F-03, F-04, F-05 (F-01 is fixed
  upstream in `4f23c9c`);
  P2: F-06…F-09, F-11…F-13, F-15…F-21, F-23; P3: F-14, F-22.
- The matrix does **not** claim a working drag flow, a LibreOffice conversion, OCR, an audio
  channels/bleep/visualizer output, a Crop output, or any conversion content: those are the FAIL rows
  and the fix tasks above.

## Drag and drop - wheel and tools

How a drop actually flows in the shipped code: the dragged paths arrive in `DropContent.Read`
(`src/FileAction.App/Wheel/DropReader.cs:31-49`), the wheel opens with the folder under the cursor plus
the folder remembered during the drag (`WheelController.Open`, `:258-289`), the slices offered are exactly
the operations whose `AppliesTo` accepts the items (`Operations`, `:122-135`; `Model.cs:130`), the choice
dispatches to a floating tool by id (`RunOperation`, `:250-256`; `ToolRegistry.For(id, items)`,
`ToolRegistry.cs:26-27`) or to a job (`JobRunner.Start`, `:27-132`), the job resolves the destination
folder (`OutputTarget.Resolve`, `:80-90`) and passes it as `OperationContext.OutputDirectory` (`:111-112`),
and the writers create a unique name and a `.part` temp (`OutputPaths.ProduceAsync`/`Unique`,
`src/FileAction.Core/OutputPaths.cs:8-41`).

The rows below are produced by `tests/FileAction.Tests/DragDropTests.cs` (commits `dda1ba9`, `286d664`,
`13555b9`, `b126ae8`) and were executed by the `Build + test` job of CI run
[39376742559](https://github.com/Faisaldhd/dropwheel/actions/runs/39376742559) on `b126ae8`: **25 of 25
`DragDropTests` cases passed** and the assembly total was **1011 passed, 0 failed** (`results.trx`). The
counts are 32 PASS and 1 FAIL (the PDF window dispatch, F-02). Each test builds its own two-folder work
area (`A` and `B`) under `_local\samples\afunc` on a workstation and under `_audit-ci` inside the CI
workspace, and deletes it again: no %TEMP%, no Documents, no desktop, no network, no external program, no
UI thread. `(inspection)` marks a row that a desktop session would have to execute, verified here by
reading the shipped code; those rows also say "needs desktop session" so nothing interactive is claimed as
run, and no row claims a real OLE drag.

| # | Function | Evidence - real output | Status |
| --- | --- | --- | --- |
| D-01 | A file drop arrives as paths, once | `A_file_drop_arrives_as_paths_and_only_copies_when_the_file_is_materialised`: `DropContent.Read(..., materialize:false)` and `materialize:true` both return exactly 1 item with the same `photo.png` path, `Prepare` is null, and A still holds one file with the identical SHA-256 | PASS |
| D-02 | Dragged plain text is rejected, not treated as a file | `A_non_file_drop_is_rejected_without_throwing_or_writing_anything`: `"some text the user dragged from Notepad"` produces `null`, no exception, A and B both empty | PASS |
| D-03 | A non-http URL and an empty data object are rejected | same test: `ftp://example.com/file.zip` produces `null`; an empty data object produces `null` | PASS |
| D-04 | A web link is recognised as one item and a preview never downloads | `A_web_link_is_recognised_as_one_item_and_a_drag_preview_never_downloads`: `DropContent.Read("https://example.com/picture.png", materialize:false)` returns exactly 1 item named `picture.png` and `Prepare == null` - the shipped preview carries no download work (`Download(..., materialize:false)`, `DropReader.cs:223-239`), and the file does not exist on disk. The lazy `Prepare` is attached only for `materialize:true`, which would create `Downloads\DropWheel` and needs the network, so the test does not execute it | PASS |
| D-05 | Drop of 1 image offers the one-file image tools only | `Drop_shape_offers_exactly_the_tools_that_accept_it("one image")`: `tools.crop` and `tools.editphotos` offered; `tools.collage` and `tools.videotrim` refused | PASS |
| D-06 | Drop of 2 images offers the multi-image tool, not the one-file tools | same theory `("two images")`: `tools.collage` offered; `tools.crop` and `tools.editphotos` refused | PASS |
| D-07 | Drop of 1 video | same theory `("one video")`: `tools.videotrim` and `tools.videocrop` offered; `tools.crop` and `tools.collage` refused | PASS |
| D-08 | Drop of 1 audio file | same theory `("one audio file")`: `tools.audiotrim` offered; `tools.videotrim` and `tools.crop` refused | PASS |
| D-09 | A dropped folder | same theory `("one folder")`: `pack.zip` offered; `tools.crop`, `tools.compress` and `tools.collage` refused | PASS |
| D-10 | Mixed types are decided per file | same theory `("image and video together")`: for a png + mp4 drop only `pack.zip` is offered among the pinned ids; `tools.crop`, `tools.videotrim` and `tools.collage` all refuse it | PASS |
| D-11 | An empty drop offers nothing | `An_empty_drop_offers_nothing`: 0 operations for 0 items | PASS |
| D-12 | Every registered tool window is reachable from the wheel by its own id | `Every_registered_tool_window_is_reachable_from_the_wheel_by_its_own_id`: 26 registered ids, no duplicates, and after `4f23c9c` all 26 are present in `BuildOperations()` - `Assert.Empty(missing)` passes, so a registered-but-unwired window now fails CI | PASS |
| D-13 | A one-file tool is dispatched by id and rejects N files | `A_tool_that_accepts_one_file_is_dispatched_by_id_and_refuses_a_multi_file_drop`: `ToolRegistry.For("tools.editphotos", one)` returns the `tools.editphotos` spec, with two images it returns `null`; `ToolRegistry.For("tools.crop", two)` returns `null`; `For("tools.collage", one)` returns `null` and with two images returns `tools.collage`; `EditPhotosOperation.AppliesTo` is true for one and false for two | PASS |
| D-14 | The wheel never offers a tool whose own window refuses the same drop | `A_tool_is_on_the_wheel_only_when_its_registered_window_accepts_the_same_drop`: for all six drop shapes, every registered id in the offered set returns `Applies == true` | PASS |
| D-15 | A to B, copy-style: the result lands in B and A is untouched | `Between_folders_copy_style_writes_into_B_and_leaves_A_byte_identical`: the label is `Folder` for B, `OutputTarget.Resolve` returns `UseDetectedFolder` with `_b`, one output file is created in B at 80x120 (source 120x80 rotated right), and A still contains only `photo.png` with the identical SHA-256 | PASS |
| D-16 | The "save to" label names the folder the write uses | same test: `DropTargetLabels.Choose(A, B, null)` returns `Folder` / `_b`, the same folder `OutputTarget.Resolve` selects and the same folder the produced file appears in | PASS |
| D-17 | A to B, move-style: the only copy is relocated, not duplicated | `Between_folders_move_style_relocates_the_only_copy_into_B`: `ToolOutputFolder.For` returns B for drop target B and A for the source folder; `LockService.Protect(note.txt, B)` reports `SourceRemoved = true` with container `note.txt.dwenc` in B, A empty, no `*.tmp*` left in B; unlocking restores the identical SHA-256 and the text `keep me` | PASS |
| D-18 | A to A (same folder): next to the file, no overwrite, no loss | `Same_folder_drop_writes_next_to_the_file_and_never_overwrites_or_loses_data`: `Resolve(A, A)` returns `SameAsSource` with a null directory; two runs leave 3 files in A, the second name differs from the first, the first result is still byte-identical and the source keeps its SHA-256 | PASS |
| D-19 | A to A with a name collision keeps the existing file | `Same_folder_name_collision_gets_a_new_name_and_keeps_the_existing_file`: a pre-existing `photo (Rotated).png` (bytes 01 02 03 04) keeps its SHA-256 and bytes, the result is `photo (Rotated) (2).png`, the source is unchanged and A holds 3 files | PASS |
| D-20 | Several files plus a dropped folder become one archive in B | `Several_files_and_a_dropped_folder_pack_into_one_zip_in_the_drop_folder`: `pack.zip` accepts `photo.png`, `notes.txt` and the folder `bundle`; the one output is a `.zip` in B whose entries are exactly `bundle/inner.txt`, `notes.txt`, `photo.png`; `notes.txt` still reads `hello`; B holds 1 file | PASS |
| D-21 | Cancel before the job starts writes nothing | `Cancel_mid_flow_leaves_the_source_intact_and_writes_no_partial_output`: an already-cancelled `OperationContext.CancellationToken` raises `OperationCanceledException`, the source SHA-256 is unchanged and B is empty | PASS |
| D-22 | Cancel after half a write leaves no output and no `.part` | `Cancel_after_half_a_file_leaves_no_output_and_no_part_file`: a producer that writes 2048 B and then throws `OperationCanceledException` leaves 0 files in B (the temp is deleted in `OutputPaths.ProduceAsync`'s `finally`) | PASS |
| D-23 | Actions Copy / Move between folders, and the same-folder skip | `(inspection)` `FileActions.CopyOrMoveAsync` asks for a destination with `OpenFolderDialog`, then hands each item to the shell (`FileSystem.CopyFile`/`MoveFile` with `UIOption.AllDialogs`, `FileActions.cs:56-80`); an item whose destination equals its source is skipped (`:63-64`) so a same-folder action is a no-op instead of a self-overwrite. The dialog and the shell progress window cannot be driven headless. Needs desktop session | PASS |
| D-24 | Drag gesture opens the wheel and a drop on the hub cancels | `(inspection)` a modifier tap during a drag calls `WheelController.OnModifierTapped` and opens `WheelMode.Drag`; the folder under the cursor is sampled every 150 ms (`SampleDrag`, `:161-169`) and the drop target is snapshotted when the wheel opens (`:265-270`); a drop that hits no slice finishes without executing (`WheelWindow.OnDrop`). No OLE drag, key tap or window was executed in this environment. Needs desktop session | PASS |

### Per-tool drop rules

The `Applies` rules are what the wheel uses to decide whether a tool slice exists for a drop
(`WheelController.Operations`, `:122-135`), and the same rule is what the tool window re-checks when it is
opened by id (`ToolRegistry.For(id, items)`). Rows marked `(inspection)` are source-read; the rest are
asserted by `DragDropTests`.

| # | Tool | Drop rule - real output | Status |
| --- | --- | --- | --- |
| P-01 | `tools.crop`, `tools.editphotos`, `tools.qr`, `tools.compress`, `tools.metadata` | Exactly one file. `ToolRegistry.For(id, two)` returns `null` for `tools.editphotos` and `tools.crop` (D-13); the others declare `items.Count == 1` (`CompressTool.cs:37-38`, `QrTool.cs:32-33`, `MetadataTool.Applies`) `(inspection)` | PASS |
| P-02 | `tools.collage` | 2 or more images; one image is refused and a folder is refused (D-06, D-13; `CollageTool.cs:44-46`) | PASS |
| P-03 | `tools.videotrim`, `tools.videocrop`, `tools.videoframes`, `tools.videoredact`, `tools.videospeed`, `tools.videosplit` | Exactly one video (D-07 for trim/crop; the four others declare the same `items.Count == 1 && Formats.Video.Contains(...)` rule) `(inspection)` | PASS |
| P-04 | `tools.videojoin` | 2 or more videos, no folders (`VideoJoinTool.Applies`) `(inspection)`; not exercised by a test here | PASS |
| P-05 | `tools.audiotrim`, `tools.audionormalize`, `tools.audiochannels`, `tools.audiobleep`, `tools.audiovisualizer` | Exactly one audio file (D-08 for trim; the other four declare the same rule) `(inspection)` | PASS |
| P-06 | `tools.lock`, `tools.protect` | A file, a folder or several files (Lock), or one file/folder (Protect). Lock's move-style drop is executed for a file (D-17): container in B, plaintext removed from A, unlock restores the identical bytes | PASS |
| P-07 | `tools.unlock` | Exactly one `.dwenc` container, ZIP or PDF; a folder is refused (`UnlockTool.Applies`) `(inspection)` | PASS |
| P-08 | `tools.annotate` / `tools.redact` | One image each by their own `Applies`, and after `4f23c9c` both are offered from a drop: all 26 registered windows are in `BuildOperations()` (D-12) | PASS |
| P-09 | `tools.pdfmerge`, `tools.pdforganize` | Merge wants 2+ PDFs and Organize exactly 1 PDF, but both resolve their window with the first-match `ToolRegistry.For(items)` (`PdfToolOperations.cs:35-39,59-63`), so a drop of one PDF or of two images can open Lock, Protect, Collage or Compress instead of the PDF tool. Not executed here `(inspection)`; F-02 | FAIL |
