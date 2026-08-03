<p align="center">
  <img
    src="https://github.com/user-attachments/assets/1cb1d515-c755-4456-bd4c-3ac5e4373d8e"
    alt="XUI Editor logo"
    width="1280"
  />
</p>

<p align="center">
   A standalone Windows editor for Dying Light XUI documents.
</p>

## Run the portable build
You can either run the release, an artifact from the [Actions](https://github.com/BalancedLight/Dying-Light-XUI-Editor/actions) page, or build the executable yourself.

The self-contained Windows x64 build is one executable:

```text
artifacts\publish\win-x64\DyingLightXuiEditor.exe
```

It does not require Unity or an installed .NET runtime, and it does not need
framework DLLs beside it. Native WPF components are unpacked to .NET's
per-user single-file cache when the editor starts. Dying Light assets are not
bundled.

On first use:

1. Open **Settings > Dying Light Resources**.
2. Choose the Dying Light folder containing `DyingLightGame.exe` and
   `DW\Data0.pak`.
3. Select the preview language and keyboard/controller prompt set.
4. Optionally choose a writable mod workspace, add Dying Light project or
   loose-resource folders, and add standalone texture definitions or RP6L
   `.rpack` files.
5. Use **File > Open Stock XUI** to browse the installed screens, or open a
   loose `.xui` file directly.

No separate extraction is required for stock XUI, strings, fonts, or menu
textures. The editor indexes loose installed data, base and patch PAKs, DLC
PAKs, the selected locale with English fallback, and both base and
locale-specific menu RP6 RPACKs. These sources are always read-only. Opening
an installed or extracted file requires **Save As** into the writable
workspace before it can be saved.

## What is implemented

- Lossless XML loading and token-level editing. Unchanged documents are never
  rewritten, and edits preserve unrelated bytes, comments, whitespace,
  duplicate properties, unknown nodes, line endings, and encoding.
- Atomic same-directory saves with one backup, external-change detection,
  undo/redo, recent files, and isolated recovery snapshots.
- An indexed, virtualized, fixed-height hierarchy with stable expansion
  state, debounced search, collapse/reveal commands, breadcrumbs, and
  synchronized selection. Rows can be dragged onto other elements to
  reparent them, onto a sibling's upper or lower edge to reorder them, or
  onto hierarchy whitespace to return them to the canvas root; the move is
  validated and undoable. Eye and padlock icons distinguish direct and
  inherited editor-only visibility/lock states without modifying the XUI.
  A row context menu can hide everything except that item and its subtree,
  then restore the exact prior visibility state.
- A retained `DrawingVisual` canvas with transform-only pan/zoom, fit, actual
  pixels, rulers, three independently colored grid tiers, the stock 90%
  safe-area overlay, snapping, declaration-order compositing, clipping,
  selection bounds, and hidden-editor-style move, resize, rotation, and pivot
  handles. Camera
  gestures temporarily flatten the retained HUD layer so a 4,000-node canvas
  does not have to be recomposited for every pointer move. Dragging inside an
  already-selected element keeps that semantic XUI owner even when a visual
  template or overlapping canvas node is painted above it; Alt-click cycles
  overlapping owners. Committing a move, resize, or rotation also preserves
  the selection while the lossless document model is reparsed.
- One-click transparent PNG export at 2× authored design resolution. The
  lossless PNG contains the current visible XUI pose, textures, text,
  transforms, clipping, opacity, and editor hide overrides without the editor
  background, reference image, grid, rulers, selection handles, or
  unknown-control bounds.
- An embedded, facts-only Dying Light class/property catalog drives the typed
  Common and Advanced inspector views. Unauthored inherited defaults appear as
  ghost values and serialize only after editing; Reset removes the authored
  property. Add Property is searchable, class-filtered, typed, and retains an
  explicit raw/unknown escape hatch. Evidence, default source, animation
  support, and preview accuracy are visible in inspector tooltips. Invalid
  values remain visible with diagnostics instead of being silently normalized.
  Authored properties can be copied as a set, individual rows can be copied
  directly, and Advanced Copy offers a searchable picker that can also include
  inherited defaults. Paste filters the set against each selected destination
  class, protects `Id` and `ClassOverride`, skips incompatible values, and
  commits a multi-element paste as one undoable edit.
  Raw XML is
  materialized only when expanded; subtrees over 256 KiB require an explicit
  load action. `IUIText` nodes expose a typed
  `ColorControlSequenceEnabled` checkbox. The inspector and hierarchy provide
  undoable **Add child** and identity-preserving **Add parent** workflows for
  groups, images, text, antialiased rectangles, stock buttons, and validated
  custom XML. **Add property** inserts a validated raw property on the selected
  node without rewriting unrelated source bytes. The element palette also
  exposes the stock class catalog and its observed default dimensions.
- A semantic text section decodes legacy `TextStyle` into bold, italic,
  underline, horizontal alignment, and vertical alignment while preserving
  every unmapped bit. Decimal and hexadecimal raw editing remains available.
  Existing standalone formatting/alignment properties remain standalone and
  override the bit-derived preview; Dying Light bottom alignment authors
  `VerticalAlignDown`.
- Dying Light anchors, pivots, transforms, opacity/show inheritance, keep and
  resolution flags, stack/wrap-panel layout, visual templates, stock control
  families, evidence-backed material profiles, forced-mask substitution, DDS
  textures, atlases, tilesets, and nine-slice definitions. Pivot authoring
  supports unrestricted XYZ values, a 3x3 preset grid, raw-runtime and
  preserve-visual-position modes, timeline-key rebasing, and an explicit
  animated-scale/rotation restriction.
- Hidden-editor visibility tools select the parent group, mask or gray outside
  its bounds, force-show the current group or all elements, and include or
  exclude design-time elements. Six-way navigation links resolve direct IDs
  and `..\` paths, visualize missing/ambiguous links, and support safe drag
  assignment or clearing without guessing.
- Direct install-backed resolution for PAK and RP6L/RPACK assets. In
  particular, HUD definitions referring to `hud_dw` resolve the real
  `hud_dw.dds` atlas from the installed menu packs.
- Automatic custom-project resolution. Opening an XUI below
  `data\menu\...` indexes that project's `data` tree first, including sibling
  texture definitions and nested DDS files, before configured mod,
  extraction, and install sources. Loader-owned `PakAssets\XUI` files resolve
  through the sibling `PakAssets` tree.
- Persistent resource settings for additional Dying Light project folders,
  loose-resource trees, extracted roots, individual `.def`/texture-definition
  `.scr` files, and individual RP6L `.rpack` containers. Explicit sources
  override installed game assets and remain read-only where appropriate.
- Installed localization binaries come only from the selected Dying Light
  language PAKs, with English fallback; extracted copies cannot override the
  chosen language. Explicit `Locale\<language>` catalogs in configured
  projects, workspaces, loose-resource roots, or RPACKs remain supported.
  `basicfonts.scr`, `fontstyles.scr`, `.fm` bitmap metrics, game input
  glyphs, and locale-specific font-atlas DDS resources are indexed alongside
  them. Exact engine bitmap fonts are used when available; Unicode-capable
  Windows families are used as diagnosed CJK/Thai fallbacks while an atlas is
  unavailable, and an incomplete atlas cannot replace readable Unicode with
  question-mark glyphs. Enabled `%COLOR(RRGGBB)` and
  `%COLOR(reset)` sequences render as per-run system-font and bitmap-font
  colors; disabled or malformed markup stays literal like the game.
- A separate asset catalog browses screens, visuals, textures, and fonts
  through the existing resolver. Texture drops can create `MyImage` nodes or
  rebind `ImagePath`; auto-size is always explicit. Installed PAK/RPACK
  entries stay read-only and require Copy to Workspace. Loose workspace XUI
  screens and visual libraries can be created, opened, renamed, and
  recoverably deleted. Exact references can be inspected and rebound through
  a content-hash preflighted, backed-up, undoable atomic transaction; archives
  are never rewritten.
- A composed preview for hidden/runtime-populated HUD elements, per-node
  force-show controls, and a variable-opacity reference screenshot overlay.
  A compact effective-state explanation below the Animation slider reports
  authored, animated,
  controller, ancestor, opacity, clipping, and off-canvas visibility, with
  one-click force-show and composed-pose recovery.
- Full 60 Hz timeline parsing with independent per-owner scope state,
  scope-local playback/scrubbing, exact stepped-key transitions,
  interpolation/easing, named frames, loop diagnostics, and undoable timeline
  commands. Tracks are filtered to the selected elements; **Include
  descendants** expands each selected hierarchy subtree without crossing into
  nested independent timeline scopes. Trackless selections disable transport
  instead of playing unrelated owner targets, while switching selections
  remembers each scope's local tick. New documents open in a non-destructive
  composed pose that settles each scope at its earliest fully visible key;
  **Stop** returns the active scope to authored tick 0 before playback. Safe
  timeline-only changes update just the affected retained visuals and
  transform/show subtrees, with a full-layout fallback for layout- or
  resource-sensitive changes.
- No background music, UI sounds, novelty transparency, or blocking alpha
  warning.

Unknown engine-only controls are transparent in the preview. Optional
editor-only bounds and searchable diagnostics identify them without putting
fake labels into the rendered scene.

## Common controls

| Action | Shortcut |
| --- | --- |
| Open / save / save as | `Ctrl+O` / `Ctrl+S` / `Ctrl+Shift+S` |
| Copy / advanced-copy / paste inspector properties | `Ctrl+C` / `Ctrl+Shift+C` / `Ctrl+V` |
| Undo / redo | `Ctrl+Z` / `Ctrl+Y` |
| Duplicate / delete | `Ctrl+D` / `Delete` |
| Add visual child | `Ctrl+Insert` |
| Move before / after sibling | `Alt+Up` / `Alt+Down` |
| Indent / outdent | `Alt+Right` / `Alt+Left` |
| Fit / actual pixels | `F` / `0` |
| Zoom in / out | `+` / `-` |
| Export transparent PNG | `PNG` toolbar button or File menu |
| Focus hierarchy search | `Ctrl+F` |
| Play or pause | `Space` |
| Previous / next tick | `,` / `.` |
| Copy / paste keyframe | `Ctrl+Alt+C` / `Ctrl+Alt+V` |

Middle-drag the canvas to pan, use the mouse wheel to zoom, and use Ctrl-click or
Shift-click for multi-selection.

## Build and validate

The SDK is pinned by `global.json`; NuGet versions and lock files are
committed.

```powershell
dotnet restore XuiEditor.slnx --locked-mode
dotnet test tests\XuiEditor.Tests\XuiEditor.Tests.csproj `
  -c Debug --no-restore
dotnet build XuiEditor.slnx -c Release --no-restore
dotnet test tests\XuiEditor.Tests\XuiEditor.Tests.csproj `
  -c Release --no-restore --no-build
```

Optional read-only acceptance tests take local game and extracted-data roots
from `DYING_LIGHT_INSTALL` and `XUI_EDITOR_TEST_CORPUS_ROOT`. Checked-in tests
must use portable synthetic fixtures rather than developer-machine paths or
private third-party projects.

Every successful Release build automatically refreshes the self-contained,
single-file `artifacts\publish\win-x64\DyingLightXuiEditor.exe` distributable.
No separate Visual Studio Publish action is required. The executable embeds the
supplied multi-resolution XUI icon.

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
- [Chrome 6 editor evidence](docs/chrome6-editor-evidence.md)
- [Runtime comparison](docs/runtime-comparison.md)
- [Known approximations](docs/known-approximations.md)
- [Validation record](docs/validation.md)

The extracted game data and decompiles are research inputs only. They are not
redistributed by this project.

## Disclaimer

The Dying Light XUI Editor was created with assistance from AI tools.
