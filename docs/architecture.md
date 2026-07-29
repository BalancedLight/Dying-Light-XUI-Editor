# Architecture

## Projects

`XuiEditor.Core` is independent of WPF. It owns the contracts that need exact,
fast tests:

- `XuiDocument`, `XuiSyntaxNode`, and ordered `XuiPropertyEntry`
- `IXuiCommand` and document history
- `DyingLightLayoutEngine`, compiled `DyingLightLayoutSession`, and
  `XuiMaterialCatalog`
- `XuiButtonLayoutProfile`, `XuiControllerRuntimeProfile`, and
  `XuiDocumentAssetContext`
- `IAssetResolver` and Dying Light resource models
- `IXuiAssetCatalog` and workspace-safe resource transactions
- `XuiClassCatalog`, typed property/class definitions, and evidence metadata
- `XuiTextStyleCodec`, pivot editing, and navigation path resolution
- `XuiTimeline`, tracks, keyframes, named frames, and `TimelineEvaluator`
- `XuiTimelineScopeCatalog`, `XuiTimelineEvaluationState`, and the
  selection-bound timeline workspace

`XuiEditor.Wpf` supplies the desktop shell. A revision-bound hierarchy index
feeds a recycling, virtualized flat list rather than a recursive control tree.
The canvas retains one lightweight visual per render node below a camera
container, so panning and zooming update a transform instead of repainting
thousands of HUD nodes.

Syntax nodes are indexed by stable key and source start. Timeline scopes are
indexed onto selectable nodes during compilation. A selection snapshot then
resolves nodes, IDs, breadcrumb, and scope once; changing selection updates
only the inspector, timeline, and selection overlay. It does not request a new
layout sample.

`XuiEditor.Tests` exercises the core directly and starts WPF only for focused
fixed-DPI rendering and workspace behavior tests.

## Lossless document model

Loading retains the original byte array and records source spans for editable
tokens. Syntax nodes retain ordered properties and child nodes, including
duplicates and unknown content. Comments, whitespace, XML attributes, newline
style, and encoding therefore stay outside an edit unless the user actually
changes the corresponding token.

All mutations implement `IXuiCommand`. A command validates its proposed
change before committing it, records an inverse operation, and is shared by
the inspector, hierarchy, transform handles, and timeline editor.

Saving follows this sequence:

1. Refuse a direct save when the source is under a protected game or extraction
   root.
2. Refuse malformed or transactionally invalid edits.
3. Verify that the source file has not changed externally.
4. Apply only the validated source-span patches.
5. Write a temporary file in the destination directory.
6. Atomically replace the destination and retain one backup.

An unchanged document returns its original bytes and is never rewritten.
Recovery files live below `%LocalAppData%\DyingLightXuiEditor\Recovery`; they
never replace the source document.

## Semantic catalog and inspector

`XuiClassCatalog` is loaded from a facts-only embedded JSON resource generated
from the Dying Light stock corpus, Dying Light binary metadata, and separately
tagged shared Chrome 6 evidence. The runtime has no dependency on extracted
research files. Class inheritance supplies applicable property definitions and
defaults; authored properties remain ordered syntax entries in the lossless
document.

The Common inspector combines authored properties with useful inherited
defaults. Advanced exposes the complete inherited schema. A ghost default is
presentation state only: editing it inserts a property command, while Reset
removes the authored token and reveals the default again. Unknown properties
remain editable through a deliberately separate raw route.

`XuiTextStyleCodec` mutates only proven legacy bit masks and returns the raw
value, decoded flags, and unmapped bits. Standalone text properties take
preview precedence when present. `XuiPivotEditing` owns unrestricted pivot
presets and the preserve-position transform equation so the viewport and
timeline command paths share one tested implementation.

## Layout evaluation

`DyingLightLayoutEngine.Evaluate(document, viewport, tick)` produces immutable
render nodes containing:

- declaration order and source identity
- local and world transforms
- local and transformed bounds
- inherited opacity and visibility
- clipping and mask information
- resolved text/image/control presentation data
- explicit diagnostics

The authored coordinate system is top-left and normally 1280×720. Parent size
changes affect evaluated transforms; they do not rewrite descendant XUI
properties.

