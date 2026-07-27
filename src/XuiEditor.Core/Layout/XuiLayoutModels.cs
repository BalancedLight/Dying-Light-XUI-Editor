using System.Numerics;
using XuiEditor.Core.Diagnostics;
using XuiEditor.Core.Values;

namespace XuiEditor.Core.Layout;

[Flags]
public enum XuiAnchor
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
    CenterX = 16,
    CenterY = 32,
}

public enum XuiRenderKind
{
    Scene,
    Group,
    Image,
    Text,
    Rectangle,
    Presenter,
    Control,
    Unknown,
}

public enum XuiTextHorizontalAlignment
{
    Left,
    Center,
    Right,
    Justify,
}

public enum XuiTextVerticalAlignment
{
    Top,
    Middle,
    Bottom,
}

public readonly record struct XuiViewport(
    double Width,
    double Height,
    double DpiScale = 1,
    bool PreserveAspect = true)
{
    public static readonly XuiViewport Default = new(1280, 720);
}

public sealed record XuiRenderNode(
    string Key,
    string? ParentKey,
    string Id,
    string ElementName,
    XuiRenderKind Kind,
    int Depth,
    int DeclarationOrder,
    XuiVector2 Size,
    XuiVector3 Position,
    XuiVector3 Pivot,
    XuiVector3 Scale,
    double RotationDegrees,
    double Opacity,
    bool IsShown,
    Matrix3x2 LocalTransform,
    Matrix3x2 WorldTransform,
    XuiRect LocalBounds,
    XuiRect WorldBounds,
    XuiRect? ClipBounds,
    string Text,
    string ImagePath,
    string Material,
    string Font,
    XuiColor Color,
    string Visual,
    string ClassOverride,
    bool IsApproximation,
    string SelectionKey,
    bool IsVisualTemplatePart,
    bool VisualResolved)
{
    public XuiVector2 AuthoredSize { get; init; } = Size;

    public double PointSize { get; init; }

    public bool Uppercase { get; init; }

    public bool MultiLine { get; init; }

    public bool Bold { get; init; }

    public bool Italic { get; init; }

    public bool Underline { get; init; }

    public XuiTextHorizontalAlignment HorizontalTextAlignment { get; init; }

    public XuiTextVerticalAlignment VerticalTextAlignment { get; init; }

    public XuiVector2 TextBorder { get; init; }

    public bool Outline { get; init; }

    public double OutlineSize { get; init; } = 1;

    public XuiColor OutlineColor { get; init; } = new(255, 0, 0, 0);

    public bool Shadow { get; init; }

    public double ShadowOffset { get; init; } = 1;

    public XuiColor ShadowColor { get; init; } = new(160, 0, 0, 0);
}

public sealed record XuiRenderFrame(
    XuiVector2 DesignSize,
    XuiViewport Viewport,
    Matrix3x2 ViewportTransform,
    IReadOnlyList<XuiRenderNode> Nodes,
    IReadOnlyList<XuiDiagnostic> Diagnostics)
{
    public XuiRenderNode? HitTest(XuiVector2 logicalPoint) =>
        Nodes
            .Where(static node => node.IsShown && node.Opacity > 0)
            .Reverse()
            .FirstOrDefault(node =>
                node.WorldBounds.Contains(logicalPoint) &&
                (node.ClipBounds is null ||
                 node.ClipBounds.Value.Contains(logicalPoint)));
}
