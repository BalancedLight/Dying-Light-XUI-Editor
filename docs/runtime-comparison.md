# Runtime comparison

## Reference comparison — 2026-07-27

The published editor opened stock files directly from the selected Dying Light
installation and its PAK/RP6 containers. A desktop smoke test opened
`data\menu\hud\hud.xui` read-only and reported:

- 4,061 XUI nodes
- 1,896 timelines
- 24,735 indexed assets after applying the configured roots
- 20,091 selected-language and English-fallback strings

The gameplay preview scenario resolved the real `hud_dw` atlas, displayed its
20×20 `aggro_skull` region, applied sample health/medkit/quest values, and left
the source bytes untouched. The fixed-height hierarchy, dark dialogs, stock
browser, persisted install profile, preview switching, diagnostics pane, and
clean shutdown were also exercised.

No automated Dying Light or Player run was performed as part of this
validation. The in-game HUD and menu screenshots supplied by the user are the
visual reference set. No installed or extracted game asset was changed.

## Observed comparison

| Area | Editor | Reference screenshots |
| --- | --- | --- |
| Logical scene | Authored 1280×720 canvas | 1280×720 XUI presented at the captured display resolution |
| HUD resources | Installed DDS/atlas regions, including `hud_dw`, resolve directly | Matching stock HUD icon families |
| Localization | Installed selected locale with English fallback | Runtime-resolved game strings |
| Font | Exact bitmap metrics/atlas where available, explicit fallback otherwise | Proprietary engine font/style |
| Final placement | Flat authored XUI coordinates | Some menus pass through 3D placement and camera projection |
| Dynamic data | Explicit editor-only preview scenarios | Live quest, inventory, online, and player-owned values |

The current build selects a Dying Light installation and resolves its locale
catalogs, exact bitmap-font metrics/atlases, input glyphs, visual libraries,
and RP6 menu textures including `hud_dw`.

The largest known menu difference is final screen-space projection. Stock
`UseScreenTransform=false` content is placed by the game's 3D menu camera; the
editor deliberately shows the authored flat layout and does not claim to
emulate that proprietary scene. The application and test corpus validate
`menuoptionscontrolskeyboard.xui`, `menuskin.xui`, HUD files, and the stock
0/1/11/12/22-tick animation offline.

## Interpretation

The editor preview is authoritative for the document's authored 2D layout,
timeline state, visual-template bounds, and resource selection. It is not an
engine embed. A screen that the game later places in 3D must be judged with the
declared projection approximation in mind.
