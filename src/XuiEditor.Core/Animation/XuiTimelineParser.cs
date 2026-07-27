using XuiEditor.Core.Diagnostics;
using XuiEditor.Core.Documents;
using XuiEditor.Core.Values;

namespace XuiEditor.Core.Animation;

public sealed class XuiTimelineParser
{
    private static readonly Dictionary<string, XuiTimelineProperty> Properties =
        Enum.GetValues<XuiTimelineProperty>()
            .ToDictionary(static value => value.ToString(), StringComparer.Ordinal);

    public static XuiTimelineSet Parse(XuiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Parse(document.Root, document.Text);
    }

    public static XuiTimelineSet Parse(
        XuiSyntaxNode root,
        string source)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(source);
        List<XuiTimeline> timelines = [];
        List<XuiNamedFrame> namedFrames = [];
        List<XuiDiagnostic> diagnostics = [];

        foreach (XuiSyntaxNode timelinesNode in root
                     .DescendantsAndSelf()
                     .Where(static node =>
                         node.Kind == XuiSyntaxKind.Element &&
                         node.Name == "Timelines"))
        {
            string scopeKey = timelinesNode.Parent?.Key ?? root.Key;
            ParseNamedFrames(
                timelinesNode,
                source,
                scopeKey,
                namedFrames,
                diagnostics);

            foreach (XuiSyntaxNode timelineNode in timelinesNode.Elements("Timeline"))
            {
                XuiTimeline? timeline = ParseTimeline(
                    timelineNode,
                    source,
                    scopeKey,
                    diagnostics);
                if (timeline is not null)
                {
                    timelines.Add(timeline);
                }
            }
        }

