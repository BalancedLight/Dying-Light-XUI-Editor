# Dying Light XUI Editor

A standalone Windows editor for Dying Light XUI documents, rebuilt with
.NET 10 and WPF. It replaces the old Unity editor without changing or requiring
the legacy Unity project.

The editor is intentionally Dying Light-specific. Dead Island sources may be
useful supporting evidence, but the application does not claim Dead Island
compatibility.

## Run the portable build

The self-contained Windows x64 build is:

```text
artifacts\publish\win-x64\DyingLightXuiEditor.exe
```

It does not require Unity or an installed .NET runtime. Dying Light assets are
not bundled.

On first use:

1. Open **File > Asset Roots**.
2. Choose a writable mod workspace.
3. Add the extracted Dying Light data root as **Extracted Dying Light**.
4. Add any loose mod roots whose assets should override the extraction.
5. Open an `.xui` file.

Extracted/game roots are always treated as read-only. Opening a file from one
of those roots requires **Save As** into the writable workspace before edits
can be saved.

## What is implemented

- Lossless XML loading and token-level editing. Unchanged documents are never
  rewritten, and edits preserve unrelated bytes, comments, whitespace,
  duplicate properties, unknown nodes, line endings, and encoding.
- Atomic same-directory saves with one backup, external-change detection,
  undo/redo, recent files, and isolated recovery snapshots.
- A virtualized, fixed-height hierarchy with stable expansion state, search,
  breadcrumbs, visibility/lock switches, and synchronized selection.
- A `DrawingVisual` canvas with pan, zoom, fit, actual pixels, rulers, grid,
  safe-area overlay, snapping, declaration-order compositing, clipping,
  selection bounds, and transform handles.
- Typed property groups plus a raw/unknown escape hatch. Invalid values remain
  visible with diagnostics instead of being silently normalized.
- Dying Light anchors, pivots, transforms, opacity/show inheritance, keep and
  resolution flags, visual templates, stock control families, DDS textures,
  atlases, tilesets, nine-slice definitions, and font mappings.
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
