using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using XuiEditor.Core.Schema;
using XuiEditor.Core.Values;
using XuiEditor.Wpf.Services;

namespace XuiEditor.Wpf;

public partial class AddXuiPropertyWindow : Window
{
    private readonly IReadOnlyList<XuiPropertyDefinition> _definitions;
    private XuiPropertyDefinition? _selectedDefinition;

    public AddXuiPropertyWindow(
        string ownerDisplayName,
        IReadOnlyList<XuiPropertyDefinition>? definitions = null,
        IReadOnlyCollection<string>? authoredNames = null)
    {
        UiLocalization.EnsureApplied();
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerDisplayName);
        InitializeComponent();
        Language = UiLocalization.XmlLanguage;
        HashSet<string> authored = new(
            authoredNames ?? [],
            StringComparer.Ordinal);
        _definitions = (definitions ?? XuiClassCatalog.Default.Properties)
            .Where(definition => !authored.Contains(definition.Name))
            .OrderBy(static definition => definition.Category, StringComparer.Ordinal)
            .ThenBy(static definition => definition.Name, StringComparer.Ordinal)
            .ToArray();
        OwnerText.Text = UiLocalization.Format(
            "Ui.AddProperty.Owner",
            ownerDisplayName);
        ApplyFilter();
        SearchTextBox.Focus();
    }

    public string PropertyName { get; private set; } = string.Empty;

    public string PropertyValue { get; private set; } = string.Empty;

    internal IReadOnlyList<XuiPropertyDefinition> VisibleDefinitionsForTesting =>
        PropertyList.Items
            .Cast<AddXuiPropertyOption>()
            .Select(static option => option.Definition)
            .ToArray();

    internal bool RawEditorVisibleForTesting =>
        RawEditorPanel.Visibility == Visibility.Visible;

    internal bool CatalogVisibleForTesting =>
        PropertyList.Visibility == Visibility.Visible &&
        SearchTextBox.Visibility == Visibility.Visible;

    internal void SetRawModeForTesting(bool enabled) =>
        RawModeCheckBox.IsChecked = enabled;

    private void SearchTextBox_TextChanged(
        object sender,
        TextChangedEventArgs eventArgs) =>
        ApplyFilter();

    private void ApplyFilter()
    {
        if (PropertyList is null)
        {
            return;
        }

        string query = SearchTextBox.Text.Trim();
        PropertyList.ItemsSource = _definitions
            .Where(definition =>
                query.Length == 0 ||
                definition.Name.Contains(
                    query,
                    StringComparison.OrdinalIgnoreCase) ||
                definition.Category.Contains(
                    query,
                    StringComparison.OrdinalIgnoreCase) ||
                UiLocalization.Category(definition.Category).Contains(
                    query,
                    StringComparison.OrdinalIgnoreCase) ||
                InspectorHelpText.Description(definition).Contains(
                    query,
                    StringComparison.OrdinalIgnoreCase))
            .Select(static definition =>
                new AddXuiPropertyOption(definition))
            .ToArray();
    }

    private void PropertyList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs eventArgs)
    {
        if (PropertyList.SelectedItem is not
            AddXuiPropertyOption { Definition: var definition })
        {
            return;
        }

        SelectDefinition(definition);
    }

    private void PropertyList_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs eventArgs)
    {
        if (PropertyList.SelectedItem is
            AddXuiPropertyOption { Definition: var definition })
        {
            SelectDefinition(definition);
            ValueTextBox.Focus();
        }
    }

    private void SelectDefinition(XuiPropertyDefinition definition)
    {
        _selectedDefinition = definition;
        NameTextBox.Text = definition.Name;
        TypeText.Text = UiLocalization.Format(
            "Ui.AddProperty.TypeSummary",
            UiLocalization.PropertyType(definition.Type),
            UiLocalization.Evidence(definition.Evidence),
            UiLocalization.Text(
                definition.IsAnimatable
                    ? "Ui.AddProperty.Animatable"
                    : "Ui.AddProperty.NotAnimatable"),
            UiLocalization.PreviewSupport(definition.PreviewSupport));
        DescriptionText.Text = UiLocalization.Format(
            "Ui.AddProperty.DescriptionDefault",
            InspectorHelpText.Description(definition),
            definition.DefaultValue.Length == 0
                ? UiLocalization.Text("Ui.Common.Empty")
                : definition.DefaultValue);
        ErrorText.Text = string.Empty;

        IReadOnlyList<string> choices = definition.Choices;
        if (definition.IsBoolean && choices.Count == 0)
        {
            choices = ["false", "true"];
        }

        bool useChoices = choices.Count > 0;
        ChoiceValueCombo.Visibility =
            useChoices ? Visibility.Visible : Visibility.Collapsed;
        ValueTextBox.Visibility =
            useChoices ? Visibility.Collapsed : Visibility.Visible;
        if (useChoices)
        {
            ChoiceValueCombo.ItemsSource = choices;
            ChoiceValueCombo.SelectedItem =
                choices.Contains(definition.DefaultValue, StringComparer.Ordinal)
                    ? definition.DefaultValue
                    : choices[0];
        }
        else
        {
            ValueTextBox.Text = definition.DefaultValue;
        }
    }

    private void RawModeCheckBox_Changed(
        object sender,
        RoutedEventArgs eventArgs)
    {
        bool raw = RawModeCheckBox.IsChecked == true;
        CatalogSearchPanel.Visibility = Visibility.Visible;
        SearchLabel.Visibility = raw
            ? Visibility.Collapsed
            : Visibility.Visible;
        SearchTextBox.Visibility = raw
            ? Visibility.Collapsed
            : Visibility.Visible;
        PropertyList.Visibility = raw
            ? Visibility.Collapsed
            : Visibility.Visible;
        RawEditorPanel.Visibility = raw
            ? Visibility.Visible
            : Visibility.Collapsed;
        NameTextBox.IsEnabled = raw;
        ChoiceValueCombo.Visibility = Visibility.Collapsed;
        ValueTextBox.Visibility = Visibility.Visible;
        _selectedDefinition = raw ? null : _selectedDefinition;
        TypeText.Text = raw
            ? UiLocalization.Text("Ui.AddProperty.RawType")
            : string.Empty;
        DescriptionText.Text = raw
            ? UiLocalization.Text("Ui.AddProperty.RawDescription")
            : string.Empty;
        ErrorText.Text = string.Empty;
        if (raw)
        {
            NameTextBox.Text = string.Empty;
            ValueTextBox.Text = string.Empty;
            NameTextBox.Focus();
        }
        else if (PropertyList.SelectedItem is
                 AddXuiPropertyOption { Definition: var definition })
        {
            SelectDefinition(definition);
        }
    }

    private void Add_Click(object sender, RoutedEventArgs eventArgs)
    {
        string name = NameTextBox.Text.Trim();
        if (!IsValidXmlName(name))
        {
            ErrorText.Text =
                UiLocalization.Text("Ui.AddProperty.InvalidName");
            return;
        }

        if (RawModeCheckBox.IsChecked != true &&
            _selectedDefinition is null)
        {
            ErrorText.Text =
                UiLocalization.Text("Ui.AddProperty.ChooseApplicable");
            return;
        }

        string value = ChoiceValueCombo.Visibility == Visibility.Visible
            ? ChoiceValueCombo.SelectedItem as string ?? string.Empty
            : ValueTextBox.Text;
        if (_selectedDefinition is XuiPropertyDefinition definition &&
            !IsValidTypedValue(definition.Type, value))
        {
            ErrorText.Text = UiLocalization.Format(
                "Ui.AddProperty.InvalidValue",
                value,
                UiLocalization.PropertyType(definition.Type),
                name);
            return;
        }

        PropertyName = name;
        PropertyValue = value;
        DialogResult = true;
    }

    private static bool IsValidTypedValue(
        XuiPropertyType type,
        string value) =>
        type switch
        {
            XuiPropertyType.Boolean =>
                XuiValueParser.TryBoolean(value, out _),
            XuiPropertyType.WholeNumber =>
                XuiValueParser.TryInteger(value, out _),
            XuiPropertyType.Number =>
                double.TryParse(
                    value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double number) &&
                double.IsFinite(number),
            XuiPropertyType.Vector2 =>
                XuiValueParser.TryVector2(value, out _),
            XuiPropertyType.Vector3 =>
                XuiValueParser.TryVector3(value, out _),
            XuiPropertyType.Vector4 =>
                XuiValueParser.TryVector4(value, out _),
            XuiPropertyType.Quaternion =>
                XuiValueParser.TryQuaternion(value, out _),
            XuiPropertyType.Color =>
                XuiValueParser.TryColor(value, out _),
            _ => true,
        };

    private static bool IsValidXmlName(string name) =>
        name.Length > 0 &&
        (char.IsLetter(name[0]) || name[0] is '_' or ':') &&
        name.All(character =>
            char.IsLetterOrDigit(character) ||
            character is '_' or '-' or ':' or '.');
}

public sealed class AddXuiPropertyOption
{
    public AddXuiPropertyOption(XuiPropertyDefinition definition)
    {
        Definition = definition ??
                     throw new ArgumentNullException(nameof(definition));
    }

    public XuiPropertyDefinition Definition { get; }

    public string Name => Definition.Name;

    public string Category =>
        UiLocalization.Category(Definition.Category);

    public string EvidenceLabel =>
        UiLocalization.Evidence(Definition.Evidence);

    public string ToolTip =>
        InspectorHelpText.BuildToolTip(
            Definition.Name,
            Definition,
            isAuthored: false);
}