        ReportDuplicateNamedFrames(namedFrames, diagnostics);
        return new XuiTimelineSet(timelines, namedFrames, diagnostics);
    }

    private static XuiTimeline? ParseTimeline(
        XuiSyntaxNode timelineNode,
        string source,
        string scopeKey,
        List<XuiDiagnostic> diagnostics)
    {
        string targetId = Value(timelineNode, source, "Id");
        if (string.IsNullOrWhiteSpace(targetId))
        {
            diagnostics.Add(new XuiDiagnostic(
                "XUI-TL001",
                XuiDiagnosticSeverity.Error,
                "A timeline has no target Id.",
                timelineNode.Span,
                timelineNode.Key));
            return null;
        }

        List<(XuiTimelineProperty Property, int SourceIndex)> properties = [];
        int propertyIndex = 0;
        foreach (XuiSyntaxNode propertyNode in timelineNode.Elements("TimelineProp"))
        {
            string name = propertyNode.GetDecodedValue(source);
            if (Properties.TryGetValue(name, out XuiTimelineProperty property))
            {
                properties.Add((property, propertyIndex));
            }
            else
            {
                diagnostics.Add(new XuiDiagnostic(
                    "XUI-TL002",
                    XuiDiagnosticSeverity.Warning,
                    $"Unknown timeline property '{name}' is preserved but not evaluated.",
                    propertyNode.Span,
                    propertyNode.Key));
            }

            propertyIndex++;
        }

        if (properties.Count == 0)
        {
            return new XuiTimeline(targetId, scopeKey, [], timelineNode);
        }

        List<XuiKeyFrame> keyFrames = [];
        foreach (XuiSyntaxNode keyFrameNode in timelineNode.Elements("KeyFrame"))
        {
            XuiKeyFrame? keyFrame = ParseKeyFrame(
                keyFrameNode,
                source,
                properties,
                propertyIndex,
                diagnostics);
            if (keyFrame is not null)
            {
                keyFrames.Add(keyFrame);
            }
        }

        List<XuiTrack> tracks = [];
        for (int index = 0; index < properties.Count; index++)
        {
            XuiTimelineProperty property = properties[index].Property;
            IReadOnlyList<XuiKeyFrame> frames = keyFrames
                .Where(frame => frame.Values.Count > index)
                .OrderBy(static frame => frame.Tick)
                .ToArray();
            tracks.Add(new XuiTrack(property, index, frames)
            {
                SourcePropertyIndex = properties[index].SourceIndex,
            });
        }

        return new XuiTimeline(targetId, scopeKey, tracks, timelineNode);
    }

    private static XuiKeyFrame? ParseKeyFrame(
        XuiSyntaxNode keyFrameNode,
        string source,
        List<(XuiTimelineProperty Property, int SourceIndex)> properties,
        int sourcePropertyCount,
        List<XuiDiagnostic> diagnostics)
    {
        if (!XuiValueParser.TryInteger(Value(keyFrameNode, source, "Time"), out int tick) ||
            tick < 0)
        {
            diagnostics.Add(new XuiDiagnostic(
                "XUI-TL003",
                XuiDiagnosticSeverity.Error,
                "A keyframe has an invalid non-negative integer Time.",
                keyFrameNode.Span,
                keyFrameNode.Key));
            return null;
        }

        int rawInterpolation = 0;
        _ = XuiValueParser.TryInteger(
            Value(keyFrameNode, source, "Interpolation"),
            out rawInterpolation);
        XuiInterpolation interpolation = rawInterpolation switch
        {
            0 => XuiInterpolation.Linear,
            2 => XuiInterpolation.Eased,
            _ => XuiInterpolation.Unknown,
        };
        if (interpolation == XuiInterpolation.Unknown)
        {
            diagnostics.Add(new XuiDiagnostic(
                "XUI-TL004",
                XuiDiagnosticSeverity.Warning,
                $"Interpolation code {rawInterpolation} is unknown; step behavior is used.",
                keyFrameNode.Span,
                keyFrameNode.Key));
        }

        double easeIn = NumberOrDefault(keyFrameNode, source, "EaseIn", 0);
        double easeOut = NumberOrDefault(keyFrameNode, source, "EaseOut", 0);
        double easeScale = NumberOrDefault(keyFrameNode, source, "EaseScale", 1);
        List<XuiSyntaxNode> propNodes = keyFrameNode.Elements("Prop").ToList();
        List<XuiAnimatedValue> values = [];
        for (int index = 0; index < properties.Count; index++)
        {
            int sourceIndex = properties[index].SourceIndex;
            if (sourceIndex >= propNodes.Count)
            {
                break;
            }

            string raw = propNodes[sourceIndex].GetDecodedValue(source);
            if (TryParsePropertyValue(
                    properties[index].Property,
                    raw,
                    out XuiAnimatedValue? value))
            {
                values.Add(value!);
            }
            else
            {
                diagnostics.Add(new XuiDiagnostic(
                    "XUI-TL005",
                    XuiDiagnosticSeverity.Error,
                    $"'{raw}' is invalid for timeline property {properties[index].Property}.",
                    propNodes[sourceIndex].Span,
                    propNodes[sourceIndex].Key));
                values.Add(new XuiAnimatedValue(XuiTimelineValueKind.Textual, Text: raw));
            }
        }

        if (propNodes.Count != sourcePropertyCount)
        {
            diagnostics.Add(new XuiDiagnostic(
                "XUI-TL006",
                XuiDiagnosticSeverity.Warning,
                $"Keyframe at tick {tick} has {propNodes.Count} values for {sourcePropertyCount} properties.",
                keyFrameNode.Span,
                keyFrameNode.Key));
        }

        return new XuiKeyFrame(
            tick,
            interpolation,
            rawInterpolation,
            easeIn,
            easeOut,
            easeScale,
            values,
            keyFrameNode);
    }

    private static void ParseNamedFrames(
        XuiSyntaxNode timelinesNode,
        string source,
        string scopeKey,
        List<XuiNamedFrame> result,
        List<XuiDiagnostic> diagnostics)
    {
        foreach (XuiSyntaxNode namedFramesNode in timelinesNode.Elements("NamedFrames"))
        {
            foreach (XuiSyntaxNode namedFrameNode in namedFramesNode.Elements("NamedFrame"))
            {
                string name = Value(namedFrameNode, source, "Name");
                if (string.IsNullOrWhiteSpace(name) ||
                    !XuiValueParser.TryInteger(
                        Value(namedFrameNode, source, "Time"),
                        out int tick) ||
                    tick < 0)
                {
                    diagnostics.Add(new XuiDiagnostic(
                        "XUI-TL007",
                        XuiDiagnosticSeverity.Error,
                        "A named frame requires a name and a non-negative integer Time.",
                        namedFrameNode.Span,
                        namedFrameNode.Key));
                    continue;
                }

                string command = Value(namedFrameNode, source, "Command")
                    .Trim()
                    .ToLowerInvariant();
                if (command.Length > 0 &&
                    command is not ("stop" or "goto" or "gotoandstop" or "gotoandplay"))
                {
                    diagnostics.Add(new XuiDiagnostic(
                        "XUI-TL008",
                        XuiDiagnosticSeverity.Warning,
                        $"Unknown named-frame command '{command}' is preserved and ignored.",
                        namedFrameNode.Span,
                        namedFrameNode.Key));
                }

                result.Add(new XuiNamedFrame(
                    name,
                    tick,
                    command,
                    Value(namedFrameNode, source, "CommandParams"),
                    scopeKey,
                    namedFrameNode));
            }
        }
    }

    private static void ReportDuplicateNamedFrames(
        IEnumerable<XuiNamedFrame> frames,
        List<XuiDiagnostic> diagnostics)
    {
        foreach (IGrouping<(string ScopeKey, string Name), XuiNamedFrame> group in frames
                     .GroupBy(
                         static frame => (frame.ScopeKey, frame.Name),
                         new ScopeNameComparer())
                     .Where(static group => group.Count() > 1))
        {
            XuiNamedFrame duplicate = group.Last();
            diagnostics.Add(new XuiDiagnostic(
                "XUI-TL009",
                XuiDiagnosticSeverity.Warning,
                $"Named-frame target '{duplicate.Name}' is duplicated in the same scope.",
                duplicate.Syntax.Span,
                duplicate.Syntax.Key));
        }
    }

    public static bool TryParsePropertyValue(
        XuiTimelineProperty property,
        string raw,
        out XuiAnimatedValue? value)
    {
        switch (property)
        {
            case XuiTimelineProperty.Opacity:
            case XuiTimelineProperty.Width:
            case XuiTimelineProperty.Height:
            case XuiTimelineProperty.TextProgress:
            case XuiTimelineProperty.Const0:
            case XuiTimelineProperty.Const1:
                if (XuiValueParser.TryNumber(raw, out double number))
                {
                    value = new XuiAnimatedValue(
                        XuiTimelineValueKind.Number,
                        Number: number);
                    return true;
                }

                break;

            case XuiTimelineProperty.Outline:
            case XuiTimelineProperty.Shadow:
                if (bool.TryParse(raw.Trim(), out bool outlineBoolean))
                {
                    value = new XuiAnimatedValue(
                        XuiTimelineValueKind.Boolean,
                        Boolean: outlineBoolean);
                    return true;
                }

                if (XuiValueParser.TryNumber(raw, out double outlineNumber))
                {
                    value = new XuiAnimatedValue(
                        XuiTimelineValueKind.Number,
                        Number: outlineNumber);
                    return true;
                }

                break;

            case XuiTimelineProperty.Show:
                if (XuiValueParser.TryBoolean(raw, out bool boolean))
                {
                    value = new XuiAnimatedValue(
                        XuiTimelineValueKind.Boolean,
                        Boolean: boolean);
                    return true;
                }

                break;

            case XuiTimelineProperty.Scale:
            case XuiTimelineProperty.Position:
            case XuiTimelineProperty.Pivot:
                if (XuiValueParser.TryVector3(raw, out XuiVector3 vector3))
                {
                    value = new XuiAnimatedValue(
                        XuiTimelineValueKind.Vector3,
                        Vector3: vector3);
                    return true;
                }

                if (XuiValueParser.TryVector2(raw, out XuiVector2 vector2))
                {
                    value = new XuiAnimatedValue(
                        XuiTimelineValueKind.Vector2,
                        Vector2: vector2);
                    return true;
                }

                break;

            case XuiTimelineProperty.Rotation:
                if (XuiValueParser.TryQuaternion(raw, out XuiQuaternion quaternion))
                {
                    value = new XuiAnimatedValue(
                        XuiTimelineValueKind.Quaternion,
                        Quaternion: quaternion);
                    return true;
                }

                if (XuiValueParser.TryVector3(raw, out XuiVector3 rotation3))
                {
                    value = new XuiAnimatedValue(
                        XuiTimelineValueKind.Vector3,
                        Vector3: rotation3);
                    return true;
                }

                if (XuiValueParser.TryNumber(raw, out double rotation))
                {
                    value = new XuiAnimatedValue(
                        XuiTimelineValueKind.Number,
                        Number: rotation);
                    return true;
                }

                break;

            case XuiTimelineProperty.Color:
            case XuiTimelineProperty.TextColor:
            case XuiTimelineProperty.OutlineColor:
            case XuiTimelineProperty.DefaultFontColor:
                if (XuiValueParser.TryColor(raw, out XuiColor color))
                {
                    value = new XuiAnimatedValue(
                        XuiTimelineValueKind.Color,
                        Color: color);
                    return true;
                }

                break;

            case XuiTimelineProperty.ImagePath:
            case XuiTimelineProperty.Material:
                value = new XuiAnimatedValue(
                    XuiTimelineValueKind.Textual,
                    Text: raw);
                return true;
        }

        value = null;
        return false;
    }

    private static string Value(
        XuiSyntaxNode parent,
        string source,
        string name) =>
        parent.FirstElement(name)?.GetDecodedValue(source) ?? string.Empty;

    private static double NumberOrDefault(
        XuiSyntaxNode parent,
        string source,
        string name,
        double fallback) =>
        XuiValueParser.TryNumber(Value(parent, source, name), out double value)
            ? value
            : fallback;

    private sealed class ScopeNameComparer :
        IEqualityComparer<(string ScopeKey, string Name)>
    {
        public bool Equals(
            (string ScopeKey, string Name) left,
            (string ScopeKey, string Name) right) =>
            string.Equals(left.ScopeKey, right.ScopeKey, StringComparison.Ordinal) &&
            string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string ScopeKey, string Name) value) =>
            HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(value.ScopeKey),
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.Name));
    }
}
