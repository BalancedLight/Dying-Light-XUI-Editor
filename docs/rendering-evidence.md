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
`Position` is always the authored top-left coordinate at the authored parent
size. Anchors do not reinterpret that coordinate. They act only when the
evaluated parent size differs from its authored size: trailing anchors move by
the full delta, center anchors by half, opposing anchors preserve both margins,
and leading anchors stay fixed. Keep-position and keep-size flags take
precedence. Unanchored visual-template children retain the recovered
proportional scaling behavior.

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

Recovered `UIVerticalGroup` command strips use independent left and right
cursors with 15 logical pixels between children. Right-anchored children are
placed directly from the panel's right edge; their authored positions are not
converted into inverted anchor distances.

## Hinted and dialog buttons

Button width measurement follows the recovered controller classes:

| Class/profile | Keyboard | Controller |
| --- | --- | --- |
| `UIButtonWithHints` | label + hint + `29 × resolution` | label + hint + `4 × resolution` |
| `UIDialogButton` | label + `20 × resolution` + hint + `32 × resolution` | label + `20 × resolution` + hint + `8 × resolution` |

Generic hinted buttons require authored `AutoAdjustWidth`. Dialog buttons
always apply their recovered sizing contract. Visual-library presenter offsets
remain authored; only the root/background and active label/hint blocks resize.
Keyboard Enter/Escape glyphs keep their self-framed artwork. The separate hint
background uses the recovered factor `0.7` for generic hinted buttons and
`1.0` for dialog buttons.

Controller-owned mutually exclusive branches use deterministic preview
profiles. `MenuYesNoDialogDw` defaults to the common Yes/No branch with OK
hidden, while editor force-show can reveal the alternate branch without
changing the document.

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

An opened XUI under `data\menu` contributes its project `data` directory as
the highest-precedence asset root. Loader XUIs under `PakAssets\XUI` use their
sibling `PakAssets` tree. Texture definitions retain their owning root and
relative path, so project definitions and DDS files override installed files
with the same names while missing project assets still fall back to configured
and installed roots. Same-root basename collisions are selected
deterministically and diagnosed.

The DDS path supports the pinned BC1–BC7 decoder plus a bounded direct path for
classic 32-bit RGB/RGBA DDS files, including the `B8G8R8X8_UNORM` files used by
external Workshop HUD projects.

## Materials and masked groups

The Dying Light decompile shows `UIMaskedGroup` selecting separate defaults
for image, text, and antialiased-rectangle descendants. With
`ForceMaterials=true`, the editor recursively applies `ImageMaskMaterial`,
`TextMaskMaterial`, and `AARectangleMaskMaterial`, matching the stock
`menu_mask_clip.mat`, `menu_text_clip.mat`, and
`menu_antialias_clip.mat` pattern.

`sprite*.mat`, `menu_text*.mat`, `menu_antialias*.mat`, button backgrounds,
clip families, and HUD color modulation receive explicit material profiles.
The special `ImagePath=white` alias performs no texture lookup and fills the
node from its authored ARGB color. Effect-group materials do not paint an
opaque group rectangle.

Map, radar, fog-of-war, noise, and similar materials depend on runtime
geometry or shader inputs. Their authored bounds are retained for inspection,
but generated content is not invented.

## Evidence discipline

Every approximation is explicit. Unknown controls are transparent, missing
fonts and textures are diagnosed, and runtime-generated values are not
fabricated. See [Known approximations](known-approximations.md).
