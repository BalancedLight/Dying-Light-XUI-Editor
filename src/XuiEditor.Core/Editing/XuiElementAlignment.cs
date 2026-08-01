using XuiEditor.Core.Values;

namespace XuiEditor.Core.Editing;

/// <summary>
/// Describes an element position relative to its immediate visual parent.
/// </summary>
public enum XuiElementAlignment
{
    Center,
    Left,
    Right,
    Top,
    Bottom,
}

/// <summary>
/// Calculates local position deltas for editor alignment actions.
/// </summary>
public static class XuiElementAlignmentCalculator
{
    public static bool TryGetPositionDelta(
        XuiElementAlignment alignment,
        XuiVector2 parentSize,
        XuiVector3 childPosition,
        XuiVector2 childSize,
        out XuiVector2 delta)
    {
        if (!IsFinite(parentSize) ||
            !IsFinite(childPosition) ||
            !IsFinite(childSize))
        {
            delta = default;
            return false;
        }

        double targetX = alignment switch
        {
            XuiElementAlignment.Center =>
                (parentSize.X - childSize.X) * 0.5,
            XuiElementAlignment.Left => 0,
            XuiElementAlignment.Right => parentSize.X - childSize.X,
            XuiElementAlignment.Top or XuiElementAlignment.Bottom =>
                childPosition.X,
            _ => throw new ArgumentOutOfRangeException(
                nameof(alignment),
                alignment,
                "The requested element alignment is unsupported."),
        };
        double targetY = alignment switch
        {
            XuiElementAlignment.Center =>
                (parentSize.Y - childSize.Y) * 0.5,
            XuiElementAlignment.Top => 0,
            XuiElementAlignment.Bottom => parentSize.Y - childSize.Y,
            XuiElementAlignment.Left or XuiElementAlignment.Right =>
                childPosition.Y,
            _ => throw new ArgumentOutOfRangeException(
                nameof(alignment),
                alignment,
                "The requested element alignment is unsupported."),
        };
        delta = new XuiVector2(
            targetX - childPosition.X,
            targetY - childPosition.Y);
        return true;
    }

    private static bool IsFinite(XuiVector2 value) =>
        double.IsFinite(value.X) &&
        double.IsFinite(value.Y);

    private static bool IsFinite(XuiVector3 value) =>
        double.IsFinite(value.X) &&
        double.IsFinite(value.Y) &&
        double.IsFinite(value.Z);
}
