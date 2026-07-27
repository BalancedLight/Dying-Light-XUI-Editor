namespace XuiEditor.Core.Documents;

public sealed record XuiPropertyEntry(
    string Name,
    string RawValue,
    string Value,
    XuiSyntaxNode Element,
    int Ordinal);

public static class XuiModelReader
{
    private static readonly HashSet<string> StructuralNames = new(StringComparer.Ordinal)
    {
        "Properties",
        "Timelines",
        "Timeline",
        "TimelineProp",
        "KeyFrame",
        "NamedFrames",
        "NamedFrame",
        "Name",
        "Time",
        "Command",
        "CommandParams",
        "Interpolation",
        "EaseIn",
        "EaseOut",
        "EaseScale",
        "Prop",
    };

    public static IReadOnlyList<XuiPropertyEntry> GetProperties(
        XuiSyntaxNode element,
        string source)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(source);
        XuiSyntaxNode? propertiesNode = element.FirstElement("Properties");
        if (propertiesNode is null)
        {
            return [];
        }

        List<XuiPropertyEntry> entries = [];
        int ordinal = 0;
        foreach (XuiSyntaxNode property in propertiesNode.ElementChildren)
        {
            string rawValue = property.GetRawContent(source);
            entries.Add(new XuiPropertyEntry(
                property.Name,
                rawValue,
                property.GetDecodedValue(source),
                property,
                ordinal++));
        }

        return entries;
    }

    public static XuiPropertyEntry? GetProperty(
        XuiSyntaxNode element,
        string source,
        string name) =>
        GetProperties(element, source).LastOrDefault(property =>
            string.Equals(property.Name, name, StringComparison.Ordinal));

    public static string? GetPropertyValue(
        XuiSyntaxNode element,
        string source,
        string name) =>
        GetProperty(element, source, name)?.Value;

    public static string? GetId(XuiSyntaxNode element, string source) =>
        GetPropertyValue(element, source, "Id");

    public static bool IsStructural(XuiSyntaxNode node) =>
        StructuralNames.Contains(node.Name);

    public static IEnumerable<XuiSyntaxNode> VisualChildren(XuiSyntaxNode element) =>
        element.ElementChildren.Where(child => !IsStructural(child));

    public static IEnumerable<XuiSyntaxNode> VisualDescendants(XuiSyntaxNode root)
    {
        foreach (XuiSyntaxNode child in VisualChildren(root))
        {
            yield return child;
            foreach (XuiSyntaxNode descendant in VisualDescendants(child))
            {
                yield return descendant;
            }
        }
    }
}
