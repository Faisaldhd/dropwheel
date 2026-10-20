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
