using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using XuiEditor.Core.Assets;
using XuiEditor.Wpf.Services;

namespace XuiEditor.Wpf;

public partial class AssetRootsWindow : Window
{
    private readonly EditorSettings _settings;

    public AssetRootsWindow(EditorSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Roots = new ObservableCollection<AssetRootSetting>(
            settings.AssetRoots.Select(static root => new AssetRootSetting
            {
                Path = root.Path,
                Kind = root.Kind,
                IsReadOnly = root.EffectiveIsReadOnly,
            }));
        FontMappings = new ObservableCollection<FontMappingRow>(
            settings.FontMappings.Select(static mapping =>
                new FontMappingRow
                {
                    EngineId = mapping.Key,
                    Mapping = mapping.Value,
                }));
        InitializeComponent();
        DataContext = this;
        InstallText.Text = settings.DyingLightInstallPath ?? string.Empty;
        WorkspaceText.Text = settings.WorkspaceRoot ?? string.Empty;
        InputGlyphCombo.ItemsSource = Enum.GetValues<XuiInputGlyphScheme>();
        InputGlyphCombo.SelectedItem = settings.InputGlyphScheme;
        ConfigureKindColumn();
        RefreshInstallState();
    }

    public ObservableCollection<AssetRootSetting> Roots { get; }

    public ObservableCollection<FontMappingRow> FontMappings { get; }

