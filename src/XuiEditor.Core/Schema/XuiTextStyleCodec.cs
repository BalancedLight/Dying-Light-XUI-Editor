using System.Globalization;

namespace XuiEditor.Core.Schema;

[Flags]
public enum XuiKnownTextStyle
{
    None = 0,
    ScaleAwareGlyphSizing = 0x0001,
    Italic = 0x0002,
    Bold = 0x0004,
    Underline = 0x0008,
    Compatibility0010 = 0x0010,
    HorizontalLeft = 0x0100,
    HorizontalRight = 0x0200,
    HorizontalCenter = 0x0400,
    VerticalMiddle = 0x1000,
    Compatibility4000 = 0x4000,
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
    public const int VisualMask = (int)(
        XuiKnownTextStyle.Italic |
        XuiKnownTextStyle.Bold |
        XuiKnownTextStyle.Underline |
        XuiKnownTextStyle.HorizontalLeft |
        XuiKnownTextStyle.HorizontalRight |
        XuiKnownTextStyle.HorizontalCenter |
        XuiKnownTextStyle.VerticalMiddle);
    public const int CompatibilityMask = (int)(
        XuiKnownTextStyle.ScaleAwareGlyphSizing |
        XuiKnownTextStyle.Compatibility0010 |
        XuiKnownTextStyle.Compatibility4000);
    public const int KnownMask = VisualMask | CompatibilityMask;
    public const int HorizontalMask = (int)(
        XuiKnownTextStyle.HorizontalLeft |
        XuiKnownTextStyle.HorizontalRight |
        XuiKnownTextStyle.HorizontalCenter);

    public bool Italic =>
        Has(XuiKnownTextStyle.Italic);

    public bool ScaleAwareGlyphSizing =>
        Has(XuiKnownTextStyle.ScaleAwareGlyphSizing);

    public bool Bold =>
        Has(XuiKnownTextStyle.Bold);

    public bool Underline =>
        Has(XuiKnownTextStyle.Underline);

    public bool VerticalMiddle =>
        Has(XuiKnownTextStyle.VerticalMiddle);

    public bool Compatibility0010 =>
        Has(XuiKnownTextStyle.Compatibility0010);

    public bool Compatibility4000 =>
        Has(XuiKnownTextStyle.Compatibility4000);

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

    // Compatibility property retained for callers that historically treated
    // every non-visual bit as unmapped.
    public int UnmappedBits => RawValue & ~VisualMask;

    public int CompatibilityBits => RawValue & CompatibilityMask;

    public int UnknownBits => RawValue & ~KnownMask;

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
