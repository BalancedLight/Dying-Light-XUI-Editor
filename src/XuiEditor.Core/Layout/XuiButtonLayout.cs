using XuiEditor.Core.Assets;

namespace XuiEditor.Core.Layout;

public enum XuiButtonLayoutKind
{
    GenericWithHints,
    Dialog,
}

public sealed record XuiButtonLayoutProfile(
    string Id,
    XuiButtonLayoutKind Kind,
    bool RequiresAutoAdjustWidth,
    double KeyboardGap,
    double ControllerGap,
    double LabelPadding,
    double HintBackgroundScale)
{
    public static XuiButtonLayoutProfile GenericWithHints { get; } = new(
        "button-with-hints",
        XuiButtonLayoutKind.GenericWithHints,
        RequiresAutoAdjustWidth: true,
        KeyboardGap: 29,
        ControllerGap: 4,
        LabelPadding: 0,
        HintBackgroundScale: 0.7);

    public static XuiButtonLayoutProfile Dialog { get; } = new(
        "dialog-button",
        XuiButtonLayoutKind.Dialog,
        RequiresAutoAdjustWidth: false,
        KeyboardGap: 32,
        ControllerGap: 8,
        LabelPadding: 20,
        HintBackgroundScale: 1);

    public XuiButtonLayoutResult Measure(
        XuiInputGlyphScheme inputGlyphScheme,
        double measuredLabelWidth,
        double measuredHintWidth,
        double resolutionScale = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(measuredLabelWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(measuredHintWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(resolutionScale);

        bool keyboard =
            inputGlyphScheme == XuiInputGlyphScheme.KeyboardAndMouse;
        double gap = (keyboard ? KeyboardGap : ControllerGap) *
                     resolutionScale;
        double labelBlock =
            measuredLabelWidth + (LabelPadding * resolutionScale);
        double hintElement = measuredHintWidth +
                             (keyboard ? 0 : gap);
        return new XuiButtonLayoutResult(
            this,
            inputGlyphScheme,
            measuredLabelWidth,
            measuredHintWidth,
            labelBlock,
            hintElement,
            measuredHintWidth + gap,
            labelBlock + measuredHintWidth + gap,
            HintBackgroundScale);
    }

    public static XuiButtonLayoutProfile? Resolve(
        string classOverride,
        string visual)
    {
        if (classOverride.Contains(
                "DialogButton",
                StringComparison.OrdinalIgnoreCase) ||
            visual.Contains(
                "ButtonDialog",
                StringComparison.OrdinalIgnoreCase))
        {
            return Dialog;
        }

        if (classOverride.Contains(
                "ButtonWithHints",
                StringComparison.OrdinalIgnoreCase) ||
            visual.Contains(
                "ButtonWithHint",
                StringComparison.OrdinalIgnoreCase))
        {
            return GenericWithHints;
        }

        return null;
    }
}

public sealed record XuiButtonLayoutResult(
    XuiButtonLayoutProfile Profile,
    XuiInputGlyphScheme ActiveHintScheme,
    double MeasuredLabelWidth,
    double MeasuredHintWidth,
    double LabelBlockWidth,
    double HintElementWidth,
    double HintBlockWidth,
    double TotalWidth,
    double HintBackgroundScale);
