using System.Globalization;
using XuiEditor.Core.Diagnostics;

namespace XuiEditor.Core.Assets;

public sealed record FontDefinitionParseResult(
    IReadOnlyList<XuiFontDefinition> Fonts,
    IReadOnlyList<XuiFontStyle> Styles,
    IReadOnlyList<XuiDiagnostic> Diagnostics)
{
    public double GlobalScale { get; init; } = 1;
}

public static class FontDefinitionParser
{
    public static FontDefinitionParseResult Parse(
        IEnumerable<(string Path, string Text)> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        List<XuiFontDefinition> fonts = [];
        List<XuiFontStyle> styles = [];
        List<XuiDiagnostic> diagnostics = [];
        double globalScale = 1;

        foreach ((string path, string text) in sources)
        {
            int lineNumber = 0;
            foreach (string originalLine in text.ReplaceLineEndings("\n").Split('\n'))
            {
                lineNumber++;
                string line = StripComment(originalLine).Trim();
                if (line.Length == 0 || line[0] == '!' ||
                    !TryInvocation(line, out string name, out List<string> arguments))
                {
                    continue;
                }

                if (name == "Scaling" &&
                    arguments.Count >= 1 &&
                    TryDouble(arguments[0], out double parsedScale) &&
                    parsedScale > 0)
                {
                    globalScale = parsedScale;
                }
                else if (name is "Font" or "FontAlias")
                {
                    if (arguments.Count < 6 ||
                        !TryDouble(arguments[2], out double size) ||
                        !TryInt(arguments[3], out int style) ||
                        !TryDouble(arguments[5], out double heightScale))
                    {
                        AddInvalid(diagnostics, path, lineNumber, name);
                        continue;
                    }

                    fonts.Add(new XuiFontDefinition(
                        arguments[0],
                        arguments[1],
                        size,
                        style,
                        heightScale,
                        arguments.Count >= 7 ? arguments[6] : null));
                }
                else if (name is "FontStyle" or "FontStyleAlias")
                {
                    if (arguments.Count < 6 ||
                        !TryDouble(arguments[2], out double scale) ||
                        !TryDouble(arguments[3], out double outline) ||
                        !TryDouble(arguments[4], out double spacing) ||
                        !TryDouble(arguments[5], out double signsScale))
                    {
                        AddInvalid(diagnostics, path, lineNumber, name);
                        continue;
                    }

                    styles.Add(new XuiFontStyle(
                        arguments[0],
                        arguments[1],
                        scale,
                        outline,
                        spacing,
                        signsScale)
                    {
                        IsAlias = name == "FontStyleAlias",
                    });
                }
            }
        }

        return new FontDefinitionParseResult(fonts, styles, diagnostics)
        {
            GlobalScale = globalScale,
        };
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
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                value = value[1..^1];
            }

            arguments.Add(value);
            start = index + 1;
        }

        return true;
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

            if (!quoted && line[index] == '/' && line[index + 1] == '/')
            {
                return line[..index];
            }
        }

        return line;
    }

    private static bool TryDouble(string text, out double value) =>
        double.TryParse(
            text,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value);

    private static bool TryInt(string text, out int value) =>
        int.TryParse(
            text,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value);

    private static void AddInvalid(
        List<XuiDiagnostic> diagnostics,
        string path,
        int line,
        string primitive) =>
        diagnostics.Add(new XuiDiagnostic(
            "XUI-ASSET002",
            XuiDiagnosticSeverity.Warning,
            $"Invalid {primitive} declaration at {path}:{line}."));
}
