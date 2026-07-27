using System.Buffers.Binary;
using System.Text;
using XuiEditor.Core.Diagnostics;

namespace XuiEditor.Core.Assets;

public enum XuiInputGlyphScheme
{
    KeyboardAndMouse,
    Xbox,
    DualShock4,
}

public sealed record XuiInputGlyph(
    string Token,
    string Glyph,
    string Source);

public sealed class InputGlyphCatalog
{
    private const int MaximumEntries = 100_000;
    private readonly Dictionary<string, XuiInputGlyph> _glyphs =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<XuiInputGlyph> Glyphs => _glyphs.Values;

    public IReadOnlyList<XuiDiagnostic> Diagnostics { get; private set; } = [];

    public bool TryResolve(string token, out string glyph)
    {
        if (_glyphs.TryGetValue(token, out XuiInputGlyph? entry))
        {
            glyph = entry.Glyph;
            return true;
        }

        glyph = string.Empty;
        return false;
    }

    public static InputGlyphCatalog Parse(
        IEnumerable<(string Source, ReadOnlyMemory<byte> Bytes)> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        InputGlyphCatalog catalog = new();
        List<XuiDiagnostic> diagnostics = [];
        foreach ((string source, ReadOnlyMemory<byte> memory) in sources)
        {
            ReadOnlySpan<byte> bytes = memory.Span;
            if (bytes.Length < 8)
            {
                diagnostics.Add(new XuiDiagnostic(
                    "XUI-GLYPH001",
                    XuiDiagnosticSeverity.Warning,
                    $"Input glyph catalog '{source}' is truncated."));
                continue;
            }

            int count = BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]);
            if (count < 0 || count > MaximumEntries)
            {
                diagnostics.Add(new XuiDiagnostic(
                    "XUI-GLYPH001",
                    XuiDiagnosticSeverity.Warning,
                    $"Input glyph catalog '{source}' declares an unsafe entry count."));
                continue;
            }

            int cursor = 8;
            try
            {
                for (int index = 0; index < count; index++)
                {
                    int tokenLength = ReadLength(bytes, ref cursor);
                    Ensure(bytes, cursor, tokenLength);
                    string token = Encoding.UTF8.GetString(
                        bytes.Slice(cursor, tokenLength));
                    cursor += tokenLength;
                    int glyphCharacters = ReadLength(bytes, ref cursor);
                    int glyphBytes = checked(glyphCharacters * 2);
                    Ensure(bytes, cursor, glyphBytes);
                    string glyph = Encoding.Unicode.GetString(
                        bytes.Slice(cursor, glyphBytes));
                    cursor += glyphBytes;
                    catalog._glyphs[token] = new XuiInputGlyph(
                        token,
                        glyph,
                        source);
                }
            }
            catch (InvalidDataException exception)
            {
                diagnostics.Add(new XuiDiagnostic(
                    "XUI-GLYPH001",
                    XuiDiagnosticSeverity.Warning,
                    $"Input glyph catalog '{source}' is invalid: {exception.Message}"));
            }
        }

        catalog.Diagnostics = diagnostics;
        return catalog;
    }

    private static int ReadLength(
        ReadOnlySpan<byte> bytes,
        ref int cursor)
    {
        Ensure(bytes, cursor, 2);
        int length = BinaryPrimitives.ReadUInt16LittleEndian(bytes[cursor..]);
        cursor += 2;
        return length;
    }

    private static void Ensure(
        ReadOnlySpan<byte> bytes,
        int cursor,
        int amount)
    {
        if (cursor < 0 ||
            amount < 0 ||
            cursor > bytes.Length - amount)
        {
            throw new InvalidDataException("An entry extends beyond the catalog.");
        }
    }
}
