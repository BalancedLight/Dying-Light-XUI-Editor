using System.ComponentModel;
using System.Runtime.CompilerServices;
using XuiEditor.Core.Schema;

namespace XuiEditor.Wpf.Models;

public sealed class InspectorPropertyRow : INotifyPropertyChanged
{
    private string _value;
    private string? _error;

    public InspectorPropertyRow(
        string name,
        string value,
        string category,
        bool isMixed,
        bool isUnknown,
        IReadOnlyList<string>? choices = null,
        bool isBooleanToggle = false,
        bool isAuthored = true,
        XuiPropertyDefinition? definition = null)
    {
        Name = name;
        _value = value;
        Category = category;
        IsMixed = isMixed;
        IsUnknown = isUnknown;
        Choices = choices ?? [];
        IsBooleanToggle = isBooleanToggle;
        IsAuthored = isAuthored;
        Definition = definition;
    }

    public string Name { get; }

    public string Category { get; }

    public bool IsMixed { get; }

    public bool IsUnknown { get; }

    public bool IsAuthored { get; }

    public bool IsGhostDefault => !IsAuthored;

    public bool CanReset => IsAuthored;

    public XuiPropertyDefinition? Definition { get; }

    public string ToolTip => Definition is null
        ? string.Join(
            Environment.NewLine,
            Name,
            "Unknown mod-authored property. It will be preserved losslessly.")
        : string.Join(
            Environment.NewLine,
            new[]
            {
                Name,
                Definition.Description,
                $"Evidence: {Definition.EvidenceLabel}",
                $"Preview: {Definition.PreviewSupport}",
                Definition.IsAnimatable
                    ? "Timeline: animatable"
                    : "Timeline: noanim",
                IsAuthored
                    ? "Authored in the selected XML."
                    : $"Inherited default: {Definition.DefaultValue}",
            });

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
