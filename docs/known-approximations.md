# Known approximations

The editor reports these boundaries instead of disguising them as accurate
game output:

- **3D menu placement.** `UseScreenTransform=false` menu scenes are shown on
  their authored flat canvas. The game's camera actions, 3D placement,
  correction transforms, depth, and perspective are not emulated.
- **Fonts.** The renderer uses installed `.fm` glyph metrics and font-atlas DDS
  data from the selected locale when both are available. Engine IDs without
  an exact bitmap resource are mapped through `basicfonts.scr`,
  `fontstyles.scr`, user-supplied font files, or language-appropriate
  Unicode-capable installed families and receive a visible approximation
  diagnostic. If an atlas does not contain every displayed rune, the editor
  retains the readable system-font path instead of substituting `?` glyphs.
- **Localization.** Stock localization binaries are read only from the
  selected install's language PAKs and use the English PAK as fallback.
  Extracted copies are ignored so they cannot impersonate the selected
  language. Explicit locale folders in configured projects, workspaces,
  loose-resource roots, and RPACKs may override stock strings. Tokens absent
  from those catalogs remain tokens; the editor does not invent translations.
- **Shaders and materials.** The stock sprite, text, antialias, button,
  clipping, tint, and forced-mask families have explicit editor profiles.
  Unsupported proprietary shaders use their static bounds, template, color,
  opacity, and texture where possible. Shader-specific effects are approximate.
- **Map and radar materials.** Runtime map shapes, radar geometry,
  fog-of-war, noise, and damage-indicator geometry remain transparent with
  optional editor bounds. The editor does not fabricate game-generated data.
- **Masks.** Rectangular/transformed clipping is represented. Proprietary
  alpha-mask shader behavior is approximate.
- **Engine controls.** Controls that depend on game code render transparently
  with optional editor-only bounds and a diagnostic. Giant placeholder labels
  are never inserted into the actual preview.
- **Runtime content.** Online news, saves, profiles, inventory, server data,
  native list population, and similar game-owned content are not fabricated.
  Curated preview presets can supply clearly editor-only values and reveal
  hidden authored nodes without changing the source document.
- **Probabilistic tiles.** The highest-probability declared variant is selected
  deterministically and diagnosed so repeated previews stay stable.
- **Resolution rules.** Recovered anchor, aspect, keep-position, keep-size,
  parent, and resolution flags are implemented. Behavior requiring unknown
  engine safe-area state or platform-specific policy is approximated and
  diagnosed.
- **Unknown timeline commands/properties.** They are preserved losslessly and
  diagnosed; they are not executed speculatively.
- **Composed first view.** The editor initially positions each independent
  timeline owner at the earliest key tick that maximizes sampled visibility
  (`Show`, `Opacity`, `Scale`, and color alpha). This is a deterministic
  editor-only inspection pose, not a claim about which controller event the
  game will fire. The header labels it `composed`, and stopping a scope returns
  that scope to authored tick 0.

These limitations do not make saving lossy. Unknown XML and unimplemented
properties remain intact unless the user explicitly edits them.
