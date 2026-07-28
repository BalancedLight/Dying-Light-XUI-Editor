using XuiEditor.Core.Animation;
using XuiEditor.Core.Assets;
using XuiEditor.Core.Documents;
using XuiEditor.Core.Values;

namespace XuiEditor.Core.Layout;

internal sealed class DyingLightLayoutCompilation
{
    private readonly XuiDocument _document;
    private readonly IAssetResolver? _assetResolver;
    private readonly Dictionary<string, XuiSyntaxNode> _documentNodesByKey;
    private readonly Dictionary<XuiSyntaxNode, CompiledXuiNode> _nodes =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, XuiVisualTemplate?> _visuals =
        new(StringComparer.Ordinal);
    private readonly Dictionary<XuiVisualTemplate, AnimationOverrides>
        _visualAnimations =
            new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(string Material, XuiRenderKind Kind), XuiMaterialProfile>
        _materials = [];

    public DyingLightLayoutCompilation(
        XuiDocument document,
        IAssetResolver? assetResolver)
    {
        _document = document;
        _assetResolver = assetResolver;
        _documentNodesByKey = document.Root
            .DescendantsAndSelf()
            .ToDictionary(
                static node => node.Key,
                StringComparer.Ordinal);
        CompileTree(document.Root, document.Text);
        ControllerRuntimeProfile =
            XuiControllerRuntimeProfileCatalog.Resolve(
                _nodes.Values.Select(static node =>
                    node.ClassOverride));
    }

    public int NodeCount => _nodes.Count;

    public int VisualCount => _visuals.Count;

    public int MaterialProfileCount => _materials.Count;

    public XuiControllerRuntimeProfile? ControllerRuntimeProfile { get; }

    public bool IsTargetForceShown(
        string target,
        XuiRenderContext context) =>
        _nodes.Any(pair =>
            pair.Value.Id.Equals(target, StringComparison.Ordinal) &&
            context.IsForceShown(pair.Value.Id, pair.Key.Key));

    public CompiledXuiNode Node(
        XuiSyntaxNode syntax,
        string source)
    {
        if (_nodes.TryGetValue(syntax, out CompiledXuiNode? compiled))
        {
            return compiled;
        }

        return CompileTree(syntax, source);
    }

    public XuiVisualTemplate? ResolveVisual(string visualId)
    {
        if (visualId.Length == 0 || _assetResolver is null)
        {
            return null;
        }

        if (_visuals.TryGetValue(
                visualId,
                out XuiVisualTemplate? visual))
        {
            return visual;
        }

        visual = _assetResolver.ResolveVisual(visualId);
        _visuals.Add(visualId, visual);
        if (visual is not null)
        {
            CompileTree(visual.Syntax, visual.Source);
        }

        return visual;
    }

    public AnimationOverrides ResolveVisualAnimation(
        XuiVisualTemplate visualTemplate)
    {
        ArgumentNullException.ThrowIfNull(visualTemplate);
        if (_visualAnimations.TryGetValue(
                visualTemplate,
                out AnimationOverrides? animation))
        {
            return animation;
        }

        animation = new AnimationOverrides(
            visualTemplate.Timelines,
            XuiTimelineEvaluationState.Initial);
        _visualAnimations.Add(visualTemplate, animation);
        return animation;
    }

    public XuiMaterialProfile ResolveMaterial(
        string material,
        XuiRenderKind kind)
    {
        (string Material, XuiRenderKind Kind) key = (material, kind);
        if (_materials.TryGetValue(
                key,
                out XuiMaterialProfile? profile))
        {
            return profile;
        }

        profile = XuiMaterialCatalog.Resolve(material, kind);
        _materials.Add(key, profile);
        return profile;
    }

    public XuiSyntaxNode? DocumentNode(string key) =>
        _documentNodesByKey.GetValueOrDefault(key);

    public bool HasAuthoredProperty(string key, string property) =>
        DocumentNode(key) is XuiSyntaxNode syntax &&
        Node(syntax, _document.Text).Properties.ContainsKey(property);

