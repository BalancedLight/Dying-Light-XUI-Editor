using System.ComponentModel;
using XuiEditor.Core.Schema;

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

    public string Category => Definition.Category;

    public XuiPropertyType Type => Definition.Type;

    public string Value { get; }

    public bool IsAuthored { get; }

    public bool CanCopy { get; }

    public string SourceLabel => IsAuthored
        ? "authored"
        : "inherited default";

    public string ToolTip => CanCopy
        ? $"{Definition.Description}{Environment.NewLine}{SourceLabel}: {Value}"
        : $"{Name} is protected because pasting it could change element identity or class compatibility.";

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
