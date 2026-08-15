using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using XuiEditor.Core.Schema;
using XuiEditor.Core.Values;
using XuiEditor.Wpf.Services;

namespace XuiEditor.Wpf.Controls;

public partial class PressKeyPicker : UserControl
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(
            nameof(Value),
            typeof(string),
            typeof(PressKeyPicker),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                PresentationPropertyChanged));

    public static readonly DependencyProperty IsMixedProperty =
        DependencyProperty.Register(
            nameof(IsMixed),
            typeof(bool),
            typeof(PressKeyPicker),
            new PropertyMetadata(false, PresentationPropertyChanged));

    private PressKeyPickerGroup[] _visibleGroups = [];

    public PressKeyPicker()
    {
        InitializeComponent();
        RefreshLocalizedContent();
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public bool IsMixed
    {
        get => (bool)GetValue(IsMixedProperty);
        set => SetValue(IsMixedProperty, value);
    }

    public event EventHandler<PressKeyValueCommittedEventArgs>? ValueCommitted;

    internal IReadOnlyList<PressKeyPickerGroup> VisibleGroupsForTesting =>
        _visibleGroups;

    internal string SummaryForTesting => SummaryNameText.Text;

    internal bool IsPopupOpenForTesting => PickerPopup.IsOpen;

    internal FrameworkElement PopupContentForTesting =>
        (FrameworkElement)PickerPopup.Child;

    internal string CustomErrorForTesting => CustomErrorText.Text;

    internal void OpenForTesting()
    {
        PickerPopup.StaysOpen = true;
        OpenButton.IsChecked = true;
    }

    internal void SearchForTesting(string query) => SearchTextBox.Text = query;

    internal void CloseForTesting() => ClosePicker();

    internal bool SelectForTesting(string id)
    {
        XuiPressKeyOption? option = XuiPressKeyCatalog.Options.FirstOrDefault(
            candidate => candidate.Id.Equals(id, StringComparison.Ordinal));
        if (option is null)
        {
            return false;
        }

        CommitValue(option.CanonicalText);
        return true;
    }

    internal void ApplyCustomForTesting(string value)
    {
        CustomValueTextBox.Text = value;
        ApplyCustomValue();
    }

    private static void PresentationPropertyChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        _ = eventArgs;
        ((PressKeyPicker)dependencyObject).UpdateSummary();
    }

    private void RefreshLocalizedContent()
    {
        BuildGroups(SearchTextBox?.Text ?? string.Empty);
        UpdateSummary();
    }

    private void BuildGroups(string query)
    {
        string trimmedQuery = query.Trim();
        _visibleGroups = XuiPressKeyCatalog.Options
            .Select(CreateItem)
            .Where(item =>
                trimmedQuery.Length == 0 ||
                item.SearchText.Contains(
                    trimmedQuery,
                    StringComparison.OrdinalIgnoreCase))
            .GroupBy(static item => item.Option.Group)
            .OrderBy(static group => group.Key)
            .Select(group => new PressKeyPickerGroup(
                GroupHeader(group.Key),
                group.ToArray()))
            .ToArray();
        GroupItems.ItemsSource = _visibleGroups;
        NoResultsText.Visibility = _visibleGroups.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static PressKeyPickerItem CreateItem(XuiPressKeyOption option)
    {
        string displayName = UiLocalization.Text(option.DisplayNameKey);
        string canonical = option.CanonicalText;
        string runtimeAliases = string.Join(
            " ",
            option.RuntimeAliases.Select(alias =>
                alias.ToString(CultureInfo.InvariantCulture)));
        string searchText = string.Join(
            " ",
            new[]
            {
                displayName,
                option.Id,
                option.EngineName,
                canonical,
                runtimeAliases,
                option.KeyboardLabel,
                option.GamepadLabel,
            }.Concat(option.SearchAliases));
        string tooltip = UiLocalization.Format(
            "Ui.PressKey.Option.ToolTip",
            displayName,
            option.KeyboardLabel.Length == 0
                ? UiLocalization.Text("Ui.Common.Empty")
                : option.KeyboardLabel,
            option.GamepadLabel.Length == 0
                ? UiLocalization.Text("Ui.Common.Empty")
                : option.GamepadLabel,
            canonical,
            option.EngineName);
        string automationName = UiLocalization.Format(
            "Ui.PressKey.Option.Automation",
            displayName,
            option.KeyboardLabel,
            option.GamepadLabel);
        return new PressKeyPickerItem(
            option,
            displayName,
            option.KeyboardLabel,
            option.GamepadLabel,
            tooltip,
            automationName,
            searchText);
    }

    private static string GroupHeader(XuiPressKeyGroup group) =>
        UiLocalization.Text($"Ui.PressKey.Group.{group}");

    private void UpdateSummary()
    {
        if (SummaryNameText is null)
        {
            return;
        }

        if (IsMixed)
        {
            SetSummary(
                UiLocalization.Text("Ui.Common.Mixed"),
                string.Empty,
                string.Empty);
            return;
        }

        if (XuiPressKeyCatalog.TryResolve(Value, out XuiPressKeyOption option))
        {
            SetSummary(
                UiLocalization.Text(option.DisplayNameKey),
                option.KeyboardLabel,
                option.GamepadLabel);
            return;
        }

        string text = XuiValueParser.TryInteger(Value, out int customValue)
            ? UiLocalization.Format(
                "Ui.PressKey.CustomSummary",
                customValue.ToString(CultureInfo.InvariantCulture))
            : UiLocalization.Format(
                "Ui.PressKey.UnrecognizedSummary",
                Value);
        SetSummary(text, string.Empty, string.Empty);
    }

    private void SetSummary(
        string name,
        string keyboardLabel,
        string gamepadLabel)
    {
        SummaryNameText.Text = name;
        SummaryKeyboardText.Text = keyboardLabel;
        SummaryGamepadText.Text = gamepadLabel;
        SummaryKeyboardBadge.Visibility = keyboardLabel.Length == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
        SummaryGamepadBadge.Visibility = gamepadLabel.Length == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
        AutomationProperties.SetName(
            OpenButton,
            UiLocalization.Format("Ui.PressKey.OpenAutomation", name));
    }

    private void OpenButton_Checked(object sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        RefreshLocalizedContent();
        SearchTextBox.Text = string.Empty;
        CustomErrorText.Text = string.Empty;
        CustomValueTextBox.Text =
            XuiValueParser.TryInteger(Value, out int value) &&
            !XuiPressKeyCatalog.TryResolve(value, out _)
                ? value.ToString(CultureInfo.InvariantCulture)
                : string.Empty;
        PickerPopup.IsOpen = true;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            SearchTextBox.Focus);
    }

    private void OpenButton_Unchecked(object sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        PickerPopup.IsOpen = false;
    }

    private void PickerPopup_Closed(object? sender, EventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        PickerPopup.StaysOpen = false;
        OpenButton.IsChecked = false;
    }

    private void SearchTextBox_TextChanged(
        object sender,
        TextChangedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        if (GroupItems is not null)
        {
            BuildGroups(SearchTextBox.Text);
        }
    }

    private void SearchTextBox_KeyDown(object sender, KeyEventArgs eventArgs)
    {
        _ = sender;
        if (eventArgs.Key == Key.Enter &&
            _visibleGroups.SelectMany(static group => group.Items).FirstOrDefault()
                is PressKeyPickerItem first)
        {
            CommitValue(first.Option.CanonicalText);
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Key.Escape)
        {
            ClosePicker();
            eventArgs.Handled = true;
        }
    }

    private void PickerTile_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button
            {
                Tag: PressKeyPickerItem item,
            })
        {
            CommitValue(item.Option.CanonicalText);
            eventArgs.Handled = true;
        }
    }

    private void ApplyCustom_Click(object sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        ApplyCustomValue();
    }

    private void CustomValueTextBox_TextChanged(
        object sender,
        TextChangedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        if (CustomErrorText is not null)
        {
            CustomErrorText.Text = string.Empty;
        }
    }

    private void CustomValueTextBox_KeyDown(
        object sender,
        KeyEventArgs eventArgs)
    {
        _ = sender;
        if (eventArgs.Key == Key.Enter)
        {
            ApplyCustomValue();
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Key.Escape)
        {
            ClosePicker();
            eventArgs.Handled = true;
        }
    }

    private void ApplyCustomValue()
    {
        if (!XuiValueParser.TryInteger(
                CustomValueTextBox.Text,
                out int customValue))
        {
            CustomErrorText.Text =
                UiLocalization.Text("Ui.PressKey.InvalidCustom");
            return;
        }

        CommitValue(customValue.ToString(CultureInfo.InvariantCulture));
    }

    private void PickerPopup_PreviewKeyDown(
        object sender,
        KeyEventArgs eventArgs)
    {
        _ = sender;
        if (eventArgs.Key == Key.Escape)
        {
            ClosePicker();
            eventArgs.Handled = true;
        }
    }

    private void CommitValue(string value)
    {
        Value = value;
        ValueCommitted?.Invoke(
            this,
            new PressKeyValueCommittedEventArgs(value));
        ClosePicker();
    }

    private void ClosePicker()
    {
        PickerPopup.IsOpen = false;
        OpenButton.IsChecked = false;
    }

    private void Root_Unloaded(object sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        PickerPopup.IsOpen = false;
    }
}

public sealed class PressKeyValueCommittedEventArgs(string value) : EventArgs
{
    public string Value { get; } = value;
}

internal sealed record PressKeyPickerGroup(
    string Header,
    IReadOnlyList<PressKeyPickerItem> Items);

internal sealed record PressKeyPickerItem(
    XuiPressKeyOption Option,
    string DisplayName,
    string KeyboardLabel,
    string GamepadLabel,
    string ToolTip,
    string AutomationName,
    string SearchText)
{
    public bool HasKeyboardLabel => KeyboardLabel.Length > 0;

    public bool HasGamepadLabel => GamepadLabel.Length > 0;
}
