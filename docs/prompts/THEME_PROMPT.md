# DropWheel — Apply the new brand theme ("Violet Glass")

The new app icon is in `branding\`:
- `DropWheel-1024.png`: master artwork
- `DropWheel-256.png`
- `DropWheel.ico`: 16–256 px, multi-size

It shows a flat 8-slice radial wheel on a dark tile (#181220): dark slices (#241A35) with lavender outlines (#B48AE0), one solid violet slice (#8224D4) and an arrow dropping into it.
Tray icons are READY: `branding\DropWheel-tray-dark.ico` (for a dark taskbar) and `branding\DropWheel-tray-light.ico` (for a light taskbar), 16–48 px.

Apply this look to the whole app. Commit when done.

## 1. Icons
- Replace `src\FileAction.App\Assets\FileAction.ico` and `FileAction.png` with `branding\DropWheel.ico` and `branding\DropWheel-256.png`. Rename them to `DropWheel.ico` / `DropWheel.png` and update the csproj (`ApplicationIcon`), the installer (`SetupIconFile`) and every reference.
- **Tray icon:** use the provided `DropWheel-tray-dark.ico` / `DropWheel-tray-light.ico`. Pick one from the Windows taskbar theme (`HKCU\...\Themes\Personalize\SystemUsesLightTheme`), and switch live when it changes.

## 2. Colours — replace the palettes in `src\FileAction.App\UI\Theme.cs`
Keep the same `Palette` record, the same property names and the same public API. Change values only.

**Accent (replaces orange → coral):**
```csharp
public static readonly Color AccentColor  = Color.FromRgb(0x82, 0x24, 0xD4); // violet (icon highlighted slice)
public static readonly Color AccentColor2 = Color.FromRgb(0xB4, 0x8A, 0xE0); // lavender (icon outlines)
// AccentGradient stays AccentColor → AccentColor2, top-left → bottom-right.
// Light mode: use a darker accent for contrast: 0x6A,0x2B,0xB8 → 0x9B,0x5D,0xE5.
```

**DarkPalette:**
| Token | Value (ARGB) | From the icon |
|---|---|---|
| Backdrop | `E6 15 11 22` | deep purple-black tile |
| BackdropEdge | `33 B7 8A E7` | lavender rim |
| Slice | `FF 24 1A 35` | dark slice |
| SliceEdge | `26 C0 8C F0` | glass edge |
| Hub | `FF 1A 15 28` | wheel centre |
| Panel | `F7 1D 18 30` | |
| PanelEdge | `2E B7 8A E7` | |
| Text | `FF F5 F2 FA` | |
| Muted | `FF A8 9F BF` | |
| Track | `33 C0 8C F0` | |
| Page | `FF 18 12 20` | page background |
| Card | `FF 23 21 31` | |
| Hover | `1F C0 8C F0` | |
| Shadow | `0.6` | |

**LightPalette:**
| Token | Value (ARGB) |
|---|---|
| Backdrop | `F0 F6 F3 FB` |
| BackdropEdge | `1F 3D 35 5E` |
| Slice | `FF FF FF FF` |
| SliceEdge | `1F 6A 2B B8` |
| Hub | `FF FF FF FF` |
| Panel | `FA FB F9 FE` |
| PanelEdge | `1F 3D 35 5E` |
| Text | `FF 1E 15 30` |
| Muted | `FF 6E 66 80` |
| Track | `1F 6A 2B B8` |
| Page | `FF F4 F1 F9` |
| Card | `FF FF FF FF` |
| Hover | `14 6A 2B B8` |
| Shadow | `0.22` |

`Success` and `Error` stay as they are (green and red). Do not tint them violet.

## 3. Style: dark + violet, SOLID (owner decision: NO glass, NO acrylic/mica, NO frosted effects)
- **Idle slices:** solid `Slice` fill with a 1.5 px lavender (`AccentColor2`, 70% alpha) outline, like the icon. No gradients, no transparency effects.
- **Hovered slice:** solid `AccentColor` fill with white text and icon. Replace `AccentGradient` usage with solid `AccentColor` everywhere (selected slice, active tab, progress bars).
- Glow: keep it subtle. Reduce `Glow()` to BlurRadius 14 and Opacity 0.45 (dark) / 0.3 (light).
- **Default to dark mode:** dark is the brand look. Keep following Windows if the user picks "System" in Settings, but make the default setting "Dark".
- Keep all animations and timings unchanged.

## 4. Check every surface
Wheel (Convert / Tools / Actions), progress cards, shelf / Float, Settings, the wheel editor, the parameter dialogs, the tray menu and the installer wizard (`WizardStyle=modern`, plus a wizard image made from the icon if possible).

## 5. Verify
- **Contrast:** white text on `AccentColor` and on the gradient must be ≥ 4.5:1 (#8224D4 vs white ≈ 7:1 ✓). Check that Muted text on Page/Card is ≥ 4.5:1 in both modes. Adjust Muted if needed.
- **Screenshots:** run `DropWheel.exe --self-test <folder>`. It renders every window offscreen in both languages and both themes. Save the PNGs to `docs\screenshots\` and list them in CHANGES.md.
- `dotnet test` passes.
- **REA:** run `inspect_pe_resources` on `DropWheel.exe` and on the setup exe, and confirm the new icon group is embedded (sizes 16–256).

## 6. Soft corners everywhere (owner request: no sharp square corners)
- Windows (Settings, wheel editor, dialogs): on Windows 11 request rounded corners via `DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2)`; on Windows 10 use a borderless window with a `Border CornerRadius="12"` and transparent background.
- Progress cards, shelf/Float card, tooltips, menus: CornerRadius 12.
- Buttons, inputs, pills, tabs: CornerRadius 8 (pills fully rounded).
- Wheel slices: round the slice corners slightly (≈4 px arc join) so no sharp points; keep the gap between slices.
- The app icon is already a rounded tile (24% radius) in `branding\`.
