# Validation record

Validation performed on Windows x64 on 2026-07-27:

```text
dotnet restore XuiEditor.slnx --locked-mode
dotnet test XuiEditor.slnx -c Debug --no-restore
dotnet test XuiEditor.slnx -c Release --no-restore
dotnet publish src\XuiEditor.Wpf\XuiEditor.Wpf.csproj
  -c Release -r win-x64 --self-contained true --no-restore
  -o artifacts\publish\win-x64
```

Both test configurations passed all 55 tests.

Coverage includes:

- byte-identical no-op saves and token-level mutation preservation
- comments, whitespace, property order, duplicate/unknown nodes, CRLF/LF, and
  encoding
- malformed XML, DTD/entities, nesting limits, duplicate IDs, invalid edits,
  and external-change conflicts
- anchors, pivots, nested transforms, clipping, aspect and resolution flags
- visual-library overrides, atlas/tile/nine-slice parsing, ARGB colors,
  BC-compressed DDS decoding, cache invalidation, precedence, and missing
  resources
- every supported timeline property, step/linear/eased sampling, named-frame
  commands, loops, recursion, duplicate targets, and keyframe undo/redo
- the deterministic stock 0/1/11/12/22-tick fixture
- extracted `menumain_pc.xui`, `menuoptionscontrolskeyboard.xui`,
  `menuskin.xui`, `intro.xui`, and a large HUD document
- 10,000-node hierarchy virtualization, fixed 24-pixel rows, expansion/filter
  state, selection synchronization, settings/pane persistence, recovery
  isolation, fixed-DPI WPF rendering, and absence of audio APIs

The self-contained published executable was launched without relying on Unity
or an installed .NET runtime. It opened the extracted stock main menu,
rendered its assets, played its timelines, and closed cleanly.

The controlled Player comparison is documented in
[runtime-comparison.md](runtime-comparison.md).
