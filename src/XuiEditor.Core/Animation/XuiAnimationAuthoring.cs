using System.Globalization;
using System.Net;
using XuiEditor.Core.Documents;
using XuiEditor.Core.Schema;

namespace XuiEditor.Core.Animation;

public enum XuiAnimationEvidence
{
    StockExact,
    EditorConvenience,
}

public enum XuiAnimationConflictSeverity
{
    Information,
    Warning,
    Error,
}

public sealed record XuiAnimationKeyTemplate(
    int Tick,
    string Value,
    XuiInterpolation Interpolation = XuiInterpolation.Linear,
    XuiAnimationEvidence Evidence = XuiAnimationEvidence.StockExact);

public sealed record XuiAnimationTrackTemplate(
    string PropertyName,
    IReadOnlyList<XuiAnimationKeyTemplate> Keys,
    XuiAnimationEvidence Evidence = XuiAnimationEvidence.StockExact);

public sealed record XuiAnimationNamedFrameTemplate(
    string Name,
    int Tick,
    string Command,
    string CommandParameter = "",
    XuiAnimationEvidence Evidence = XuiAnimationEvidence.StockExact);

public sealed record XuiAnimationPreset(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<XuiAnimationTrackTemplate> Tracks,
    IReadOnlyList<XuiAnimationNamedFrameTemplate> NamedFrames)
{
    public int MaximumTick => Tracks
        .SelectMany(static track => track.Keys)
        .Select(static key => key.Tick)
        .Concat(NamedFrames.Select(static frame => frame.Tick))
        .DefaultIfEmpty()
        .Max();
}

public sealed record XuiAnimationAuthoringRequest(
    string OwnerKey,
    IReadOnlyList<string> TargetKeys,
    XuiAnimationPreset Preset,
    int StartTick = 0,
    string FramePrefix = "",
    bool MarkersOnly = false,
    string? CustomProperty = null,
    string? CustomStartValue = null,
    string? CustomEndValue = null,
    int CustomDuration = 5);

public sealed record XuiAnimationConflict(
    XuiAnimationConflictSeverity Severity,
    string Message,
    string? ResourceKey = null,
    IReadOnlyList<object?>? Arguments = null);

public sealed record XuiAnimationConflictReport(
    IReadOnlyList<XuiAnimationConflict> Conflicts)
{
    public bool HasErrors => Conflicts.Any(static conflict =>
        conflict.Severity == XuiAnimationConflictSeverity.Error);
}

public sealed record XuiAnimationAuthoringResult(
    IXuiCommand? Command,
    XuiAnimationConflictReport ConflictReport,
    string OwnerKey,
    int FirstTick,
    IReadOnlyList<(string TargetId, string PropertyName)> GeneratedTracks);

public static class XuiAnimationPresets
{
    public const string BaseColorToken = "$BASE_COLOR";

    public static IReadOnlyList<XuiAnimationPreset> BuiltIn { get; } =
    [
        new(
            "quick-show-hide",
            "Quick Show / Hide",
            "Stock 0/5/6/11 visibility timing used by game-specific menu visuals.",
            [
                Track("Show", (0, "true"), (5, "true"), (6, "true"), (11, "false")),
                Track("Opacity", (0, "0"), (5, "1"), (6, "1"), (11, "0")),
            ],
            [
                Frame("Show", 0, ""),
                Frame("EndShow", 5, "stop"),
                Frame("Hide", 6, ""),
                Frame("EndHide", 11, "stop"),
            ]),
        new(
            "menu-transition",
            "Menu Transition",
            "Stock 0/1/20/21/40 menu opacity transition timing.",
            [Track("Opacity", (0, "1"), (1, "0"), (20, "1"), (21, "1"), (40, "0"))],
            [
                Frame("Idle", 0, "stop"),
                Frame("TransIn", 1, ""),
                Frame("EndTransIn", 20, "goto", "Idle"),
                Frame("TransOut", 21, ""),
                Frame("EndTransOut", 40, "stop"),
            ]),
        new(
            "hud-pop",
            "HUD Pop",
            "Observed HUD 0/8/9/10 show range with a stock-style 2 to 1 pop scale.",
            [
                Track("Show", (0, "true"), (8, "true"), (9, "true"), (10, "false")),
                Track("Opacity", (0, "0"), (8, "1"), (9, "1"), (10, "0")),
                new XuiAnimationTrackTemplate(
                    "Scale",
                    [
                        new(0, "2,2,1"),
                        new(8, "1,1,1"),
                        new(9, "1,1,1"),
                        new(10, "2,2,1"),
                    ]),
            ],
            [
                Frame("Show", 0, ""),
                Frame("EndShow", 8, "stop"),
                Frame("Hide", 9, ""),
                Frame("EndHide", 10, "stop"),
            ]),
        BuildButtonStates(),
        new(
            "custom-property",
            "Custom Property",
            "Two editable keys for any compatible timeline property.",
            [],
            []),
    ];

