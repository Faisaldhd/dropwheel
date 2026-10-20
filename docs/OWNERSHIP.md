# Ownership by surface - 2.4.0

| # | Surface | Owner agent | Independent auditor | Owned files/folders |
|---|---|---|---|---|
| 1 | Main app window + Settings (settings, About, Jobs window, tray, first-run) | Protect/Unlock agent (Team A) | Design auditor (Team B) | src/FileAction.App/{App.cs, UI/SettingsWindow.cs, UI/JobsWindow.cs, Shell/*, Update/*} |
| 2 | Wheel / rings (WheelController, Shift/Ctrl/Alt rings, ToolRegistry wiring, ring animation, hit-testing, ring icons) | Annotate agent (Team A, dispatch lane) | Motion auditor + Function auditor (dispatch parity) | src/FileAction.App/{Wheel/**, UI/Tools/ToolRegistry.cs, UI/Tools/IInteractiveTool.cs} |
| 3 | The 26 tools (logic + backends) | One owner per tool = its existing per-tool agent (Crop, Add BG, Annotate, Edit photos, Redact, Collage, Video x5, Audio x5, PDF x2, Compress, Metadata, QR, Protect/Unlock, Lock) | Function auditor (Team B); Security auditor for Lock/Protect | src/FileAction.Core per tool core + tests per tool |
| 4 | Tool popup windows (shared framework: layout, drop zone, progress, theming, en/ar RTL, DPI) | Collage agent (Team A) owns the shared framework; each tool agent owns its own view | Design auditor + Motion auditor (Team B) | src/FileAction.App/UI/Tools/{ToolWindow.cs, IToolWindowSpec.cs, ToolSizes.cs}, Theme.cs, Motion/** |