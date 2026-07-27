using System.ComponentModel;
using System.Runtime.CompilerServices;

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
        IReadOnlyList<string>? choices = null)
    {
        Name = name;
        _value = value;
        Category = category;
        IsMixed = isMixed;
        IsUnknown = isUnknown;
        Choices = choices ?? [];
    }

    public string Name { get; }

    public string Category { get; }

    public bool IsMixed { get; }

    public bool IsUnknown { get; }

    public IReadOnlyList<string> Choices { get; }

    public bool HasChoices => Choices.Count > 0;

    public string Value
    {
        get => _value;
        set => SetField(ref _value, value);
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