    public static XuiAnimationPreset Find(string id) =>
        BuiltIn.First(preset => preset.Id.Equals(id, StringComparison.Ordinal));

    private static XuiAnimationPreset BuildButtonStates()
    {
        XuiAnimationNamedFrameTemplate[] frames =
        [
            Frame("Normal", 0, "stop"),
            Frame("EndNormal", 5, "gotoandstop", "Normal"),
            Frame("InitFocus", 6, ""),
            Frame("EndInitFocus", 14, "goto", "Focus"),
            Frame("Focus", 15, "stop"),
            Frame("EndFocus", 24, "stop"),
            Frame("KillFocus", 25, ""),
            Frame("EndKillFocus", 33, "goto", "Normal"),
            Frame("Press", 34, ""),
            Frame("EndPress", 50, "goto", "Focus"),
            Frame("NormalPress", 51, ""),
            Frame("EndNormalPress", 67, "goto", "Normal"),
            Frame("NormalSel", 68, "stop"),
            Frame("EndNormalSel", 74, "goto", "NormalSel"),
            Frame("FocusSel", 75, "stop"),
            Frame("EndFocusSel", 82, "goto", "FocusSel"),
            Frame("NormalDisable", 83, "stop"),
            Frame("EndNormalDisable", 87, "goto", "NormalDisable"),
            Frame("FocusDisable", 88, "stop"),
            Frame("EndFocusDisable", 92, "goto", "FocusDisable"),
        ];
        return new XuiAnimationPreset(
            "button-states",
            "Button States",
            "Stock button state ranges through tick 92; focus tint and disabled opacity are convenience motion values.",
            [
                new XuiAnimationTrackTemplate(
                    "Scale",
                    [
                        new(0, "1,1,1"), new(34, "1,1,1"),
                        new(42, "0.7,0.7,1"), new(50, "1,1,1"),
                        new(51, "1,1,1"), new(59, "0.7,0.7,1"),
                        new(67, "1,1,1"), new(92, "1,1,1"),
                    ]),
                new XuiAnimationTrackTemplate(
                    "Color",
                    [
                        new(0, BaseColorToken, Evidence: XuiAnimationEvidence.EditorConvenience),
                        new(6, BaseColorToken, Evidence: XuiAnimationEvidence.EditorConvenience),
                        new(14, "0xffdd6f18", Evidence: XuiAnimationEvidence.EditorConvenience),
                        new(24, "0xffdd6f18", Evidence: XuiAnimationEvidence.EditorConvenience),
                        new(33, BaseColorToken, Evidence: XuiAnimationEvidence.EditorConvenience),
                        new(92, BaseColorToken, Evidence: XuiAnimationEvidence.EditorConvenience),
                    ],
                    XuiAnimationEvidence.EditorConvenience),
                new XuiAnimationTrackTemplate(
                    "Opacity",
                    [
                        new(0, "1"), new(82, "1"),
                        new(83, "0.1", Evidence: XuiAnimationEvidence.EditorConvenience),
                        new(92, "0.1", Evidence: XuiAnimationEvidence.EditorConvenience),
                    ],
                    XuiAnimationEvidence.EditorConvenience),
            ],
            frames);
    }

