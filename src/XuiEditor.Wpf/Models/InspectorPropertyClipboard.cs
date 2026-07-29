using System.ComponentModel;
using XuiEditor.Core.Schema;
using XuiEditor.Wpf.Services;

namespace XuiEditor.Wpf.Models;

public sealed record XuiCopiedInspectorProperty(
    string Name,
    string Value,
    string Category,
    XuiPropertyType Type,
    bool WasAuthored);

public sealed record XuiInspectorPropertyClipboard(
    string SourceDisplayName,
    string SourceClassName,
    IReadOnlyList<XuiCopiedInspectorProperty> Properties);

public sealed record XuiInspectorPropertyPasteResult(
    int DestinationCount,
    int PropertyAssignments,
    int IncompatibleAssignments,
    int UnchangedAssignments);

public sealed class XuiCopyPropertyOption :
    INotifyPropertyChanged
{
    private bool _isSelected;

    public XuiCopyPropertyOption(
        XuiPropertyDefinition definition,
        string value,
        bool isAuthored)
    {
        Definition = definition ??
                     throw new ArgumentNullException(nameof(definition));
        Value = value ?? string.Empty;
        IsAuthored = isAuthored;
        CanCopy = XuiPropertyTransfer.CanCopy(definition.Name);
        _isSelected = CanCopy && isAuthored;
    }

    public XuiPropertyDefinition Definition { get; }

    public string Name => Definition.Name;

    public string Category => UiLocalization.Category(Definition.Category);

    public XuiPropertyType Type => Definition.Type;

    public string Value { get; }

    public bool IsAuthored { get; }

    public bool CanCopy { get; }

    public string SourceLabel => IsAuthored
        ? UiLocalization.Text("Ui.Inspector.Source.Authored")
        : UiLocalization.Text("Ui.Inspector.Source.InheritedDefault");

    public string ToolTip => CanCopy
        ? string.Join(
            Environment.NewLine,
            InspectorHelpText.Description(Definition),
            UiLocalization.Format(
                "Ui.Inspector.CopySource",
                SourceLabel,
                Value))
        : UiLocalization.Format(
            "Ui.Inspector.ProtectedProperty",
            Name);

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            bool selected = CanCopy && value;
            if (_isSelected == selected)
            {
                return;
            }

            _isSelected = selected;
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public XuiCopiedInspectorProperty ToCopiedProperty() =>
        new(
            Name,
            Value,
            Category,
            Type,
            IsAuthored);

    public event PropertyChangedEventHandler? PropertyChanged;
}
