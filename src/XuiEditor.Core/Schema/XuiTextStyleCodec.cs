using System.Globalization;

namespace XuiEditor.Core.Schema;

[Flags]
public enum XuiKnownTextStyle
{
    None = 0,
    Italic = 0x0002,
    Bold = 0x0004,
    Underline = 0x0008,
    HorizontalLeft = 0x0100,
    HorizontalRight = 0x0200,
    HorizontalCenter = 0x0400,
    VerticalMiddle = 0x1000,
}

public enum XuiTextHorizontalStyle
{
    Unspecified,
    Left,
    Right,
    Center,
}

public readonly record struct XuiDecodedTextStyle(int RawValue)
{
    public const int KnownMask = (int)(
        XuiKnownTextStyle.Italic |
        XuiKnownTextStyle.Bold |
        XuiKnownTextStyle.Underline |
        XuiKnownTextStyle.HorizontalLeft |
        XuiKnownTextStyle.HorizontalRight |
        XuiKnownTextStyle.HorizontalCenter |
        XuiKnownTextStyle.VerticalMiddle);
    public const int HorizontalMask = (int)(
        XuiKnownTextStyle.HorizontalLeft |
        XuiKnownTextStyle.HorizontalRight |
        XuiKnownTextStyle.HorizontalCenter);

    public bool Italic =>
        Has(XuiKnownTextStyle.Italic);

    public bool Bold =>
        Has(XuiKnownTextStyle.Bold);

    public bool Underline =>
        Has(XuiKnownTextStyle.Underline);

    public bool VerticalMiddle =>
        Has(XuiKnownTextStyle.VerticalMiddle);

    public XuiTextHorizontalStyle HorizontalAlignment =>
        (RawValue & HorizontalMask) switch
        {
            (int)XuiKnownTextStyle.HorizontalLeft =>
                XuiTextHorizontalStyle.Left,
            (int)XuiKnownTextStyle.HorizontalRight =>
                XuiTextHorizontalStyle.Right,
            (int)XuiKnownTextStyle.HorizontalCenter =>
                XuiTextHorizontalStyle.Center,
            _ => XuiTextHorizontalStyle.Unspecified,
        };

    public int UnmappedBits => RawValue & ~KnownMask;

    public bool Has(XuiKnownTextStyle style) =>
        (RawValue & (int)style) != 0;
}

public static class XuiTextStyleCodec
{
    public static bool TryParse(
        string? value,
        out XuiDecodedTextStyle style)
    {
        string text = value?.Trim() ?? string.Empty;
        NumberStyles numberStyles = NumberStyles.Integer;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
            numberStyles = NumberStyles.AllowHexSpecifier;
        }

        if (int.TryParse(
                text,
                numberStyles,
                CultureInfo.InvariantCulture,
                out int raw))
        {
            style = new XuiDecodedTextStyle(raw);
            return true;
        }

        style = default;
        return false;
    }

    public static XuiDecodedTextStyle Decode(int rawValue) =>
        new(rawValue);

    public static int SetFlag(
        int rawValue,
        XuiKnownTextStyle flag,
        bool enabled) =>
        enabled
            ? rawValue | (int)flag
            : rawValue & ~(int)flag;

    public static int SetHorizontalAlignment(
        int rawValue,
        XuiTextHorizontalStyle alignment)
    {
        int updated = rawValue & ~XuiDecodedTextStyle.HorizontalMask;
        return alignment switch
        {
            XuiTextHorizontalStyle.Left =>
                updated | (int)XuiKnownTextStyle.HorizontalLeft,
            XuiTextHorizontalStyle.Right =>
                updated | (int)XuiKnownTextStyle.HorizontalRight,
            XuiTextHorizontalStyle.Center =>
                updated | (int)XuiKnownTextStyle.HorizontalCenter,
            _ => updated,
        };
    }

    public static int SetVerticalMiddle(int rawValue, bool enabled) =>
        SetFlag(rawValue, XuiKnownTextStyle.VerticalMiddle, enabled);

    public static string ToDecimalString(int rawValue) =>
        rawValue.ToString(CultureInfo.InvariantCulture);

    public static string ToHexString(int rawValue) =>
        $"0x{rawValue:X8}";
}

