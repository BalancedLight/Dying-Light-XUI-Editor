using System.ComponentModel;
using System.Runtime.CompilerServices;
using XuiEditor.Core.Documents;

namespace XuiEditor.Wpf.Models;

public sealed class HierarchyRow : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isEditorVisible = true;
    private bool _isLocked;

    public HierarchyRow(
        XuiSyntaxNode node,
        string displayName,
        int depth,
        bool hasChildren,
        bool isExpanded,
        bool isEditorVisible,
        bool isLocked)
    {
        NodeKey = node.Key;
        ElementName = node.Name;
        DisplayName = displayName;
        Depth = depth;
        HasChildren = hasChildren;
        _isExpanded = isExpanded;
        _isEditorVisible = isEditorVisible;
        _isLocked = isLocked;
    }

    public string NodeKey { get; }

    public string ElementName { get; }

    public string DisplayName { get; }

    public int Depth { get; }

    public double Indent => Depth * 14;

    public bool HasChildren { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    public bool IsEditorVisible
    {
        get => _isEditorVisible;
        set => SetField(ref _isEditorVisible, value);
    }

    public bool IsLocked
    {
        get => _isLocked;
        set => SetField(ref _isLocked, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}
