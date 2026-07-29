using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using XuiEditor.Core.Schema;
using XuiEditor.Core.Values;

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
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerDisplayName);
        InitializeComponent();
        HashSet<string> authored = new(
            authoredNames ?? [],
            StringComparer.Ordinal);
        _definitions = (definitions ?? XuiClassCatalog.Default.Properties)
            .Where(definition => !authored.Contains(definition.Name))
            .OrderBy(static definition => definition.Category, StringComparer.Ordinal)
            .ThenBy(static definition => definition.Name, StringComparer.Ordinal)
            .ToArray();
        OwnerText.Text =
            $"Add one authored property to {ownerDisplayName}. Catalog defaults stay ghosted until you edit them.";
        ApplyFilter();
        SearchTextBox.Focus();
    }

    public string PropertyName { get; private set; } = string.Empty;

    public string PropertyValue { get; private set; } = string.Empty;

    internal IReadOnlyList<XuiPropertyDefinition> VisibleDefinitionsForTesting =>
        PropertyList.Items.Cast<XuiPropertyDefinition>().ToArray();

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
                definition.Description.Contains(
                    query,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private void PropertyList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs eventArgs)
    {
        if (PropertyList.SelectedItem is not XuiPropertyDefinition definition)
        {
            return;
        }

        SelectDefinition(definition);
    }

    private void PropertyList_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs eventArgs)
    {
        if (PropertyList.SelectedItem is XuiPropertyDefinition definition)
        {
            SelectDefinition(definition);
            ValueTextBox.Focus();
        }
    }

    private void SelectDefinition(XuiPropertyDefinition definition)
    {
        _selectedDefinition = definition;
        NameTextBox.Text = definition.Name;
        TypeText.Text =
            $"{definition.Type} • {definition.EvidenceLabel} • " +
            $"{(definition.IsAnimatable ? "animatable" : "noanim")} • " +
            $"{definition.PreviewSupport} preview";
        DescriptionText.Text =
            $"{definition.Description} Default: " +
            $"{(definition.DefaultValue.Length == 0 ? "(empty)" : definition.DefaultValue)}";
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
        PropertyList.IsEnabled = !raw;
        SearchTextBox.IsEnabled = !raw;
        NameTextBox.IsEnabled = raw;
        ChoiceValueCombo.Visibility = Visibility.Collapsed;
        ValueTextBox.Visibility = Visibility.Visible;
        _selectedDefinition = raw ? null : _selectedDefinition;
        TypeText.Text = raw
            ? "Raw custom property • preserved losslessly • preview support unknown"
            : string.Empty;
        DescriptionText.Text = raw
            ? "Use this explicit route only for mod-authored or otherwise unclassified engine properties."
            : string.Empty;
        ErrorText.Text = string.Empty;
        if (raw)
        {
            NameTextBox.Text = string.Empty;
            ValueTextBox.Text = string.Empty;
            NameTextBox.Focus();
        }
        else if (PropertyList.SelectedItem is XuiPropertyDefinition definition)
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
                "Choose a catalog property or enter a valid raw XML name such as Opacity.";
            return;
        }

        if (RawModeCheckBox.IsChecked != true &&
            _selectedDefinition is null)
        {
            ErrorText.Text = "Choose an applicable catalog property.";
            return;
        }

        string value = ChoiceValueCombo.Visibility == Visibility.Visible
            ? ChoiceValueCombo.SelectedItem as string ?? string.Empty
            : ValueTextBox.Text;
        if (_selectedDefinition is XuiPropertyDefinition definition &&
            !IsValidTypedValue(definition.Type, value))
        {
            ErrorText.Text =
                $"'{value}' is not a valid {definition.Type} value for {name}.";
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
