using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using XuiEditor.Core.Schema;
using XuiEditor.Wpf.Services;

namespace XuiEditor.Wpf.Controls;

public partial class TextStylePicker : UserControl
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(
            nameof(Value),
            typeof(string),
            typeof(TextStylePicker),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                PresentationPropertyChanged));

    public static readonly DependencyProperty IsMixedProperty =
        DependencyProperty.Register(
            nameof(IsMixed),
            typeof(bool),
            typeof(TextStylePicker),
            new PropertyMetadata(false, PresentationPropertyChanged));

    private TextStylePickerGroup[] _visibleGroups = [];
    private int _composerRawValue;

    public TextStylePicker()
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

    public event EventHandler<TextStyleValueCommittedEventArgs>?
        ValueCommitted;

    internal IReadOnlyList<TextStylePickerGroup> VisibleGroupsForTesting =>
        _visibleGroups;

    internal string SummaryForTesting => SummaryNameText.Text;

    internal bool IsPopupOpenForTesting => PickerPopup.IsOpen;

    internal FrameworkElement PopupContentForTesting =>
        (FrameworkElement)PickerPopup.Child;

    internal string CustomErrorForTesting => CustomErrorText.Text;

    internal void OpenForTesting() => OpenButton.IsChecked = true;

    internal void SearchForTesting(string query) => SearchTextBox.Text = query;

    internal void CloseForTesting() => ClosePicker();

    internal bool SelectForTesting(int rawValue)
    {
        if (!XuiTextStyleCatalog.TryResolve(
                rawValue,
                out XuiTextStyleProfile profile))
        {
            return false;
        }

        CommitValue(profile.CanonicalText);
        return true;
    }

    internal void ApplyCustomForTesting(string value)
    {
        CustomValueTextBox.Text = value;
        ApplyCustomValue();
    }

    internal void ApplyCompositionForTesting(
        bool bold,
        bool italic,
        bool underline,
        XuiTextHorizontalStyle horizontal,
        bool verticalMiddle,
        bool compatibility0010,
        bool scaleAware,
        bool compatibility4000)
    {
        LoadComposer();
        BoldCheckBox.IsChecked = bold;
        ItalicCheckBox.IsChecked = italic;
        UnderlineCheckBox.IsChecked = underline;
        HorizontalComboBox.SelectedValue = horizontal.ToString();
        VerticalComboBox.SelectedValue = verticalMiddle
            ? "Middle"
            : "Top";
        Compatibility0010CheckBox.IsChecked = compatibility0010;
        ScaleAwareCheckBox.IsChecked = scaleAware;
        Compatibility4000CheckBox.IsChecked = compatibility4000;
        ApplyComposed_Click(this, new RoutedEventArgs());
    }

    private static void PresentationPropertyChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        _ = eventArgs;
        ((TextStylePicker)dependencyObject).UpdateSummary();
    }

    private void RefreshLocalizedContent()
    {
        BuildGroups(SearchTextBox?.Text ?? string.Empty);
        UpdateSummary();
    }

    private void BuildGroups(string query)
    {
        string trimmedQuery = query.Trim();
        _visibleGroups = XuiTextStyleCatalog.Profiles
            .Select(CreateItem)
            .Where(item =>
                trimmedQuery.Length == 0 ||
                item.SearchText.Contains(
                    trimmedQuery,
                    StringComparison.OrdinalIgnoreCase))
            .GroupBy(static item => item.Profile.Decoded.VerticalMiddle)
            .OrderBy(static group => group.Key)
            .Select(group => new TextStylePickerGroup(
                UiLocalization.Text(
                    group.Key
                        ? "Ui.TextStyle.Group.Middle"
                        : "Ui.TextStyle.Group.Top"),
                group.ToArray()))
            .ToArray();
        GroupItems.ItemsSource = _visibleGroups;
        NoResultsText.Visibility = _visibleGroups.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static TextStylePickerItem CreateItem(
        XuiTextStyleProfile profile)
    {
        XuiDecodedTextStyle decoded = profile.Decoded;
        string displayName = BuildDisplayName(decoded);
        string sample = UiLocalization.Text("Ui.TextStyle.SampleText");
        string uses = UiLocalization.Format(
            "Ui.TextStyle.StockUseCount",
            profile.StockOccurrenceCount);
        string tooltip = UiLocalization.Format(
            "Ui.TextStyle.Option.ToolTip",
            displayName,
            profile.CanonicalText,
            profile.HexText,
            uses);
        string searchText = string.Join(
            " ",
            displayName,
            profile.CanonicalText,
            profile.HexText,
            uses,
            decoded.Bold ? "bold" : string.Empty,
            decoded.Italic ? "italic" : string.Empty,
            decoded.Underline ? "underline" : string.Empty,
            decoded.VerticalMiddle ? "middle" : "top",
            decoded.HorizontalAlignment.ToString(),
            decoded.Compatibility0010 ? "compatibility 0x0010" : string.Empty);
        return new TextStylePickerItem(
            profile,
            displayName,
            sample,
            profile.HexText,
            tooltip,
            UiLocalization.Format(
                "Ui.TextStyle.Option.Automation",
                displayName,
                profile.HexText),
            searchText,
            decoded.Bold ? FontWeights.Bold : FontWeights.Normal,
            decoded.Italic ? FontStyles.Italic : FontStyles.Normal,
            decoded.Underline ? TextDecorations.Underline : null,
            decoded.HorizontalAlignment switch
            {
                XuiTextHorizontalStyle.Center => TextAlignment.Center,
                XuiTextHorizontalStyle.Right => TextAlignment.Right,
                _ => TextAlignment.Left,
            },
            decoded.VerticalMiddle
                ? VerticalAlignment.Center
                : VerticalAlignment.Top);
    }

    private static string BuildDisplayName(XuiDecodedTextStyle decoded)
    {
        List<string> parts = [];
        if (decoded.VerticalMiddle)
        {
            parts.Add(UiLocalization.Text("Ui.TextStyle.Middle"));
        }

        parts.Add(decoded.HorizontalAlignment switch
        {
            XuiTextHorizontalStyle.Left =>
                UiLocalization.Text("Ui.TextStyle.Left"),
            XuiTextHorizontalStyle.Right =>
                UiLocalization.Text("Ui.TextStyle.Right"),
            XuiTextHorizontalStyle.Center =>
                UiLocalization.Text("Ui.TextStyle.Center"),
            _ => UiLocalization.Text("Ui.TextStyle.Unspecified"),
        });
        if (decoded.Bold)
        {
            parts.Add(UiLocalization.Text("Ui.TextStyle.Bold"));
        }

        if (decoded.Italic)
        {
            parts.Add(UiLocalization.Text("Ui.TextStyle.Italic"));
        }

        if (decoded.Underline)
        {
            parts.Add(UiLocalization.Text("Ui.TextStyle.Underline"));
        }

        if (decoded.Compatibility0010)
        {
            parts.Add(UiLocalization.Text(
                "Ui.TextStyle.Compatibility0010"));
        }

        if (decoded.ScaleAwareGlyphSizing)
        {
            parts.Add(UiLocalization.Text("Ui.TextStyle.ScaleAware"));
        }

        if (decoded.Compatibility4000)
        {
            parts.Add(UiLocalization.Text(
                "Ui.TextStyle.Compatibility4000"));
        }

        if (decoded.UnknownBits != 0)
        {
            parts.Add(UiLocalization.Format(
                "Ui.TextStyle.UnknownBits",
                XuiTextStyleCodec.ToHexString(decoded.UnknownBits)));
        }

        return string.Join(" · ", parts);
    }

    private void UpdateSummary()
    {
        if (SummaryNameText is null)
        {
            return;
        }

        if (IsMixed)
        {
            SummaryNameText.Text = UiLocalization.Text("Ui.Common.Mixed");
            SummaryValueText.Text = string.Empty;
        }
        else if (XuiTextStyleCodec.TryParse(
                     Value,
                     out XuiDecodedTextStyle decoded))
        {
            SummaryNameText.Text = BuildDisplayName(decoded);
            SummaryValueText.Text = XuiTextStyleCodec.ToHexString(
                decoded.RawValue);
        }
        else
        {
            SummaryNameText.Text = UiLocalization.Format(
                "Ui.TextStyle.UnrecognizedSummary",
                Value);
            SummaryValueText.Text = string.Empty;
        }

        AutomationProperties.SetName(
            OpenButton,
            UiLocalization.Format(
                "Ui.TextStyle.OpenAutomation",
                SummaryNameText.Text));
    }

    private void OpenButton_Checked(object sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        RefreshLocalizedContent();
        SearchTextBox.Text = string.Empty;
        CustomErrorText.Text = string.Empty;
        LoadComposer();
        CustomValueTextBox.Text = Value;
        PickerPopup.IsOpen = true;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            SearchTextBox.Focus);
    }

    private void LoadComposer()
    {
        _composerRawValue = !IsMixed &&
                            XuiTextStyleCodec.TryParse(
                                Value,
                                out XuiDecodedTextStyle decoded)
            ? decoded.RawValue
            : 0;
        decoded = XuiTextStyleCodec.Decode(_composerRawValue);
        BoldCheckBox.IsChecked = decoded.Bold;
        ItalicCheckBox.IsChecked = decoded.Italic;
        UnderlineCheckBox.IsChecked = decoded.Underline;
        HorizontalComboBox.SelectedValue =
            decoded.HorizontalAlignment.ToString();
        VerticalComboBox.SelectedValue = decoded.VerticalMiddle
            ? "Middle"
            : "Top";
        Compatibility0010CheckBox.IsChecked =
            decoded.Compatibility0010;
        ScaleAwareCheckBox.IsChecked = decoded.ScaleAwareGlyphSizing;
        Compatibility4000CheckBox.IsChecked =
            decoded.Compatibility4000;
        PreservedBitsText.Text = UiLocalization.Format(
            IsMixed
                ? "Ui.TextStyle.MixedComposer"
                : "Ui.TextStyle.PreservedBits",
            XuiTextStyleCodec.ToHexString(decoded.UnknownBits));
    }

    private void ApplyComposed_Click(object sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        int raw = _composerRawValue;
        raw = XuiTextStyleCodec.SetFlag(
            raw,
            XuiKnownTextStyle.Bold,
            BoldCheckBox.IsChecked == true);
        raw = XuiTextStyleCodec.SetFlag(
            raw,
            XuiKnownTextStyle.Italic,
            ItalicCheckBox.IsChecked == true);
        raw = XuiTextStyleCodec.SetFlag(
            raw,
            XuiKnownTextStyle.Underline,
            UnderlineCheckBox.IsChecked == true);
        raw = XuiTextStyleCodec.SetHorizontalAlignment(
            raw,
            Enum.TryParse(
                HorizontalComboBox.SelectedValue as string,
                ignoreCase: true,
                out XuiTextHorizontalStyle horizontal)
                ? horizontal
                : XuiTextHorizontalStyle.Unspecified);
        raw = XuiTextStyleCodec.SetVerticalMiddle(
            raw,
            string.Equals(
                VerticalComboBox.SelectedValue as string,
                "Middle",
                StringComparison.Ordinal));
        raw = XuiTextStyleCodec.SetFlag(
            raw,
            XuiKnownTextStyle.Compatibility0010,
            Compatibility0010CheckBox.IsChecked == true);
        raw = XuiTextStyleCodec.SetFlag(
            raw,
            XuiKnownTextStyle.ScaleAwareGlyphSizing,
            ScaleAwareCheckBox.IsChecked == true);
        raw = XuiTextStyleCodec.SetFlag(
            raw,
            XuiKnownTextStyle.Compatibility4000,
            Compatibility4000CheckBox.IsChecked == true);
        CommitValue(XuiTextStyleCodec.ToDecimalString(raw));
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
            _visibleGroups.SelectMany(static group => group.Items)
                .FirstOrDefault() is TextStylePickerItem first)
        {
            CommitValue(first.Profile.CanonicalText);
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
        if (sender is Button { Tag: TextStylePickerItem item })
        {
            CommitValue(item.Profile.CanonicalText);
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
        if (!XuiTextStyleCodec.TryParse(
                CustomValueTextBox.Text,
                out XuiDecodedTextStyle decoded))
        {
            CustomErrorText.Text =
                UiLocalization.Text("Ui.TextStyle.InvalidCustom");
            return;
        }

        CommitValue(XuiTextStyleCodec.ToDecimalString(decoded.RawValue));
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
            new TextStyleValueCommittedEventArgs(value));
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

public sealed class TextStyleValueCommittedEventArgs(string value) : EventArgs
{
    public string Value { get; } = value;
}

internal sealed record TextStylePickerGroup(
    string Header,
    IReadOnlyList<TextStylePickerItem> Items);

internal sealed record TextStylePickerItem(
    XuiTextStyleProfile Profile,
    string DisplayName,
    string SampleText,
    string HexValue,
    string ToolTip,
    string AutomationName,
    string SearchText,
    FontWeight SampleFontWeight,
    FontStyle SampleFontStyle,
    TextDecorationCollection? SampleTextDecorations,
    TextAlignment SampleTextAlignment,
    VerticalAlignment SampleVerticalAlignment);
