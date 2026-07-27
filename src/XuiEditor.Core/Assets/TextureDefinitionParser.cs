using System.Globalization;
using XuiEditor.Core.Diagnostics;
using XuiEditor.Core.Values;

namespace XuiEditor.Core.Assets;

public sealed record TextureDefinitionParseResult(
    IReadOnlyList<XuiTextureRegion> Regions,
    IReadOnlyList<XuiDiagnostic> Diagnostics);

public static class TextureDefinitionParser
{
    public static TextureDefinitionParseResult Parse(string text, string definitionPath)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionPath);
        List<XuiTextureRegion> regions = [];
        List<XuiDiagnostic> diagnostics = [];
        TextureContext? texture = null;
        TileSetContext? tileSet = null;
        int lineNumber = 0;

        foreach (string originalLine in text.ReplaceLineEndings("\n").Split('\n'))
        {
            lineNumber++;
            string line = StripComment(originalLine).Trim();
            if (line.Length == 0 || line[0] == '!')
            {
                continue;
            }

            if (line.StartsWith('}'))
            {
                if (tileSet is not null)
                {
                    if (texture is not null)
                    {
                        regions.Add(new XuiTextureRegion(
                            tileSet.Name,
                            texture.File,
                            texture.Width,
                            texture.Height,
                            new XuiRect(0, 0, texture.Width, texture.Height),
                            XuiTexturePrimitive.TileSet,
                            default,
                            tileSet.Parts.ToArray(),
                            definitionPath));
                    }

                    tileSet = null;
                }
                else
                {
                    texture = null;
                }

                continue;
            }

            if (!TryInvocation(line, out string name, out List<string> arguments))
            {
                continue;
            }

            if (name == "Atlas")
            {
                continue;
            }

            if (name == "Texture")
            {
                if (arguments.Count < 3 ||
                    !TryInt(arguments[1], out int textureWidth) ||
                    !TryInt(arguments[2], out int textureHeight) ||
                    textureWidth <= 0 ||
                    textureHeight <= 0)
                {
                    AddInvalid(diagnostics, definitionPath, lineNumber, "Texture");
                    texture = null;
                    continue;
                }

                texture = new TextureContext(
                    arguments[0],
                    textureWidth,
                    textureHeight);
                tileSet = null;
                continue;
            }

            if (texture is null)
            {
                continue;
            }

            if (name == "Whole" && arguments.Count >= 1)
            {
                regions.Add(new XuiTextureRegion(
                    arguments[0],
                    texture.File,
                    texture.Width,
                    texture.Height,
                    new XuiRect(0, 0, texture.Width, texture.Height),
                    XuiTexturePrimitive.Whole,
                    default,
                    [],
                    definitionPath));
                continue;
            }

            if (name is "Rect" or "RectWithCorner")
            {
                int required = name == "Rect" ? 5 : 7;
                if (arguments.Count < required ||
                    !TryInt(arguments[1], out int left) ||
                    !TryInt(arguments[2], out int top) ||
                    !TryInt(arguments[3], out int right) ||
                    !TryInt(arguments[4], out int bottom) ||
                    right < left ||
                    bottom < top)
                {
                    AddInvalid(diagnostics, definitionPath, lineNumber, name);
                    continue;
                }

                XuiVector2 corners = default;
                int cornerWidth = 0;
                int cornerHeight = 0;
                if (name == "RectWithCorner" &&
                    (!TryInt(arguments[5], out cornerWidth) ||
                     !TryInt(arguments[6], out cornerHeight)))
                {
                    AddInvalid(diagnostics, definitionPath, lineNumber, name);
                    continue;
                }
                else if (name == "RectWithCorner")
                {
                    corners = new XuiVector2(cornerWidth, cornerHeight);
                }

                XuiTextureRegion region = new(
                    arguments[0],
                    texture.File,
                    texture.Width,
                    texture.Height,
                    new XuiRect(left, top, right - left, bottom - top),
                    name == "Rect"
                        ? XuiTexturePrimitive.Rectangle
                        : XuiTexturePrimitive.RectangleWithCorner,
                    corners,
                    [],
                    definitionPath);
                regions.Add(region);
                tileSet?.Regions.Add(region);
                continue;
            }

            if (name == "Tileset" && arguments.Count >= 1)
            {
                tileSet = new TileSetContext(arguments[0]);
                continue;
            }

            if (tileSet is not null &&
                Enum.TryParse(name, ignoreCase: false, out XuiTileRole role) &&
                arguments.Count >= 2 &&
                TryInt(arguments[1], out int probability))
            {
                int rotationMode = 0;
                if (arguments.Count >= 3)
                {
                    _ = TryInt(arguments[2], out rotationMode);
                }

                tileSet.Parts.Add(new XuiTilePart(
                    role,
                    arguments[0],
                    probability,
                    rotationMode));
            }
        }

        return new TextureDefinitionParseResult(regions, diagnostics);
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
        arguments = ParseArguments(line.AsSpan(open + 1, close - open - 1));
        return name.Length > 0;
    }

    private static List<string> ParseArguments(ReadOnlySpan<char> source)
    {
        List<string> result = [];
        int start = 0;
        bool quoted = false;
        for (int index = 0; index <= source.Length; index++)
        {
            if (index < source.Length && source[index] == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (index < source.Length && (source[index] != ',' || quoted))
            {
                continue;
            }

            string value = source[start..index].Trim().ToString();
            if (value.Length >= 2 &&
                value[0] == '"' &&
                value[^1] == '"')
            {
                value = value[1..^1];
            }

            result.Add(value);
            start = index + 1;
        }

        return result;
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

    private static bool TryInt(string text, out int value) =>
        int.TryParse(
            text,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value);

    private static void AddInvalid(
        List<XuiDiagnostic> diagnostics,
        string definitionPath,
        int lineNumber,
        string primitive) =>
        diagnostics.Add(new XuiDiagnostic(
            "XUI-ASSET001",
            XuiDiagnosticSeverity.Warning,
            $"Invalid {primitive} declaration at {definitionPath}:{lineNumber}."));

    private sealed record TextureContext(string File, int Width, int Height);

    private sealed class TileSetContext
    {
        public TileSetContext(string name) => Name = name;

        public string Name { get; }

        public List<XuiTextureRegion> Regions { get; } = [];

        public List<XuiTilePart> Parts { get; } = [];
    }
}
