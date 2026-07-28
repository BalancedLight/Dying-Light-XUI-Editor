using System.ComponentModel;
using System.Runtime.CompilerServices;
using XuiEditor.Core.Documents;

namespace XuiEditor.Wpf.Models;

public enum HierarchyVisibilityState
{
    Visible,
    Hidden,
    HiddenByAncestor,
}

public enum HierarchyLockState
{
    Unlocked,
    Locked,
    LockedByAncestor,
}

public sealed class HierarchyRow : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isEditorVisible = true;
    private bool _isLocked;
    private HierarchyVisibilityState _visibilityState;
    private HierarchyLockState _lockState;
    private string? _hiddenBy;
    private string? _lockedBy;

    public HierarchyRow(
        XuiSyntaxNode node,
        string displayName,
        int depth,
        bool hasChildren)
    {
        NodeKey = node.Key;
        ElementName = node.Name;
        DisplayName = displayName;
        Depth = depth;
        HasChildren = hasChildren;
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

    public HierarchyVisibilityState VisibilityState
    {
        get => _visibilityState;
        private set => SetField(ref _visibilityState, value);
    }

    public HierarchyLockState LockState
    {
        get => _lockState;
        private set => SetField(ref _lockState, value);
    }

    public bool CanToggleVisibility =>
        VisibilityState != HierarchyVisibilityState.HiddenByAncestor;

    public bool CanToggleLock =>
        LockState != HierarchyLockState.LockedByAncestor;

    public double RowTextOpacity =>
        VisibilityState == HierarchyVisibilityState.Visible ? 1.0 : 0.48;

    public string VisibilityToolTip => VisibilityState switch
    {
        HierarchyVisibilityState.Hidden =>
            "Hidden in editor — click to show",
        HierarchyVisibilityState.HiddenByAncestor =>
            $"Hidden by {_hiddenBy ?? "an ancestor"}",
        _ => "Visible in editor — click to hide",
    };

    public string LockToolTip => LockState switch
    {
        HierarchyLockState.Locked =>
            "Locked in editor — click to unlock",
        HierarchyLockState.LockedByAncestor =>
            $"Locked by {_lockedBy ?? "an ancestor"}",
        _ => "Unlocked in editor — click to lock",
    };

    public string VisibilityAutomationName => VisibilityState switch
    {
        HierarchyVisibilityState.Hidden =>
            $"{DisplayName}, hidden in editor. Activate to show.",
        HierarchyVisibilityState.HiddenByAncestor =>
            $"{DisplayName}, hidden by {_hiddenBy ?? "an ancestor"}.",
        _ => $"{DisplayName}, visible in editor. Activate to hide.",
    };

    public string LockAutomationName => LockState switch
    {
        HierarchyLockState.Locked =>
            $"{DisplayName}, locked in editor. Activate to unlock.",
        HierarchyLockState.LockedByAncestor =>
            $"{DisplayName}, locked by {_lockedBy ?? "an ancestor"}.",
        _ => $"{DisplayName}, unlocked in editor. Activate to lock.",
    };

    internal void SetEditorStates(
        HierarchyVisibilityState visibilityState,
        string? hiddenBy,
        HierarchyLockState lockState,
        string? lockedBy)
    {
        _hiddenBy = hiddenBy;
        _lockedBy = lockedBy;
        IsEditorVisible =
            visibilityState != HierarchyVisibilityState.Hidden;
        IsLocked = lockState == HierarchyLockState.Locked;
        VisibilityState = visibilityState;
        LockState = lockState;
        Notify(nameof(CanToggleVisibility));
        Notify(nameof(CanToggleLock));
        Notify(nameof(RowTextOpacity));
        Notify(nameof(VisibilityToolTip));
        Notify(nameof(LockToolTip));
        Notify(nameof(VisibilityAutomationName));
        Notify(nameof(LockAutomationName));
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
        Notify(propertyName);
    }

    private void Notify(string? propertyName) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
}