The WPF canvas consumes this result and has no authority to reinterpret the
document. This keeps layout behavior deterministic and testable without a UI
thread.

The desktop keeps a `DyingLightLayoutSession` while the document and asset
resolver revisions remain unchanged. Timeline parsing and immutable metadata
are reused during playback; document or asset-index changes invalidate the
session. Render frames are diffed by stable node key, and completed textures
redraw only the visible visuals that reference them. Texture work uses a
bounded priority queue: a selected visible image is promoted ahead of the
ordinary HUD backlog, while invisible nodes wait until first reveal.

`SampleWithChanges` returns an `XuiRenderSample` containing the immutable frame,
changed render keys, and whether the full evaluator was required. When exactly
one scope tick changes, the session compares only that scope's cached target
values. Paint/text changes update their node; show and opacity propagate
through that subtree; transform changes recompute that transform subtree.
Width, height, layout-sensitive text, resource/material changes, unknown
dependencies, document edits, and render-context changes deliberately fall
back to a full evaluation.

Materials resolve to explicit default-alpha, text, clip, tint,
group-pass-through, runtime-generated, or unsupported profiles. Recovered
`UIMaskedGroup::ApplyMaterials` behavior recursively substitutes its image,
text, and antialiased-rectangle materials when `ForceMaterials` is enabled.

## Asset resolution

Roots are indexed once and resolved in this order:

1. the opened document's discovered project root
2. writable workspace
3. configured Dying Light project, loose-resource, loose-mod, and extracted
   folders in user-defined order
4. standalone texture-definition and RP6L RPACK sources
5. selected Dying Light installation
6. bounded placeholders

The install source indexes loose `DW*\Data` overrides, every non-language
`Data*.pak` layer (including numeric patch and DLC packs), the selected locale
plus English fallback, base `DW*\Data\menu*_PC.rpack` resources, and the
selected locale's higher-precedence
`DW*\Data<locale>\Data\menu*_PC.rpack` resources. Installed entries are
virtual, read-only files; the source archive is reopened for each bounded read
and is never modified. The searchable stock-XUI browser opens these entries
without first extracting them.

`IXuiAssetCatalog` is intentionally separate from `IAssetResolver`: resolution
consumers and existing test doubles do not acquire authoring responsibilities.
The catalog adds browse metadata and bounded Copy to Workspace. Loose-resource
create, rename, recoverable delete, reference preflight, backup, and atomic
replace/undo operations are confined to the configured workspace. Preflights
retain exact property-node identities and source hashes, and recovery/backup
folders are excluded from indexing; PAK and RPACK sources never become
writable.

Standalone `.def` and texture-definition `.scr` sources retain a structurally
discovered project/data root for provenance and DDS lookup. Standalone
`.rpack` sources expose only bounded type-32 texture entries. Both are indexed
read-only and are ordered ahead of the install source. Folder and file source
order is persisted rather than being silently resorted by enum value.

`ClassOverride` and `Visual` names can resolve through XUI visual libraries
such as `menuskin.xui`. Texture definitions support `Texture`, `Whole`,
`Rect`, `RectWithCorner`, atlas rectangles, corner/edge/tile roles, rotations,
and flips. Texture regions retain their definition root and relative path so
same-named project and installed resources cannot lose provenance. DDS data is
decoded with pinned `BCnEncoder.Net` 2.3.0, plus a bounded direct decoder for
classic uncompressed 32-bit RGB/RGBA DDS, and cached by content and definition
identity. RP6 type-32 texture resources are reconstructed as standard DDS
streams before decoding; this is how stock HUD references resolve
`hud_dw.dds`.

The selected installed localization catalog is parsed with declaration order
and duplicate-key diagnostics. Installed `basicfonts.scr`, `fontstyles.scr`,
`.fm` glyph metrics, game input-glyph catalogs, and the corresponding DDS
font atlases from the selected locale provide exact bitmap text when present.
A user mapping or language-appropriate Unicode system font is an explicit
diagnosed fallback.

The resolver never silently invents a successful result. Missing, ambiguous,
probabilistic, corrupt, and approximate resources carry diagnostics into the
viewport and diagnostics panel.

## Timeline system

