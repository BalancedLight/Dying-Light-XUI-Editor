# Dying Light XUI Editor

A standalone Windows editor for Dying Light XUI documents, rebuilt with
.NET 10 and WPF. It replaces the old Unity editor without changing or requiring
the legacy Unity project.

The editor is intentionally Dying Light-specific. Dead Island sources may be
useful supporting evidence, but the application does not claim Dead Island
compatibility.

## Run the portable build

The self-contained Windows x64 build is one executable:

```text
artifacts\publish\win-x64\DyingLightXuiEditor.exe
```

It does not require Unity or an installed .NET runtime, and it does not need
framework DLLs beside it. Native WPF components are unpacked to .NET's
per-user single-file cache when the editor starts. Dying Light assets are not
bundled.

On first use:

1. Open **File > Dying Light Data**.
2. Choose the Dying Light folder containing `DyingLightGame.exe` and
   `DW\Data0.pak`.
3. Select the preview language and keyboard/controller prompt set.
4. Optionally choose a writable mod workspace or add loose mod roots.
5. Use **File > Open Stock XUI** to browse the installed screens, or open a
   loose `.xui` file directly.

No separate extraction is required for stock XUI, strings, fonts, or menu
textures. The editor indexes loose installed data, base and patch PAKs, DLC
PAKs, the selected locale with English fallback, and menu RP6 RPACKs. These
sources are always read-only. Opening an installed or extracted file requires
**Save As** into the writable workspace before it can be saved.

## What is implemented

- Lossless XML loading and token-level editing. Unchanged documents are never
  rewritten, and edits preserve unrelated bytes, comments, whitespace,
  duplicate properties, unknown nodes, line endings, and encoding.
- Atomic same-directory saves with one backup, external-change detection,
  undo/redo, recent files, and isolated recovery snapshots.
- An indexed, virtualized, fixed-height hierarchy with stable expansion
  state, debounced search, collapse/reveal commands, breadcrumbs,
  visibility/lock switches, and synchronized selection.
- A retained `DrawingVisual` canvas with transform-only pan/zoom, fit, actual
  pixels, rulers, grid, safe-area overlay, snapping, declaration-order
  compositing, clipping, selection bounds, and live transform handles.
- Typed property groups plus a raw/unknown escape hatch. Invalid values remain
  visible with diagnostics instead of being silently normalized.
- Dying Light anchors, pivots, transforms, opacity/show inheritance, keep and
  resolution flags, stack/wrap-panel layout, visual templates, stock control
  families, evidence-backed material profiles, forced-mask substitution, DDS
  textures, atlases, tilesets, and nine-slice definitions.
- Direct install-backed resolution for PAK and RP6L/RPACK assets. In
  particular, HUD definitions referring to `hud_dw` resolve the real
  `hud_dw.dds` atlas from the installed menu packs.
- Installed localization catalogs, `basicfonts.scr`, `fontstyles.scr`, `.fm`
  bitmap metrics, private input glyphs, and font-atlas DDS resources. Exact
  engine bitmap fonts are used when available; mappings and diagnosed
  fallbacks remain available.
- Curated preview presets for hidden/runtime-populated HUD text and imagery,
  per-node force-show controls, and a variable-opacity reference
  screenshot overlay. Scenario data affects only the preview and never
  rewrites the XUI.
- Full 60 Hz timeline parsing, playback, sampling, keyframe editing,
  interpolation/easing, named frames, loop diagnostics, and undoable timeline
  commands.
- No background music, UI sounds, novelty transparency, or blocking alpha
  warning.

Unknown engine-only controls are transparent in the preview. Optional
editor-only bounds and searchable diagnostics identify them without putting
fake labels into the rendered scene.

## Common controls

| Action | Shortcut |
| --- | --- |
| Open / save / save as | `Ctrl+O` / `Ctrl+S` / `Ctrl+Shift+S` |
| Undo / redo | `Ctrl+Z` / `Ctrl+Y` |
| Duplicate / delete | `Ctrl+D` / `Delete` |
| Move before / after sibling | `Alt+Up` / `Alt+Down` |
| Indent / outdent | `Alt+Right` / `Alt+Left` |
| Fit / actual pixels | `F` / `0` |
| Zoom in / out | `+` / `-` |
| Focus hierarchy search | `Ctrl+F` |
| Play or pause | `Space` |
| Previous / next tick | `,` / `.` |
| Copy / paste keyframe | `Ctrl+Alt+C` / `Ctrl+Alt+V` |

Drag the canvas to pan, use the mouse wheel to zoom, and use Ctrl-click or
Shift-click for multi-selection.

## Build and validate

The SDK is pinned by `global.json`; NuGet versions and lock files are
committed.

```powershell
dotnet restore XuiEditor.slnx --locked-mode
dotnet test XuiEditor.slnx -c Debug --no-restore
dotnet test XuiEditor.slnx -c Release --no-restore
dotnet publish src\XuiEditor.Wpf\XuiEditor.Wpf.csproj `
  -c Release -r win-x64 --self-contained true --no-restore `
  -o artifacts\publish\win-x64
```

The project enables .NET single-file publishing and embeds the supplied
multi-resolution XUI icon, so the publish directory contains
`DyingLightXuiEditor.exe` as the distributable application.

The solution contains:

- `src\XuiEditor.Core` — framework-independent document, command, layout,
  asset, and animation logic.
- `src\XuiEditor.Wpf` — the Windows desktop workspace and visual-layer
  renderer.
- `tests\XuiEditor.Tests` — parser/writer, layout, asset, timeline, corpus,
  performance, recovery, and WPF UI tests.

## Technical notes

- [Architecture](docs/architecture.md)
- [Recovered rendering evidence](docs/rendering-evidence.md)
- [Runtime comparison](docs/runtime-comparison.md)
- [Known approximations](docs/known-approximations.md)
- [Validation record](docs/validation.md)

The extracted game data and decompiles are research inputs only. They are not
redistributed by this project.