    private static XuiAnimationTrackTemplate Track(
        string property,
        params (int Tick, string Value)[] keys) =>
        new(
            property,
            keys.Select(static key =>
                new XuiAnimationKeyTemplate(key.Tick, key.Value)).ToArray());

    private static XuiAnimationNamedFrameTemplate Frame(
        string name,
        int tick,
        string command,
        string parameter = "") =>
        new(name, tick, command, parameter);
}

public sealed class XuiAnimationAuthoringService
{
    public static XuiAnimationAuthoringResult Plan(
        XuiDocument document,
        XuiAnimationAuthoringRequest request,
        XuiTimelineSet? parsedTimelines = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);
        if (request.StartTick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Start tick cannot be negative.");
        }

        List<XuiAnimationConflict> conflicts = [];
        List<(string TargetId, string PropertyName)> generatedTracks = [];
        XuiSyntaxNode? owner = document.SyntaxTree.FindByKey(request.OwnerKey);
        if (owner is null || owner.Kind != XuiSyntaxKind.Element)
        {
            conflicts.Add(Error(
                "Ui.Animation.Error.OwnerMissing",
                "The selected animation owner no longer exists."));
            return Result(null);
        }
        if (owner.IsSelfClosing || owner.EndTagStart < 0)
        {
            conflicts.Add(Error(
                "Ui.Animation.Error.OwnerCannotContain",
                $"Element '{owner.Name}' cannot contain animation data.",
                owner.Name));
            return Result(null);
        }

        XuiSyntaxNode[] targets = request.TargetKeys
            .Select(document.SyntaxTree.FindByKey)
            .Where(static node => node is not null)
            .Cast<XuiSyntaxNode>()
            .DistinctBy(static node => node.Key, StringComparer.Ordinal)
            .ToArray();
        if (!request.MarkersOnly && targets.Length != request.TargetKeys.Count)
        {
            conflicts.Add(Error(
                "Ui.Animation.Error.TargetMissing",
                "One or more animation targets no longer exist."));
        }

        List<XuiAnimationTrackTemplate> templates = ResolveTemplates(request, conflicts);
        if (!request.MarkersOnly && targets.Length == 0)
        {
            conflicts.Add(Error(
                "Ui.Animation.Error.SelectTarget",
                "Select at least one XUI element to generate animation tracks."));
        }

