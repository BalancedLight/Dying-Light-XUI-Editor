using System.Globalization;
using XuiEditor.Core.Diagnostics;
using XuiEditor.Core.Values;

namespace XuiEditor.Core.Assets;

public sealed record XuiBitmapGlyph(
    int CodePoint,
    double Advance,
    XuiRect SourceRectangle,
    double VerticalOffset,
    bool IsSpecial);

public sealed record XuiBitmapFontMetrics(
    string Id,
    string Name,
    int MapWidth,
    int MapHeight,
    double FontHeight,
    IReadOnlyDictionary<int, XuiBitmapGlyph> Glyphs,
    string SourcePath);

public sealed record BitmapFontParseResult(
    XuiBitmapFontMetrics? Metrics,
    IReadOnlyList<XuiDiagnostic> Diagnostics);

public sealed record ResolvedBitmapFont(
    string RequestedId,
    string EngineFontId,
    double Size,
    double FontHeight,
    double CharacterSpacing,
    double SpecialSignsScale,
    XuiBitmapFontMetrics Metrics,
    int AtlasWidth,
    int AtlasHeight,
    byte[] AtlasBgraPixels,
    string AtlasSource,
    string ContentHash,
    IReadOnlyList<XuiDiagnostic> Diagnostics);

public static class BitmapFontParser
{
    public static BitmapFontParseResult Parse(
        string text,
        string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        string id = Path.GetFileNameWithoutExtension(sourcePath);
        string name = id;
        int mapWidth = 0;
        int mapHeight = 0;
        double fontHeight = 0;
        Dictionary<int, XuiBitmapGlyph> glyphs = [];
        List<XuiDiagnostic> diagnostics = [];
        int lineNumber = 0;
        foreach (string originalLine in text.ReplaceLineEndings("\n").Split('\n'))
        {
            lineNumber++;
            string line = StripComment(originalLine).Trim();
            if (line.Length == 0 ||
                !TryInvocation(
                    line,
                    out string invocation,
                    out List<string> arguments))
            {
                continue;
            }

            if (invocation == "Name" && arguments.Count >= 1)
            {
                name = arguments[0];
                continue;
            }

            if (invocation == "MapWidth" &&
                TryInteger(arguments, 0, out int width))
            {
                mapWidth = width;
                continue;
            }

            if (invocation == "MapHeight" &&
                TryInteger(arguments, 0, out int height))
            {
                mapHeight = height;
                continue;
            }

            if (invocation == "FontHeight" &&
                TryDouble(arguments, 0, out double heightValue))
            {
                fontHeight = heightValue;
                continue;
            }

            if (invocation is not ("Char" or "CharHeight" or
                "SpecialCharHeight"))
            {
                continue;
            }

            if (arguments.Count < 6 ||
                !TryInteger(arguments, 0, out int codePoint) ||
                !TryDouble(arguments, 1, out double advance) ||
                !TryDouble(arguments, 2, out double left) ||
                !TryDouble(arguments, 3, out double top) ||
                !TryDouble(arguments, 4, out double right) ||
                !TryDouble(arguments, 5, out double bottom) ||
                codePoint < 0 ||
                right < left ||
                bottom < top)
            {
                diagnostics.Add(new XuiDiagnostic(
                    "XUI-FONT003",
                    XuiDiagnosticSeverity.Warning,
                    $"Invalid bitmap glyph declaration at {sourcePath}:{lineNumber}."));
                continue;
            }

            double verticalOffset = 0;
            if (arguments.Count >= 8)
            {
                _ = TryDouble(arguments, 7, out verticalOffset);
            }

            glyphs[codePoint] = new XuiBitmapGlyph(
                codePoint,
                advance,
                new XuiRect(
                    left,
                    top,
                    right - left,
                    bottom - top),
                verticalOffset,
                invocation == "SpecialCharHeight");
        }

        if (mapWidth <= 0 ||
            mapHeight <= 0 ||
            fontHeight <= 0 ||
            glyphs.Count == 0)
        {
            diagnostics.Add(new XuiDiagnostic(
                "XUI-FONT004",
                XuiDiagnosticSeverity.Warning,
                $"Bitmap font metrics '{sourcePath}' are incomplete."));
            return new BitmapFontParseResult(null, diagnostics);
        }

        return new BitmapFontParseResult(
            new XuiBitmapFontMetrics(
                id,
                name,
                mapWidth,
                mapHeight,
                fontHeight,
                glyphs,
                sourcePath),
            diagnostics);
    }

    private static bool TryInvocation(
        string line,
        out string name,
        out List<string> arguments)
    {
        int open = line.IndexOf('(');
        int close = line.LastIndexOf(')');
        if (open <= 0 || close <= open)
        {
            name = string.Empty;
            arguments = [];
            return false;
        }

        name = line[..open].Trim();
        arguments = [];
        bool quoted = false;
        int start = open + 1;
        for (int index = start; index <= close; index++)
        {
            if (index < close && line[index] == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (index < close && (line[index] != ',' || quoted))
            {
                continue;
            }

            string value = line[start..index].Trim();
            if (value.Length >= 2 &&
                value[0] == '"' &&
                value[^1] == '"')
            {
                value = value[1..^1];
            }

            arguments.Add(value);
            start = index + 1;
        }

        return name.Length > 0;
    }

    private static string StripComment(string line)
    {
        bool quoted = false;
        for (int index = 0; index < line.Length - 1; index++)
        {
            if (line[index] == '"')
            {
                quoted = !quoted;
            }

            if (!quoted &&
                line[index] == '/' &&
                line[index + 1] == '/')
            {
                return line[..index];
            }
        }

        return line;
    }

    private static bool TryInteger(
        List<string> arguments,
        int index,
        out int value)
    {
        value = 0;
        return index < arguments.Count &&
               int.TryParse(
            arguments[index],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value);
    }

    private static bool TryDouble(
        List<string> arguments,
        int index,
        out double value)
    {
        value = 0;
        return index < arguments.Count &&
               double.TryParse(
            arguments[index],
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value) &&
               double.IsFinite(value);
    }
}
