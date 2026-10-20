# TOOL-ORDER — how the reference sequences tools, mapped to DropWheel

Reference pages: **page 0** = `p0_*` tiles (1080×41404 render, narrow page, y in tiles), **page 1** =
`p1_*` tiles (2160×14950 render, long page). Both are the same design language.

## 1. Source of the order

The reference states its own ordering in a **grouped text index at the top of page 0**
(`p0_t00_y0.png` y 290..1400, continuing in `p0_t01_y1280.png` y 0..640). It is a plain 2–3 column
link list, no icons, groups separated by generous whitespace — not a grid of cards. The long page
then repeats every tool as a full-width card in the same order (`p0_t01_y1280.png` y 980+ begins the
first card; `p1_t00_y0.png` onward is the desktop run).

## 2. Exact order and grouping

| # | Group | Entry (reference label) | Tile reference |
| --- | --- | --- | --- |
| 1 | Any file | Compress | `p0_t00_y0.png` y≈470 |
| 2 | Any file | Edit metadata | `p0_t00_y0.png` y≈470 |
| 3 | Images | Edit photos | `p0_t00_y0.png` y≈725 |
| 4 | Images | Annotate photos | `p0_t00_y0.png` y≈725 |
| 5 | Images | Add a background | `p0_t00_y0.png` y≈725 |
| 6 | Images | Crop images | `p0_t00_y0.png` y≈820 |
| 7 | Images | Redact photos | `p0_t00_y0.png` y≈820 |
| 8 | Images | Create a collage | `p0_t00_y0.png` y≈820 |
| 9 | Video | Trim video | `p0_t00_y0.png` y≈1085 |
| 10 | Video | Crop video | `p0_t00_y0.png` y≈1085 |
| 11 | Video | Change video speed | `p0_t00_y0.png` y≈1085 |
| 12 | Video | Join videos | `p0_t00_y0.png` y≈1185 |
| 13 | Video | Save video frames | `p0_t00_y0.png` y≈1185 |
| 14 | Video | Split video | `p0_t00_y0.png` y≈1185 |
| 15 | Video | Redact video | `p0_t01_y1280.png` y≈0 |
| 16 | Audio | Normalize volume | `p0_t01_y1280.png` y≈270 |
| 17 | Audio | Create an audio visualizer | `p0_t01_y1280.png` y≈270 |
| 18 | Audio | Trim audio | `p0_t01_y1280.png` y≈365 |
| 19 | Audio | Convert audio channels | `p0_t01_y1280.png` y≈365 |
| 20 | Audio | Bleep audio | `p0_t01_y1280.png` y≈365 |
| 21 | PDF | Merge PDFs | `p0_t01_y1280.png` y≈590 |
| 22 | PDF | Organize PDF pages | `p0_t01_y1280.png` y≈590 |

Five groups, 22 entries, groups ordered **Any file → Images → Video → Audio → PDF**, entries inside a
group ordered by how often a user reaches for them (the generic "any file" pair first, the risky
redaction tools last in their group), not alphabetically.

## 3. Mapping to DropWheel's registered tools

Registry: `src/FileAction.App/UI/Tools/ToolRegistry.cs` (`All` = registered, de-duplicated by `Id`).
Ids below were taken from the `Id => "tools.…"` declarations in `src/**`, **26 registered ids**.
Type names are given so the lead can re-check without grepping.

| # | DropWheel id | Spec type | Reference slot | Note |
| --- | --- | --- | --- | --- |
| 1 | `tools.compress` | `CompressTool` | Any file #1 | exact |
| 2 | `tools.metadata` | `MetadataTool` | Any file #2 | exact |
| 3 | `tools.editphotos` | `EditPhotosTool` | Images #1 | exact |
| 4 | `tools.annotate` | `AnnotateTool` | Images #2 | exact |
| 5 | `tools.addbackground` | `AddBackgroundTool` | Images #3 | exact |
| 6 | `tools.crop` | `CropTool` | Images #4 | exact |
| 7 | `tools.redact` | `RedactTool` | Images #5 | exact |
| 8 | `tools.collage` | `CollageTool` | Images #6 | exact |
| 9 | `tools.videotrim` | `VideoTrimTool` | Video #1 | exact |
| 10 | `tools.videocrop` | `VideoCropTool` | Video #2 | exact |
| 11 | `tools.videospeed` | `VideoSpeedTool` | Video #3 | exact |
| 12 | `tools.videojoin` | `VideoJoinTool` | Video #4 | exact |
| 13 | `tools.videoframes` | `VideoFramesTool` | Video #5 | exact |
| 14 | `tools.videosplit` | `VideoSplitTool` | Video #6 | exact |
| 15 | `tools.videoredact` | `VideoRedactTool` | Video #7 | exact |
| 16 | `tools.audionormalize` | `AudioNormalizeTool` | Audio #1 | exact |
| 17 | `tools.audiovisualizer` | `AudioVisualizerTool` | Audio #2 | exact |
| 18 | `tools.audiotrim` | `AudioTrimTool` | Audio #3 | exact |
| 19 | `tools.audiochannels` | `AudioChannelsTool` | Audio #4 | exact |
| 20 | `tools.audiobleep` | `AudioBleepTool` | Audio #5 | exact |
| 21 | `tools.pdfmerge` | `PdfMergeTool` | PDF #1 | exact |
| 22 | `tools.pdforganize` | `PdfOrganizeTool` | PDF #2 | exact |
| — | `tools.lock` | `LockTool` | **none** | unplaced |
| — | `tools.protect` | `ProtectTool` | **none** | unplaced |
| — | `tools.unlock` | `UnlockTool` | **none** | unplaced |
| — | `tools.qr` | `QrTool` | **none** | unplaced |

