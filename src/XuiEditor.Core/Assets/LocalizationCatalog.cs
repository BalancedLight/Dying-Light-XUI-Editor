using System.Buffers.Binary;
using System.Text;
using XuiEditor.Core.Diagnostics;

namespace XuiEditor.Core.Assets;

public sealed record XuiLocalizedString(
    string Key,
    string Value,
    int DeclarationOrder);

public interface ILocalizationCatalog
{
    string Locale { get; }

    IReadOnlyList<XuiLocalizedString> Entries { get; }

    IReadOnlyList<XuiDiagnostic> Diagnostics { get; }

    bool TryResolve(string key, out string value);

    string ResolveOrOriginal(string keyOrLiteral);
}

public sealed class LocalizationCatalog : ILocalizationCatalog
{
    private readonly Dictionary<string, string> _values;
    private readonly ILocalizationCatalog? _fallback;

    public LocalizationCatalog(
        string locale,
        IReadOnlyList<XuiLocalizedString> entries,
        IReadOnlyList<XuiDiagnostic> diagnostics,
        ILocalizationCatalog? fallback = null)
    {
        Locale = DyingLightInstallProfile.NormalizeLocale(locale);
        Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        Diagnostics = diagnostics ??
                      throw new ArgumentNullException(nameof(diagnostics));
        _fallback = fallback;
        _values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (XuiLocalizedString entry in entries)
        {
            _values[entry.Key] = entry.Value;
        }
    }

    public string Locale { get; }

    public IReadOnlyList<XuiLocalizedString> Entries { get; }

    public IReadOnlyList<XuiDiagnostic> Diagnostics { get; }

    public bool TryResolve(string key, out string value)
    {
        if (_values.TryGetValue(key, out string? resolved))
        {
            value = resolved;
            return true;
        }

        if (_fallback is not null &&
            _fallback.TryResolve(key, out resolved))
        {
            value = resolved;
            return true;
        }

        value = string.Empty;
        return false;
    }

    public string ResolveOrOriginal(string keyOrLiteral) =>
        TryResolve(keyOrLiteral, out string resolved)
            ? resolved
            : keyOrLiteral;
}

public static class LocalizationCatalogParser
{
    private const int MaximumEntries = 2_000_000;
    private const int MaximumTextLength = 65_535;

    public static LocalizationCatalog Parse(
        ReadOnlySpan<byte> bytes,
        string locale,
        ILocalizationCatalog? fallback = null,
        string? sourcePath = null)
    {
        if (bytes.Length < 8)
        {
            throw new InvalidDataException(
                "Dying Light localization catalog is smaller than its header.");
        }

        int version = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        int declaredCount = BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]);
        if (version <= 0 || declaredCount < 0 || declaredCount > MaximumEntries)
        {
            throw new InvalidDataException(
                $"Dying Light localization header is invalid (version {version}, entries {declaredCount}).");
        }

        List<XuiLocalizedString> entries = new(declaredCount);
        List<XuiDiagnostic> diagnostics = [];
        Dictionary<string, int> firstDeclarations =
            new(StringComparer.Ordinal);
        int cursor = 8;
        for (int index = 0; index < declaredCount; index++)
        {
            int keyLength = ReadLength(bytes, ref cursor, "key", index);
            EnsureAvailable(bytes, cursor, keyLength, "key", index);
            string key = Encoding.UTF8.GetString(
                bytes.Slice(cursor, keyLength));
            cursor += keyLength;

            int valueCharacters = ReadLength(
                bytes,
                ref cursor,
                "value",
                index);
            int valueBytes = checked(valueCharacters * 2);
            EnsureAvailable(bytes, cursor, valueBytes, "value", index);
            string value = Encoding.Unicode.GetString(
                bytes.Slice(cursor, valueBytes));
            cursor += valueBytes;
            entries.Add(new XuiLocalizedString(key, value, index));
            if (!firstDeclarations.TryAdd(key, index))
            {
                diagnostics.Add(new XuiDiagnostic(
                    "XUI-LOC002",
                    XuiDiagnosticSeverity.Info,
                    $"Localization key '{key}' is declared more than once; declaration {index + 1:N0} wins."));
            }
        }

        if (cursor != bytes.Length)
        {
            diagnostics.Add(new XuiDiagnostic(
                "XUI-LOC003",
                XuiDiagnosticSeverity.Warning,
                $"Localization catalog '{sourcePath ?? locale}' has {bytes.Length - cursor:N0} trailing bytes."));
        }

        return new LocalizationCatalog(
            locale,
            entries,
            diagnostics,
            fallback);
    }

    private static int ReadLength(
        ReadOnlySpan<byte> bytes,
        ref int cursor,
        string field,
        int index)
    {
        EnsureAvailable(bytes, cursor, 2, field, index);
        int length = BinaryPrimitives.ReadUInt16LittleEndian(bytes[cursor..]);
        cursor += 2;
        if (length > MaximumTextLength)
        {
            throw new InvalidDataException(
                $"Localization entry {index} has an unsafe {field} length.");
        }

        return length;
    }

    private static void EnsureAvailable(
        ReadOnlySpan<byte> bytes,
        int cursor,
        int amount,
        string field,
        int index)
    {
        if (cursor < 0 ||
            amount < 0 ||
            cursor > bytes.Length - amount)
        {
            throw new InvalidDataException(
                $"Localization entry {index} has a truncated {field}.");
        }
    }
}
