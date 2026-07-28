using System.Globalization;
using System.Net;
using XuiEditor.Core.Values;

namespace XuiEditor.Core.Documents;

public enum XuiElementPreset
{
    Group,
    Image,
    Text,
    Rectangle,
    Button,
    CustomXml,
}

public sealed record XuiElementCreationRequest
{
    public required XuiElementPreset Preset { get; init; }

    public required string Id { get; init; }

    public double Width { get; init; }

    public double Height { get; init; }

    public XuiVector3 Position { get; init; }

    public string Text { get; init; } = string.Empty;

    public string ImagePath { get; init; } = string.Empty;

    public string Color { get; init; } = "0xffffffff";

    public string Font { get; init; } = "boxed_l_10";

    public string Visual { get; init; } = "ButtonV";

    public string RawXml { get; init; } = string.Empty;
}

public static class XuiElementFactory
{
    public static XuiVector2 DefaultSize(XuiElementPreset preset) =>
        preset switch
        {
            XuiElementPreset.Group => new XuiVector2(320, 180),
            XuiElementPreset.Image => new XuiVector2(128, 128),
            XuiElementPreset.Text => new XuiVector2(320, 40),
            XuiElementPreset.Rectangle => new XuiVector2(240, 120),
            XuiElementPreset.Button => new XuiVector2(260, 34),
            _ => new XuiVector2(100, 100),
        };

    public static string SuggestedIdPrefix(XuiElementPreset preset) =>
        preset switch
        {
            XuiElementPreset.Group => "G_NewGroup",
            XuiElementPreset.Image => "I_NewImage",
            XuiElementPreset.Text => "T_NewText",
            XuiElementPreset.Rectangle => "R_NewRectangle",
            XuiElementPreset.Button => "B_NewButton",
            _ => "X_NewElement",
        };

    public static string CreateXml(
        XuiElementCreationRequest request,
        string newLine = "\r\n")
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(newLine);
        if (request.Preset == XuiElementPreset.CustomXml)
        {
            return request.RawXml.Trim();
        }

        if (string.IsNullOrWhiteSpace(request.Id))
        {
            throw new InvalidOperationException(
                "A new XUI element requires a non-empty Id.");
        }

        if (!double.IsFinite(request.Width) ||
            !double.IsFinite(request.Height) ||
            request.Width < 0 ||
            request.Height < 0 ||
            !double.IsFinite(request.Position.X) ||
            !double.IsFinite(request.Position.Y) ||
            !double.IsFinite(request.Position.Z))
        {
            throw new InvalidOperationException(
                "New-element geometry must contain finite, non-negative dimensions.");
        }

        List<(string Name, string Value)> properties =
        [
            ("Id", request.Id.Trim()),
            ("Width", Number(request.Width)),
            ("Height", Number(request.Height)),
            ("Position", string.Create(
                CultureInfo.InvariantCulture,
                $"{request.Position.X:0.000000},{request.Position.Y:0.000000},{request.Position.Z:0.000000}")),
        ];
        string elementName;
        switch (request.Preset)
        {
            case XuiElementPreset.Group:
                elementName = "AdvGroup";
                break;
            case XuiElementPreset.Image:
                elementName = "MyImage";
                properties.Add((
                    "ImagePath",
                    string.IsNullOrWhiteSpace(request.ImagePath)
                        ? "white"
                        : request.ImagePath.Trim()));
                properties.Add(("Color", request.Color.Trim()));
                break;
            case XuiElementPreset.Text:
                elementName = "MyText";
                properties.Add((
                    "Text",
                    string.IsNullOrEmpty(request.Text)
                        ? "New text"
                        : request.Text));
                properties.Add(("TextColor", request.Color.Trim()));
                properties.Add(("Font", request.Font.Trim()));
                properties.Add(("PointSize", "24.000000"));
                break;
            case XuiElementPreset.Rectangle:
                elementName = "IUIAARectangle";
                properties.Add(("ImagePath", "white"));
                properties.Add(("ClassOverride", "IUIAARectangle"));
                properties.Add(("Color", request.Color.Trim()));
                properties.Add(("Material", "menu_antialias.mat"));
                properties.Add(("UseVertexColor", "true"));
                break;
            case XuiElementPreset.Button:
                elementName = "AdvButton";
                properties.Add(("Visual", request.Visual.Trim()));
                properties.Add((
                    "Text",
                    string.IsNullOrEmpty(request.Text)
                        ? "Button"
                        : request.Text));
                properties.Add(("AutoAdjustWidth", "true"));
                properties.Add(("WidthAdjust", "30"));
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(request),
                    request.Preset,
                    "Unknown XUI element preset.");
        }

        string[] propertyLines = properties
            .Select(property =>
                $"    <{property.Name}>{Encode(property.Value)}</{property.Name}>")
            .ToArray();
        return string.Join(
            newLine,
            new[]
            {
                $"<{elementName}>",
                "  <Properties>",
            }
            .Concat(propertyLines)
            .Concat(
            [
                "  </Properties>",
                $"</{elementName}>",
            ]));
    }

    private static string Number(double value) =>
        value.ToString("0.000000", CultureInfo.InvariantCulture);

    private static string Encode(string value) =>
        WebUtility.HtmlEncode(value)
            .Replace("&#39;", "&apos;", StringComparison.Ordinal);
}
