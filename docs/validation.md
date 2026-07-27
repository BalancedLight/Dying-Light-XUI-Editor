# Validation record

Validation performed on Windows x64 on 2026-07-27:

```text
dotnet restore XuiEditor.slnx --locked-mode
dotnet build XuiEditor.slnx -c Debug --no-restore
dotnet tests\XuiEditor.Tests\bin\Debug\net10.0-windows\XuiEditor.Tests.dll
dotnet build XuiEditor.slnx -c Release --no-restore
dotnet tests\XuiEditor.Tests\bin\Release\net10.0-windows\XuiEditor.Tests.dll
dotnet publish src\XuiEditor.Wpf\XuiEditor.Wpf.csproj
  -c Release -r win-x64 --self-contained true --no-restore
  -o artifacts\publish\win-x64
```

Both test configurations passed all 92 tests with zero build warnings.

Coverage includes:

- byte-identical no-op saves and token-level mutation preservation
- comments, whitespace, property order, duplicate/unknown nodes, CRLF/LF, and
  encoding
- malformed XML, DTD/entities, nesting limits, duplicate IDs, invalid edits,
  and external-change conflicts
- transactional multi-command undo/redo and failed-batch rollback
- anchors, pivots, nested transforms, clipping, aspect and resolution flags
- compiled revision-bound layout sessions, retained nested visuals, camera-only
  pan/zoom, and live multi-selection move/rotation previews
- material profiles for stock sprite/text/button/antialias/clip/tint/group
  families, recursive masked-group material substitution, solid-color white
  images, runtime-only shapes, and aggregated unsupported-material diagnostics
- visual-library overrides, atlas/tile/nine-slice parsing, ARGB colors,
  BC-compressed DDS decoding, cache invalidation, precedence, and missing
  resources
- every supported timeline property, step/linear/eased sampling, named-frame
  commands, loops, recursion, duplicate targets, and keyframe undo/redo
- the deterministic stock 0/1/11/12/22-tick fixture
- extracted `menumain_pc.xui`, `menuoptionscontrolskeyboard.xui`,
  `menuskin.xui`, `intro.xui`, and the large HUD
- direct Dying Light install indexing, PAK precedence, RP6 resource lookup,
  selected-locale/English fallback, input glyphs, and exact bitmap fonts
- the real `hud_dw` texture definition and DDS source for the 20×20
  `aggro_skull` atlas region (the similarly named `hud_dl` is not used)
- HUD preview scenarios, runtime text placeholders, hidden-node reveal rules,
  and source-byte isolation
- 10,000-node hierarchy virtualization, fixed 24-pixel rows, expansion/filter
  state, selection synchronization, settings/pane persistence, recovery
  isolation, fixed-DPI WPF rendering, and absence of audio APIs
- dark editor-owned templates for menus, combo boxes, context menus, tooltips,
  scrollbars, and other native WPF controls
- texture-only visual invalidation and the selection-scoped timeline mode
- the single-file publish contract and embedded multi-resolution icon

The current installation at
`E:\SteamLibrary\steamapps\common\Dying Light` exposed 174 stock XUIs and
8,793 install assets. With the configured optional extracted roots, the live
editor indexed 24,735 assets and 20,091 selected-language/English-fallback
strings. Stock `hud.xui` opened read-only with 4,061 nodes and 1,896
timelines. The gameplay-HUD scenario resolved installed imagery, populated
sample health/medkit/quest values, and did not produce the former false
`XUI-TL005` Const0/Const1 diagnostics.

The stock DLC file `data/menu/hud/hud_btz.xui` is malformed at its source
(`TimelineProp` is closed as `Timeline</Prop>` near line 4255). The parser
rejects it safely. All other 173 currently installed stock XUIs parse and
evaluate without mutation.

The final `win-x64` publish contains one file and no sidecars:

```text
DyingLightXuiEditor.exe
65,486,344 bytes
SHA-256 27CCFABAFFA7DB7426A01AB8DDC4D1B327C4E45DE5B47C1F5F02C104CB6B5D30
```

The self-contained executable was launched without Unity or an installed .NET
runtime. Its embedded application icon was extracted successfully and the
process remained healthy through the startup smoke window before the exact
spawned process was closed. Earlier interactive acceptance covered the dark
workspace, data-root dialog, 174-entry stock browser, read-only HUD load,
preview scenario switch, diagnostics pane, persisted settings, and clean
shutdown. Dying Light and Dying Light Player were not launched during this
validation.

The controlled Player comparison is documented in
[runtime-comparison.md](runtime-comparison.md).