Totals: 26 registered = **22 placed** (each exactly once) + **4 unplaced**. Reference entries with no
DropWheel equivalent: **0** — every reference slot has a live DropWheel tool.

### Unplaced tools — proposed home (no reference precedent, so keep the reference's *grammar*)

- New group **"Lock and protect"** after *Any file*: `tools.lock`, `tools.protect`, `tools.unlock`.
  Put it before *Images*: it is file-level, not media-level, and grouping it with *Any file* would
  bury it under the two most generic tools.
- `tools.qr` → append to **Any file** (it is format-agnostic: reads a QR from an image or writes one).

## 4. Mapping ambiguity (read before building)

1. **`tools.ocr` exists as a spec but is not in the registered list.** `src/FileAction.App/Ocr/OcrOperation.cs`
   and `src/FileAction.Linux/TesseractOcr.cs` both build a spec with id `tools.ocr`, but no
   `ToolRegistry.Register(...)` call was found for it and it has no `Id =>` declaration, so
   `ToolRegistry.All` (26) does not include it. If it is registered in a build-conditional or
   platform-only path, it is a **27th tool with no reference slot** (nearest reference slot would be
   *Any file*, next to *Edit metadata*). Needs the lead's confirmation.
2. **`tools.audiochannels` label** — the reference calls its slot "Convert audio channels", which
   implies conversion; the DropWheel tool may also expose mixing. Copy must follow our behaviour, not
   the reference label.
3. **`tools.addbackground`** — the reference places "Add a background" last in *Images*; our tool is
   a framing tool, so its position (Images #3) is inherited, not derived from our own IA. Fine to
   keep for muscle-memory, but flag if our wheel grouping differs.
4. **`tools.videoredact` / `tools.redact`** are split across groups in the reference (redaction of
   images filed under *Images*, of video under *Video*). If DropWheel groups by *action* rather than
   *media*, they must sit together instead — a real IA fork, decide once.

## 5. Card copy template (placeholder wording only)

Anatomy per card, fixed order (measured in `p1_t02_y2560.png`, `p1_t03_y3840.png`,
`p1_t06_y7680.png`):

```
[EYEBROW]        category, 1–2 words, uppercase in EN / no case change in AR
[Title]          tool name, 1–3 words, sentence case
[One line]       <verb> <object> <constraint or outcome>.  ≤ 60 characters, ends with a period
────────────────────────────────────────────────────────────  rule: start at text column, bleed to end edge
[✓ option 1]     capability, ≤ 48 characters, verb- or noun-led, no period
[✓ option 2]     capability, ≤ 48 characters
[✓ option 3]     optional, only when a real third capability exists (reference uses 2 far more than 3)
```

Placeholder example (structure only — never ship this wording):

| Slot | Placeholder |
| --- | --- |
| Eyebrow | `GROUP` |
| Title | `<Tool name>` |
| One line | `Does <thing> to <files> without <cost>.` |
| Option 1 | `<Name of the main control the user gets>` |
| Option 2 | `<Name of the second capability, phrased as an outcome>` |
| Option 3 | `<Only if it exists>` |

Rules the template enforces:
- one line only — the reference's lede never exceeds 2 lines at its measure and usually fits 1
  (`p1_t02_y2560.png`, `p1_t06_y7680.png`);
- options name **capabilities**, not UI widgets and not adjectives;
- no tool gets more than 3 options, and 2 is the default;
- the eyebrow is the only place the group name appears — no repeated section headers between cards.

## 6. Suggested DropWheel order (reference grammar, our inventory)

1. **Any file** — `tools.compress`, `tools.metadata`, `tools.qr`
2. **Lock and protect** — `tools.lock`, `tools.protect`, `tools.unlock` *(no reference slot)*
3. **Images** — `tools.editphotos`, `tools.annotate`, `tools.addbackground`, `tools.crop`,
   `tools.redact`, `tools.collage`
4. **Video** — `tools.videotrim`, `tools.videocrop`, `tools.videospeed`, `tools.videojoin`,
   `tools.videoframes`, `tools.videosplit`, `tools.videoredact`
5. **Audio** — `tools.audionormalize`, `tools.audiovisualizer`, `tools.audiotrim`,
   `tools.audiochannels`, `tools.audiobleep`
6. **PDF** — `tools.pdfmerge`, `tools.pdforganize`

Feature the first 3 as full-width cards (reference rhythm), then the 2-up grid; the grouped index
from `p0_t00_y0.png` becomes our sticky category nav.
