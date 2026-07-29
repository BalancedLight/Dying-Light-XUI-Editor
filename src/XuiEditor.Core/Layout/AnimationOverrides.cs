using XuiEditor.Core.Animation;

namespace XuiEditor.Core.Layout;

internal sealed class TimelineAnimationCache
{
    private const int MaximumSamplesPerScope = 256;
    private readonly Dictionary<string, IReadOnlyList<XuiTimelineScope>>
        _scopesByTarget;
    private readonly Dictionary<string, ScopeSampleCache> _samplesByScope =
        new(StringComparer.Ordinal);

    public TimelineAnimationCache(XuiTimelineScopeCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _scopesByTarget = catalog.Scopes
            .SelectMany(scope => scope.TargetIds.Select(target =>
                (Target: target, Scope: scope)))
            .GroupBy(static entry => entry.Target, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group =>
                    (IReadOnlyList<XuiTimelineScope>)group
                        .Select(static entry => entry.Scope)
                        .OrderBy(static scope => scope.ScopeKey.Length)
                        .ToArray(),
                StringComparer.Ordinal);
        foreach (XuiTimelineScope scope in catalog.Scopes)
        {
            _samplesByScope[scope.ScopeKey] =
                new ScopeSampleCache();
        }
    }

    public int ScopeEvaluationCount { get; private set; }

    public IReadOnlyDictionary<string, XuiAnimatedValue>? ForNode(
        XuiTimelineEvaluationState timelineState,
        string targetId,
        string nodeKey,
        string? recursionBarrier)
    {
        if (targetId.Length == 0 ||
            !_scopesByTarget.TryGetValue(
                targetId,
                out IReadOnlyList<XuiTimelineScope>? scopes))
        {
            return null;
        }

        IReadOnlyDictionary<string, XuiAnimatedValue>? first = null;
        Dictionary<string, XuiAnimatedValue>? merged = null;
        foreach (XuiTimelineScope scope in scopes)
        {
            if (!XuiTimelineScopeCatalog.IsAncestorOrSelf(
                    scope.ScopeKey,
                    nodeKey) ||
                (recursionBarrier is not null &&
                 !XuiTimelineScopeCatalog.IsAncestorOrSelf(
                     recursionBarrier,
                     scope.ScopeKey)))
            {
                continue;
            }

            ScopeSample sample = SampleScope(
                scope,
                timelineState.TickFor(scope.ScopeKey));
            if (!sample.ValuesByTarget.TryGetValue(
                    targetId,
                    out IReadOnlyDictionary<string, XuiAnimatedValue>? values))
            {
                continue;
            }

            if (first is null)
            {
                first = values;
                continue;
            }

            merged ??= new Dictionary<string, XuiAnimatedValue>(
                first,
                StringComparer.Ordinal);
            foreach ((string property, XuiAnimatedValue value) in values)
            {
                merged[property] = value;
            }
        }

        return merged ?? first;
    }

    private ScopeSample SampleScope(
        XuiTimelineScope scope,
        int tick)
    {
        ScopeSampleCache cache = _samplesByScope[scope.ScopeKey];
        if (cache.Samples.TryGetValue(tick, out ScopeSample? sample))
        {
            return sample;
        }

        Dictionary<
            string,
            IReadOnlyDictionary<string, XuiAnimatedValue>> byTarget =
            new(StringComparer.Ordinal);
        foreach (IGrouping<string, XuiTimeline> targetTimelines in
                 scope.Timelines.GroupBy(
                     static timeline => timeline.TargetId,
                     StringComparer.Ordinal))
        {
            Dictionary<string, XuiAnimatedValue> values =
                new(StringComparer.Ordinal);
            foreach (XuiTimeline timeline in targetTimelines)
            {
                foreach (XuiTrack track in timeline.Tracks)
                {
                    XuiAnimatedValue? value =
                        TimelineEvaluator.Sample(track, tick);
                    if (value is not null)
                    {
                        values[track.PropertyName] = value;
                    }
                }
            }

            if (values.Count > 0)
            {
                byTarget[targetTimelines.Key] = values;
            }
        }

        sample = new ScopeSample(byTarget);
        cache.Samples[tick] = sample;
        cache.InsertionOrder.Enqueue(tick);
        ScopeEvaluationCount++;
        while (cache.Samples.Count > MaximumSamplesPerScope)
        {
            int oldest = cache.InsertionOrder.Dequeue();
            cache.Samples.Remove(oldest);
        }

        return sample;
    }

