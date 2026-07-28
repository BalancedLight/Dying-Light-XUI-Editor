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
- `XuiTimeline`, tracks, keyframes, named frames, and `TimelineEvaluator`

`XuiEditor.Wpf` supplies the desktop shell. A revision-bound hierarchy index
feeds a recycling, virtualized flat list rather than a recursive control tree.
The canvas retains one lightweight visual per render node below a camera
container, so panning and zooming update a transform instead of repainting
thousands of HUD nodes.

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
redraw only the visuals that reference them.

Materials resolve to explicit default-alpha, text, clip, tint,
group-pass-through, runtime-generated, or unsupported profiles. Recovered
`UIMaskedGroup::ApplyMaterials` behavior recursively substitutes its image,
text, and antialiased-rectangle materials when `ForceMaterials` is enabled.

## Asset resolution

Roots are indexed once and resolved in this order:

1. the opened document's discovered project root
2. writable workspace
3. additional loose mod roots
4. extracted Dying Light data
5. selected Dying Light installation
6. bounded placeholders

The install source indexes loose `DW*\Data` overrides, every non-language
`Data*.pak` layer (including numeric patch and DLC packs), the selected locale
plus English fallback, and `menu*_PC.rpack` RP6L resources. Installed entries
are virtual, read-only files; the source archive is reopened for each bounded
read and is never modified. The searchable stock-XUI browser opens these
entries without first extracting them.

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
`.fm` glyph metrics, private input-glyph catalogs, and the corresponding DDS
font atlases provide exact bitmap text when present. A user mapping or system
font is an explicit diagnosed fallback.

The resolver never silently invents a successful result. Missing, ambiguous,
probabilistic, corrupt, and approximate resources carry diagnostics into the
viewport and diagnostics panel.

## Timeline system

Timeline time is an integer 60 Hz tick. Parsing follows the real child-node
structure under `Timelines`, `Timeline`, `KeyFrame`, `NamedFrames`, and
`NamedFrame`.

Numeric interpolation code `0` is linear. Code `2` applies the authored
`EaseIn`, `EaseOut`, and `EaseScale` values. Boolean, string, image, font, and
material transitions are stepped. Named-frame commands support `stop`, `goto`,
`gotoandstop`, and `gotoandplay`, with duplicate-target, unknown-command,
recursion, and cycle diagnostics.

Keyframe and marker edits use the same command history and source-patching
pipeline as inspector edits.

## Desktop state

Panel sizes, viewport options, recent files, the selected install and locale,
input-glyph scheme, preview scenario, reference-overlay opacity, writable
workspace, asset roots, and font mappings are persisted in:

```text
%LocalAppData%\DyingLightXuiEditor\settings.json
```

Programmatic expansion, filtering, selection, playback, and property changes
do not call any audio API.
