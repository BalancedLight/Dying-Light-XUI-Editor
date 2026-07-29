using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using XuiEditor.Core.Documents;

namespace XuiEditor.Core.Schema;

public sealed partial class XuiClassCatalog : IXuiClassCatalog
{
    private const string ResourceSuffix = "Schema.DyingLightXuiCatalog.json";
    private static readonly Lazy<XuiClassCatalog> DefaultCatalog =
        new(LoadEmbedded);
    private readonly Dictionary<string, XuiPropertyDefinition> _properties;
    private readonly Dictionary<string, XuiClassDefinition> _classes;

    public XuiClassCatalog(
        IEnumerable<XuiPropertyDefinition> properties,
        IEnumerable<XuiClassDefinition> classes,
        IEnumerable<string> timelinePropertyNames)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(classes);
        ArgumentNullException.ThrowIfNull(timelinePropertyNames);
        _properties = properties.ToDictionary(
            static property => property.Name,
            StringComparer.Ordinal);
        _classes = classes.ToDictionary(
            static definition => definition.Name,
            StringComparer.Ordinal);
        TimelinePropertyNames = timelinePropertyNames
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public static XuiClassCatalog Default => DefaultCatalog.Value;

    public IReadOnlyList<XuiPropertyDefinition> Properties =>
        _properties.Values
            .OrderBy(static property => property.Category, StringComparer.Ordinal)
            .ThenBy(static property => property.Name, StringComparer.Ordinal)
            .ToArray();

    public IReadOnlyList<XuiClassDefinition> Classes =>
        _classes.Values
            .OrderBy(static definition => definition.Name, StringComparer.Ordinal)
            .ToArray();

    public IReadOnlyList<string> TimelinePropertyNames { get; }