    public string? TimelineRecursionBarrier(string key)
    {
        XuiSyntaxNode? current = DocumentNode(key)?.Parent;
        while (current is not null)
        {
            CompiledXuiNode compiled = Node(current, _document.Text);
            if (compiled.Properties.TryGetValue(
                    "DisableTimelineRecursion",
                    out string? raw) &&
                XuiValueParser.TryBoolean(raw, out bool disabled) &&
                disabled)
            {
                return current.Key;
            }

            current = current.Parent;
        }

        return null;
    }

    public static XuiRenderKind Classify(
        string name,
        string classOverride,
        string visual)
    {
        string combined = name + " " + classOverride;
        if (combined.Contains("Canvas", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("Scene", StringComparison.OrdinalIgnoreCase))
        {
            return XuiRenderKind.Scene;
        }

        if (combined.Contains("Text", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("Html", StringComparison.OrdinalIgnoreCase))
        {
            return XuiRenderKind.Text;
        }

        if (combined.Contains("Rectangle", StringComparison.OrdinalIgnoreCase))
        {
            return XuiRenderKind.Rectangle;
        }

        if (combined.Contains("Shape", StringComparison.OrdinalIgnoreCase))
        {
            return XuiRenderKind.Shape;
        }

        if (combined.Contains("Image", StringComparison.OrdinalIgnoreCase))
        {
            return XuiRenderKind.Image;
        }

        if (combined.Contains("Group", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("Panel", StringComparison.OrdinalIgnoreCase))
        {
            return XuiRenderKind.Group;
        }

        if (combined.Contains(
                "Presenter",
                StringComparison.OrdinalIgnoreCase))
        {
            return XuiRenderKind.Presenter;
        }

        if (name.StartsWith("UI", StringComparison.Ordinal) ||
            name.StartsWith("Adv", StringComparison.Ordinal) ||
            classOverride.StartsWith("UI", StringComparison.Ordinal) ||
            visual.Length > 0)
        {
            return XuiRenderKind.Control;
        }

        return XuiRenderKind.Unknown;
    }

    private CompiledXuiNode CompileTree(
        XuiSyntaxNode syntax,
        string source)
    {
        if (_nodes.TryGetValue(syntax, out CompiledXuiNode? existing))
        {
            return existing;
        }

        Dictionary<string, string> properties =
            new(StringComparer.Ordinal);
        foreach (XuiPropertyEntry property in
                 XuiModelReader.GetProperties(syntax, source))
        {
            properties[property.Name] = property.Value;
        }

        XuiSyntaxNode[] children =
            XuiModelReader.VisualChildren(syntax).ToArray();
        string id = properties.GetValueOrDefault("Id", string.Empty);
        string visual = properties.GetValueOrDefault(
            "Visual",
            string.Empty).Trim();
        string classOverride = properties.GetValueOrDefault(
            "ClassOverride",
            string.Empty);
        XuiRenderKind kind = Classify(
            syntax.Name,
            classOverride,
            visual);
        string material = properties.GetValueOrDefault(
            "Material",
            string.Empty).Trim();
        CompiledXuiNode compiled = new(
            syntax.Name,
            properties,
            children,
            id,
            visual,
            classOverride,
            kind,
            material,
            ResolveMaterial(material, kind));
        _nodes.Add(syntax, compiled);

        foreach (XuiSyntaxNode child in children)
        {
            CompileTree(child, source);
        }

        if (visual.Length > 0)
        {
            ResolveVisual(visual);
        }

        return compiled;
    }
}

internal sealed record CompiledXuiNode(
    string SyntaxName,
    IReadOnlyDictionary<string, string> Properties,
    IReadOnlyList<XuiSyntaxNode> VisualChildren,
    string Id,
    string Visual,
    string ClassOverride,
    XuiRenderKind Kind,
    string Material,
    XuiMaterialProfile MaterialProfile);
