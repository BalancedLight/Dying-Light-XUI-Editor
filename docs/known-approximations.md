# Known approximations

The editor reports these boundaries instead of disguising them as accurate
game output:

- **3D menu placement.** `UseScreenTransform=false` menu scenes are shown on
  their authored flat canvas. The game's camera actions, 3D placement,
  correction transforms, depth, and perspective are not emulated.
- **Fonts.** The renderer uses installed `.fm` glyph metrics and font-atlas DDS
  data when both are available. Engine IDs without an exact bitmap resource
  are mapped through `basicfonts.scr`, `fontstyles.scr`, user-supplied font
  files, or installed families and receive a visible approximation diagnostic.
- **Localization.** The selected installed locale uses English fallback.
  Tokens absent from both catalogs remain tokens; the editor does not invent
  translated strings.
- **Shaders and materials.** Unsupported proprietary shaders and runtime
  materials use their static bounds, template, color, opacity, and texture
  where possible. Shader-specific effects are approximate.
- **Masks.** Rectangular/transformed clipping is represented. Proprietary
  alpha-mask shader behavior is approximate.
- **Engine controls.** Controls that depend on game code render transparently
  with optional editor-only bounds and a diagnostic. Giant placeholder labels
  are never inserted into the actual preview.
- **Runtime content.** Online news, saves, profiles, inventory, server data,
  native list population, and similar game-owned content are not fabricated.
  Editable preview scenarios can supply clearly editor-only values and reveal
  hidden authored nodes without changing the source document.
- **Probabilistic tiles.** The highest-probability declared variant is selected
  deterministically and diagnosed so repeated previews stay stable.
- **Resolution rules.** Recovered anchor, aspect, keep-position, keep-size,
  parent, and resolution flags are implemented. Behavior requiring unknown
  engine safe-area state or platform-specific policy is approximated and
  diagnosed.
- **Unknown timeline commands/properties.** They are preserved losslessly and
  diagnosed; they are not executed speculatively.

These limitations do not make saving lossy. Unknown XML and unimplemented
properties remain intact unless the user explicitly edits them.
