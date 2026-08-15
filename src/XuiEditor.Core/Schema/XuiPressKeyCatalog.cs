using System.Globalization;
using XuiEditor.Core.Values;

namespace XuiEditor.Core.Schema;

public enum XuiPressKeyGroup
{
    None,
    FaceButtons,
    ShouldersAndTriggers,
    DirectionalPad,
    SystemAndSticks,
}

public sealed record XuiPressKeyOption(
    string Id,
    string EngineName,
    string DisplayNameKey,
    XuiPressKeyGroup Group,
    int CanonicalSerializedValue,
    IReadOnlyList<int> RuntimeAliases,
    string KeyboardHint,
    string GamepadHint,
    string KeyboardLabel,
    string GamepadLabel,
    bool KeyboardHintUsesSeparateBackground,
    IReadOnlyList<string> SearchAliases)
{
    public string CanonicalText =>
        CanonicalSerializedValue.ToString(CultureInfo.InvariantCulture);

    public bool Accepts(int value) =>
        value == CanonicalSerializedValue || RuntimeAliases.Contains(value);
}

public static class XuiPressKeyCatalog
{
    private static readonly IReadOnlyList<XuiPressKeyOption> KnownOptions =
    [
        new(
            "None",
            "EPressKey::None",
            "Ui.PressKey.Option.None",
            XuiPressKeyGroup.None,
            22594,
            [],
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            false,
            ["disabled", "empty"]),
        new(
            "PadA",
            "EPressKey::Pad_A",
            "Ui.PressKey.Option.PadA",
            XuiPressKeyGroup.FaceButtons,
            22528,
            [3840],
            "&[PC_ENTER]&",
            "&[A]&",
            "Enter",
            "A",
            false,
            ["confirm", "accept"]),
        new(
            "PadB",
            "EPressKey::Pad_B",
            "Ui.PressKey.Option.PadB",
            XuiPressKeyGroup.FaceButtons,
            22529,
            [3841],
            "&[PC_ESC]&",
            "&[B]&",
            "Esc",
            "B",
            false,
            ["escape", "cancel", "back"]),
        new(
            "PadX",
            "EPressKey::Pad_X",
            "Ui.PressKey.Option.PadX",
            XuiPressKeyGroup.FaceButtons,
            22530,
            [3842],
            "F",
            "&[X]&",
            "F",
            "X",
            true,
            []),
        new(
            "PadY",
            "EPressKey::Pad_Y",
            "Ui.PressKey.Option.PadY",
            XuiPressKeyGroup.FaceButtons,
            22531,
            [3843],
            "C",
            "&[Y]&",
            "C",
            "Y",
            true,
            []),
        new(
            "PadAOrStart",
            "EPressKey::Pad_A_or_Start",
            "Ui.PressKey.Option.PadAOrStart",
            XuiPressKeyGroup.FaceButtons,
            22592,
            [],
            "&[PC_ENTER]&",
            "&[A]&",
            "Enter",
            "A / Start",
            false,
            ["confirm", "accept"]),
        new(
            "PadBOrBack",
            "EPressKey::Pad_B_or_Back",
            "Ui.PressKey.Option.PadBOrBack",
            XuiPressKeyGroup.FaceButtons,
            22593,
            [],
            "&[PC_ESC]&",
            "&[B]&",
            "Esc",
            "B / Back",
            false,
            ["escape", "cancel"]),
        new(
            "PadRShoulder",
            "EPressKey::Pad_RShoulder",
            "Ui.PressKey.Option.PadRShoulder",
            XuiPressKeyGroup.ShouldersAndTriggers,
            22532,
            [3845],
            "E",
            "&[RB]&",
            "E",
            "RB",
            true,
            ["right shoulder", "right bumper"]),
        new(
            "PadLShoulder",
            "EPressKey::Pad_LShoulder",
            "Ui.PressKey.Option.PadLShoulder",
            XuiPressKeyGroup.ShouldersAndTriggers,
            22533,
            [3844],
            "Q",
            "&[LB]&",
            "Q",
            "LB",
            true,
            ["left shoulder", "left bumper"]),
        new(
            "PadRTrigger",
            "EPressKey::Pad_RTrigger",
            "Ui.PressKey.Option.PadRTrigger",
            XuiPressKeyGroup.ShouldersAndTriggers,
            22535,
            [7941],
            "&[PC_RT]&",
            "&[RT]&",
            "F7",
            "RT",
            true,
            ["right trigger", "f7"]),
        new(
            "PadLTrigger",
            "EPressKey::Pad_LTrigger",
            "Ui.PressKey.Option.PadLTrigger",
            XuiPressKeyGroup.ShouldersAndTriggers,
            22534,
            [7938],
            "&[PC_LT]&",
            "&[LT]&",
            "F6",
            "LT",
            true,
            ["left trigger", "f6"]),
        new(
            "DPadUp",
            "EPressKey::DPAD_Up",
            "Ui.PressKey.Option.DPadUp",
            XuiPressKeyGroup.DirectionalPad,
            22544,
            [3850],
            "&[ArrowUp]&",
            "&[DpadUp]&",
            "Up Arrow",
            "D-pad Up",
            false,
            ["direction up", "arrow up", "keyboard up"]),
        new(
            "DPadDown",
            "EPressKey::DPAD_Down",
            "Ui.PressKey.Option.DPadDown",
            XuiPressKeyGroup.DirectionalPad,
            22545,
            [3851],
            "&[ArrowDown]&",
            "&[DpadDown]&",
            "Down Arrow",
            "D-pad Down",
            false,
            ["direction down", "arrow down", "keyboard down"]),
        new(
            "DPadLeft",
            "EPressKey::DPAD_Left",
            "Ui.PressKey.Option.DPadLeft",
            XuiPressKeyGroup.DirectionalPad,
            22546,
            [3852],
            "&[ArrowLeft]&",
            "&[DpadLeft]&",
            "Left Arrow",
            "D-pad Left",
            false,
            ["direction left", "arrow left", "keyboard left"]),
        new(
            "DPadRight",
            "EPressKey::DPAD_Right",
            "Ui.PressKey.Option.DPadRight",
            XuiPressKeyGroup.DirectionalPad,
            22547,
            [3853],
            "&[ArrowRight]&",
            "&[DpadRight]&",
            "Right Arrow",
            "D-pad Right",
            false,
            ["direction right", "arrow right", "keyboard right"]),
        new(
            "PadStart",
            "EPressKey::Pad_START",
            "Ui.PressKey.Option.PadStart",
            XuiPressKeyGroup.SystemAndSticks,
            22548,
            [3847],
            "&[PC_START]&",
            "&[Start]&",
            "F4",
            "Start",
            true,
            ["menu", "f4"]),
        new(
            "PadBack",
            "EPressKey::Pad_BACK",
            "Ui.PressKey.Option.PadBack",
            XuiPressKeyGroup.SystemAndSticks,
            22549,
            [3846],
            "&[PC_BACK]&",
            "&[Back]&",
            "F5",
            "Back",
            true,
            ["select", "view", "f5"]),
        new(
            "PadLThumbPress",
            "EPressKey::Pad_LThumbPress",
            "Ui.PressKey.Option.PadLThumbPress",
            XuiPressKeyGroup.SystemAndSticks,
            22550,
            [3848],
            "&[PC_LThumb]&",
            "&[L3]&",
            "Z",
            "L3",
            true,
            ["left thumb", "left stick press", "z"]),
        new(
            "PadRThumbPress",
            "EPressKey::Pad_RThumbPress",
            "Ui.PressKey.Option.PadRThumbPress",
            XuiPressKeyGroup.SystemAndSticks,
            22551,
            [3849],
            "&[PC_RThumb]&",
            "&[R3]&",
            "X",
            "R3",
            true,
            ["right thumb", "right stick press", "x"]),
    ];

    private static readonly Dictionary<int, XuiPressKeyOption>
        OptionsByValue = BuildValueIndex();

    public static IReadOnlyList<XuiPressKeyOption> Options => KnownOptions;

    public static bool TryResolve(
        string? rawValue,
        out XuiPressKeyOption option)
    {
        if (XuiValueParser.TryInteger(rawValue, out int value))
        {
            return TryResolve(value, out option);
        }

        option = null!;
        return false;
    }

    public static bool TryResolve(int value, out XuiPressKeyOption option) =>
        OptionsByValue.TryGetValue(value, out option!);

    public static string Canonicalize(XuiPressKeyOption option)
    {
        ArgumentNullException.ThrowIfNull(option);
        return option.CanonicalText;
    }

    private static Dictionary<int, XuiPressKeyOption> BuildValueIndex()
    {
        Dictionary<int, XuiPressKeyOption> index = [];
        foreach (XuiPressKeyOption option in KnownOptions)
        {
            index.Add(option.CanonicalSerializedValue, option);
            foreach (int alias in option.RuntimeAliases)
            {
                index.Add(alias, option);
            }
        }

        return index;
    }
}
