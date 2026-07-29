using System.IO;
using System.Windows;
using System.Windows.Controls;
using XuiEditor.Core.Assets;
using XuiEditor.Wpf.Services;

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
        UiLocalization.EnsureApplied();
        _service = service ??
            throw new ArgumentNullException(nameof(service));
        _visualDefinitionSourcePath = visualDefinitionSourcePath;
        ArgumentException.ThrowIfNullOrWhiteSpace(currentValue);
        InitializeComponent();
        Language = UiLocalization.XmlLanguage;
        CurrentValueText.Text = currentValue;
        if (_visualDefinitionSourcePath is not null)
        {
            Title = UiLocalization.Text("Ui.ReferenceRename.Title");
            InstructionText.Text =
                UiLocalization.Text("Ui.ReferenceRename.Instruction");
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
            UiLocalization.Text("Ui.ReferenceReplace.Changed");
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
                UiLocalization.Text("Ui.ReferenceReplace.InvalidValue");
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
                ? UiLocalization.Text("Ui.ReferenceReplace.NoMatches")
                : UiLocalization.Format(
                    "Ui.ReferenceReplace.MatchSummary",
                    _preflight.Replacements.Count,
                    _preflight.Replacements
                        .Select(static item => item.FilePath)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count());
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidOperationException)
        {
            StatusText.Text = UiLocalization.Format(
                "Ui.Common.ErrorDetails",
                exception.Message);
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
                UiLocalization.Text("Ui.ReferenceReplace.PreviewFirst");
            return;
        }

        if (MessageBox.Show(
                this,
                UiLocalization.Format(
                    "Ui.ReferenceReplace.Confirm",
                    _preflight.Replacements.Count),
                UiLocalization.Text("Ui.ReferenceReplace.ConfirmTitle"),
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
                UiLocalization.Format(
                    "Ui.ReferenceReplace.Failed",
                    exception.Message);
        }
    }
}
