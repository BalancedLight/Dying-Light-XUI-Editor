using System.Collections.ObjectModel;
using XuiEditor.Core.Documents;

namespace XuiEditor.Core.Animation;

public sealed class XuiTimelineWorkspace
{
    private readonly Dictionary<string, int> _ticks =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _composedTicks =
        new(StringComparer.Ordinal);
    private XuiTimelineEvaluationState? _evaluationState;

    public XuiTimelineWorkspace(XuiTimelineScopeCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        Catalog = catalog;
        SetComposedTicks(catalog);
        ActiveScope = catalog.RootScope;
    }

    public XuiTimelineScopeCatalog Catalog { get; private set; }

    public XuiTimelineScope? ActiveScope { get; private set; }

    public bool HasMixedSelection { get; private set; }

    public int ActiveTick =>
        ActiveScope is null
            ? 0
            : TickFor(ActiveScope.ScopeKey);

    public IReadOnlyDictionary<string, int> RememberedTicks =>
        new ReadOnlyDictionary<string, int>(
            new Dictionary<string, int>(
                _ticks,
                StringComparer.Ordinal));

    public XuiTimelineEvaluationState EvaluationState
    {
        get
        {
            if (_evaluationState is not null)
            {
                return _evaluationState;
            }

            Dictionary<string, int> effectiveTicks =
                new(_composedTicks, StringComparer.Ordinal);
            foreach ((string scopeKey, int tick) in _ticks)
            {
                effectiveTicks[scopeKey] = tick;
            }

            _evaluationState =
                XuiTimelineEvaluationState.ScopeLocal(effectiveTicks);
            return _evaluationState;
        }
    }

    public bool ActiveTickIsComposed =>
        ActiveScope is not null &&
        IsComposed(ActiveScope.ScopeKey);

    public bool ResolveSelection(
        IEnumerable<XuiSyntaxNode> selectedNodes,
        string source,
        string? preferredScopeKey = null)
    {
        ArgumentNullException.ThrowIfNull(selectedNodes);
        ArgumentNullException.ThrowIfNull(source);

        XuiTimelineScope[] resolved = selectedNodes
            .Select(node => Catalog.ResolveForNode(node, source))
            .Where(static scope => scope is not null)
            .Cast<XuiTimelineScope>()
            .DistinctBy(static scope => scope.ScopeKey)
            .ToArray();
        return ResolveScopes(resolved, preferredScopeKey);
    }

    public bool ResolveScopes(
        IEnumerable<XuiTimelineScope> resolvedScopes,
        string? preferredScopeKey = null)
    {
        ArgumentNullException.ThrowIfNull(resolvedScopes);
        XuiTimelineScope? previous = ActiveScope;
        bool wasMixed = HasMixedSelection;
        if (!string.IsNullOrEmpty(preferredScopeKey))
        {
            XuiTimelineScope? preferred =
                Catalog.Find(preferredScopeKey);
            if (preferred is not null)
            {
                ActiveScope = preferred;
                HasMixedSelection = false;
                return !ReferenceEquals(previous, ActiveScope) || wasMixed;
            }
        }

        XuiTimelineScope[] resolved = resolvedScopes
            .DistinctBy(static scope => scope.ScopeKey)
            .ToArray();
        if (resolved.Length > 1)
        {
            ActiveScope = null;
            HasMixedSelection = true;
        }
        else
        {
            ActiveScope = resolved.Length == 1
                ? resolved[0]
                : Catalog.RootScope;
            HasMixedSelection = false;
        }

        return !ReferenceEquals(previous, ActiveScope) ||
               wasMixed != HasMixedSelection;
    }

