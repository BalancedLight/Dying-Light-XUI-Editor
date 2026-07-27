using XuiEditor.Core.Documents;
using XuiEditor.Core.Values;

namespace XuiEditor.Core.Animation;

public enum XuiTimelineValueKind
{
    Number,
    Boolean,
    Vector2,
    Vector3,
    Quaternion,
    Color,
    Textual,
}

public enum XuiTimelineProperty
{
    Opacity,
    Show,
    Scale,
    Position,
    Color,
    TextColor,
    Width,
    Height,
    Outline,
    TextProgress,
    Rotation,
    Const0,
    Const1,
    OutlineColor,
    Shadow,
    ImagePath,
    DefaultFontColor,
    Pivot,
    Material,
}

public enum XuiInterpolation
{
    Linear = 0,
    Eased = 2,
    Unknown = -1,
}

public sealed record XuiAnimatedValue(
    XuiTimelineValueKind Kind,
    double Number = 0,
    bool Boolean = false,
    XuiVector2 Vector2 = default,
    XuiVector3 Vector3 = default,
    XuiQuaternion Quaternion = default,
    XuiColor Color = default,
    string Text = "")
{
    public string ToXuiString() => Kind switch
    {
        XuiTimelineValueKind.Number => Number.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture),
        XuiTimelineValueKind.Boolean => Boolean ? "true" : "false",
        XuiTimelineValueKind.Vector2 => FormattableString.Invariant($"{Vector2.X:0.######},{Vector2.Y:0.######}"),
        XuiTimelineValueKind.Vector3 => FormattableString.Invariant($"{Vector3.X:0.######},{Vector3.Y:0.######},{Vector3.Z:0.######}"),
        XuiTimelineValueKind.Quaternion => FormattableString.Invariant($"{Quaternion.X:0.######},{Quaternion.Y:0.######},{Quaternion.Z:0.######},{Quaternion.W:0.######}"),
        XuiTimelineValueKind.Color => $"0x{Color.Argb:X8}",
        _ => Text,
    };
}

public sealed record XuiKeyFrame(
    int Tick,
    XuiInterpolation Interpolation,
    int RawInterpolation,
    double EaseIn,
    double EaseOut,
    double EaseScale,
    IReadOnlyList<XuiAnimatedValue> Values,
    XuiSyntaxNode Syntax);

public sealed record XuiTrack(
    XuiTimelineProperty Property,
    int PropertyIndex,
    IReadOnlyList<XuiKeyFrame> KeyFrames)
{
    public int SourcePropertyIndex { get; init; } = PropertyIndex;
}

public sealed record XuiTimeline(
    string TargetId,
    string ScopeKey,
    IReadOnlyList<XuiTrack> Tracks,
    XuiSyntaxNode Syntax);

public sealed record XuiNamedFrame(
    string Name,
    int Tick,
    string Command,
    string CommandParameter,
    string ScopeKey,
    XuiSyntaxNode Syntax);

public sealed record XuiTimelineSet(
    IReadOnlyList<XuiTimeline> Timelines,
    IReadOnlyList<XuiNamedFrame> NamedFrames,
    IReadOnlyList<Diagnostics.XuiDiagnostic> Diagnostics)
{
    public int MaximumTick
    {
        get
        {
            int keyMaximum = Timelines
                .SelectMany(static timeline => timeline.Tracks)
                .SelectMany(static track => track.KeyFrames)
                .Select(static keyFrame => keyFrame.Tick)
                .DefaultIfEmpty()
                .Max();
            int frameMaximum = NamedFrames
                .Select(static frame => frame.Tick)
                .DefaultIfEmpty()
                .Max();
            return Math.Max(keyMaximum, frameMaximum);
        }
    }
}