        HashSet<string> ambiguousTargetIds = document.Root
            .DescendantsAndSelf()
            .Where(static node =>
                node.Kind == XuiSyntaxKind.Element &&
                !XuiModelReader.IsStructural(node))
            .Select(node => XuiModelReader.GetId(node, document.Text))
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .GroupBy(static id => id, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToHashSet(StringComparer.Ordinal);

        XuiSyntaxNode? timelinesNode = owner.FirstElement("Timelines");
        string scopeKey = owner.Key;
        XuiTimelineSet timelineSet = parsedTimelines ??
                                     XuiTimelineParser.Parse(document);
        IReadOnlyList<XuiTimeline> scopeTimelines = timelineSet.Timelines
            .Where(timeline => timeline.ScopeKey.Equals(scopeKey, StringComparison.Ordinal))
            .ToArray();
        IReadOnlyList<XuiNamedFrame> scopeFrames = timelineSet.NamedFrames
            .Where(frame => frame.ScopeKey.Equals(scopeKey, StringComparison.Ordinal))
            .ToArray();

        List<XuiAnimationNamedFrameTemplate> newFrames = [];
        foreach (XuiAnimationNamedFrameTemplate template in request.Preset.NamedFrames)
        {
            XuiAnimationNamedFrameTemplate desired = template with
            {
                Name = request.FramePrefix + template.Name,
                Tick = request.StartTick + template.Tick,
                CommandParameter = PrefixCommandParameter(template, request.FramePrefix),
            };
            XuiNamedFrame[] matches = scopeFrames
                .Where(frame => frame.Name.Equals(desired.Name, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length == 0)
            {
                newFrames.Add(desired);
            }
            else if (matches.Length == 1 && FrameEquals(matches[0], desired))
            {
                conflicts.Add(Info(
                    "Ui.Animation.Info.FrameReused",
                    $"Named frame '{desired.Name}' already exists identically and will be reused.",
                    desired.Name));
            }
            else
            {
                conflicts.Add(Error(
                    "Ui.Animation.Error.FrameConflict",
                    $"Named frame '{desired.Name}' conflicts in this scope. Add a frame prefix or choose another scope.",
                    desired.Name));
            }
        }

        List<string> newTimelineXml = [];
        Dictionary<string, ExistingTimelineInsertion> existingInsertions =
            new(StringComparer.Ordinal);
        if (!request.MarkersOnly)
        {
            foreach (XuiSyntaxNode target in targets)
            {
                string? targetId = XuiModelReader.GetId(target, document.Text);
                if (string.IsNullOrWhiteSpace(targetId))
                {
                    conflicts.Add(Error(
                        "Ui.Animation.Error.MissingId",
                        $"Element '{target.Name}' has no Id and cannot be targeted by a timeline.",
                        target.Name));
                    continue;
                }
                if (ambiguousTargetIds.Contains(targetId))
                {
                    conflicts.Add(Error(
                        "Ui.Animation.Error.DuplicateTargetId",
                        $"Target Id '{targetId}' is duplicated in the document and cannot be animated unambiguously.",
                        targetId));
                    continue;
                }

                foreach (XuiAnimationTrackTemplate rawTemplate in templates)
                {
                    XuiAnimationTrackTemplate template = ResolveTargetTemplate(
                        rawTemplate,
                        target,
                        document.Text);
                    XuiTimelineProperty? knownProperty =
                        Enum.TryParse(
                            template.PropertyName,
                            ignoreCase: false,
                            out XuiTimelineProperty parsedProperty)
                            ? parsedProperty
                            : null;
                    XuiAnimationKeyTemplate? invalidKey = template.Keys
                        .FirstOrDefault(key =>
                            !XuiTimelineParser.TryParsePropertyValue(
                                template.PropertyName,
                                knownProperty,
                                key.Value,
                                out _));
                    if (invalidKey is not null)
                    {
                        conflicts.Add(Error(
                            "Ui.Animation.Error.InvalidValue",
                            $"'{invalidKey.Value}' is invalid for timeline property '{template.PropertyName}'.",
                            invalidKey.Value,
                            template.PropertyName));
                        continue;
                    }
                    XuiTimeline[] matches = scopeTimelines
                        .Where(timeline => timeline.TargetId.Equals(targetId, StringComparison.Ordinal) &&
                            timeline.Tracks.Any(track => track.PropertyName.Equals(template.PropertyName, StringComparison.Ordinal)))
                        .ToArray();
                    int matchingTrackCount = matches.Sum(timeline =>
                        timeline.Tracks.Count(track =>
                            track.PropertyName.Equals(
                                template.PropertyName,
                                StringComparison.Ordinal)));
                    if (matchingTrackCount > 1)
                    {
                        conflicts.Add(Error(
                            "Ui.Animation.Error.DuplicateTrack",
                            $"Target '{targetId}' has ambiguous duplicate '{template.PropertyName}' tracks in this scope.",
                            targetId,
                            template.PropertyName));
                        continue;
                    }

                    XuiAnimationTrackTemplate shifted = template with
                    {
                        Keys = template.Keys.Select(key => key with
                        {
                            Tick = request.StartTick + key.Tick,
                        }).ToArray(),
                    };
                    if (matches.Length == 0)
                    {
                        newTimelineXml.Add(BuildTimelineXml(targetId, shifted, document.Format.NewLine));
                        generatedTracks.Add((targetId, shifted.PropertyName));
                        continue;
                    }

                    XuiTimeline timeline = matches[0];
                    if (!existingInsertions.TryGetValue(timeline.Syntax.Key, out ExistingTimelineInsertion? insertion))
                    {
                        insertion = new ExistingTimelineInsertion(timeline);
                        existingInsertions.Add(timeline.Syntax.Key, insertion);
                    }

                    MergeTrack(insertion, shifted, conflicts);
                    generatedTracks.Add((targetId, shifted.PropertyName));
                }
            }
        }

        if (conflicts.Any(static conflict => conflict.Severity == XuiAnimationConflictSeverity.Error))
        {
            return Result(null);
        }

        List<XuiTextPatch> patches = [];
        List<string> keyXml = existingInsertions.Values
            .SelectMany(insertion => BuildExistingKeyXml(insertion, conflicts, document.Format.NewLine))
            .ToList();
        if (conflicts.Any(static conflict => conflict.Severity == XuiAnimationConflictSeverity.Error))
        {
            return Result(null);
        }

        if (timelinesNode is null)
        {
            string contents = BuildTimelinesXml(newFrames, newTimelineXml.Concat(keyXml), document.Format.NewLine);
            patches.Add(InsertAsChildPatch(document, owner, contents));
        }
        else
        {
            if (newFrames.Count > 0)
            {
                XuiSyntaxNode? namedFrames = timelinesNode.FirstElement("NamedFrames");
                string xml = string.Join(document.Format.NewLine, newFrames.Select(frame => BuildNamedFrameXml(frame, document.Format.NewLine)));
                if (namedFrames is null)
                {
                    patches.Add(InsertAsChildPatch(document, timelinesNode, Wrap("NamedFrames", xml, document.Format.NewLine)));
                }
                else
                {
                    patches.Add(InsertAsChildPatch(document, namedFrames, xml));
                }
            }

            if (newTimelineXml.Count > 0)
            {
                patches.Add(InsertAsChildPatch(document, timelinesNode, string.Join(document.Format.NewLine, newTimelineXml)));
            }

            foreach (ExistingTimelineInsertion insertion in existingInsertions.Values)
            {
                string xml = string.Join(
                    document.Format.NewLine,
                    BuildExistingKeyXml(insertion, conflicts, document.Format.NewLine));
                if (xml.Length > 0)
                {
                    patches.Add(InsertAsChildPatch(document, insertion.Timeline.Syntax, xml));
                }
            }
        }

        patches = CoalescePatches(patches);
        IXuiCommand? command = patches.Count == 0
            ? null
            : new XuiTextPatchCommand(
                document,
                $"Create {request.Preset.Name} animation",
                patches,
                CommandDescriptor(request.Preset));
        return Result(command);

        XuiAnimationAuthoringResult Result(IXuiCommand? command) =>
            new(
                command,
                new XuiAnimationConflictReport(conflicts),
                request.OwnerKey,
                request.StartTick,
                generatedTracks);
    }

    public static XuiAnimationAuthoringResult PlanTrackKey(
        XuiDocument document,
        string ownerKey,
        string targetKey,
        string propertyName,
        string value,
        int tick,
        XuiTimelineSet? parsedTimelines = null)
    {
        XuiSyntaxNode? target = document.SyntaxTree.FindByKey(targetKey);
        string? targetId = target is null
            ? null
            : XuiModelReader.GetId(target, document.Text);
        XuiTimelineSet timelineSet = parsedTimelines ??
                                     XuiTimelineParser.Parse(document);
        XuiTimeline[] existing = timelineSet.Timelines
            .Where(timeline => timeline.ScopeKey.Equals(ownerKey, StringComparison.Ordinal) &&
                timeline.TargetId.Equals(targetId, StringComparison.Ordinal) &&
                timeline.Tracks.Any(track => track.PropertyName.Equals(propertyName, StringComparison.Ordinal)))
            .ToArray();
        if (existing.Length == 1 &&
            existing[0].Tracks.Count(track => track.PropertyName.Equals(
                propertyName,
                StringComparison.Ordinal)) == 1)
        {
            XuiTrack track = existing[0].Tracks.Single(candidate =>
                candidate.PropertyName.Equals(propertyName, StringComparison.Ordinal));
            XuiKeyFrame? key = track.KeyFrames.FirstOrDefault(frame => frame.Tick == tick);
            XuiSyntaxNode? prop = key?.Syntax.Elements("Prop")
                .ElementAtOrDefault(track.SourcePropertyIndex);
            if (prop is not null)
            {
                return new XuiAnimationAuthoringResult(
                    XuiCommandFactory.SetElementValue(document, prop, value),
                    new XuiAnimationConflictReport([]),
                    ownerKey,
                    tick,
                    [(targetId!, propertyName)]);
            }
        }

        XuiAnimationPreset preset = new(
            "single-track-key",
            propertyName,
            "Single property track/key.",
            [new XuiAnimationTrackTemplate(propertyName, [new XuiAnimationKeyTemplate(0, value, Evidence: XuiAnimationEvidence.EditorConvenience)])],
            []);
        return Plan(
            document,
            new XuiAnimationAuthoringRequest(
                ownerKey,
                [targetKey],
                preset,
                tick),
            timelineSet);
    }

    private static List<XuiAnimationTrackTemplate> ResolveTemplates(
        XuiAnimationAuthoringRequest request,
        List<XuiAnimationConflict> conflicts)
    {
        if (!request.Preset.Id.Equals("custom-property", StringComparison.Ordinal))
        {
            return request.Preset.Tracks.ToList();
        }

        if (string.IsNullOrWhiteSpace(request.CustomProperty))
        {
            conflicts.Add(Error(
                "Ui.Animation.Error.PropertyRequired",
                "Choose a property for the custom animation."));
            return [];
        }

        if (!XuiClassCatalog.Default.TimelinePropertyNames.Contains(
                request.CustomProperty.Trim(),
                StringComparer.Ordinal))
        {
            conflicts.Add(Error(
                "Ui.Animation.Error.PropertyIncompatible",
                $"'{request.CustomProperty.Trim()}' is not a compatible Dying Light timeline property.",
                request.CustomProperty.Trim()));
            return [];
        }

        if (request.CustomDuration < 1)
        {
            conflicts.Add(Error(
                "Ui.Animation.Error.Duration",
                "Custom animation duration must be at least one tick."));
            return [];
        }

        return
        [
            new XuiAnimationTrackTemplate(
                request.CustomProperty.Trim(),
                [
                    new(0, request.CustomStartValue ?? "0", Evidence: XuiAnimationEvidence.EditorConvenience),
                    new(request.CustomDuration, request.CustomEndValue ?? "1", Evidence: XuiAnimationEvidence.EditorConvenience),
                ],
                XuiAnimationEvidence.EditorConvenience),
        ];
    }

    private static XuiAnimationTrackTemplate ResolveTargetTemplate(
        XuiAnimationTrackTemplate template,
        XuiSyntaxNode target,
        string source)
    {
        string baseColor = XuiModelReader.GetPropertyValue(target, source, template.PropertyName) ??
                           XuiModelReader.GetPropertyValue(target, source, "TextColor") ??
                           XuiModelReader.GetPropertyValue(target, source, "Color") ??
                           "0xffffffff";
        return template with
        {
            Keys = template.Keys.Select(key => key.Value.Equals(XuiAnimationPresets.BaseColorToken, StringComparison.Ordinal)
                ? key with { Value = baseColor }
                : key).ToArray(),
        };
    }

    private static void MergeTrack(
        ExistingTimelineInsertion insertion,
        XuiAnimationTrackTemplate template,
        List<XuiAnimationConflict> conflicts)
    {
        XuiTrack track = insertion.Timeline.Tracks.Single(candidate =>
            candidate.PropertyName.Equals(template.PropertyName, StringComparison.Ordinal));
        foreach (XuiAnimationKeyTemplate key in template.Keys)
        {
            XuiKeyFrame? existing = track.KeyFrames.FirstOrDefault(frame => frame.Tick == key.Tick);
            if (existing is not null)
            {
                string actual = existing.Values.Count > track.PropertyIndex
                    ? existing.Values[track.PropertyIndex].ToXuiString()
                    : string.Empty;
                if (!EquivalentValue(actual, key.Value))
                {
                    conflicts.Add(Error(
                        "Ui.Animation.Error.KeyConflict",
                        $"Track '{insertion.Timeline.TargetId}.{template.PropertyName}' already has a different value at tick {key.Tick}.",
                        insertion.Timeline.TargetId,
                        template.PropertyName,
                        key.Tick));
                }
                continue;
            }

            string[] values = insertion.ValuesForTick(key.Tick);
            values[track.PropertyIndex] = key.Value;
            insertion.Keys[key.Tick] = new PendingKey(key.Tick, key.Interpolation, values);
        }
    }

    private static IEnumerable<string> BuildExistingKeyXml(
        ExistingTimelineInsertion insertion,
        List<XuiAnimationConflict> conflicts,
        string newline)
    {
        foreach (PendingKey key in insertion.Keys.Values.OrderBy(static key => key.Tick))
        {
            if (key.Values.Any(string.IsNullOrEmpty))
            {
                conflicts.Add(Error(
                    "Ui.Animation.Error.SiblingSample",
                    $"Could not sample all sibling properties for '{insertion.Timeline.TargetId}' at tick {key.Tick}.",
                    insertion.Timeline.TargetId,
                    key.Tick));
                continue;
            }

            yield return BuildKeyFrameXml(key.Tick, key.Interpolation, key.Values, newline);
        }
    }

    private static string BuildTimelineXml(
        string targetId,
        XuiAnimationTrackTemplate track,
        string newline)
    {
        List<string> lines =
        [
            "<Timeline>",
            $"    <Id>{Encode(targetId)}</Id>",
            $"    <TimelineProp>{Encode(track.PropertyName)}</TimelineProp>",
        ];
        foreach (XuiAnimationKeyTemplate key in track.Keys.OrderBy(static key => key.Tick))
        {
            lines.Add(Indent(BuildKeyFrameXml(key.Tick, key.Interpolation, [key.Value], newline), "    ", newline));
        }
        lines.Add("</Timeline>");
        return string.Join(newline, lines);
    }

    private static string BuildKeyFrameXml(
        int tick,
        XuiInterpolation interpolation,
        IReadOnlyList<string> values,
        string newline)
    {
        List<string> lines =
        [
            "<KeyFrame>",
            $"    <Time>{tick.ToString(CultureInfo.InvariantCulture)}</Time>",
            $"    <Interpolation>{(interpolation == XuiInterpolation.Eased ? 2 : 0)}</Interpolation>",
        ];
        lines.AddRange(values.Select(value => $"    <Prop>{Encode(value)}</Prop>"));
        lines.Add("</KeyFrame>");
        return string.Join(newline, lines);
    }

    private static string BuildNamedFrameXml(
        XuiAnimationNamedFrameTemplate frame,
        string newline) =>
        string.Join(newline,
        [
            "<NamedFrame>",
            $"    <Name>{Encode(frame.Name)}</Name>",
            $"    <Time>{frame.Tick.ToString(CultureInfo.InvariantCulture)}</Time>",
            $"    <Command>{Encode(frame.Command)}</Command>",
            $"    <CommandParams>{Encode(frame.CommandParameter)}</CommandParams>",
            "</NamedFrame>",
        ]);

    private static string BuildTimelinesXml(
        List<XuiAnimationNamedFrameTemplate> frames,
        IEnumerable<string> timelines,
        string newline)
    {
        List<string> children = [];
        if (frames.Count > 0)
        {
            children.Add(Wrap("NamedFrames", string.Join(newline, frames.Select(frame => BuildNamedFrameXml(frame, newline))), newline));
        }
        children.AddRange(timelines);
        return Wrap("Timelines", string.Join(newline, children), newline);
    }

    private static string Wrap(string name, string body, string newline) =>
        string.Join(newline,
        [
            $"<{name}>",
            Indent(body, "    ", newline),
            $"</{name}>",
        ]);

    private static XuiTextPatch InsertAsChildPatch(
        XuiDocument document,
        XuiSyntaxNode parent,
        string rawXml)
    {
        if (parent.IsSelfClosing || parent.EndTagStart < 0)
        {
            throw new InvalidOperationException($"Element '{parent.Name}' cannot own animation XML.");
        }

        string parentIndent = LineIndent(document.Text, parent.Start);
        string childIndent = parent.ElementChildren.FirstOrDefault() is XuiSyntaxNode first
            ? LineIndent(document.Text, first.Start)
            : parentIndent + "    ";
        string insertion = document.Format.NewLine +
                           Indent(rawXml, childIndent, document.Format.NewLine);
        int offset = parent.ElementChildren.LastOrDefault()?.End ?? parent.StartTagEnd;
        return new XuiTextPatch(offset, string.Empty, insertion);
    }

    private static List<XuiTextPatch> CoalescePatches(
        IEnumerable<XuiTextPatch> patches) =>
        patches
            .GroupBy(static patch => (patch.Start, patch.ExpectedText))
            .Select(group => new XuiTextPatch(
                group.Key.Start,
                group.Key.ExpectedText,
                string.Concat(group.Select(static patch => patch.ReplacementText))))
            .OrderByDescending(static patch => patch.Start)
            .ToList();

    private static string LineIndent(string source, int offset)
    {
        int lineStart = source.LastIndexOf('\n', Math.Max(0, offset - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        int index = lineStart;
        while (index < offset && source[index] is ' ' or '\t')
        {
            index++;
        }
        return source[lineStart..index];
    }

    private static string Indent(string text, string indentation, string newline) =>
        string.Join(newline, text.Split(newline, StringSplitOptions.None).Select(line => indentation + line));

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static bool FrameEquals(
        XuiNamedFrame actual,
        XuiAnimationNamedFrameTemplate desired) =>
        actual.Tick == desired.Tick &&
        actual.Command.Equals(desired.Command, StringComparison.OrdinalIgnoreCase) &&
        actual.CommandParameter.Equals(desired.CommandParameter, StringComparison.Ordinal);

    private static string PrefixCommandParameter(
        XuiAnimationNamedFrameTemplate frame,
        string prefix) =>
        frame.CommandParameter.Length == 0 ? string.Empty : prefix + frame.CommandParameter;

    private static Diagnostics.XuiMessageDescriptor CommandDescriptor(
        XuiAnimationPreset preset) => preset.Id switch
        {
            "quick-show-hide" => new(
                "Ui.Command.CreateAnimation.QuickShowHide",
                "Create Quick Show / Hide animation"),
            "menu-transition" => new(
                "Ui.Command.CreateAnimation.MenuTransition",
                "Create Menu Transition animation"),
            "hud-pop" => new(
                "Ui.Command.CreateAnimation.HudPop",
                "Create HUD Pop animation"),
            "button-states" => new(
                "Ui.Command.CreateAnimation.ButtonStates",
                "Create Button States animation"),
            "custom-property" => new(
                "Ui.Command.CreateAnimation.CustomProperty",
                "Create Custom Property animation"),
            _ => new(
                "Ui.Command.CreateTrack",
                "Create {0} animation track",
                preset.Name),
        };

    private static bool EquivalentValue(string left, string right) =>
        left.Trim().Equals(right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static XuiAnimationConflict Error(
        string resourceKey,
        string message,
        params object?[] arguments) =>
        new(
            XuiAnimationConflictSeverity.Error,
            message,
            resourceKey,
            arguments);

    private static XuiAnimationConflict Info(
        string resourceKey,
        string message,
        params object?[] arguments) =>
        new(
            XuiAnimationConflictSeverity.Information,
            message,
            resourceKey,
            arguments);

    private sealed class ExistingTimelineInsertion
    {
        public ExistingTimelineInsertion(XuiTimeline timeline) => Timeline = timeline;

        public XuiTimeline Timeline { get; }

        public Dictionary<int, PendingKey> Keys { get; } = [];

        public string[] ValuesForTick(int tick)
        {
            if (Keys.TryGetValue(tick, out PendingKey? pending))
            {
                return pending.Values;
            }

            return Timeline.Tracks
                .Select(track => TimelineEvaluator.Sample(track, tick)?.ToXuiString() ?? string.Empty)
                .ToArray();
        }
    }

    private sealed record PendingKey(
        int Tick,
        XuiInterpolation Interpolation,
        string[] Values);
}
