# Recovered rendering evidence

The Dying Light assets and Windows Developer Tools reconstruction are the
authority for implemented behavior. The named macOS decompile and Dead Island
Chrome Engine 5 code are supporting semantic evidence only; their offsets,
addresses, vtables, and calling conventions are not copied.

## Anchor mask

The recovered anchor bits are:

| Bit | Meaning |
| ---: | --- |
| `0x01` | left |
| `0x02` | top |
| `0x04` | right |
| `0x08` | bottom |
| `0x10` | horizontal center |
| `0x20` | vertical center |

The evaluator combines the authored anchor, pivot, parent-relative position,
size, scale, and rotation before transforming the node and its clip.

## Frame flags

The Windows reconstruction identifies these layout flags:

| Bit | Meaning |
| ---: | --- |
| `0x00001` | HoldAspectRatio |
| `0x00002` | inherited aspect flag |
| `0x00004` | HoldAspectRatioX |
| `0x00008` | HoldAspectPivotPosition |
| `0x00010` | KeepWidth |
| `0x00020` | KeepWidthOnResolution |
| `0x00040` | KeepHeight |
| `0x00080` | KeepHeightOnResolution |
| `0x00100` | KeepPosX |
| `0x00200` | KeepPosXOnResolution |
| `0x00400` | KeepPosY |
| `0x00800` | KeepPosYOnResolution |
| `0x02000` | ScaleWidthByResolution |
| `0x04000` | ScaleHeightByResolution |
| `0x08000` | KeepWidthOnParent |
| `0x10000` | KeepHeightOnParent |
| `0x20000` | KeepPosXOnParent |
| `0x40000` | KeepPosYOnParent |

Stock-file golden tests cover anchors, pivots, nested transforms, masks,
aspect behavior, keep flags, and viewport resolution changes.

## Text

Recovered HTML alignment values are:

- horizontal: left `0`, center `1`, right `2`, justify `3`
- vertical: top `0`, middle `1`, bottom `2`

Recovered `TextStyle` bits used by the renderer are:

| Bit | Meaning |
| ---: | --- |
| `0x0002` | italic |
| `0x0004` | bold |
| `0x0008` | underline |
| `0x0100` | left |
| `0x0200` | right |
| `0x0400` | horizontal center |
| `0x1000` | vertical middle |

Point size, uppercase, multiline behavior, text and default font color,
outline size/color, and shadow offset/color are retained in immutable render
nodes. `SourceString` is used when an HTML node has no ordinary text property.
When installed bitmap resources are present, glyph advance, atlas rectangle,
vertical offset, special-sign scaling, wrapping, and alignment come from the
game's `.fm` metrics rather than WPF font metrics.

## Layout panels

Recovered `UIStackPanel` behavior defaults to reverse-child traversal,
skipping hidden/transparent children, applying margins, and vertically
stacking items. Its inverse, left-margin, and column-wrap flags are evaluated.

Recovered `UIWrapPanel` behavior uses declaration order, margins, horizontal
flow, row wrapping, and optional inverse/right-aligned placement. Content
auto-sizing changes the evaluated panel bounds and never rewrites descendant
properties.

## Templates and resources

The recovered `CUIElement::FillInstanceParameters` behavior supports scaling a
visual instance from its authored template size rather than rewriting the
template's descendants. Declaration order is the compositing order.

Observed texture-definition primitives include whole textures, atlas
rectangles, rectangles with corners, and independent corner/edge/tile roles.
All declared roles are resolved. When a definition contains probabilistic
variants, the editor chooses a deterministic highest-probability variant and
emits a diagnostic rather than producing a random preview.

Colors use Dying Light's observed `0xAARRGGBB` representation.

## Evidence discipline

Every approximation is explicit. Unknown controls are transparent, missing
fonts and textures are diagnosed, and runtime-generated values are not
fabricated. See [Known approximations](known-approximations.md).
