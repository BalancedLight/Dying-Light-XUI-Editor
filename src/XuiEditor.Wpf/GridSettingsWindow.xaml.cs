using System.Globalization;
using System.Windows;
using System.Windows.Media;
using XuiEditor.Wpf.Services;

namespace XuiEditor.Wpf;

public partial class GridSettingsWindow : Window
{
    private readonly EditorSettings _settings;

    public GridSettingsWindow(EditorSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        InitializeComponent();
        MinorSizeText.Text = Number(settings.GridSize);
        MajorSizeText.Text = Number(settings.MajorGridSize);
        CoarseSizeText.Text = Number(settings.CoarseGridSize);
        MinorColorText.Text = settings.MinorGridColor;
        MajorColorText.Text = settings.MajorGridColor;
        CoarseColorText.Text = settings.CoarseGridColor;
        (settings.SnapGridTier switch
        {
            XuiGridTier.Major => MajorSnap,
            XuiGridTier.Coarse => CoarseSnap,
            _ => MinorSnap,
        }).IsChecked = true;
    }

    private void Apply_Click(object sender, RoutedEventArgs eventArgs)
    {
        try
        {
            double minor = ParseSpacing(MinorSizeText.Text, "Minor spacing");
            double major = ParseSpacing(MajorSizeText.Text, "Major spacing");
            double coarse = ParseSpacing(CoarseSizeText.Text, "Coarse spacing");
            if (major < minor || coarse < major)
            {
                throw new InvalidOperationException(
                    "Grid spacings must be ordered minor ≤ major ≤ coarse.");
            }

            string minorColor = ParseColor(MinorColorText.Text, "Minor color");
            string majorColor = ParseColor(MajorColorText.Text, "Major color");
            string coarseColor = ParseColor(CoarseColorText.Text, "Coarse color");
            _settings.GridSize = minor;
            _settings.MajorGridSize = major;
            _settings.CoarseGridSize = coarse;
            _settings.MinorGridColor = minorColor;
            _settings.MajorGridColor = majorColor;
            _settings.CoarseGridColor = coarseColor;
            _settings.SnapGridTier = MajorSnap.IsChecked == true
                ? XuiGridTier.Major
                : CoarseSnap.IsChecked == true
                    ? XuiGridTier.Coarse
                    : XuiGridTier.Minor;
            DialogResult = true;
        }
        catch (InvalidOperationException exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private static double ParseSpacing(string raw, string label)
    {
        if (!double.TryParse(
                raw,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double value) ||
            !double.IsFinite(value) ||
            value < 0.25 ||
            value > 4096)
        {
            throw new InvalidOperationException(
                $"{label} must be between 0.25 and 4096.");
        }

        return value;
    }

    private static string ParseColor(string raw, string label)
    {
        try
        {
            if (ColorConverter.ConvertFromString(raw.Trim()) is Color color)
            {
                return color.ToString(CultureInfo.InvariantCulture);
            }
        }
        catch (FormatException)
        {
        }

        throw new InvalidOperationException(
            $"{label} must be #AARRGGBB or a WPF color name.");
    }

    private static string Number(double value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);
}
