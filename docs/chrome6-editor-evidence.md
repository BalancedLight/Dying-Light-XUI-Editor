# Chrome 6 editor evidence

This editor uses recovered Chrome 6 material as design evidence, not as a
runtime dependency. Its authority order is:

1. Dying Light binary metadata.
2. Properties, classes, values, and timeline names authored by Dying Light's
   168 stock XUI files.
3. Files proven identical between Dying Light and Dead Island Definitive
   Edition, tagged as shared Chrome 6 evidence.
4. Dead Island editor-extension and hidden-editor files, tagged as reference
   evidence only.

The generated catalog contains facts rather than copied editor XML or bitmap
assets. `tools\derive-xui-catalog.ps1` is the reproducible research step; the
application loads only the embedded `DyingLightXuiCatalog.json` and never reads
local research inputs at runtime. The catalog classifies all 174
properties authored by the stock Dying Light corpus, eight additional
Dying Light binary properties, 349 observed classes, and all 21 stock timeline
property names.

## `TextStyle`

`TextStyle` is a packed legacy text-formatting and alignment bitmask:

| Mask | Meaning |
| --- | --- |
| `0x0001` | Scale-aware glyph sizing; the recovered text panel uses the smaller X/Y scale |
| `0x0002` | Italic |
| `0x0004` | Bold |
| `0x0008` | Underline |
| `0x0010` | Engine-recognized compatibility state; exact visual effect remains unproven |
| `0x0100` | Horizontal left |
| `0x0200` | Horizontal right |
| `0x0400` | Horizontal center |
| `0x1000` | Vertical middle |
| `0x4000` | Engine-recognized compatibility state; exact visual effect remains unproven |

The stock Dying Light corpus contains 2,203 authored `TextStyle` values across
168 XUI files. The picker catalog is derived from these exact values:

| Value | Uses | Value | Uses |
| ---: | ---: | ---: | ---: |
| `0x0000` | 17 | `0x0002` | 2 |
| `0x0100` | 174 | `0x0110` | 190 |
| `0x0200` | 113 | `0x0210` | 17 |
| `0x0400` | 197 | `0x0410` | 25 |
| `0x0414` | 2 | `0x1000` | 85 |
| `0x1010` | 144 | `0x1100` | 88 |
| `0x1110` | 316 | `0x1114` | 1 |
| `0x1200` | 106 | `0x1210` | 233 |
| `0x1400` | 159 | `0x1410` | 334 |

The catalog uses structural names rather than guessing gameplay roles. Stock
preset selection applies its exact numeric value. Semantic composer edits
change only the selected masks and retain arbitrary unknown bits. `0x0010` is
common in stock files and is preserved without a fabricated preview effect;
`0x4000` is likewise selectable and lossless. The Advanced inspector exposes
the raw decimal and hexadecimal values, recognized compatibility flags, and
truly unknown bits separately.

`MultiLine`, `Uppercase`, `Outline`, `Shadow`, `Strike`, and bottom alignment
are separate properties. Normal Dying Light authoring uses
`VerticalAlignDown` for bottom alignment. If a document already authors
standalone `Bold`, `Italic`, `Underline`, `HorizontalAlign`, or
`VerticalAlign`, the editor preserves and edits that representation and it
overrides the corresponding bit-derived preview state.

XUI `TextStyle` is independent of `basicfonts.scr` numeric `nStyle` values and
`fontstyles.scr` aliases. Those font-resource systems are not offered as XUI
bitmask presets.

## `Pivot`

`Pivot` is an unrestricted local-space XYZ coordinate used as the origin of
scale and rotation. It is not normalized and is not constrained to the
element's bounds: negative, outside-bounds, fractional, and nonzero-Z values
are valid.

The default Raw Runtime edit changes only `Pivot`. Preserve Visual Position
mode additionally changes `Position` using:

```text
position' = position
          + (oldPivot - newPivot)
          - (oldPivot - newPivot)(Scale x Rotation)
```

Two-dimensional presets preserve the authored Z value. Rebase operations
offset matching `Pivot` keys; preserve mode also compensates matching
`Position` keys. Preserve mode is unavailable when `Scale` or `Rotation` is
animated because no one constant position correction can preserve every
animation frame.

The stock corpus, binary metadata, shared base definitions, and the hidden
`gizmoscreen.xui` behavior all agree that pivot is a transform origin rather
than a percentage. Authored community examples independently corroborate
`Pivot` and `HoldAspectPivotPosition` usage:

- [Dying Light ultrawide HUD guide](https://steamcommunity.com/sharedfiles/filedetails/?id=2174922000)
- [Widescreen Gaming Forum Chrome UI example](https://www.wsgf.org/phpBB3/viewtopic.php?p=147349)

## Hidden-editor interaction evidence

Dead Island Definitive Edition retained `gizmoscreen.xui`, editor texture
declarations, and class-extension files. Its `xuibaseclasses.scr` and
`xuieditortextures.scr` copies are byte-identical to Dying Light's copies, so
the interaction vocabulary is useful shared Chrome 6 evidence. The editor
recreates the useful behavior with original WPF vectors:

- pulsing movement fill, eight resize handles, and four corner rotation zones
- a separate constant-screen-size pivot knob with hover feedback
- parent masking, force-show controls, design-time visibility, and three grid
  tiers
- six directional/tab navigation connections with direct and relative-path
  resolution
- translucent asset-drag previews

Dead Island-only properties are not automatically presented as Dying
Light-valid. They enter neither the normal property catalog nor the
class-aware Add Property workflow unless independently supported by Dying
Light evidence; unknown authored XML still round-trips through the explicit
raw-property route.

This is offline editor parity. It does not claim live parity with Techland's
hidden editor or game runtime.
