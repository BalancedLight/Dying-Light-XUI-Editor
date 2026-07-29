using XuiEditor.Core.Schema;

namespace XuiEditor.Wpf.Services;

public static class InspectorHelpText
{
    private static readonly HashSet<string> VerifiedPurposeNames =
        new(
        [
            "Bold",
            "ClassOverride",
            "ClipChildren",
            "Color",
            "ColorControlSequenceEnabled",
            "Font",
            "FontYOffset",
            "Height",
            "HoldAspectPivotPosition",
            "HoldAspectRatio",
            "HoldAspectRatioX",
            "HorizontalAlign",
            "Id",
            "ImagePath",
            "Italic",
            "KeepHeight",
            "KeepHeightOnParentSizeChange",
            "KeepHeightOnResolutionChange",
            "KeepPosX",
            "KeepPosXOnParentSizeChange",
            "KeepPosXOnResolutionChange",
            "KeepPosY",
            "KeepPosYOnParentSizeChange",
            "KeepPosYOnResolutionChange",
            "KeepWidth",
            "KeepWidthOnParentSizeChange",
            "KeepWidthOnResolutionChange",
            "Material",
            "MultiLine",
            "NavDown",
            "NavLeft",
            "NavRight",
            "NavTabBackward",
            "NavTabForward",
            "NavUp",
            "Opacity",
            "Outline",
            "OutlineColor",
            "OutlineSize",
            "Pivot",
            "PointSize",
            "Position",
            "Rotation",
            "Scale",
            "ScaleWidthByResolution",
            "Shadow",
            "ShadowColor",
            "ShadowOffset",
            "Show",
            "SpecialSignsScale",
            "Strike",
            "Text",
            "TextColor",
            "TextStyle",
            "Underline",
            "Uppercase",
            "VerticalAlign",
            "VerticalAlignDown",
            "Visual",
            "Width",
        ],
        StringComparer.Ordinal);

    public static IReadOnlySet<string> VerifiedProperties =>
        VerifiedPurposeNames;

    public static bool HasVerifiedPurpose(
        XuiPropertyDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return VerifiedPurposeNames.Contains(definition.Name);
    }

    public static string? Purpose(XuiPropertyDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!HasVerifiedPurpose(definition))
        {
            return null;
        }

        string key = $"Ui.Inspector.Property.{definition.Name}.Purpose";
        return UiLocalization.TryText(key, out string? localized)
            ? localized
            : definition.Description;
    }

    public static string Description(XuiPropertyDefinition definition) =>
        Purpose(definition) ??
        UiLocalization.Text("Ui.Inspector.ObservedStock");

    public static string BuildToolTip(
        string name,
        XuiPropertyDefinition? definition,
        bool isAuthored)
    {
        if (definition is null)
        {
            return string.Join(
                Environment.NewLine,
                name,
                UiLocalization.Text("Ui.Inspector.UnknownProperty"));
        }

        List<string> lines = [name];
        string? purpose = Purpose(definition);
        if (purpose is not null)
        {
            lines.Add(purpose);
        }

        lines.Add(UiLocalization.Format(
            "Ui.Inspector.Evidence",
            UiLocalization.Evidence(definition.Evidence)));
        lines.Add(UiLocalization.Format(
            "Ui.Inspector.Preview",
            UiLocalization.PreviewSupport(definition.PreviewSupport)));
        lines.Add(UiLocalization.Text(
            definition.IsAnimatable
                ? "Ui.Inspector.Timeline.Animatable"
                : "Ui.Inspector.Timeline.NotAnimatable"));
        lines.Add(
            isAuthored
                ? UiLocalization.Text("Ui.Inspector.Authored")
                : UiLocalization.Format(
                    "Ui.Inspector.InheritedDefault",
                    definition.DefaultValue));
        return string.Join(Environment.NewLine, lines);
    }
}