Timeline time is an integer 60 Hz tick. Tracks retain their exact raw property
name and optionally expose a known evaluator kind, so properties unknown to
the current preview are never discarded. Parsing follows the real child-node
structure under `Timelines`, `Timeline`, `KeyFrame`, `NamedFrames`, and
`NamedFrame`.

Numeric interpolation code `0` is linear. Code `2` applies the authored
`EaseIn`, `EaseOut`, and `EaseScale` values. Boolean, string, image, font, and
material transitions are stepped, including switching to an intermediate key's
new value on its exact tick. Named-frame commands support `stop`, `goto`,
`gotoandstop`, and `gotoandplay`, with duplicate-target, unknown-command,
recursion, and cycle diagnostics.

Every element that owns a `Timelines` child is compiled into an independent
`XuiTimelineScope`. The editor keeps a remembered integer tick for each scope,
activates the deepest applicable owner for the current selection, and samples
unrelated scopes at their own remembered ticks. A mixed-scope selection
disables playback and mutation rather than synchronizing unrelated controllers.
**All in scope** expands only the active owner's targets, and named-frame
markers and commands are likewise scope-local. Visual-library animations are
resolved independently at tick zero.

For a useful first view, the desktop initializes each document scope to a
deterministic editor-only composed tick. Candidate key ticks are scored from
their sampled `Show`, `Opacity`, `Scale`, and color-alpha values, and the
earliest maximum is selected. This settles ordinary fade/expand-in sequences
without executing named-frame commands, coordinating playheads, or changing
the XUI. Mutually exclusive sequences remain mutually exclusive because one
tick is selected for the whole owner scope. The timeline header marks this
state as `composed`; **Stop** writes an explicit tick `0` for only the active
scope.

Per-scope animation overrides are cached by owner and tick. Sampling a new
tick re-evaluates only the changed scope; frozen scopes reuse immutable
override maps. The WPF viewport similarly skips unchanged retained-visual
presentation, resource, camera, grid, ruler, selection, hidden-state, and
diagnostic work.

During repeated pan or zoom input, the viewport applies WPF `BitmapCache` only
to the node layer beneath the camera transform. Texture completions are
deferred until the camera gesture ends, so a late asynchronous DDS does not
invalidate the flattened HUD while it is moving. Timeline or document changes
drop the temporary cache immediately, preserving incremental animation.

The integer overload of `DyingLightLayoutEngine.Evaluate` and
`DyingLightLayoutSession.Sample` remains a synchronized compatibility API.
The desktop uses `XuiTimelineEvaluationState` instead. Preview-scenario and
controller properties are applied after sampled timeline values, so curated
runtime placeholders remain higher-priority.

Some runtime UI controllers intentionally coordinate nested scopes. The editor
does not fabricate playback coordination. The composed first view positions
each independent scope at its earliest useful visible tick, so project-local
textures can appear immediately without coupling parent and child playback.
Scrubbing either scope remains independent, and the named-frame **Go to**
action still provides exact manual positioning without executing a frame
command.

Keyframe and marker edits use the same command history and source-patching
pipeline as inspector edits.

## Desktop state

Hierarchy rows persist for a document revision. Expansion changes synchronize
only the changed contiguous branch, while filtering performs one batched
reset. Eye and padlock controls expose direct and inherited editor-only states
with shape, color, tooltips, focus feedback, and accessibility names. Inherited
states identify the responsible ancestor. These states never dirty the
document. **Hide all except this** stores the current direct-hide set, hides
only the top-level excluded branches, keeps the selected subtree and its
ancestor path visible, and supports exact restoration.

The Raw XML editor is lazy. Collapsed selections do not allocate subtree text;
expanding automatically loads up to 256 KiB, while larger subtrees require an
explicit load before the editable WPF text box is populated.

Panel sizes, viewport options, recent files, the selected install and locale,
input-glyph scheme, preview scenario, reference-overlay opacity, writable
workspace, ordered folder roots, standalone texture-definition/RPACK sources,
and font mappings are persisted in:

```text
%LocalAppData%\DyingLightXuiEditor\settings.json
```

Programmatic expansion, filtering, selection, playback, and property changes
do not call any audio API.
