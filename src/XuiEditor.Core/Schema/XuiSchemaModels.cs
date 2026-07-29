using XuiEditor.Core.Documents;

namespace XuiEditor.Core.Schema;

public enum XuiPropertyType
{
    Textual,
    Boolean,
    WholeNumber,
    Number,
    Vector2,
    Vector3,
    Vector4,
    Quaternion,
    Color,
    Identifier,
    AssetReference,
}

public enum XuiEvidenceLevel
{
    DyingLightBinary,
    DyingLightStock,
    SharedChrome6,
    Chrome6Reference,
    Unknown,
}

public enum XuiPreviewSupport
{
    Exact,
    Approximate,
    PreserveOnly,
}

public sealed record XuiPropertyDefinition(
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
    public bool IsBoolean => Type == XuiPropertyType.Boolean;

    public string EvidenceLabel => Evidence switch
    {
        XuiEvidenceLevel.DyingLightBinary => "Dying Light binary",
        XuiEvidenceLevel.DyingLightStock => "Dying Light stock XUI",
        XuiEvidenceLevel.SharedChrome6 => "Shared Chrome 6",
        XuiEvidenceLevel.Chrome6Reference => "Chrome 6 reference only",
        _ => "Unknown / custom",
    };
}

public sealed record XuiClassDefinition(
    string Name,
    string? BaseClassName,
    double DefaultWidth,
    double DefaultHeight,
    string Description,
    XuiEvidenceLevel Evidence,
    IReadOnlyList<string> DirectProperties);

public sealed record XuiResolvedClassDefinition(
    XuiClassDefinition Class,
    IReadOnlyList<XuiClassDefinition> Inheritance,
    IReadOnlyList<XuiPropertyDefinition> Properties);

public sealed record XuiCatalogPropertySelection(
    XuiPropertyDefinition Definition,
    string? AuthoredValue,
    bool IsAuthored)
{
    public string EffectiveValue =>
        AuthoredValue ?? Definition.DefaultValue;
}

public interface IXuiClassCatalog
{
    IReadOnlyList<XuiPropertyDefinition> Properties { get; }

    IReadOnlyList<XuiClassDefinition> Classes { get; }

    IReadOnlyList<string> TimelinePropertyNames { get; }

    XuiPropertyDefinition? FindProperty(string name);

    XuiClassDefinition? FindClass(string name);

    XuiResolvedClassDefinition ResolveClass(
        XuiSyntaxNode node,
        string source);
}