    private sealed class ScopeSampleCache
    {
        public Dictionary<int, ScopeSample> Samples { get; } = [];

        public Queue<int> InsertionOrder { get; } = [];
    }

    private sealed record ScopeSample(
        IReadOnlyDictionary<
            string,
            IReadOnlyDictionary<string, XuiAnimatedValue>> ValuesByTarget);
}

internal sealed class AnimationOverrides
{
    private readonly Dictionary<string, IReadOnlyList<ScopedAnimation>>?
        _byTarget;
    private readonly TimelineAnimationCache? _timelineCache;
    private readonly XuiTimelineEvaluationState? _timelineState;

    public AnimationOverrides(
        TimelineAnimationCache timelineCache,
        XuiTimelineEvaluationState timelineState)
    {
        _timelineCache = timelineCache;
        _timelineState = timelineState;
    }

    public AnimationOverrides(
        XuiTimelineSet timelineSet,
        XuiTimelineEvaluationState timelineState)
    {
        Dictionary<
            (string ScopeKey, string TargetId),
            Dictionary<string, XuiAnimatedValue>> scoped = [];
        foreach (XuiTimeline timeline in timelineSet.Timelines)
        {
            int tick = timelineState.TickFor(timeline.ScopeKey);
            (string ScopeKey, string TargetId) key = (
                timeline.ScopeKey,
                timeline.TargetId);
            if (!scoped.TryGetValue(
                    key,
                    out Dictionary<string, XuiAnimatedValue>? values))
            {
                values = new Dictionary<string, XuiAnimatedValue>(
                    StringComparer.Ordinal);
                scoped.Add(key, values);
            }

            foreach (XuiTrack track in timeline.Tracks)
            {
                XuiAnimatedValue? value =
                    TimelineEvaluator.Sample(track, tick);
                if (value is not null)
                {
                    values[track.PropertyName] = value;
                }
            }
        }

        _byTarget = scoped
            .GroupBy(
                static pair => pair.Key.TargetId,
                StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group =>
                    (IReadOnlyList<ScopedAnimation>)group
                        .Select(static pair => new ScopedAnimation(
                            pair.Key.ScopeKey,
                            pair.Value))
                        .OrderBy(static entry => entry.ScopeKey.Length)
                        .ToArray(),
                StringComparer.Ordinal);
    }

    public IReadOnlyDictionary<string, XuiAnimatedValue>? ForNode(
        string targetId,
        string nodeKey,
        string? recursionBarrier)
    {
        if (_timelineCache is not null &&
            _timelineState is not null)
        {
            return _timelineCache.ForNode(
                _timelineState,
                targetId,
                nodeKey,
                recursionBarrier);
        }

        if (targetId.Length == 0 ||
            _byTarget is null ||
            !_byTarget.TryGetValue(
                targetId,
                out IReadOnlyList<ScopedAnimation>? entries))
        {
            return null;
        }

        ScopedAnimation[] applicable = entries
            .Where(entry =>
                XuiTimelineScopeCatalog.IsAncestorOrSelf(
                    entry.ScopeKey,
                    nodeKey) &&
                (recursionBarrier is null ||
                 XuiTimelineScopeCatalog.IsAncestorOrSelf(
                     recursionBarrier,
                     entry.ScopeKey)))
            .ToArray();
        if (applicable.Length == 0)
        {
            return null;
        }

        if (applicable.Length == 1)
        {
            return applicable[0].Values;
        }

        Dictionary<string, XuiAnimatedValue> merged =
            new(StringComparer.Ordinal);
        foreach (ScopedAnimation entry in applicable)
        {
            foreach ((string property, XuiAnimatedValue value) in entry.Values)
            {
                merged[property] = value;
            }
        }

        return merged;
    }

    private sealed record ScopedAnimation(
        string ScopeKey,
        IReadOnlyDictionary<string, XuiAnimatedValue> Values);
}
