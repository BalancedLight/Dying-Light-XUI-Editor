using System.ComponentModel;
using System.Runtime.CompilerServices;
using XuiEditor.Core.Schema;
using XuiEditor.Wpf.Services;

namespace XuiEditor.Wpf.Models;

public sealed class InspectorPropertyRow : INotifyPropertyChanged
{
    private string _value;
    private string? _error;
    private bool _hasAnimationTrack;
    private bool _hasAnimationKey;

    public InspectorPropertyRow(
        string name,
        string value,
        string category,
        bool isMixed,
        bool isUnknown,
        IReadOnlyList<string>? choices = null,
        bool isBooleanToggle = false,
        bool isAuthored = true,
        XuiPropertyDefinition? definition = null,
        bool hasAnimationTrack = false,
        bool hasAnimationKey = false)
    {
        Name = name;
        _value = value;
        Category = UiLocalization.Category(category);
        IsMixed = isMixed;
        IsUnknown = isUnknown;
        Choices = choices ?? [];
        IsBooleanToggle = isBooleanToggle;
        IsAuthored = isAuthored;
        Definition = definition;
        _hasAnimationTrack = hasAnimationTrack;
        _hasAnimationKey = hasAnimationKey;
    }

    public string Name { get; }

    public string Category { get; }

    public bool IsMixed { get; }

    public bool IsUnknown { get; }

    public bool IsAuthored { get; }

    public bool IsGhostDefault => !IsAuthored;

    public bool CanReset => IsAuthored;

    public XuiPropertyDefinition? Definition { get; }

    public bool IsAnimatable => Definition?.IsAnimatable == true;

    public bool HasAnimationTrack => _hasAnimationTrack;

    public bool HasAnimationKey => _hasAnimationKey;

    public string AnimationGlyph => HasAnimationKey ? "◆" : "◇";

    public string AnimationToolTip => HasAnimationKey
        ? UiLocalization.Text("Ui.Animation.Inspector.UpdateKey")
        : HasAnimationTrack
            ? UiLocalization.Text("Ui.Animation.Inspector.AddKey")
            : UiLocalization.Text("Ui.Animation.Inspector.AddTrack");

    public void UpdateAnimationState(bool hasTrack, bool hasKey)
    {
        bool trackChanged = SetField(
            ref _hasAnimationTrack,
            hasTrack,
            nameof(HasAnimationTrack));
        bool keyChanged = SetField(
            ref _hasAnimationKey,
            hasKey,
            nameof(HasAnimationKey));
        if (!trackChanged && !keyChanged)
        {
            return;
        }

        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(AnimationGlyph)));
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(AnimationToolTip)));
    }

    public string ToolTip =>
        InspectorHelpText.BuildToolTip(Name, Definition, IsAuthored);

    public string EditorToolTip => Error ?? ToolTip;

    public IReadOnlyList<string> Choices { get; }

    public bool HasChoices => !IsBooleanToggle && Choices.Count > 0;

    public bool IsBooleanToggle { get; }

    public bool? BooleanValue
    {
        get => Value.Equals("true", StringComparison.OrdinalIgnoreCase)
            ? true
            : Value.Equals("false", StringComparison.OrdinalIgnoreCase)
                ? false
                : null;
        set
        {
            if (value is bool boolean)
            {
                Value = boolean ? "true" : "false";
            }
        }
    }

    public string Value
    {
        get => _value;
        set
        {
            if (SetField(ref _value, value))
            {
                PropertyChanged?.Invoke(
                    this,
                    new PropertyChangedEventArgs(nameof(BooleanValue)));
            }
        }
    }

    public string? Error
    {
        get => _error;
        set
        {
            if (SetField(ref _error, value))
            {
                PropertyChanged?.Invoke(
                    this,
                    new PropertyChangedEventArgs(nameof(HasError)));
                PropertyChanged?.Invoke(
                    this,
                    new PropertyChangedEventArgs(nameof(EditorToolTip)));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(Error);

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
