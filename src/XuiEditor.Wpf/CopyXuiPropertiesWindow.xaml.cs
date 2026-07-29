using System.Windows;
using System.Windows.Controls;
using XuiEditor.Core.Schema;
using XuiEditor.Wpf.Models;
using XuiEditor.Wpf.Services;

namespace XuiEditor.Wpf;

public partial class CopyXuiPropertiesWindow : Window
{
    private readonly XuiCopyPropertyOption[] _options;

    public CopyXuiPropertiesWindow(
        string sourceDisplayName,
        string sourceClassName,
        IReadOnlyList<XuiCatalogPropertySelection> properties)
    {
        UiLocalization.EnsureApplied();
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceClassName);
        ArgumentNullException.ThrowIfNull(properties);
        InitializeComponent();
        Language = UiLocalization.XmlLanguage;
        _options = properties
            .Select(property => new XuiCopyPropertyOption(
                property.Definition,
                property.EffectiveValue,
                property.IsAuthored))
            .OrderBy(static property =>
                property.Category,
                StringComparer.Ordinal)
            .ThenBy(static property =>
                property.Name,
                StringComparer.Ordinal)
            .ToArray();
        SourceText.Text = UiLocalization.Format(
            "Ui.CopyProperties.Source",
            sourceDisplayName,
            sourceClassName);
        ApplyFilter();
        UpdateSummary();
        SearchTextBox.Focus();
    }

    public IReadOnlyList<XuiCopiedInspectorProperty> SelectedProperties
    {
        get;
        private set;
    } = [];

    internal IReadOnlyList<XuiCopyPropertyOption> VisibleOptionsForTesting =>
        PropertyList.Items.Cast<XuiCopyPropertyOption>().ToArray();

    internal void SelectPropertiesForTesting(
        IEnumerable<string> propertyNames)
    {
        HashSet<string> names = propertyNames.ToHashSet(
            StringComparer.Ordinal);
        foreach (XuiCopyPropertyOption option in _options)
        {
            option.IsSelected = names.Contains(option.Name);
        }

        UpdateSummary();
    }

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
        PropertyList.ItemsSource = _options
            .Where(option =>
                query.Length == 0 ||
                option.Name.Contains(
                    query,
                    StringComparison.OrdinalIgnoreCase) ||
                option.Category.Contains(
                    query,
                    StringComparison.OrdinalIgnoreCase) ||
                option.Value.Contains(
                    query,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private void SelectAuthored_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        foreach (XuiCopyPropertyOption option in _options)
        {
            option.IsSelected = option.IsAuthored;
        }

        UpdateSummary();
    }

    private void SelectAll_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        foreach (XuiCopyPropertyOption option in _options)
        {
            option.IsSelected = true;
        }

        UpdateSummary();
    }

    private void Clear_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        foreach (XuiCopyPropertyOption option in _options)
        {
            option.IsSelected = false;
        }

        UpdateSummary();
    }

    private void OptionCheckBox_Click(
        object sender,
        RoutedEventArgs eventArgs) =>
        UpdateSummary();

    private void UpdateSummary()
    {
        int selected = _options.Count(static option =>
            option.IsSelected);
        int authored = _options.Count(static option =>
            option.IsSelected && option.IsAuthored);
        SelectionSummaryText.Text =
            UiLocalization.Format(
                "Ui.CopyProperties.Summary",
                selected,
                authored,
                selected - authored);
        ErrorText.Text = string.Empty;
    }

    private void Copy_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        SelectedProperties = _options
            .Where(static option => option.IsSelected)
            .Select(static option => option.ToCopiedProperty())
            .ToArray();
        if (SelectedProperties.Count == 0)
        {
            ErrorText.Text =
                UiLocalization.Text("Ui.CopyProperties.SelectOne");
            return;
        }

        DialogResult = true;
    }
}
