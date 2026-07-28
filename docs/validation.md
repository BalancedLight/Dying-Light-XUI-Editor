# Validation record

Validation performed on Windows x64 on 2026-07-28:

```text
dotnet restore XuiEditor.slnx --locked-mode
dotnet build XuiEditor.slnx --configuration Debug --no-restore
dotnet test XuiEditor.slnx
  --configuration Debug --no-restore --no-build
dotnet build XuiEditor.slnx --configuration Release --no-restore
dotnet test XuiEditor.slnx
  --configuration Release --no-restore --no-build
dotnet publish src\XuiEditor.Wpf\XuiEditor.Wpf.csproj
  -c Release -r win-x64 --self-contained true --no-restore
  -o artifacts\publish\win-x64
```

Both test configurations passed all 167 tests with zero build warnings.

Coverage includes:

- byte-identical no-op saves and token-level mutation preservation
- comments, whitespace, property order, duplicate/unknown nodes, CRLF/LF, and
  encoding
- malformed XML, DTD/entities, nesting limits, duplicate IDs, invalid edits,
  and external-change conflicts
- transactional multi-command undo/redo and failed-batch rollback
- authored top-left anchors, parent-size delta behavior, opposing-anchor
  stretching, keep flags, proportional template descendants, pivots, nested
  transforms, clipping, aspect, and resolution flags
- recovered keyboard/controller sizing for `UIButtonWithHints` and
  `UIDialogButton`, anchor-aware growth, vertical command-strip alignment, and
  the common Yes/No controller branch with the alternative OK branch hidden
- compiled revision-bound layout sessions, retained nested visuals, camera-only
  pan/zoom, and live multi-selection move/rotation previews
- semantic canvas hit testing that excludes `XuiCanvas`, preserves a selected
  visual-template owner during a drag, cycles overlap owners with Alt, and
  allows selected animated-hidden bounds to remain movable
- transactional move commits that offset authored `Position` plus every
  applicable ancestor-scope `Position` key, reject malformed keys without a
  partial edit, undo as one command, and retain the semantic selection through
  the document reparse
- material profiles for stock sprite/text/button/antialias/clip/tint/group
  families, recursive masked-group material substitution, solid-color white
  images, runtime-only shapes, and aggregated unsupported-material diagnostics
- visual-library overrides, atlas/tile/nine-slice parsing, ARGB colors,
  BC-compressed and classic uncompressed BGRX DDS decoding, cache invalidation,
  precedence, provenance-aware cache identity, deterministic basename
  collisions, and missing resources
- every supported timeline property, exact stepped keys, linear/eased
  sampling, named-frame commands, loops, recursion, duplicate targets, and
  keyframe undo/redo
- independent timeline-owner scopes, remembered per-scope ticks,
  synchronized compatibility sampling, mixed-scope selection, scoped
  playback, and the `All in scope` track filter
- deterministic composed first poses for fade/expand-in scopes, including
  truthful scope-wide handling of mutually exclusive alternatives
- the deterministic stock 0/1/11/12/22-tick fixture
- extracted `menumain_pc.xui`, `menuoptionscontrolskeyboard.xui`,
  `menuskin.xui`, `intro.xui`, `menubountybrief.xui`,
  `menuyesnodialog.xui`, and the large HUD
- direct Dying Light install indexing, PAK precedence, RP6 resource lookup,
  selected-locale/English fallback, rejection of extracted localization
  binaries, explicit project-locale overrides, input glyphs, selected-locale
  font RPACK precedence, Japanese glyph-atlas decoding, exact bitmap fonts,
  and readable system-font retention for incomplete Unicode atlases
- exact `IUIText` `%COLOR(RRGGBB)`/`%COLOR(reset)` parsing after
  localization, literal disabled/malformed tags, cached colored runs,
  per-range WPF brushes, per-glyph bitmap colors, and stock HUD evidence
- structural `data\menu`, `PakAssets\XUI`, and isolated-document asset-root
  discovery, with project definitions and DDS files taking precedence over
  configured, extracted, and installed roots
- persisted ordered Dying Light project, loose-resource, individual
  texture-definition, and RP6L RPACK sources, including a synthetic
  definition/RPACK pair resolved through the same public resolver
- a synthetic Workshop project plus the real Irisu project, including decoded
  project-local `irisu_attack_00` pixels, selected-image priority over the
  large HUD texture backlog, a real retained `ImageDrawing`, and byte-identical
  source isolation
