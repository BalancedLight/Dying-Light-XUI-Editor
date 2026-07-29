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
the research paths or `D:\Backups` at runtime. The catalog classifies all 174
properties authored by the stock Dying Light corpus, eight additional
Dying Light binary properties, 349 observed classes, and all 21 stock timeline
property names.

## `TextStyle`

`TextStyle` is a packed legacy text-formatting and alignment bitmask:

| Mask | Meaning |
| --- | --- |
| `0x0002` | Italic |
| `0x0004` | Bold |
| `0x0008` | Underline |
| `0x0100` | Horizontal left |
| `0x0200` | Horizontal right |
| `0x0400` | Horizontal center |
| `0x1000` | Vertical middle |

Every semantic edit changes only these proven masks. Compatibility bits such
as `0x0001`, `0x0010`, and `0x4000`, as well as arbitrary unknown bits, remain
unchanged. The Advanced inspector also exposes the raw decimal and hexadecimal
value and its decoded known and unmapped portions.

`MultiLine`, `Uppercase`, `Outline`, `Shadow`, `Strike`, and bottom alignment
are separate properties. Normal Dying Light authoring uses
`VerticalAlignDown` for bottom alignment. If a document already authors
standalone `Bold`, `Italic`, `Underline`, `HorizontalAlign`, or
`VerticalAlign`, the editor preserves and edits that representation and it
overrides the corresponding bit-derived preview state.

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
