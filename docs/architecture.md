# Architecture

## Projects

`XuiEditor.Core` is independent of WPF. It owns the contracts that need exact,
fast tests:

- `XuiDocument`, `XuiSyntaxNode`, and ordered `XuiPropertyEntry`
- `IXuiCommand` and document history
- `DyingLightLayoutEngine`
- `IAssetResolver` and Dying Light resource models
- `XuiTimeline`, tracks, keyframes, named frames, and `TimelineEvaluator`

`XuiEditor.Wpf` supplies the desktop shell. The hierarchy is a recycling,
virtualized flat list rather than a recursive control tree. The canvas uses
`DrawingVisual` instances so XUI nodes do not become thousands of heavyweight
WPF controls.

`XuiEditor.Tests` exercises the core directly and starts WPF only for focused
fixed-DPI rendering and workspace behavior tests.

## Lossless document model

Loading retains the original byte array and records source spans for editable
tokens. Syntax nodes retain ordered properties and child nodes, including
duplicates and unknown content. Comments, whitespace, XML attributes, newline
style, and encoding therefore stay outside an edit unless the user actually
changes the corresponding token.

All mutations implement `IXuiCommand`. A command validates its proposed
change before committing it, records an inverse operation, and is shared by
the inspector, hierarchy, transform handles, and timeline editor.

Saving follows this sequence:

1. Refuse a direct save when the source is under a protected game or extraction
   root.
2. Refuse malformed or transactionally invalid edits.
3. Verify that the source file has not changed externally.
4. Apply only the validated source-span patches.
5. Write a temporary file in the destination directory.
6. Atomically replace the destination and retain one backup.

An unchanged document returns its original bytes and is never rewritten.
Recovery files live below `%LocalAppData%\DyingLightXuiEditor\Recovery`; they
never replace the source document.

## Layout evaluation

`DyingLightLayoutEngine.Evaluate(document, viewport, tick)` produces immutable
render nodes containing:

- declaration order and source identity
- local and world transforms
- local and transformed bounds
- inherited opacity and visibility
- clipping and mask information
- resolved text/image/control presentation data
- explicit diagnostics

The authored coordinate system is top-left and normally 1280×720. Parent size
changes affect evaluated transforms; they do not rewrite descendant XUI
properties.

The WPF canvas consumes this result and has no authority to reinterpret the
document. This keeps layout behavior deterministic and testable without a UI
thread.

## Asset resolution

Roots are indexed once and resolved in this order:

1. writable workspace
2. additional loose mod roots
3. extracted Dying Light data
4. bounded placeholders

`ClassOverride` and `Visual` names can resolve through XUI visual libraries
such as `menuskin.xui`. Texture definitions support `Texture`, `Whole`,
`Rect`, `RectWithCorner`, atlas rectangles, corner/edge/tile roles, rotations,
and flips. DDS data is decoded with pinned `BCnEncoder.Net` 2.3.0 and cached by
content identity.

The resolver never silently invents a successful result. Missing, ambiguous,
probabilistic, corrupt, and approximate resources carry diagnostics into the
viewport and diagnostics panel.

## Timeline system

Timeline time is an integer 60 Hz tick. Parsing follows the real child-node
structure under `Timelines`, `Timeline`, `KeyFrame`, `NamedFrames`, and
`NamedFrame`.

Numeric interpolation code `0` is linear. Code `2` applies the authored
`EaseIn`, `EaseOut`, and `EaseScale` values. Boolean, string, image, font, and
material transitions are stepped. Named-frame commands support `stop`, `goto`,
`gotoandstop`, and `gotoandplay`, with duplicate-target, unknown-command,
recursion, and cycle diagnostics.

Keyframe and marker edits use the same command history and source-patching
pipeline as inspector edits.

## Desktop state

Panel sizes, viewport options, recent files, the writable workspace, asset
roots, and font mappings are persisted in:

```text
%LocalAppData%\DyingLightXuiEditor\settings.json
```

Programmatic expansion, filtering, selection, playback, and property changes
do not call any audio API.
