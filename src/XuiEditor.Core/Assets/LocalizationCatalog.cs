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
    private const int MaximumSourceLength = 64 * 1024 * 1024;

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

    public static LocalizationCatalog ParseSource(
        string source,
        string locale,
        ILocalizationCatalog? fallback = null,
        string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length > MaximumSourceLength)
        {
            throw new InvalidDataException(
                "Dying Light localization source exceeds the 64 MiB safety limit.");
        }

        List<XuiLocalizedString> entries = [];
        List<XuiDiagnostic> diagnostics = [];
        Dictionary<string, int> firstDeclarations =
            new(StringComparer.Ordinal);
        int cursor = 0;
        while (cursor < source.Length)
        {
            SkipTrivia(source, ref cursor);
            if (cursor >= source.Length)
            {
                break;
            }

            if (source[cursor] == '!')
            {
                SkipLine(source, ref cursor);
                continue;
            }

            if (!IsIdentifierStart(source[cursor]))
            {
                cursor++;
                continue;
            }

            int identifierStart = cursor++;
            while (cursor < source.Length &&
                   IsIdentifierPart(source[cursor]))
            {
                cursor++;
            }

            if (!source.AsSpan(identifierStart, cursor - identifierStart)
                    .Equals("String", StringComparison.Ordinal))
            {
                continue;
            }

            int declarationStart = identifierStart;
            SkipWhitespace(source, ref cursor);
            if (!Consume(source, ref cursor, '('))
            {
                continue;
            }

            SkipWhitespace(source, ref cursor);
            if (!TryReadQuoted(source, ref cursor, out string key))
            {
                AddSourceDiagnostic(
                    diagnostics,
                    sourcePath,
                    source,
                    declarationStart,
                    "expected a quoted localization key");
                SkipLine(source, ref cursor);
                continue;
            }

            SkipWhitespace(source, ref cursor);
            if (!Consume(source, ref cursor, ','))
            {
                AddSourceDiagnostic(
                    diagnostics,
                    sourcePath,
                    source,
                    declarationStart,
                    "expected a comma after the localization key");
                SkipLine(source, ref cursor);
                continue;
            }

            SkipWhitespace(source, ref cursor);
            if (!TryReadQuoted(source, ref cursor, out string value))
            {
                AddSourceDiagnostic(
                    diagnostics,
                    sourcePath,
                    source,
                    declarationStart,
                    "expected a quoted localization value");
                SkipLine(source, ref cursor);
                continue;
            }

            SkipWhitespace(source, ref cursor);
            if (!Consume(source, ref cursor, ')'))
            {
                AddSourceDiagnostic(
                    diagnostics,
                    sourcePath,
                    source,
                    declarationStart,
                    "expected ')' after the localization value");
                SkipLine(source, ref cursor);
                continue;
            }

            if (entries.Count >= MaximumEntries)
            {
                throw new InvalidDataException(
                    "Dying Light localization source exceeds the entry safety limit.");
            }

            if (key.Length > MaximumTextLength ||
                value.Length > MaximumTextLength)
            {
                AddSourceDiagnostic(
                    diagnostics,
                    sourcePath,
                    source,
                    declarationStart,
                    "key or value exceeds the 65,535-character safety limit");
                continue;
            }

            int declarationOrder = entries.Count;
            entries.Add(new XuiLocalizedString(
                key,
                value,
                declarationOrder));
            if (!firstDeclarations.TryAdd(key, declarationOrder))
            {
                diagnostics.Add(new XuiDiagnostic(
                    "XUI-LOC002",
                    XuiDiagnosticSeverity.Info,
                    $"Localization key '{key}' is declared more than once; declaration {declarationOrder + 1:N0} wins."));
            }
        }

        return new LocalizationCatalog(
            locale,
            entries,
            diagnostics,
            fallback);
    }

    private static void SkipTrivia(string source, ref int cursor)
    {
        while (cursor < source.Length)
        {
            SkipWhitespace(source, ref cursor);
            if (cursor + 1 >= source.Length ||
                source[cursor] != '/')
            {
                return;
            }

            if (source[cursor + 1] == '/')
            {
                SkipLine(source, ref cursor);
                continue;
            }

            if (source[cursor + 1] != '*')
            {
                return;
            }

            cursor += 2;
            int close = source.IndexOf("*/", cursor, StringComparison.Ordinal);
            cursor = close < 0 ? source.Length : close + 2;
        }
    }

    private static void SkipWhitespace(string source, ref int cursor)
    {
        while (cursor < source.Length &&
               char.IsWhiteSpace(source[cursor]))
        {
            cursor++;
        }
    }

    private static void SkipLine(string source, ref int cursor)
    {
        int lineEnd = source.IndexOf('\n', cursor);
        cursor = lineEnd < 0 ? source.Length : lineEnd + 1;
    }

    private static bool Consume(
        string source,
        ref int cursor,
        char expected)
    {
        if (cursor >= source.Length || source[cursor] != expected)
        {
            return false;
        }

        cursor++;
        return true;
    }

    private static bool TryReadQuoted(
        string source,
        ref int cursor,
        out string value)
    {
        value = string.Empty;
        if (!Consume(source, ref cursor, '"'))
        {
            return false;
        }

        StringBuilder? builder = null;
        int segmentStart = cursor;
        while (cursor < source.Length)
        {
            char character = source[cursor++];
            if (character == '"')
            {
                if (builder is null)
                {
                    value = source[segmentStart..(cursor - 1)];
                }
                else
                {
                    _ = builder.Append(
                        source,
                        segmentStart,
                        cursor - segmentStart - 1);
                    value = builder.ToString();
                }

                return true;
            }

            if (character != '\\')
            {
                continue;
            }

            builder ??= new StringBuilder();
            _ = builder.Append(
                source,
                segmentStart,
                cursor - segmentStart - 1);
            if (cursor >= source.Length)
            {
                return false;
            }

            char escaped = source[cursor++];
            _ = builder.Append(escaped switch
            {
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                '"' => '"',
                '\\' => '\\',
                _ => escaped,
            });
            segmentStart = cursor;
        }

        return false;
    }

    private static void AddSourceDiagnostic(
        List<XuiDiagnostic> diagnostics,
        string? sourcePath,
        string source,
        int offset,
        string detail)
    {
        int line = 1;
        for (int index = 0;
             index < offset && index < source.Length;
             index++)
        {
            if (source[index] == '\n')
            {
                line++;
            }
        }

        diagnostics.Add(new XuiDiagnostic(
            "XUI-LOC004",
            XuiDiagnosticSeverity.Warning,
            $"Localization source '{sourcePath ?? "source"}' line {line:N0} is malformed: {detail}."));
    }

    private static bool IsIdentifierStart(char character) =>
        character == '_' || char.IsLetter(character);

    private static bool IsIdentifierPart(char character) =>
        character == '_' || char.IsLetterOrDigit(character);

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
