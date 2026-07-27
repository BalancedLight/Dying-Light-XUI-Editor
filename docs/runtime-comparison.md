# Runtime comparison

## Controlled run — 2026-07-27

The published editor opened the extracted stock
`data\menu\scr\menumain_pc.xui` read-only. It reported:

- 212 XUI nodes
- 5 timelines
- 2 roots
- 0 asset diagnostics after indexing the configured extracted roots

At tick 0 the editor showed the authored pre-transition state. Playback reached
tick 10 (`0.167 s`) and resolved the menu visual library, DDS/atlas imagery,
main list skin, news/promotion image, opacity, and keyframed visibility. The
fixed-height hierarchy expanded without overlap, and a selection was visible
simultaneously in the hierarchy, canvas, breadcrumb, typed inspector, and
timeline.

The authorized Dying Light Player run used:

```text
DyingLightPlayer.exe -nologos -debugconf=debugconf.scr
```

It was launched in vanilla mode and captured at the live 2560×1440 main menu.
No installed or extracted game asset was changed.

## Observed comparison

| Area | Editor | Player |
| --- | --- | --- |
| Logical scene | Authored 1280×720 canvas | 1280×720 XUI presented at 2560×1440 |
| Main list | Correct relative ordering, spacing, skin roles, and keyframed visibility after playback | Same list and state, with engine localization and selection |
| Texture resources | Stock menu DDS/atlas content resolved | Same stock visual family |
| Localization | Raw tokens such as `&MMAIN_PLAY&` without a configured locale catalog | Resolved English strings |
| Font | Mapped or explicit approximate fallback | Proprietary engine font/style |
| Final placement | Flat authored XUI coordinates | Main menu passes through 3D menu placement and camera projection |
| Dynamic data | Static declared structures only | Runtime news, profile, online state, and other game-owned data |

The largest visible difference was the main menu's final screen-space
projection. In the editor the flat list began near authored x≈247; the live
capture began near authored x≈287 and had camera/perspective-dependent scale
and vertical placement. This is not a 2D anchor error: the stock main menu has
`UseScreenTransform=false` content and is placed by the game's 3D menu camera.
The editor deliberately does not claim to emulate that proprietary scene.

The Player window did not accept further injected keyboard/mouse control after
the capture, so a second live Options-screen capture was not forced. The
application and test corpus still validate
`menuoptionscontrolskeyboard.xui`, `menuskin.xui`, HUD files, and the stock
0/1/11/12/22-tick animation offline. Player and editor were closed after the
run.

## Interpretation

The editor preview is authoritative for the document's authored 2D layout,
timeline state, visual-template bounds, and resource selection. It is not an
engine embed. A screen that the game later places in 3D must be judged with the
declared projection approximation in mind.
