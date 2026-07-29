using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using XuiEditor.Core.Documents;
using XuiEditor.Core.Schema;
using XuiEditor.Core.Values;
using XuiEditor.Wpf.Services;

namespace XuiEditor.Wpf;

public partial class AddXuiElementWindow : Window
{
    private readonly XuiVector2 _parentSize;
    private readonly Func<XuiElementPreset, string> _suggestedId;
    private readonly bool _identityPlacement;
    private readonly XuiClassDefinition[] _catalogClasses;
    private readonly IReadOnlyList<LocalizedEnumOption<XuiElementPreset>>
        _presetOptions;
    private bool _updatingPreset;

    public AddXuiElementWindow(
        string parentDisplayName,
        XuiVector2 parentSize,
        Func<XuiElementPreset, string> suggestedId,
        IReadOnlyList<XuiElementPreset>? availablePresets = null,
        bool identityPlacement = false,
        string? windowTitle = null,
        string? actionLabel = null,
        string? instruction = null)
    {
        UiLocalization.EnsureApplied();
        ArgumentException.ThrowIfNullOrWhiteSpace(parentDisplayName);
        _suggestedId = suggestedId ??
                       throw new ArgumentNullException(nameof(suggestedId));
        _parentSize = parentSize;
        _identityPlacement = identityPlacement;
        InitializeComponent();
        Language = UiLocalization.XmlLanguage;
        Title = windowTitle ?? Title;
        ConfirmButton.Content = actionLabel ?? ConfirmButton.Content;
        ParentText.Text = instruction ??
            UiLocalization.Format(
                "Ui.AddElement.ParentInstruction",
                parentDisplayName);
        _catalogClasses = XuiClassCatalog.Default.Classes
            .Where(static definition =>
                definition.Evidence == XuiEvidenceLevel.DyingLightStock)
            .OrderBy(static definition => definition.Name, StringComparer.Ordinal)
            .ToArray();
        CatalogClassCombo.ItemsSource = _catalogClasses;
        XuiElementPreset[] presets =
            availablePresets?.ToArray() ??
            Enum.GetValues<XuiElementPreset>();
        if (presets.Length == 0)
        {
            throw new ArgumentException(
                "At least one XUI element preset must be available.",
                nameof(availablePresets));
        }

        _presetOptions = UiLocalization.EnumOptions(presets);
        PresetCombo.ItemsSource = _presetOptions;
        PresetCombo.SelectedIndex = 0;
    }

    public XuiElementCreationRequest? Request { get; private set; }

