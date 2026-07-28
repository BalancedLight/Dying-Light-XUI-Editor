using System.Collections.ObjectModel;
using XuiEditor.Core.Documents;

namespace XuiEditor.Core.Animation;

public enum XuiTimelineSamplingMode
{
    ScopeLocal,
    Synchronized,
}

public sealed class XuiTimelineEvaluationState
{
    private readonly IReadOnlyDictionary<string, int> _scopeTicks;

    private XuiTimelineEvaluationState(
        XuiTimelineSamplingMode mode,
        int defaultTick,
        IReadOnlyDictionary<string, int> scopeTicks)
    {
        if (defaultTick < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(defaultTick),
                "Timeline ticks cannot be negative.");
        }

        Mode = mode;
        DefaultTick = defaultTick;
        Dictionary<string, int> copy = new(StringComparer.Ordinal);
        foreach ((string scopeKey, int tick) in scopeTicks)
        {
            if (string.IsNullOrEmpty(scopeKey))
            {
                throw new ArgumentException(
                    "Timeline scope keys cannot be empty.",
                    nameof(scopeTicks));
            }

            if (tick < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(scopeTicks),
                    "Timeline ticks cannot be negative.");
            }

            copy[scopeKey] = tick;
        }

        _scopeTicks = new ReadOnlyDictionary<string, int>(copy);
    }

    public static XuiTimelineEvaluationState Initial { get; } =
        ScopeLocal();

    public XuiTimelineSamplingMode Mode { get; }

    public int DefaultTick { get; }

    public IReadOnlyDictionary<string, int> ScopeTicks => _scopeTicks;

    public static XuiTimelineEvaluationState ScopeLocal(
        IReadOnlyDictionary<string, int>? scopeTicks = null,
        int defaultTick = 0) =>
        new(
            XuiTimelineSamplingMode.ScopeLocal,
            defaultTick,
            scopeTicks ?? new Dictionary<string, int>(
                StringComparer.Ordinal));

    public static XuiTimelineEvaluationState Synchronized(int tick) =>
        new(
            XuiTimelineSamplingMode.Synchronized,
            tick,
            new Dictionary<string, int>(StringComparer.Ordinal));

    public int TickFor(string scopeKey)
    {
        ArgumentNullException.ThrowIfNull(scopeKey);
        return Mode == XuiTimelineSamplingMode.Synchronized
            ? DefaultTick
            : _scopeTicks.GetValueOrDefault(scopeKey, DefaultTick);
    }
}

public sealed record XuiTimelineScope(
    string ScopeKey,
    string OwnerId,
    XuiSyntaxNode Owner,
    IReadOnlyList<XuiTimeline> Timelines,
    IReadOnlyList<XuiNamedFrame> NamedFrames,
    IReadOnlySet<string> TargetIds,
    int MaximumTick,
    int ComposedTick)
{
    public string DisplayName =>
        string.IsNullOrWhiteSpace(OwnerId)
            ? Owner.Name
            : OwnerId;
}

public sealed class XuiTimelineScopeCatalog
{
    private readonly ReadOnlyDictionary<string, XuiTimelineScope> _byKey;
    private readonly ReadOnlyDictionary<string, IReadOnlyList<XuiTimelineScope>>
        _byTarget;
    private readonly ReadOnlyDictionary<string, XuiTimelineScope>
        _scopeByNodeKey;

    private XuiTimelineScopeCatalog(
        XuiDocument document,
        IReadOnlyList<XuiTimelineScope> scopes,
        string rootKey)
    {
        Scopes = scopes;
        RootScope = scopes.FirstOrDefault(scope =>
                            scope.ScopeKey.Equals(
                                rootKey,
                                StringComparison.Ordinal)) ??
                    scopes
                        .OrderBy(static scope =>
                            scope.ScopeKey.Count(static value => value == '/'))
                        .FirstOrDefault();
        _byKey = new ReadOnlyDictionary<string, XuiTimelineScope>(
            scopes.ToDictionary(
                static scope => scope.ScopeKey,
                StringComparer.Ordinal));
        _byTarget = new ReadOnlyDictionary<
            string,
            IReadOnlyList<XuiTimelineScope>>(
            scopes
                .SelectMany(scope => scope.TargetIds.Select(target =>
                    (Target: target, Scope: scope)))
                .GroupBy(static entry => entry.Target, StringComparer.Ordinal)
                .ToDictionary(
                    static group => group.Key,
                    static group =>
                        (IReadOnlyList<XuiTimelineScope>)group
                            .Select(static entry => entry.Scope)
                            .OrderByDescending(static scope =>
                                scope.ScopeKey.Length)
                            .ToArray(),
                    StringComparer.Ordinal));
        Dictionary<string, XuiTimelineScope> scopeByNodeKey =
            new(StringComparer.Ordinal);
        IEnumerable<XuiSyntaxNode> selectableNodes =
            new[] { document.Root }
                .Concat(XuiModelReader.VisualDescendants(document.Root));
        foreach (XuiSyntaxNode node in selectableNodes)
        {
            XuiTimelineScope? resolved = ResolveUncached(
                node,
                document.Text,
                scopeByNodeKey);
            if (resolved is not null)
            {
                scopeByNodeKey[node.Key] = resolved;
            }
        }

        _scopeByNodeKey =
            new ReadOnlyDictionary<string, XuiTimelineScope>(scopeByNodeKey);
    }

