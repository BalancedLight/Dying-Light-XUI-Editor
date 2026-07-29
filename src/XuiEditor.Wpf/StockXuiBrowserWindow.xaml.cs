using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using XuiEditor.Core.Assets;
using XuiEditor.Wpf.Services;

namespace XuiEditor.Wpf;

public partial class StockXuiBrowserWindow : Window
{
    private readonly StockXuiRow[] _allFiles;

    public StockXuiBrowserWindow(IDyingLightInstallIndex index)
    {
        UiLocalization.EnsureApplied();
        ArgumentNullException.ThrowIfNull(index);
        _allFiles = index.StockXuiFiles
            .Select(static entry => new StockXuiRow(entry))
            .ToArray();
        Files = [];
        InitializeComponent();
        Language = UiLocalization.XmlLanguage;
        DataContext = this;
        ApplyFilter();
        SearchText.Focus();
    }

    public ObservableCollection<StockXuiRow> Files { get; }

    public XuiAssetEntry? SelectedEntry { get; private set; }

    private void SearchText_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs eventArgs) =>
        ApplyFilter();

    private void FilesGrid_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs eventArgs)
    {
        if (FilesGrid.SelectedItem is StockXuiRow)
        {
            AcceptSelection();
        }
    }

    private void Open_Click(object sender, RoutedEventArgs eventArgs) =>
        AcceptSelection();

    private void AcceptSelection()
    {
        if (FilesGrid.SelectedItem is not StockXuiRow row)
        {
            return;
        }

        SelectedEntry = row.Entry;
        DialogResult = true;
    }

    private void ApplyFilter()
    {
        if (Files is null)
        {
            return;
        }

        string filter = SearchText?.Text.Trim() ?? string.Empty;
        StockXuiRow[] matching = _allFiles
            .Where(row =>
                filter.Length == 0 ||
                row.FileName.Contains(
                    filter,
                    StringComparison.OrdinalIgnoreCase) ||
                row.VirtualPath.Contains(
                    filter,
                    StringComparison.OrdinalIgnoreCase) ||
                row.SourceName.Contains(
                    filter,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Files.Clear();
        foreach (StockXuiRow row in matching)
        {
            Files.Add(row);
        }

        CountText.Text = UiLocalization.Format(
            "Ui.StockBrowser.Count",
            matching.Length,
            _allFiles.Length);
        if (matching.Length > 0)
        {
            FilesGrid.SelectedIndex = 0;
        }
    }
}

public sealed class StockXuiRow
{
    public StockXuiRow(XuiAssetEntry entry)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
    }

    public XuiAssetEntry Entry { get; }

    public string FileName => Entry.FileName;

    public string VirtualPath => Entry.VirtualPath;

    public string SourceName => Entry.Origin.SourceName;

    public string FormattedSize =>
        Entry.Length <= 0
            ? UiLocalization.Text("Ui.StockBrowser.Packed")
            : Entry.Length < 1024
                ? $"{Entry.Length:N0} B"
                : $"{Entry.Length / 1024.0:N1} KiB";
}
