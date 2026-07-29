using XuiEditor.Core.Documents;

namespace XuiEditor.Core.Navigation;

public enum XuiNavigationResolutionStatus
{
    Resolved,
    Missing,
    Ambiguous,
    Invalid,
}

public sealed record XuiNavigationResolution(
    string AuthoredPath,
    XuiNavigationResolutionStatus Status,
    XuiSyntaxNode? Target,
    IReadOnlyList<XuiSyntaxNode> Candidates,
    string Message)
{
    public bool IsResolved =>
        Status == XuiNavigationResolutionStatus.Resolved &&
        Target is not null;
}

public sealed class XuiNavigationPathResolver
{
    private static readonly HashSet<string> StructuralElements =
        new(StringComparer.Ordinal)
        {
            "Properties",
            "Timelines",
            "Timeline",
            "TimelineProp",
            "KeyFrame",
            "Prop",
            "NamedFrames",
            "NamedFrame",
        };

    private readonly string _source;
    private readonly Dictionary<string, IReadOnlyList<XuiSyntaxNode>> _byId;

    public XuiNavigationPathResolver(
        XuiSyntaxNode root,
        string source)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _byId = VisualElements(root)
            .Select(node => (
                Node: node,
                Id: XuiModelReader.GetId(node, source)))
            .Where(static entry => !string.IsNullOrWhiteSpace(entry.Id))
            .GroupBy(static entry => entry.Id!, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<XuiSyntaxNode>)group
                    .Select(static entry => entry.Node)
                    .ToArray(),
                StringComparer.Ordinal);
    }

    public XuiNavigationResolution Resolve(
        XuiSyntaxNode sourceNode,
        string? authoredPath)
    {
        ArgumentNullException.ThrowIfNull(sourceNode);
        string path = authoredPath?.Trim() ?? string.Empty;
        if (path.Length == 0)
        {
            return new XuiNavigationResolution(
                path,
                XuiNavigationResolutionStatus.Missing,
                null,
                [],
                "The navigation property is empty.");
        }

        string normalized = path.Replace('/', '\\');
        if (!normalized.Contains('\\'))
        {
            return ResolveDirect(path, normalized);
        }

        string[] segments = normalized.Split(
            '\\',
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return new XuiNavigationResolution(
                path,
                XuiNavigationResolutionStatus.Invalid,
                null,
                [],
                "The navigation path has no resolvable segments.");
        }

        XuiSyntaxNode[] candidates = [sourceNode];
        foreach (string segment in segments)
        {
            if (segment == ".")
            {
                continue;
            }
            else if (segment == "..")
            {
                candidates = candidates
                    .Select(VisualParent)
                    .Where(static parent => parent is not null)
                    .Cast<XuiSyntaxNode>()
                    .DistinctBy(static node => node.Key, StringComparer.Ordinal)
                    .ToArray();
            }
            else
            {
                candidates = candidates
                    .SelectMany(VisualChildren)
                    .Where(node => string.Equals(
                        XuiModelReader.GetId(node, _source),
                        segment,
                        StringComparison.Ordinal))
                    .DistinctBy(static node => node.Key, StringComparer.Ordinal)
                    .ToArray();
            }

            if (candidates.Length == 0)
            {
                return new XuiNavigationResolution(
                    path,
                    XuiNavigationResolutionStatus.Missing,
                    null,
                    [],
                    $"Navigation segment '{segment}' was not found.");
            }
        }

        return candidates.Length == 1
            ? new XuiNavigationResolution(
                path,
                XuiNavigationResolutionStatus.Resolved,
                candidates[0],
                candidates,
                "Navigation path resolved.")
            : new XuiNavigationResolution(
                path,
                XuiNavigationResolutionStatus.Ambiguous,
                null,
                candidates,
                "The relative navigation path matches multiple elements.");
    }

    public bool TryCreateStablePath(
        XuiSyntaxNode sourceNode,
        XuiSyntaxNode targetNode,
        out string path,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(sourceNode);
        ArgumentNullException.ThrowIfNull(targetNode);
        string? targetId = XuiModelReader.GetId(targetNode, _source);
        if (string.IsNullOrWhiteSpace(targetId))
        {
            path = string.Empty;
            error = "The navigation target has no authored Id.";
            return false;
        }

        if (_byId.TryGetValue(targetId, out IReadOnlyList<XuiSyntaxNode>? direct) &&
            direct.Count == 1)
        {
            path = targetId;
            error = null;
            return true;
        }

        List<XuiSyntaxNode> sourceChain = VisualAncestorChain(sourceNode);
        List<XuiSyntaxNode> targetChain = VisualAncestorChain(targetNode);
        XuiSyntaxNode? common = sourceChain.FirstOrDefault(sourceAncestor =>
            targetChain.Any(targetAncestor =>
                targetAncestor.Key.Equals(
                    sourceAncestor.Key,
                    StringComparison.Ordinal)));
        if (common is null)
        {
            path = string.Empty;
            error = "The target is outside the current XUI visual tree.";
            return false;
        }

        int upCount = sourceChain
            .TakeWhile(node => !node.Key.Equals(
                common.Key,
                StringComparison.Ordinal))
            .Count();
        XuiSyntaxNode[] downNodes = targetChain
            .TakeWhile(node => !node.Key.Equals(
                common.Key,
                StringComparison.Ordinal))
            .Reverse()
            .ToArray();
        List<string> segments =
        [
            .. Enumerable.Repeat("..", upCount),
        ];
        if (upCount == 0 &&
            downNodes.Length == 1 &&
            _byId.TryGetValue(
                targetId,
                out IReadOnlyList<XuiSyntaxNode>? globalMatches) &&
            globalMatches.Count > 1)
        {
            segments.Add(".");
        }

        foreach (XuiSyntaxNode node in downNodes)
        {
            string? id = XuiModelReader.GetId(node, _source);
            if (string.IsNullOrWhiteSpace(id))
            {
                path = string.Empty;
                error =
                    "A relative navigation segment has no authored Id.";
                return false;
            }

            segments.Add(id);
        }

        if (segments.Count == 0)
        {
            path = targetId;
            error = null;
            return true;
        }

        string candidate = string.Join('\\', segments);
        XuiNavigationResolution resolution = Resolve(sourceNode, candidate);
        if (!resolution.IsResolved ||
            resolution.Target?.Key != targetNode.Key)
        {
            path = string.Empty;
            error =
                "A unique stable navigation path could not be produced.";
            return false;
        }

        path = candidate;
        error = null;
        return true;
    }

    public IReadOnlyList<XuiSyntaxNode> FindById(string id) =>
        string.IsNullOrWhiteSpace(id)
            ? []
            : _byId.GetValueOrDefault(id.Trim()) ?? [];

    private XuiNavigationResolution ResolveDirect(
        string authoredPath,
        string id)
    {
        IReadOnlyList<XuiSyntaxNode> candidates =
            _byId.GetValueOrDefault(id) ?? [];
        return candidates.Count switch
        {
            0 => new XuiNavigationResolution(
                authoredPath,
                XuiNavigationResolutionStatus.Missing,
                null,
                [],
                $"No element has Id '{id}'."),
            1 => new XuiNavigationResolution(
                authoredPath,
                XuiNavigationResolutionStatus.Resolved,
                candidates[0],
                candidates,
                "Navigation Id resolved."),
            _ => new XuiNavigationResolution(
                authoredPath,
                XuiNavigationResolutionStatus.Ambiguous,
                null,
                candidates,
                $"Id '{id}' is authored more than once."),
        };
    }

    private static List<XuiSyntaxNode> VisualAncestorChain(
        XuiSyntaxNode node)
    {
        List<XuiSyntaxNode> result = [];
        XuiSyntaxNode? current = node;
        while (current is not null)
        {
            if (IsVisualElement(current))
            {
                result.Add(current);
            }

            current = current.Parent;
        }

        return result;
    }

    private static XuiSyntaxNode? VisualParent(XuiSyntaxNode node)
    {
        XuiSyntaxNode? current = node.Parent;
        while (current is not null && !IsVisualElement(current))
        {
            current = current.Parent;
        }

        return current;
    }

    private static IEnumerable<XuiSyntaxNode> VisualChildren(
        XuiSyntaxNode node) =>
        node.ElementChildren.Where(IsVisualElement);

    private static IEnumerable<XuiSyntaxNode> VisualElements(
        XuiSyntaxNode root) =>
        root.DescendantsAndSelf().Where(IsVisualElement);

    private static bool IsVisualElement(XuiSyntaxNode node) =>
        node.Kind == XuiSyntaxKind.Element &&
        !StructuralElements.Contains(node.Name);
}
