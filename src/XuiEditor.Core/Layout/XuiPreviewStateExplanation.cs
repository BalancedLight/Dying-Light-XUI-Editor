namespace XuiEditor.Core.Layout;

public enum XuiPreviewStateReason
{
    Visible,
    ForceShown,
    NotRendered,
    AuthoredHidden,
    AnimatedHidden,
    RuntimeHidden,
    ForceHidden,
    AncestorHidden,
    ZeroOpacity,
    AncestorOpacity,
    Clipped,
    OutsideCanvas,
}

public sealed record XuiPreviewStateExplanation(
    bool IsVisible,
    XuiPreviewStateReason Reason,
    string Summary,
    string? ResponsibleKey = null,
    string? ScopeKey = null,
    int? ScopeTick = null);
