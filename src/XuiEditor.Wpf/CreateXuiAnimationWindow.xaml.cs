using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using XuiEditor.Core.Animation;
using XuiEditor.Wpf.Services;

namespace XuiEditor.Wpf;

public sealed record XuiAnimationScopeOption(
    string OwnerKey,
    string DisplayName,
    int StartTick,
    bool IsLocal)
{
    public override string ToString() => DisplayName;
}

public sealed record XuiAnimationPresetOption(
    XuiAnimationPreset Preset,
    string Name,
    string Description)
{
    public override string ToString() => Name;
}

public sealed record XuiAnimationDialogSelection(
    XuiAnimationPreset Preset,
    XuiAnimationScopeOption Scope,
    int StartTick,
    string Prefix,
    bool MarkersOnly,
    string? PropertyName,
    string? StartValue,
    string? EndValue,
    int Duration);

public partial class CreateXuiAnimationWindow : Window
{
    private readonly Func<XuiAnimationDialogSelection, XuiAnimationConflictReport>
        _previewConflicts;
    private bool _ready;

    public CreateXuiAnimationWindow(
        IReadOnlyList<XuiAnimationScopeOption> scopes,
        IReadOnlyList<string> properties,
        Func<XuiAnimationDialogSelection, XuiAnimationConflictReport> previewConflicts,
        string? initialPresetId = null)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(previewConflicts);
        _previewConflicts = previewConflicts;
        InitializeComponent();
        Language = UiLocalization.XmlLanguage;
        XuiAnimationPresetOption[] presetOptions = XuiAnimationPresets.BuiltIn
            .Select(preset => new XuiAnimationPresetOption(
                preset,
                UiLocalization.Text(PresetResourceKey(preset.Id, "Name")),
                UiLocalization.Text(PresetResourceKey(preset.Id, "Description"))))
            .ToArray();
        PresetComboBox.ItemsSource = presetOptions;
        ScopeComboBox.ItemsSource = scopes;
        PropertyComboBox.ItemsSource = properties;
        PresetComboBox.SelectedItem = presetOptions.FirstOrDefault(
            option => option.Preset.Id.Equals(initialPresetId, StringComparison.Ordinal)) ??
            presetOptions[0];
        ScopeComboBox.SelectedIndex = scopes.Count > 0 ? 0 : -1;
        PropertyComboBox.SelectedIndex = properties.Count > 0 ? 0 : -1;
        if (ScopeComboBox.SelectedItem is XuiAnimationScopeOption scope)
        {
            StartTickTextBox.Text = scope.StartTick.ToString(CultureInfo.InvariantCulture);
        }
        _ready = true;
        RefreshPreview();
    }

    public XuiAnimationDialogSelection? Selection { get; private set; }

    private void DialogValue_Changed(object sender, RoutedEventArgs eventArgs)
    {
        if (_ready)
        {
            if (ReferenceEquals(sender, ScopeComboBox) &&
                ScopeComboBox.SelectedItem is XuiAnimationScopeOption scope)
            {
                StartTickTextBox.Text = scope.StartTick.ToString(
                    CultureInfo.InvariantCulture);
            }
            RefreshPreview();
        }
    }

    private void DialogValue_Changed(
        object sender,
        KeyboardFocusChangedEventArgs eventArgs) => RefreshPreview();

    private void RefreshPreview()
    {
        if (!_ready ||
            PresetComboBox.SelectedItem is not XuiAnimationPresetOption option)
        {
            return;
        }

        XuiAnimationPreset preset = option.Preset;
        CustomPropertyPanel.Visibility = preset.Id.Equals(
            "custom-property",
            StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;
        MarkersOnlyCheckBox.IsEnabled = preset.NamedFrames.Count > 0;
        if (!MarkersOnlyCheckBox.IsEnabled &&
            MarkersOnlyCheckBox.IsChecked == true)
        {
            MarkersOnlyCheckBox.IsChecked = false;
        }
        bool markersOnly = MarkersOnlyCheckBox.IsChecked == true;
        IEnumerable<string> generatedTracks = markersOnly
            ? []
            : preset.Id.Equals("custom-property", StringComparison.Ordinal)
                ? [UiLocalization.Format(
                    "Ui.Animation.Dialog.TrackSummary",
                    PropertyComboBox.Text,
                    2,
                    EvidenceLabel(XuiAnimationEvidence.EditorConvenience))]
                : preset.Tracks.Select(track => UiLocalization.Format(
                    "Ui.Animation.Dialog.TrackSummary",
                    track.PropertyName,
                    track.Keys.Count,
                    EvidenceLabel(track.Evidence)));
        IEnumerable<string> markers = preset.NamedFrames.Select(frame =>
            UiLocalization.Format(
                "Ui.Animation.Dialog.MarkerSummary",
                PrefixTextBox.Text + frame.Name,
                frame.Tick,
                EvidenceLabel(frame.Evidence)));
        GeneratedText.Text = string.Join(
            Environment.NewLine,
            new[] { option.Description, string.Empty }
                .Concat(generatedTracks)
                .Concat(markers));

        if (!TryBuildSelection(out XuiAnimationDialogSelection? selection))
        {
            ConflictText.Text = UiLocalization.Text("Ui.Animation.Dialog.InvalidFields");
            CreateButton.IsEnabled = false;
            return;
        }

        XuiAnimationConflictReport report = _previewConflicts(selection!);
        ConflictText.Text = report.Conflicts.Count == 0
            ? UiLocalization.Text("Ui.Animation.Dialog.NoConflicts")
            : string.Join(Environment.NewLine, report.Conflicts.Select(conflict =>
                $"{ConflictGlyph(conflict.Severity)} {LocalizedConflict(conflict)}"));
        CreateButton.IsEnabled = !report.HasErrors;
    }

    private bool TryBuildSelection(
        out XuiAnimationDialogSelection? selection)
    {
        selection = null;
        if (PresetComboBox.SelectedItem is not XuiAnimationPresetOption option ||
            ScopeComboBox.SelectedItem is not XuiAnimationScopeOption scope ||
            !int.TryParse(StartTickTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int startTick) ||
            startTick < 0 ||
            !int.TryParse(DurationTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int duration) ||
            duration < 1)
        {
            return false;
        }

        selection = new XuiAnimationDialogSelection(
            option.Preset,
            scope,
            startTick,
            PrefixTextBox.Text.Trim(),
            MarkersOnlyCheckBox.IsChecked == true,
            PropertyComboBox.Text.Trim(),
            StartValueTextBox.Text,
            EndValueTextBox.Text,
            duration);
        return true;
    }

    private void Create_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (!TryBuildSelection(out XuiAnimationDialogSelection? selection) ||
            selection is null ||
            _previewConflicts(selection).HasErrors)
        {
            return;
        }

        Selection = selection;
        DialogResult = true;
    }

    private static string EvidenceLabel(XuiAnimationEvidence evidence) =>
        evidence == XuiAnimationEvidence.StockExact
            ? UiLocalization.Text("Ui.Animation.Evidence.Stock")
            : UiLocalization.Text("Ui.Animation.Evidence.Convenience");

    private static string ConflictGlyph(XuiAnimationConflictSeverity severity) =>
        severity switch
        {
            XuiAnimationConflictSeverity.Error => "×",
            XuiAnimationConflictSeverity.Warning => "!",
            _ => "·",
        };

    private static string LocalizedConflict(XuiAnimationConflict conflict) =>
        conflict.ResourceKey is null
            ? conflict.Message
            : UiLocalization.Format(
                conflict.ResourceKey,
                conflict.Arguments?.ToArray() ?? []);

    private static string PresetResourceKey(string id, string suffix)
    {
        string name = id switch
        {
            "quick-show-hide" => "QuickShowHide",
            "menu-transition" => "MenuTransition",
            "hud-pop" => "HudPop",
            "button-states" => "ButtonStates",
            _ => "CustomProperty",
        };
        return $"Ui.Animation.Preset.{name}.{suffix}";
    }
}
