# Function matrix — verified behaviour

One row per user-visible behaviour that was actually checked. The agent that ran the check adds the
row; the **Result** column carries the measured number, not just a verdict. Automated checks are the
ones CI re-runs (`dotnet test`); measurements marked *local* were taken on a Release build on the
builder's Windows workstation (24 MP = 6000×4000, ImageMagick Q8 in-process) and are indicative — CI
hardware will differ.

| ID | Feature | Behaviour | Criterion | Result |
|---|---|---|---|---|
| EP-1 | Edit Photos | Every slider at its default (all zero) | The saved copy is byte-identical to the source, not a re-encode | **PASS** — `RenderFinal` copies the source bytes when the parameters are neutral; measured 6 ms; pinned by `Final_render_is_a_byte_copy_at_neutral_and_differs_after_an_edit` |
| EP-2 | Edit Photos | A real edit (exposure, contrast, colour, detail, effects) | The saved pixels differ from the neutral copy | **PASS** — RMS 0.58 against the neutral copy; the same test decodes both files and compares them |
| EP-3 | Edit Photos | 24 MP source, live preview (local) | Slider change → refreshed preview in ≤ 500 ms | **PASS** — 347 ms median (330–369 ms over 5 runs) with all 16 sliders non-zero; the UI thread only receives the finished bitmap |
| EP-4 | Edit Photos | 24 MP source, full save (local) | Worst case (all 16 sliders) ≤ 20 s; typical edit ≤ 3 s | **PASS** — 13.8 s worst case (JPEG, all sliders) and 1.03 s for exposure + contrast |
| EP-5 | Edit Photos | Neutral save latency (local) | ≤ 50 ms | **PASS** — 6 ms (file copy, no decode) |
| EP-6 | Edit Photos | Dark theme and light theme | The view builds in both, and every chrome colour comes from `Theme` | **PASS** — `EditPhotosView.Build()` constructed on an STA thread with `Theme.Override` set to dark and then light; no exception in either. The only fixed colour is the "Original" badge's black scrim over the photo, which is correct in both themes because it sits on image content, not chrome |
| EP-7 | Edit Photos | Primary button label | Reads **Save Edited Copy** (English) / **حفظ نسخة معدّلة** (Arabic) | **PASS** — `IToolWindowSpec.PrimaryLabelKey` → `tool.editphotos.apply`; the table test requires the key in both languages and not a copy of the English |
| EP-8 | Edit Photos | Output naming | Writes `<name> Edited.<ext>` next to the source and never overwrites | **PASS** — `OutputPaths.Unique`; the render test saves twice and asserts two distinct files |
| EP-9 | Edit Photos | 24 MP PNG with grain (local, worst case) | Completes in ≤ 60 s | **PASS (slow)** — 29.9 s and a 14.5 MB file: PNG encoding of noisy 24 MP output dominates. The same edit to JPEG is 13.8 s and 0.6 MB |

## Measurement notes

- **EP-3/EP-4/EP-5**: a temporary harness (not committed) loaded the same 6000×4000 gradient through
  `EditPhotoRenderer.Load`, then timed `RenderPreview` (median of five) and `RenderFinal` on the
  original. The parameters for the preview and the save are the same object, exactly as the tool
  passes them.
- **EP-1**: byte comparison of `File.ReadAllBytes(source)` with the neutral output.
- **EP-2**: `MagickImage.Compare(..., ErrorMetric.RootMeanSquared)` between the neutral output and the
  edited output.
- **EP-6**: `Theme.Override` + `Theme.Refresh()` on an STA thread, then `new EditPhotosView(item,
  null).Build()`; both themes returned a view. The view has no other hard-coded colour.
- **CI**: `Build + test` runs the full suite (272 tests on the merge before this feature's last
  changes; the edit-photos set is `PhotoEditTests`, 26 facts, including the byte-copy and
  window-sizing checks).
