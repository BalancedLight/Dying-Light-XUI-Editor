using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using XuiEditor.Wpf.Services;

namespace XuiEditor.Wpf;

public partial class AddXuiTimelineTrackWindow : Window
{
    private readonly Func<string, string> _effectiveValue;
    private bool _ready;

    public AddXuiTimelineTrackWindow(
        IReadOnlyList<string> properties,
        Func<string, string> effectiveValue,
        string? initialProperty = null)
    {
        _effectiveValue = effectiveValue;
        InitializeComponent();
        Language = UiLocalization.XmlLanguage;
        PropertyComboBox.ItemsSource = properties;
        PropertyComboBox.SelectedItem = initialProperty is not null &&
                                                properties.Contains(initialProperty, StringComparer.Ordinal)
            ? initialProperty
            : properties.Count > 0 ? properties[0] : null;
        _ready = true;
        RefreshValue();
    }

    public string PropertyName { get; private set; } = string.Empty;

    public string PropertyValue { get; private set; } = string.Empty;

    private void Property_Changed(object sender, RoutedEventArgs eventArgs)
    {
        if (_ready)
        {
            RefreshValue();
        }
    }

    private void Property_Changed(
        object sender,
        KeyboardFocusChangedEventArgs eventArgs) => RefreshValue();

    private void RefreshValue()
    {
        if (!_ready || string.IsNullOrWhiteSpace(PropertyComboBox.Text))
        {
            return;
        }

        ValueTextBox.Text = _effectiveValue(PropertyComboBox.Text.Trim());
    }

    private void Create_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (string.IsNullOrWhiteSpace(PropertyComboBox.Text))
        {
            return;
        }

        PropertyName = PropertyComboBox.Text.Trim();
        PropertyValue = ValueTextBox.Text;
        DialogResult = true;
    }
}