    private void PresetCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs eventArgs)
    {
        if (_updatingPreset ||
            PresetCombo.SelectedItem is not
                LocalizedEnumOption<XuiElementPreset> option)
        {
            return;
        }

        ApplyPreset(option.Value);
    }

    private void CatalogClassCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs eventArgs)
    {
        if (_updatingPreset ||
            SelectedPreset() != XuiElementPreset.CatalogClass ||
            CatalogClassCombo.SelectedItem is not XuiClassDefinition definition)
        {
            return;
        }

        ApplyCatalogClass(definition);
    }

    private void ApplyPreset(XuiElementPreset preset)
    {
        _updatingPreset = true;
        try
        {
            if (preset == XuiElementPreset.CatalogClass &&
                CatalogClassCombo.SelectedItem is null &&
                _catalogClasses.Length > 0)
            {
                CatalogClassCombo.SelectedItem = _catalogClasses[0];
            }

            XuiVector2 size = _identityPlacement
                ? _parentSize
                : preset == XuiElementPreset.CatalogClass &&
                  CatalogClassCombo.SelectedItem is
                      XuiClassDefinition selectedClass
                    ? new XuiVector2(
                        selectedClass.DefaultWidth,
                        selectedClass.DefaultHeight)
                    : XuiElementFactory.DefaultSize(preset);
            IdText.Text = _suggestedId(preset);
            WidthText.Text = Number(size.X);
            HeightText.Text = Number(size.Y);
            XText.Text = Number(_identityPlacement
                ? 0
                : Math.Max(0, (_parentSize.X - size.X) / 2));
            YText.Text = Number(_identityPlacement
                ? 0
                : Math.Max(0, (_parentSize.Y - size.Y) / 2));
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
            CatalogClassCombo.Visibility =
                preset == XuiElementPreset.CatalogClass
                    ? Visibility.Visible
                    : Visibility.Collapsed;
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
                    UiLocalization.Text("Ui.AddElement.Help.Group"),
                XuiElementPreset.Image =>
                    UiLocalization.Text("Ui.AddElement.Help.Image"),
                XuiElementPreset.Text =>
                    UiLocalization.Text("Ui.AddElement.Help.Text"),
                XuiElementPreset.Rectangle =>
                    UiLocalization.Text("Ui.AddElement.Help.Rectangle"),
                XuiElementPreset.Button =>
                    UiLocalization.Text("Ui.AddElement.Help.Button"),
                XuiElementPreset.CatalogClass =>
                    UiLocalization.Text("Ui.AddElement.Help.CatalogClass"),
                _ =>
                    UiLocalization.Text("Ui.AddElement.Help.CustomXml"),
            };
        }
        finally
        {
            _updatingPreset = false;
        }
    }

    private void ApplyCatalogClass(XuiClassDefinition definition)
    {
        _updatingPreset = true;
        try
        {
            XuiVector2 size = _identityPlacement
                ? _parentSize
                : new XuiVector2(
                    definition.DefaultWidth,
                    definition.DefaultHeight);
            WidthText.Text = Number(size.X);
            HeightText.Text = Number(size.Y);
            XText.Text = Number(_identityPlacement
                ? 0
                : Math.Max(0, (_parentSize.X - size.X) / 2));
            YText.Text = Number(_identityPlacement
                ? 0
                : Math.Max(0, (_parentSize.Y - size.Y) / 2));
            IdText.Text = _suggestedId(XuiElementPreset.CatalogClass);
            PresetHelpText.Text =
                UiLocalization.Format(
                    "Ui.AddElement.CatalogSummary",
                    definition.Name,
                    definition.Description,
                    UiLocalization.Evidence(definition.Evidence),
                    definition.BaseClassName ??
                    UiLocalization.Text("Ui.Common.None"));
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
                SelectedPreset();
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
                        UiLocalization.Text(
                            "Ui.AddElement.Error.CustomXmlRequired"));
                }
            }
            else
            {
                double width = ParseNumber(
                    WidthText.Text,
                    UiLocalization.Text("Ui.AddElement.Field.Width"));
                double height = ParseNumber(
                    HeightText.Text,
                    UiLocalization.Text("Ui.AddElement.Field.Height"));
                double x = ParseNumber(
                    XText.Text,
                    UiLocalization.Text("Ui.AddElement.Field.XPosition"));
                double y = ParseNumber(
                    YText.Text,
                    UiLocalization.Text("Ui.AddElement.Field.YPosition"));
                if (preset is
                        XuiElementPreset.Image or
                        XuiElementPreset.Text or
                        XuiElementPreset.Rectangle &&
                    !XuiValueParser.TryColor(
                        ColorText.Text,
                        out _))
                {
                    throw new InvalidOperationException(
                        UiLocalization.Text(
                            "Ui.AddElement.Error.ColorArgb"));
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
                    ElementName =
                        preset == XuiElementPreset.CatalogClass &&
                        CatalogClassCombo.SelectedItem is
                            XuiClassDefinition catalogClass
                            ? catalogClass.Name
                            : string.Empty,
                };
                _ = XuiElementFactory.CreateXml(Request);
            }

            DialogResult = true;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            ArgumentException)
        {
            ErrorText.Text = UiLocalization.Format(
                "Ui.Common.ErrorDetails",
                exception.Message);
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
                UiLocalization.Format(
                    "Ui.AddElement.Error.FiniteNumber",
                    label));
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

    private XuiElementPreset SelectedPreset() =>
        PresetCombo.SelectedItem is
            LocalizedEnumOption<XuiElementPreset> option
            ? option.Value
            : XuiElementPreset.Group;

    private static string Number(double value) =>
        value.ToString("0.000000", CultureInfo.InvariantCulture);
}
