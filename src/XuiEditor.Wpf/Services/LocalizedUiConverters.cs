using System.Globalization;
using System.Windows.Data;
using XuiEditor.Core.Diagnostics;

namespace XuiEditor.Wpf.Services;

public sealed class LocalizedDiagnosticMessageConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        value is XuiDiagnostic diagnostic
            ? UiLocalization.DiagnosticMessage(diagnostic)
            : string.Empty;

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class LocalizedDiagnosticSeverityConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        value is XuiDiagnosticSeverity severity
            ? UiLocalization.Text($"Ui.Diagnostic.Severity.{severity}")
            : string.Empty;

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        throw new NotSupportedException();
}
