namespace XuiEditor.Core.Layout;

public sealed record XuiControllerRuntimeProperty(
    string Target,
    string Property,
    string Value);

public sealed record XuiControllerRuntimeProfile(
    string Id,
    string RootClass,
    string Description,
    IReadOnlyList<XuiControllerRuntimeProperty> Properties,
    IReadOnlySet<string> HiddenTargets)
{
    public IReadOnlyDictionary<string, string>? PropertiesFor(
        string nodeId,
        string nodeKey)
    {
        Dictionary<string, string>? result = null;
        foreach (XuiControllerRuntimeProperty property in Properties)
        {
            if (!property.Target.Equals(nodeId, StringComparison.Ordinal) &&
                !property.Target.Equals(nodeKey, StringComparison.Ordinal))
            {
                continue;
            }

            result ??= new Dictionary<string, string>(StringComparer.Ordinal);
            result[property.Property] = property.Value;
        }

        return result;
    }
}

public static class XuiControllerRuntimeProfileCatalog
{
    public static XuiControllerRuntimeProfile MenuYesNoDialog { get; } = new(
        "menu-yes-no-common",
        "MenuYesNoDialogDw",
        "The common Yes/No controller branch is previewed; the alternate OK branch remains revealable.",
        [
            new XuiControllerRuntimeProperty("ButtonYes", "Show", "true"),
            new XuiControllerRuntimeProperty("ButtonNo", "Show", "true"),
            new XuiControllerRuntimeProperty("ButtonOk", "Show", "false"),
        ],
        new HashSet<string>(["ButtonOk"], StringComparer.Ordinal));

    public static XuiControllerRuntimeProfile? Resolve(
        IEnumerable<string> classOverrides)
    {
        ArgumentNullException.ThrowIfNull(classOverrides);
        foreach (string classOverride in classOverrides)
        {
            if (classOverride.Equals(
                    MenuYesNoDialog.RootClass,
                    StringComparison.OrdinalIgnoreCase))
            {
                return MenuYesNoDialog;
            }
        }

        return null;
    }
}
