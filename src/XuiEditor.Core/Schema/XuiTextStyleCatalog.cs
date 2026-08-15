using System.Globalization;

namespace XuiEditor.Core.Schema;

public enum XuiTextStyleEvidence
{
    DyingLightStock,
    DyingLightBinary,
    Unknown,
}

public sealed record XuiTextStyleProfile(
    int RawValue,
    int StockOccurrenceCount,
    XuiTextStyleEvidence Evidence)
{
    public XuiDecodedTextStyle Decoded =>
        XuiTextStyleCodec.Decode(RawValue);

    public bool IsStockObserved =>
        Evidence == XuiTextStyleEvidence.DyingLightStock;

    public string CanonicalText =>
        RawValue.ToString(CultureInfo.InvariantCulture);

    public string HexText =>
        XuiTextStyleCodec.ToHexString(RawValue);
}

public static class XuiTextStyleCatalog
{
    private static readonly IReadOnlyList<XuiTextStyleProfile> StockProfiles =
    [
        Stock(0x0000, 17),
        Stock(0x0002, 2),
        Stock(0x0100, 174),
        Stock(0x0110, 190),
        Stock(0x0200, 113),
        Stock(0x0210, 17),
        Stock(0x0400, 197),
        Stock(0x0410, 25),
        Stock(0x0414, 2),
        Stock(0x1000, 85),
        Stock(0x1010, 144),
        Stock(0x1100, 88),
        Stock(0x1110, 316),
        Stock(0x1114, 1),
        Stock(0x1200, 106),
        Stock(0x1210, 233),
        Stock(0x1400, 159),
        Stock(0x1410, 334),
    ];

    private static readonly Dictionary<int, XuiTextStyleProfile>
        ProfilesByValue = StockProfiles.ToDictionary(
            static profile => profile.RawValue);

    public static IReadOnlyList<XuiTextStyleProfile> Profiles =>
        StockProfiles;

    public static bool TryResolve(
        string? rawValue,
        out XuiTextStyleProfile profile)
    {
        if (XuiTextStyleCodec.TryParse(rawValue, out XuiDecodedTextStyle style))
        {
            return TryResolve(style.RawValue, out profile);
        }

        profile = null!;
        return false;
    }

    public static bool TryResolve(
        int rawValue,
        out XuiTextStyleProfile profile) =>
        ProfilesByValue.TryGetValue(rawValue, out profile!);

    public static XuiTextStyleProfile Describe(int rawValue)
    {
        if (TryResolve(rawValue, out XuiTextStyleProfile profile))
        {
            return profile;
        }

        XuiDecodedTextStyle decoded = XuiTextStyleCodec.Decode(rawValue);
        XuiTextStyleEvidence evidence = decoded.UnknownBits == 0 &&
                                        decoded.CompatibilityBits != 0
            ? XuiTextStyleEvidence.DyingLightBinary
            : XuiTextStyleEvidence.Unknown;
        return new XuiTextStyleProfile(rawValue, 0, evidence);
    }

    private static XuiTextStyleProfile Stock(
        int rawValue,
        int occurrenceCount) =>
        new(
            rawValue,
            occurrenceCount,
            XuiTextStyleEvidence.DyingLightStock);
}