    public XuiPropertyDefinition? FindProperty(string name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : _properties.GetValueOrDefault(name.Trim());

    public XuiClassDefinition? FindClass(string name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : _classes.GetValueOrDefault(name.Trim());

    public XuiResolvedClassDefinition ResolveClass(
        XuiSyntaxNode node,
        string source)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(source);
        string? classOverride = XuiModelReader.GetPropertyValue(
            node,
            source,
            "ClassOverride");
        XuiClassDefinition definition =
            FindClass(classOverride ?? string.Empty) ??
            FindClass(node.Name) ??
            FindClass("XuiElement") ??
            throw new InvalidOperationException(
                "The embedded XUI catalog has no XuiElement definition.");
        List<XuiClassDefinition> inheritance = [];
        HashSet<string> visited = new(StringComparer.Ordinal);
        XuiClassDefinition? current = definition;
        while (current is not null && visited.Add(current.Name))
        {
            inheritance.Add(current);
            current = current.BaseClassName is null
                ? null
                : FindClass(current.BaseClassName);
        }

        Dictionary<string, XuiPropertyDefinition> resolved =
            new(StringComparer.Ordinal);
        foreach (XuiClassDefinition inherited in inheritance.AsEnumerable().Reverse())
        {
            foreach (string propertyName in inherited.DirectProperties)
            {
                if (FindProperty(propertyName) is XuiPropertyDefinition property)
                {
                    resolved[propertyName] = property;
                }
            }
        }

        return new XuiResolvedClassDefinition(
            definition,
            inheritance,
            resolved.Values
                .OrderBy(static property => property.Category, StringComparer.Ordinal)
                .ThenBy(static property => property.Name, StringComparer.Ordinal)
                .ToArray());
    }

    public IReadOnlyList<XuiCatalogPropertySelection> SelectProperties(
        IReadOnlyList<XuiSyntaxNode> nodes,
        string source,
        bool includeAdvanced)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(source);
        if (nodes.Count == 0)
        {
            return [];
        }

        HashSet<string>? applicable = null;
        foreach (XuiSyntaxNode node in nodes)
        {
            HashSet<string> nodeProperties = ResolveClass(node, source)
                .Properties
                .Where(property => includeAdvanced || !property.IsAdvanced)
                .Select(static property => property.Name)
                .ToHashSet(StringComparer.Ordinal);
            if (applicable is null)
            {
                applicable = nodeProperties;
            }
            else
            {
                applicable.IntersectWith(nodeProperties);
            }
        }

        applicable ??= new HashSet<string>(StringComparer.Ordinal);
        foreach (XuiSyntaxNode node in nodes)
        {
            foreach (XuiPropertyEntry property in XuiModelReader.GetProperties(node, source))
            {
                applicable.Add(property.Name);
            }
        }

        return applicable
            .Select(name =>
            {
                XuiPropertyDefinition definition =
                    FindProperty(name) ??
                    new XuiPropertyDefinition(
                        name,
                        XuiPropertyType.Textual,
                        "Raw / Unknown",
                        string.Empty,
                        "Unknown mod-authored property; preserved losslessly.",
                        [],
                        true,
                        true,
                        XuiEvidenceLevel.Unknown,
                        XuiPreviewSupport.PreserveOnly,
                        []);
                string? value = XuiModelReader.GetPropertyValue(
                    nodes[0],
                    source,
                    name);
                return new XuiCatalogPropertySelection(
                    definition,
                    value,
                    value is not null);
            })
            .OrderBy(static property => property.Definition.Category, StringComparer.Ordinal)
            .ThenBy(static property => property.Definition.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static XuiClassCatalog LoadEmbedded()
    {
        Assembly assembly = typeof(XuiClassCatalog).Assembly;
        string resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(
                ResourceSuffix,
                StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(resourceName) ??
            throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' could not be opened.");
        CatalogDto dto = JsonSerializer.Deserialize(
            stream,
            CatalogJsonContext.Default.CatalogDto) ??
            throw new InvalidOperationException(
                "The embedded XUI catalog is empty.");
        if (!string.Equals(
                dto.Format,
                "dying-light-xui-catalog-v1",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Unsupported embedded XUI catalog '{dto.Format}'.");
        }

        return new XuiClassCatalog(
            dto.Properties.Select(static property => property.ToDefinition()),
            dto.Classes.Select(static definition => definition.ToDefinition()),
            dto.TimelineProperties);
    }

    internal sealed record CatalogDto(
        string Format,
        int StockXuiCount,
        IReadOnlyList<PropertyDto> Properties,
        IReadOnlyList<ClassDto> Classes,
        IReadOnlyList<string> TimelineProperties);

    internal sealed record PropertyDto(
        string Name,
        XuiPropertyType Type,
        string Category,
        string DefaultValue,
        string Description,
        IReadOnlyList<string> Choices,
        bool IsAdvanced,
        bool IsAnimatable,
        XuiEvidenceLevel Evidence,
        XuiPreviewSupport PreviewSupport,
        IReadOnlyList<string> Flags)
    {
        public XuiPropertyDefinition ToDefinition() =>
            new(
                Name,
                Type,
                Category,
                DefaultValue,
                Description,
                Choices,
                IsAdvanced,
                IsAnimatable,
                Evidence,
                PreviewSupport,
                Flags);
    }

    internal sealed record ClassDto(
        string Name,
        string? BaseClassName,
        double DefaultWidth,
        double DefaultHeight,
        string Description,
        XuiEvidenceLevel Evidence,
        IReadOnlyList<string> DirectProperties)
    {
        public XuiClassDefinition ToDefinition() =>
            new(
                Name,
                BaseClassName,
                DefaultWidth,
                DefaultHeight,
                Description,
                Evidence,
                DirectProperties);
    }

    [JsonSourceGenerationOptions(
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        UseStringEnumConverter = true)]
    [JsonSerializable(typeof(CatalogDto))]
    internal sealed partial class CatalogJsonContext : JsonSerializerContext;
}