    public IReadOnlyList<XuiTimelineScope> Scopes { get; }

    public XuiTimelineScope? RootScope { get; }

    public static XuiTimelineScopeCatalog Build(
        XuiDocument document,
        XuiTimelineSet timelineSet)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(timelineSet);

        Dictionary<string, List<XuiTimeline>> timelinesByScope =
            timelineSet.Timelines
                .GroupBy(static timeline => timeline.ScopeKey)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.ToList(),
                    StringComparer.Ordinal);
        Dictionary<string, List<XuiNamedFrame>> framesByScope =
            timelineSet.NamedFrames
                .GroupBy(static frame => frame.ScopeKey)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.ToList(),
                    StringComparer.Ordinal);

        List<XuiTimelineScope> scopes = [];
        HashSet<string> seenScopes = new(StringComparer.Ordinal);
        foreach (XuiSyntaxNode timelinesNode in document.Root
                     .DescendantsAndSelf()
                     .Where(static node =>
                         node.Kind == XuiSyntaxKind.Element &&
                         node.Name == "Timelines"))
        {
            XuiSyntaxNode owner = timelinesNode.Parent ?? document.Root;
            string scopeKey = owner.Key;
            if (!seenScopes.Add(scopeKey))
            {
                continue;
            }

            IReadOnlyList<XuiTimeline> timelines =
                timelinesByScope.GetValueOrDefault(scopeKey) ?? [];
            IReadOnlyList<XuiNamedFrame> namedFrames =
                framesByScope.GetValueOrDefault(scopeKey) ?? [];
            int keyMaximum = timelines
                .SelectMany(static timeline => timeline.Tracks)
                .SelectMany(static track => track.KeyFrames)
                .Select(static frame => frame.Tick)
                .DefaultIfEmpty()
                .Max();
            int frameMaximum = namedFrames
                .Select(static frame => frame.Tick)
                .DefaultIfEmpty()
                .Max();
            HashSet<string> targets = timelines
                .Select(static timeline => timeline.TargetId)
                .Where(static target => target.Length > 0)
                .ToHashSet(StringComparer.Ordinal);
            scopes.Add(new XuiTimelineScope(
                scopeKey,
                XuiModelReader.GetId(owner, document.Text) ?? string.Empty,
                owner,
                timelines,
                namedFrames,
                targets,
                Math.Max(keyMaximum, frameMaximum),
                ResolveComposedTick(timelines)));
        }

        string rootScopeKey = document.Root.ElementChildren
                                  .FirstOrDefault()
                                  ?.Key ??
                              document.Root.Key;
        return new XuiTimelineScopeCatalog(
            document,
            scopes,
            rootScopeKey);
    }

    public XuiTimelineScope? Find(string? scopeKey) =>
        string.IsNullOrEmpty(scopeKey)
            ? null
            : _byKey.GetValueOrDefault(scopeKey);

    public IReadOnlyList<XuiTimelineScope> ForTarget(string? targetId) =>
        string.IsNullOrEmpty(targetId)
            ? []
            : _byTarget.GetValueOrDefault(targetId) ?? [];

    public XuiTimelineScope? ResolveForNode(
        XuiSyntaxNode node,
        string source)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(source);
        return _scopeByNodeKey.GetValueOrDefault(node.Key) ??
               ResolveUncached(node, source, _scopeByNodeKey);
    }

    private XuiTimelineScope? ResolveUncached(
        XuiSyntaxNode node,
        string source,
        IReadOnlyDictionary<string, XuiTimelineScope> resolvedAncestors)
    {
        if (_byKey.TryGetValue(
                node.Key,
                out XuiTimelineScope? ownedScope))
        {
            return ownedScope;
        }

        string? id = XuiModelReader.GetId(node, source);
        if (!string.IsNullOrEmpty(id) &&
            _byTarget.TryGetValue(
                id,
                out IReadOnlyList<XuiTimelineScope>? targetScopes))
        {
            XuiTimelineScope? targeted = targetScopes.FirstOrDefault(scope =>
                IsAncestorOrSelf(scope.ScopeKey, node.Key));
            if (targeted is not null)
            {
                return targeted;
            }
        }

        XuiSyntaxNode? ancestor = node.Parent;
        while (ancestor is not null)
        {
            if (resolvedAncestors.TryGetValue(
                    ancestor.Key,
                    out XuiTimelineScope? resolved))
            {
                return resolved;
            }

            if (_byKey.TryGetValue(
                    ancestor.Key,
                    out XuiTimelineScope? ancestorScope))
            {
                return ancestorScope;
            }

            ancestor = ancestor.Parent;
        }

        return RootScope ??
               (Scopes.Count > 0 ? Scopes[0] : null);
    }

    internal static bool IsAncestorOrSelf(
        string ancestorKey,
        string nodeKey) =>
        string.Equals(ancestorKey, nodeKey, StringComparison.Ordinal) ||
        (nodeKey.StartsWith(ancestorKey, StringComparison.Ordinal) &&
         nodeKey.Length > ancestorKey.Length &&
         nodeKey[ancestorKey.Length] == '/');

    private static int ResolveComposedTick(
        IReadOnlyList<XuiTimeline> timelines)
    {
        XuiTrack[] visibilityTracks = timelines
            .SelectMany(static timeline => timeline.Tracks)
            .Where(static track => IsVisibilityProperty(track.Property))
            .ToArray();
        if (visibilityTracks.Length == 0)
        {
            return 0;
        }

        SortedSet<int> candidates = [0];
        foreach (XuiTrack track in visibilityTracks)
        {
            foreach (XuiKeyFrame keyFrame in track.KeyFrames)
            {
                candidates.Add(keyFrame.Tick);
            }
        }

        int[] sampledCandidates =
            LimitCandidates(candidates, maximumCount: 2_048);
        int bestTick = 0;
        double bestScore = double.NegativeInfinity;
        foreach (int tick in sampledCandidates)
        {
            double score = VisibilityScore(timelines, tick);
            if (score > bestScore + 0.000001)
            {
                bestScore = score;
                bestTick = tick;
            }
        }

        return bestTick;
    }

    private static int[] LimitCandidates(
        SortedSet<int> candidates,
        int maximumCount)
    {
        if (candidates.Count <= maximumCount)
        {
            return candidates.ToArray();
        }

        int[] all = candidates.ToArray();
        HashSet<int> limited = [all[0], all[^1]];
        double step = (double)(all.Length - 1) / (maximumCount - 1);
        for (int index = 1; index < maximumCount - 1; index++)
        {
            limited.Add(all[(int)Math.Round(index * step)]);
        }

        return limited.Order().ToArray();
    }

    private static double VisibilityScore(
        IReadOnlyList<XuiTimeline> timelines,
        int tick)
    {
        Dictionary<string, double> targetScores =
            new(StringComparer.Ordinal);
        HashSet<string> signaledTargets =
            new(StringComparer.Ordinal);
        foreach (XuiTimeline timeline in timelines)
        {
            foreach (XuiTrack track in timeline.Tracks)
            {
                if (!IsVisibilityProperty(track.Property))
                {
                    continue;
                }

                XuiAnimatedValue? value = TimelineEvaluator.Sample(
                    track,
                    tick);
                if (value is null)
                {
                    continue;
                }

                if (signaledTargets.Add(timeline.TargetId))
                {
                    targetScores[timeline.TargetId] = 1;
                }

                targetScores[timeline.TargetId] *=
                    VisibilityFactor(track.Property, value);
            }
        }

        return targetScores.Values.Sum();
    }

    private static bool IsVisibilityProperty(
        XuiTimelineProperty property) =>
        property is
            XuiTimelineProperty.Show or
            XuiTimelineProperty.Opacity or
            XuiTimelineProperty.Scale or
            XuiTimelineProperty.Color or
            XuiTimelineProperty.TextColor or
            XuiTimelineProperty.DefaultFontColor;

    private static double VisibilityFactor(
        XuiTimelineProperty property,
        XuiAnimatedValue value) =>
        property switch
        {
            XuiTimelineProperty.Show =>
                value.Kind == XuiTimelineValueKind.Boolean
                    ? value.Boolean ? 1 : 0
                    : value.Number > 0 ? 1 : 0,
            XuiTimelineProperty.Opacity =>
                Math.Clamp(value.Number, 0, 1),
            XuiTimelineProperty.Scale =>
                ScaleFactor(value),
            XuiTimelineProperty.Color or
            XuiTimelineProperty.TextColor or
            XuiTimelineProperty.DefaultFontColor
                when value.Kind == XuiTimelineValueKind.Color =>
                value.Color.A / 255.0,
            _ => 1,
        };

    private static double ScaleFactor(XuiAnimatedValue value)
    {
        double scale = value.Kind switch
        {
            XuiTimelineValueKind.Number => Math.Abs(value.Number),
            XuiTimelineValueKind.Vector2 =>
                (Math.Abs(value.Vector2.X) +
                 Math.Abs(value.Vector2.Y)) * 0.5,
            XuiTimelineValueKind.Vector3 =>
                (Math.Abs(value.Vector3.X) +
                 Math.Abs(value.Vector3.Y)) * 0.5,
            XuiTimelineValueKind.Vector4 =>
                (Math.Abs(value.Vector4.X) +
                 Math.Abs(value.Vector4.Y)) * 0.5,
            _ => 1,
        };
        return Math.Clamp(scale, 0, 1);
    }
}
