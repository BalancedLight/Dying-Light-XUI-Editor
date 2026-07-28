using XuiEditor.Core.Documents;

namespace XuiEditor.Wpf.Models;

public sealed class HierarchyIndex
{
    private readonly Dictionary<string, Entry> _entries;
    private readonly IReadOnlyList<Entry> _declarationOrder;
    private readonly HashSet<string> _effectivelyHiddenKeys =
        new(StringComparer.Ordinal);

    private HierarchyIndex(
        XuiDocument document,
        Dictionary<string, Entry> entries,
        IReadOnlyList<Entry> declarationOrder)
    {
        Document = document;
        Revision = document.Revision;
        RootKey = document.Root.Key;
        _entries = entries;
        _declarationOrder = declarationOrder;
    }

    public XuiDocument Document { get; }

    public long Revision { get; }

    public string RootKey { get; }

    public IReadOnlySet<string> EffectivelyHiddenKeys =>
        _effectivelyHiddenKeys;

    public static HierarchyIndex Build(XuiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
        List<Entry> declarationOrder = [];

        void Add(XuiSyntaxNode node, string? parentKey, int depth)
        {
            XuiSyntaxNode[] children =
                XuiModelReader.VisualChildren(node).ToArray();
            string id =
                XuiModelReader.GetId(node, document.Text) ??
                string.Empty;
            string visual =
                XuiModelReader.GetPropertyValue(
                    node,
                    document.Text,
                    "Visual") ??
                string.Empty;
            string classOverride =
                XuiModelReader.GetPropertyValue(
                    node,
                    document.Text,
                    "ClassOverride") ??
                string.Empty;
            string display = id.Length > 0
                ? id
                : $"<{node.Name}>";
            HierarchyRow row = new(
                node,
                display,
                depth,
                children.Length > 0);
            Entry entry = new(
                node,
                parentKey,
                children.Select(static child => child.Key).ToArray(),
                display,
                string.Join(
                        '\u001f',
                        node.Name,
                        id,
                        visual,
                        classOverride)
                    .ToUpperInvariant(),
                row);
            entries.Add(node.Key, entry);
            declarationOrder.Add(entry);
            foreach (XuiSyntaxNode child in children)
            {
                Add(child, node.Key, depth + 1);
            }
        }

        Add(document.Root, parentKey: null, depth: 0);
        return new HierarchyIndex(document, entries, declarationOrder);
    }

    public bool IsCurrent(XuiDocument document) =>
        ReferenceEquals(Document, document) &&
        Revision == document.Revision;

    public Entry? Find(string key) => _entries.GetValueOrDefault(key);

    public HierarchyRow? FindRow(string key) => Find(key)?.Row;

    public IEnumerable<string> Ancestors(string key)
    {
        Entry? current = Find(key);
        while (current?.ParentKey is string parentKey &&
               _entries.TryGetValue(parentKey, out current))
        {
            yield return current.Node.Key;
        }
    }

    public IReadOnlyList<string> HiddenBranchRootsExcept(string key)
    {
        if (!_entries.ContainsKey(key))
        {
            return [];
        }

        HashSet<string> kept = new(StringComparer.Ordinal);
        kept.Add(key);
        kept.UnionWith(Ancestors(key));
        AddDescendants(key);

        List<string> hiddenRoots = [];
        foreach (Entry entry in _declarationOrder)
        {
            if (kept.Contains(entry.Node.Key))
            {
                continue;
            }

            if (entry.ParentKey is null || kept.Contains(entry.ParentKey))
            {
                hiddenRoots.Add(entry.Node.Key);
            }
        }

        return hiddenRoots;

        void AddDescendants(string parentKey)
        {
            foreach (string childKey in _entries[parentKey].Children)
            {
                if (kept.Add(childKey))
                {
                    AddDescendants(childKey);
                }
            }
        }
    }

    public void UpdateEditorStates(
        IReadOnlySet<string> hidden,
        IReadOnlySet<string> locked)
    {
        _effectivelyHiddenKeys.Clear();
        Update(RootKey, hiddenBy: null, lockedBy: null);
        return;

        void Update(string key, string? hiddenBy, string? lockedBy)
        {
            Entry entry = _entries[key];
            bool directlyHidden = hidden.Contains(key);
            bool directlyLocked = locked.Contains(key);
            HierarchyVisibilityState visibilityState = directlyHidden
                ? HierarchyVisibilityState.Hidden
                : hiddenBy is not null
                    ? HierarchyVisibilityState.HiddenByAncestor
                    : HierarchyVisibilityState.Visible;
            HierarchyLockState lockState = directlyLocked
                ? HierarchyLockState.Locked
                : lockedBy is not null
                    ? HierarchyLockState.LockedByAncestor
                    : HierarchyLockState.Unlocked;
            entry.Row.SetEditorStates(
                visibilityState,
                directlyHidden ? null : hiddenBy,
                lockState,
                directlyLocked ? null : lockedBy);
            if (visibilityState != HierarchyVisibilityState.Visible)
            {
                _effectivelyHiddenKeys.Add(key);
            }

            string? descendantHiddenBy = directlyHidden
                ? entry.DisplayName
                : hiddenBy;
            string? descendantLockedBy = directlyLocked
                ? entry.DisplayName
                : lockedBy;
            foreach (string childKey in entry.Children)
            {
                Update(childKey, descendantHiddenBy, descendantLockedBy);
            }
        }
    }

    public IReadOnlyList<HierarchyRow> Flatten(
        string filter,
        IReadOnlySet<string> expanded)
    {
        string normalizedFilter = filter.Trim().ToUpperInvariant();
        List<HierarchyRow> rows = [];
        if (normalizedFilter.Length == 0)
        {
            AddExpanded(RootKey);
            return rows;
        }

        HashSet<string> matchingSubtrees = new(StringComparer.Ordinal);
        HashSet<string> matchingAncestors = new(StringComparer.Ordinal);
        foreach (Entry entry in _declarationOrder)
        {
            if (!entry.SearchText.Contains(
                    normalizedFilter,
                    StringComparison.Ordinal))
            {
                continue;
            }

            matchingSubtrees.Add(entry.Node.Key);
            Entry? ancestor = entry;
            while (ancestor?.ParentKey is string parentKey &&
                   _entries.TryGetValue(parentKey, out ancestor))
            {
                matchingAncestors.Add(ancestor.Node.Key);
            }
        }

        AddFiltered(RootKey, ancestorMatched: false);
        return rows;

        void AddExpanded(string key)
        {
            Entry entry = _entries[key];
            entry.Row.IsExpanded = expanded.Contains(key);
            rows.Add(entry.Row);
            if (!entry.Row.IsExpanded)
            {
                return;
            }

            foreach (string childKey in entry.Children)
            {
                AddExpanded(childKey);
            }
        }

        void AddFiltered(string key, bool ancestorMatched)
        {
            Entry entry = _entries[key];
            bool selfMatches = matchingSubtrees.Contains(key);
            if (!ancestorMatched &&
                !selfMatches &&
                !matchingAncestors.Contains(key))
            {
                return;
            }

            entry.Row.IsExpanded = true;
            rows.Add(entry.Row);
            foreach (string childKey in entry.Children)
            {
                AddFiltered(
                    childKey,
                    ancestorMatched || selfMatches);
            }
        }
    }

    public sealed record Entry(
        XuiSyntaxNode Node,
        string? ParentKey,
        IReadOnlyList<string> Children,
        string DisplayName,
        string SearchText,
        HierarchyRow Row);
}
