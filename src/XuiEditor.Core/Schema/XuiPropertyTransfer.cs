using XuiEditor.Core.Documents;

namespace XuiEditor.Core.Schema;

public static class XuiPropertyTransfer
{
    private static readonly HashSet<string> ProtectedPropertyNames =
        new(StringComparer.Ordinal)
        {
            "Id",
            "ClassOverride",
        };

    public static bool CanCopy(string propertyName) =>
        !string.IsNullOrWhiteSpace(propertyName) &&
        !ProtectedPropertyNames.Contains(propertyName.Trim());

    public static bool IsApplicable(
        XuiClassCatalog catalog,
        XuiSyntaxNode destination,
        string source,
        string propertyName)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(source);
        if (!CanCopy(propertyName))
        {
            return false;
        }

        string name = propertyName.Trim();
        if (XuiModelReader.GetProperty(destination, source, name) is not null)
        {
            return true;
        }

        if (catalog.FindProperty(name) is null)
        {
            return false;
        }

        return catalog.ResolveClass(destination, source)
            .Properties
            .Any(property =>
                property.Name.Equals(name, StringComparison.Ordinal));
    }
}
