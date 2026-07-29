using System.Numerics;
using XuiEditor.Core.Animation;
using XuiEditor.Core.Values;

namespace XuiEditor.Core.Editing;

public enum XuiPivotPreset
{
    Origin,
    TopLeft,
    TopCenter,
    TopRight,
    MiddleLeft,
    Center,
    MiddleRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
}

public static class XuiPivotEditing
{
    public static XuiVector3 ApplyPreset(
        XuiPivotPreset preset,
        XuiVector2 size,
        double currentZ)
    {
        double centerX = size.X * 0.5;
        double centerY = size.Y * 0.5;
        return preset switch
        {
            XuiPivotPreset.Origin or
            XuiPivotPreset.TopLeft =>
                new XuiVector3(0, 0, currentZ),
            XuiPivotPreset.TopCenter =>
                new XuiVector3(centerX, 0, currentZ),
            XuiPivotPreset.TopRight =>
                new XuiVector3(size.X, 0, currentZ),
            XuiPivotPreset.MiddleLeft =>
                new XuiVector3(0, centerY, currentZ),
            XuiPivotPreset.Center =>
                new XuiVector3(centerX, centerY, currentZ),
            XuiPivotPreset.MiddleRight =>
                new XuiVector3(size.X, centerY, currentZ),
            XuiPivotPreset.BottomLeft =>
                new XuiVector3(0, size.Y, currentZ),
            XuiPivotPreset.BottomCenter =>
                new XuiVector3(centerX, size.Y, currentZ),
            XuiPivotPreset.BottomRight =>
                new XuiVector3(size.X, size.Y, currentZ),
            _ => throw new ArgumentOutOfRangeException(
                nameof(preset),
                preset,
                "Unknown pivot preset."),
        };
    }

    public static XuiVector3 CompensatePosition(
        XuiVector3 position,
        XuiVector3 oldPivot,
        XuiVector3 newPivot,
        XuiVector3 scale,
        double rotationDegrees)
    {
        Vector2 difference = new(
            (float)(oldPivot.X - newPivot.X),
            (float)(oldPivot.Y - newPivot.Y));
        Matrix3x2 linear =
            Matrix3x2.CreateScale((float)scale.X, (float)scale.Y) *
            Matrix3x2.CreateRotation(
                (float)(rotationDegrees * Math.PI / 180));
        Vector2 transformed = Vector2.TransformNormal(difference, linear);
        return new XuiVector3(
            position.X + difference.X - transformed.X,
            position.Y + difference.Y - transformed.Y,
            position.Z);
    }

    public static bool CanPreserveVisualPosition(
        IEnumerable<XuiTimeline> timelines,
        string targetId)
    {
        ArgumentNullException.ThrowIfNull(timelines);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        return !timelines
            .Where(timeline => timeline.TargetId.Equals(
                targetId,
                StringComparison.Ordinal))
            .SelectMany(static timeline => timeline.Tracks)
            .Any(static track =>
                track.KnownProperty is
                    XuiTimelineProperty.Scale or
                    XuiTimelineProperty.Rotation);
    }
}
