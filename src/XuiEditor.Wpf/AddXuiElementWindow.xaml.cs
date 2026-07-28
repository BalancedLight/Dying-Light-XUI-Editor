using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using XuiEditor.Core.Documents;
using XuiEditor.Core.Values;

namespace XuiEditor.Wpf;

public partial class AddXuiElementWindow : Window
{
    private readonly XuiVector2 _parentSize;
    private readonly Func<XuiElementPreset, string> _suggestedId;
    private bool _updatingPreset;

    public AddXuiElementWindow(
        string parentDisplayName,
        XuiVector2 parentSize,
        Func<XuiElementPreset, string> suggestedId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentDisplayName);
        _suggestedId = suggestedId ??
                       throw new ArgumentNullException(nameof(suggestedId));
        _parentSize = parentSize;
        InitializeComponent();
        ParentText.Text =
            $"Add a visual child under {parentDisplayName}. The edit is lossless and undoable.";
        PresetCombo.ItemsSource = Enum.GetValues<XuiElementPreset>();
        PresetCombo.SelectedItem = XuiElementPreset.Group;
    }

    public XuiElementCreationRequest? Request { get; private set; }

    private void PresetCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs eventArgs)
    {
        if (_updatingPreset ||
            PresetCombo.SelectedItem is not XuiElementPreset preset)
        {
            return;
        }

        ApplyPreset(preset);
    }

    private void ApplyPreset(XuiElementPreset preset)
    {
        _updatingPreset = true;
        try
        {
            XuiVector2 size = XuiElementFactory.DefaultSize(preset);
            IdText.Text = _suggestedId(preset);
            WidthText.Text = Number(size.X);
            HeightText.Text = Number(size.Y);
            XText.Text = Number(Math.Max(0, (_parentSize.X - size.X) / 2));
            YText.Text = Number(Math.Max(0, (_parentSize.Y - size.Y) / 2));
            ContentText.Text = preset switch
            {
                XuiElementPreset.Text => "New text",
                XuiElementPreset.Button => "Button",
                _ => string.Empty,
            };
            ImagePathText.Text =
                preset == XuiElementPreset.Image ? "white" : string.Empty;
            ColorText.Text = "0xffffffff";
            FontText.Text = "boxed_l_10";
            VisualText.Text = "ButtonV";
            AdvancedXmlExpander.IsExpanded =
                preset == XuiElementPreset.CustomXml;
            RawXmlText.IsEnabled =
                preset == XuiElementPreset.CustomXml;
            if (preset == XuiElementPreset.CustomXml)
            {
                RawXmlText.Text = XuiElementFactory.CreateXml(
                    new XuiElementCreationRequest
                    {
                        Preset = XuiElementPreset.Group,
                        Id = _suggestedId(XuiElementPreset.CustomXml),
                        Width = size.X,
                        Height = size.Y,
                        Position = new XuiVector3(
                            ParseOrZero(XText.Text),
                            ParseOrZero(YText.Text),
                            0),
                    },
                    "\r\n");
            }

            PresetHelpText.Text = preset switch
            {
                XuiElementPreset.Group =>
                    "A lightweight AdvGroup container.",
                XuiElementPreset.Image =>
                    "A MyImage node. 'white' draws the selected color without requiring a texture.",
                XuiElementPreset.Text =>
                    "A MyText node using the selected engine font and text color.",
                XuiElementPreset.Rectangle =>
                    "An antialiased color rectangle using menu_antialias.mat and the white-image contract.",
                XuiElementPreset.Button =>
                    "An AdvButton using the stock ButtonV visual and auto-width behavior.",
                _ =>
                    "Edit the raw XML below. Preset fields are ignored in custom mode.",
            };
        }
        finally
        {
            _updatingPreset = false;
        }
    }

    private void Add_Click(object sender, RoutedEventArgs eventArgs)
    {
        try
        {
            XuiElementPreset preset =
                PresetCombo.SelectedItem is XuiElementPreset selected
                    ? selected
                    : XuiElementPreset.Group;
            if (preset == XuiElementPreset.CustomXml)
            {
                Request = new XuiElementCreationRequest
                {
                    Preset = preset,
                    Id = IdText.Text.Trim(),
                    RawXml = RawXmlText.Text,
                };
                if (string.IsNullOrWhiteSpace(Request.RawXml))
                {
                    throw new InvalidOperationException(
                        "Enter one visual XUI element in the custom XML box.");
                }
            }
            else
            {
                double width = ParseNumber(WidthText.Text, "Width");
                double height = ParseNumber(HeightText.Text, "Height");
                double x = ParseNumber(XText.Text, "X position");
                double y = ParseNumber(YText.Text, "Y position");
                if (preset is
                        XuiElementPreset.Image or
                        XuiElementPreset.Text or
                        XuiElementPreset.Rectangle &&
                    !XuiValueParser.TryColor(
                        ColorText.Text,
                        out _))
                {
                    throw new InvalidOperationException(
                        "Color must be an ARGB value such as 0xffffffff.");
                }

                Request = new XuiElementCreationRequest
                {
                    Preset = preset,
                    Id = IdText.Text.Trim(),
                    Width = width,
                    Height = height,
                    Position = new XuiVector3(x, y, 0),
                    Text = ContentText.Text,
                    ImagePath = ImagePathText.Text,
                    Color = ColorText.Text,
                    Font = FontText.Text,
                    Visual = VisualText.Text,
                };
                _ = XuiElementFactory.CreateXml(Request);
            }

            DialogResult = true;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            ArgumentException)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private static double ParseNumber(string raw, string label)
    {
        if (!double.TryParse(
                raw,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double value) ||
            !double.IsFinite(value))
        {
            throw new InvalidOperationException(
                $"{label} must be a finite number.");
        }

        return value;
    }

    private static double ParseOrZero(string raw) =>
        double.TryParse(
            raw,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out double value)
            ? value
            : 0;

    private static string Number(double value) =>
        value.ToString("0.000000", CultureInfo.InvariantCulture);
}
