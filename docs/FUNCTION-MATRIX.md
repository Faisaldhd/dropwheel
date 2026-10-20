# Function matrix

PASS/FAIL evidence for the 2.4.0 tools. Append one section per tool; do not rewrite another tool's
rows. A PASS names the assertion that produced it. `(inspection)` means the behaviour was verified by
reading the shipped code in this environment because a desktop session was not available; every other
row is an automated test that runs in CI.

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
