using System.IO;
using System.Windows;
using System.Windows.Controls;
using XuiEditor.Core.Assets;

namespace XuiEditor.Wpf;

public partial class ReferenceReplacementWindow : Window
{
    private readonly XuiWorkspaceResourceService _service;
    private readonly string? _visualDefinitionSourcePath;
    private XuiReferencePreflight? _preflight;

    public ReferenceReplacementWindow(
        XuiWorkspaceResourceService service,
        string currentValue,
        string? visualDefinitionSourcePath = null)
    {
        _service = service ??
            throw new ArgumentNullException(nameof(service));
        _visualDefinitionSourcePath = visualDefinitionSourcePath;
        ArgumentException.ThrowIfNullOrWhiteSpace(currentValue);
        InitializeComponent();
        CurrentValueText.Text = currentValue;
        if (_visualDefinitionSourcePath is not null)
        {
            Title = "Rename Workspace XUI Visual";
            InstructionText.Text =
                "Preview includes the XuiVisual Id and every exact workspace reference. Apply creates backups and rolls back all committed files on failure.";
        }
    }

    public XuiReferenceTransactionResult? Result { get; private set; }

    private void ReplacementValue_TextChanged(
        object sender,
        TextChangedEventArgs eventArgs)
    {
        _preflight = null;
        ApplyButton.IsEnabled = false;
        PreflightGrid.ItemsSource = null;
        StatusText.Text =
            "Replacement changed; preview the diff again before applying.";
    }

    private async void Preview_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        string replacement = ReplacementValueText.Text.Trim();
        if (replacement.Length == 0 ||
            replacement.Equals(
                CurrentValueText.Text,
                StringComparison.Ordinal))
        {
            StatusText.Text =
                "Enter a non-empty replacement different from the current value.";
            return;
        }

        IsEnabled = false;
        try
        {
            _preflight = _visualDefinitionSourcePath is null
                ? await _service.PreflightReplacementAsync(
                    CurrentValueText.Text,
                    replacement).ConfigureAwait(true)
                : await _service.PreflightVisualRenameAsync(
                    _visualDefinitionSourcePath,
                    CurrentValueText.Text,
                    replacement).ConfigureAwait(true);
            PreflightGrid.ItemsSource = _preflight.Replacements;
            ApplyButton.IsEnabled = _preflight.Replacements.Count > 0;
            StatusText.Text = _preflight.Replacements.Count == 0
                ? "No matching workspace references were found."
                : $"{_preflight.Replacements.Count} exact references in " +
                  $"{_preflight.Replacements.Select(static item => item.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).Count()} files. Review every row before applying.";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidOperationException)
        {
            StatusText.Text = exception.Message;
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private async void Apply_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (_preflight is null ||
            !_preflight.ReplacementValue.Equals(
                ReplacementValueText.Text.Trim(),
                StringComparison.Ordinal))
        {
            StatusText.Text =
                "Preview the current replacement before applying.";
            return;
        }

        if (MessageBox.Show(
                this,
                $"Apply {_preflight.Replacements.Count} exact replacements with backups?",
                "Apply reference transaction",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        IsEnabled = false;
        try
        {
            Result = await _service.ApplyReplacementAsync(_preflight)
                .ConfigureAwait(true);
            DialogResult = true;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidOperationException)
        {
            IsEnabled = true;
            StatusText.Text =
                $"Transaction failed and committed files were rolled back: {exception.Message}";
        }
    }
}
