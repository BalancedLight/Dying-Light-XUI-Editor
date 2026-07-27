using XuiEditor.Core.Documents;

namespace XuiEditor.Wpf.Models;

public sealed class HierarchyIndex
{
    private readonly Dictionary<string, Entry> _entries;
    private readonly IReadOnlyList<Entry> _declarationOrder;

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

    public static HierarchyIndex Build(XuiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
        List<Entry> declarationOrder = [];

        void Add(XuiSyntaxNode node, string? parentKey)
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
                    .ToUpperInvariant());
            entries.Add(node.Key, entry);
            declarationOrder.Add(entry);
            foreach (XuiSyntaxNode child in children)
            {
                Add(child, node.Key);
            }
        }

        Add(document.Root, parentKey: null);
        return new HierarchyIndex(document, entries, declarationOrder);
    }

    public bool IsCurrent(XuiDocument document) =>
        ReferenceEquals(Document, document) &&
        Revision == document.Revision;

    public Entry? Find(string key) => _entries.GetValueOrDefault(key);

    public IEnumerable<string> Ancestors(string key)
    {
        Entry? current = Find(key);
        while (current?.ParentKey is string parentKey &&
               _entries.TryGetValue(parentKey, out current))
        {
            yield return current.Node.Key;
        }
    }

    public IReadOnlyList<HierarchyRow> Flatten(
        string filter,
        IReadOnlySet<string> expanded,
        IReadOnlySet<string> hidden,
        IReadOnlySet<string> locked)
    {
        string normalizedFilter = filter.Trim().ToUpperInvariant();
        List<HierarchyRow> rows = [];
        if (normalizedFilter.Length == 0)
        {
            AddExpanded(RootKey, 0);
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

        AddFiltered(RootKey, 0, ancestorMatched: false);
        return rows;

        void AddExpanded(string key, int depth)
        {
            Entry entry = _entries[key];
            rows.Add(CreateRow(entry, depth, expanded.Contains(key)));
            if (!expanded.Contains(key))
            {
                return;
            }

            foreach (string childKey in entry.Children)
            {
                AddExpanded(childKey, depth + 1);
            }
        }

        void AddFiltered(string key, int depth, bool ancestorMatched)
        {
            Entry entry = _entries[key];
            bool selfMatches = matchingSubtrees.Contains(key);
            if (!ancestorMatched &&
                !selfMatches &&
                !matchingAncestors.Contains(key))
            {
                return;
            }

            rows.Add(CreateRow(entry, depth, isExpanded: true));
            foreach (string childKey in entry.Children)
            {
                AddFiltered(
                    childKey,
                    depth + 1,
                    ancestorMatched || selfMatches);
            }
        }

        HierarchyRow CreateRow(
            Entry entry,
            int depth,
            bool isExpanded) =>
            new(
                entry.Node,
                entry.DisplayName,
                depth,
                entry.Children.Count > 0,
                isExpanded,
                !hidden.Contains(entry.Node.Key),
                locked.Contains(entry.Node.Key));
    }

    public sealed record Entry(
        XuiSyntaxNode Node,
        string? ParentKey,
        IReadOnlyList<string> Children,
        string DisplayName,
        string SearchText);
}