- the real `hud_dw` texture definition and DDS source for the 20×20
  `aggro_skull` atlas region (the similarly named `hud_dl` is not used)
- composed runtime text placeholders, hidden-node reveal rules, removal of the
  toolbar preview-preset selector, and source-byte isolation
- indexed effective-state explanations for authored, animated, controller,
  ancestor, opacity, clipping, and off-canvas visibility, plus ancestor-aware
  force-show and composed-pose recovery without selection-time layout samples
- lossless, undoable visual-child insertion with typed group, image, text,
  antialiased rectangle, and stock-button presets plus validated custom XML,
  duplicate-ID rejection, and correct placement before timeline structures
- lossless, undoable identity-parent wrapping and raw-property insertion,
  including duplicate-ID/property rejection, retained selection, and placement
  of the effective-state explanation beneath the Animation slider
- 10,000-node hierarchy virtualization, fixed 24-pixel rows, expansion/filter
  state, selection synchronization, persistent hierarchy rows, direct and
  inherited eye/lock states, settings/pane persistence, recovery isolation,
  fixed-DPI WPF rendering, and absence of audio APIs
- reversible editor-only `Hide all except this` isolation that retains the
  selected node's ancestors and subtree without changing document bytes
- constant-time syntax-key and nearest-scope lookup, 100 warm cross-scope
  selections without layout sampling or hierarchy resets, and lazy Raw XML
  with an explicit gate for subtrees over 256 KiB
- dark editor-owned templates for menus, combo boxes, context menus, tooltips,
  scrollbars, and other native WPF controls
- texture-only visual invalidation, frozen-scope animation caching,
  scope-target property deltas, incremental transform/show/opacity/paint
  propagation, retained-visual updates, and correctness-first full-evaluation
  fallbacks during selection-scoped playback
- bounded visible-texture scheduling and temporary flattened viewport caching
  during repeated HUD pan/zoom input, with deferred resource redraws
- transparent 2× design-resolution PNG export of the current retained XUI
  pose, including visibility/alpha preservation and exclusion of canvas chrome,
  reference imagery, editor overlays, and unknown-control bounds
- independent Animation-header rows that keep transport controls at their
  authored height when the effective preview-state explanation is visible
- the single-file publish contract and embedded multi-resolution icon

The current installation at
`E:\SteamLibrary\steamapps\common\Dying Light` exposed 174 stock XUIs and
8,793 install assets. With the configured optional extracted roots, the live
editor indexed 24,735 assets. The install-only English acceptance resolver
indexed 23,766 PAK-backed strings; extracted localization binaries no longer
enter the catalog, while structurally identified project locale folders can
still add explicit overrides. Stock `hud.xui` opened read-only with 4,061
nodes and 1,896 timelines. The gameplay-HUD acceptance context resolved
installed imagery, populated sample health/medkit/quest values, and did not
produce the former false `XUI-TL005` Const0/Const1 diagnostics.

The stock DLC file `data/menu/hud/hud_btz.xui` is malformed at its source
(`TimelineProp` is closed as `Timeline</Prop>` near line 4255). The parser
rejects it safely. All other 173 currently installed stock XUIs parse and
evaluate without mutation.

The final `win-x64` publish contains one file and no sidecars:

```text
DyingLightXuiEditor.exe
65,560,813 bytes
SHA-256 2F5344B532420FEC442D664EA978C5675F1141139D4B906A1132DC89FE1871B0
```

The self-contained executable was launched without Unity or an installed .NET
runtime. Its PE resources contain one multi-resolution icon group and ten icon
images. The desktop smoke test opened extracted `menuyesnodialog.xui` read-only:
the dialog remained at its authored position, Yes and No were separated and
fully textured, and the common runtime profile hid OK. It then opened the real
4,066-node, 1,898-timeline Irisu Workshop `hud.xui`, discovered the project's
`data` root, resolved `I_Irisu_00` to the project-local `irisu_attack_00`
definition/DDS, and rendered the red attack overlay immediately from the
composed `HUD_DI=0`, `HudZoneInfoDI=3`, and `G_Group=1` state. Its active image
scope exposed 3 tracks and 5 named frames rather than constructing the
document-wide timeline, and three warm WPF samples stayed within the 100 ms
30-FPS budget while updating at most four retained presentations. Both
documents remained clean. A hidden single-file smoke start reached input-idle
before its exact spawned process was cleaned up; Dying Light and Dying Light
Player were not launched.

The controlled Player comparison is documented in
[runtime-comparison.md](runtime-comparison.md).