    private void BrowseInstall_Click(object sender, RoutedEventArgs eventArgs)
    {
        OpenFolderDialog dialog = new()
        {
            Title = "Choose the Dying Light installation folder",
            InitialDirectory = Directory.Exists(InstallText.Text)
                ? InstallText.Text
                : Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFilesX86),
        };
        if (dialog.ShowDialog(this) == true)
        {
            InstallText.Text = dialog.FolderName;
        }
    }

    private void InstallText_TextChanged(
        object sender,
        TextChangedEventArgs eventArgs) =>
        RefreshInstallState();

    private void BrowseWorkspace_Click(object sender, RoutedEventArgs eventArgs)
    {
        OpenFolderDialog dialog = new()
        {
            Title = "Choose writable XUI workspace",
            InitialDirectory = Directory.Exists(WorkspaceText.Text)
                ? WorkspaceText.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) == true)
        {
            WorkspaceText.Text = dialog.FolderName;
        }
    }

    private void AddLoose_Click(object sender, RoutedEventArgs eventArgs) =>
        AddRoot(XuiAssetRootKind.LooseMod, isReadOnly: false);

    private void AddExtracted_Click(object sender, RoutedEventArgs eventArgs) =>
        AddRoot(XuiAssetRootKind.ExtractedDyingLight, isReadOnly: true);

    private void Remove_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (RootsGrid.SelectedItem is AssetRootSetting root)
        {
            Roots.Remove(root);
        }
    }

    private void MoveUp_Click(object sender, RoutedEventArgs eventArgs) => Move(-1);

    private void MoveDown_Click(object sender, RoutedEventArgs eventArgs) => Move(1);

    private void Ok_Click(object sender, RoutedEventArgs eventArgs)
    {
        string install = InstallText.Text.Trim();
        if (install.Length > 0)
        {
            try
            {
                install = Path.TrimEndingDirectorySeparator(
                    Path.GetFullPath(install));
            }
            catch (Exception exception) when (
                exception is ArgumentException or
                NotSupportedException or
                PathTooLongException)
            {
                ShowInvalidPath(exception.Message);
                return;
            }

            if (!DyingLightInstallIndex.LooksLikeInstall(install))
            {
                ShowInvalidPath(
                    "Choose the folder containing DyingLightGame.exe and DW\\Data0.pak.");
                return;
            }
        }

        string workspace = WorkspaceText.Text.Trim();
        if (workspace.Length > 0)
        {
            try
            {
                workspace = Path.GetFullPath(workspace);
                Directory.CreateDirectory(workspace);
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                ArgumentException)
            {
                MessageBox.Show(
                    this,
                    exception.Message,
                    "Invalid workspace",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }
        }

        _settings.DyingLightInstallPath =
            install.Length == 0 ? null : install;
        _settings.Locale = DyingLightInstallProfile.NormalizeLocale(
            LocaleCombo.Text);
        _settings.InputGlyphScheme =
            InputGlyphCombo.SelectedItem is XuiInputGlyphScheme scheme
                ? scheme
                : XuiInputGlyphScheme.KeyboardAndMouse;
        _settings.WorkspaceRoot = workspace.Length == 0 ? null : workspace;
        _settings.AssetRoots = Roots
            .Where(static root => !string.IsNullOrWhiteSpace(root.Path))
            .Select(static root => new AssetRootSetting
            {
                Path = Path.GetFullPath(root.Path),
                Kind = root.Kind,
                IsReadOnly = root.EffectiveIsReadOnly,
            })
            .ToList();
        _settings.FontMappings = FontMappings
            .Where(static mapping =>
                !string.IsNullOrWhiteSpace(mapping.EngineId) &&
                !string.IsNullOrWhiteSpace(mapping.Mapping))
            .GroupBy(
                static mapping => mapping.EngineId.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.Last().Mapping.Trim(),
                StringComparer.OrdinalIgnoreCase);
        DialogResult = true;
    }

    private void AddRoot(XuiAssetRootKind kind, bool isReadOnly)
    {
        OpenFolderDialog dialog = new()
        {
            Title = kind == XuiAssetRootKind.LooseMod
                ? "Choose a loose Dying Light mod root"
                : "Choose an extracted Dying Light asset root",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        AssetRootSetting root = new()
        {
            Path = dialog.FolderName,
            Kind = kind,
            IsReadOnly = isReadOnly,
        };
        Roots.Add(root);
        RootsGrid.SelectedItem = root;
        RootsGrid.ScrollIntoView(root);
    }

    private void Move(int direction)
    {
        if (RootsGrid.SelectedItem is not AssetRootSetting root)
        {
            return;
        }

        int index = Roots.IndexOf(root);
        int destination = index + direction;
        if (destination < 0 || destination >= Roots.Count)
        {
            return;
        }

        Roots.Move(index, destination);
        RootsGrid.SelectedItem = root;
    }

    private void ConfigureKindColumn()
    {
        DataGridComboBoxColumn? kindColumn =
            RootsGrid.Columns.OfType<DataGridComboBoxColumn>().FirstOrDefault();
        if (kindColumn is not null)
        {
            kindColumn.ItemsSource = new[]
            {
                XuiAssetRootKind.Workspace,
                XuiAssetRootKind.LooseMod,
                XuiAssetRootKind.ExtractedDyingLight,
            };
        }
    }

    private void RefreshInstallState()
    {
        if (InstallValidationText is null ||
            LocaleCombo is null)
        {
            return;
        }

        string install = InstallText.Text.Trim();
        bool valid = DyingLightInstallIndex.LooksLikeInstall(install);
        InstallValidationText.Text = install.Length == 0
            ? "Not configured — stock browser disabled"
            : valid
                ? "Valid Dying Light install · read-only"
                : "Not a Dying Light install";
        InstallValidationText.Foreground = valid || install.Length == 0
            ? (Brush)FindResource("MutedTextBrush")
            : (Brush)FindResource("DangerBrush");

        string selected = LocaleCombo.Text.Length > 0
            ? LocaleCombo.Text
            : _settings.Locale;
        string[] locales = valid
            ? DiscoverLocales(install)
            : ["En"];
        LocaleCombo.ItemsSource = locales;
        string normalized = DyingLightInstallProfile.NormalizeLocale(selected);
        LocaleCombo.SelectedItem =
            locales.FirstOrDefault(locale =>
                locale.Equals(
                    normalized,
                    StringComparison.OrdinalIgnoreCase)) ??
            "En";
    }

    private static string[] DiscoverLocales(string install)
    {
        string dw = Path.Combine(install, "DW");
        if (!Directory.Exists(dw))
        {
            return ["En"];
        }

        return Directory.EnumerateFiles(dw, "Data??.pak")
            .Select(static path =>
                Path.GetFileNameWithoutExtension(path)["Data".Length..])
            .Where(static locale =>
                locale.Length == 2 &&
                locale.All(char.IsLetter))
            .Select(DyingLightInstallProfile.NormalizeLocale)
            .Append("En")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static locale => locale, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void ShowInvalidPath(string message)
    {
        MessageBox.Show(
            this,
            message,
            "Invalid Dying Light installation",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}

public sealed class FontMappingRow
{
    public string EngineId { get; set; } = string.Empty;

    public string Mapping { get; set; } = string.Empty;
}