    public void Rebind(XuiTimelineScopeCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        XuiTimelineScopeCatalog previousCatalog = Catalog;
        XuiTimelineScope? previousActive = ActiveScope;
        Dictionary<string, int> reboundTicks =
            new(StringComparer.Ordinal);
        foreach (XuiTimelineScope scope in catalog.Scopes)
        {
            string? previousKey = PreviousScopeKey(
                previousCatalog,
                scope);
            if (previousKey is not null &&
                _ticks.TryGetValue(previousKey, out int tick))
            {
                reboundTicks[scope.ScopeKey] = Math.Clamp(
                    tick,
                    0,
                    scope.MaximumTick);
            }
        }

        _ticks.Clear();
        foreach ((string key, int tick) in reboundTicks)
        {
            _ticks[key] = tick;
        }

        Catalog = catalog;
        SetComposedTicks(catalog);
        ActiveScope = previousActive is null
            ? catalog.RootScope
            : catalog.Find(previousActive.ScopeKey) ??
              UniqueEquivalentScope(catalog, previousActive) ??
              catalog.RootScope;
        HasMixedSelection = false;
        _evaluationState = null;
    }

    public int TickFor(string scopeKey)
    {
        ArgumentNullException.ThrowIfNull(scopeKey);
        XuiTimelineScope? scope = Catalog.Find(scopeKey);
        int maximum = scope?.MaximumTick ?? 0;
        return Math.Clamp(
            _ticks.TryGetValue(scopeKey, out int remembered)
                ? remembered
                : _composedTicks.GetValueOrDefault(scopeKey),
            0,
            maximum);
    }

    public bool SetActiveTick(int tick)
    {
        if (ActiveScope is null)
        {
            return false;
        }

        int clamped = Math.Clamp(tick, 0, ActiveScope.MaximumTick);
        int previous = TickFor(ActiveScope.ScopeKey);
        _ticks[ActiveScope.ScopeKey] = clamped;
        _evaluationState = null;
        return previous != clamped;
    }

    public bool SetTick(string scopeKey, int tick)
    {
        ArgumentNullException.ThrowIfNull(scopeKey);
        XuiTimelineScope? scope = Catalog.Find(scopeKey);
        if (scope is null)
        {
            return false;
        }

        int clamped = Math.Clamp(tick, 0, scope.MaximumTick);
        int previous = TickFor(scopeKey);
        _ticks[scopeKey] = clamped;
        _evaluationState = null;
        return previous != clamped;
    }

    public bool ResetActiveTick() => SetActiveTick(0);

    public bool IsComposed(string scopeKey)
    {
        ArgumentNullException.ThrowIfNull(scopeKey);
        return !_ticks.ContainsKey(scopeKey) &&
               _composedTicks.ContainsKey(scopeKey);
    }

    private void SetComposedTicks(XuiTimelineScopeCatalog catalog)
    {
        _composedTicks.Clear();
        foreach (XuiTimelineScope scope in catalog.Scopes)
        {
            _composedTicks[scope.ScopeKey] = scope.ComposedTick;
        }
    }

    private static string? PreviousScopeKey(
        XuiTimelineScopeCatalog previousCatalog,
        XuiTimelineScope current)
    {
        if (previousCatalog.Find(current.ScopeKey) is not null)
        {
            return current.ScopeKey;
        }

        XuiTimelineScope[] matches = previousCatalog.Scopes
            .Where(scope => SameOwner(scope, current))
            .ToArray();
        return matches.Length == 1 ? matches[0].ScopeKey : null;
    }

    private static bool SameOwner(
        XuiTimelineScope left,
        XuiTimelineScope right) =>
        left.Owner.Name.Equals(
            right.Owner.Name,
            StringComparison.Ordinal) &&
        left.OwnerId.Equals(
            right.OwnerId,
            StringComparison.Ordinal) &&
        left.TargetIds.SetEquals(right.TargetIds);

    private static XuiTimelineScope? UniqueEquivalentScope(
        XuiTimelineScopeCatalog catalog,
        XuiTimelineScope previous)
    {
        XuiTimelineScope[] matches = catalog.Scopes
            .Where(scope => SameOwner(previous, scope))
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
}
