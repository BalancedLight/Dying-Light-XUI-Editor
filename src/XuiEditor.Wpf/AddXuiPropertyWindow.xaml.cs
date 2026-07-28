using System.Windows;

namespace XuiEditor.Wpf;

public partial class AddXuiPropertyWindow : Window
{
    public AddXuiPropertyWindow(string ownerDisplayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerDisplayName);
        InitializeComponent();
        OwnerText.Text =
            $"Add one authored property to {ownerDisplayName}. The edit is lossless and undoable.";
        NameTextBox.Focus();
    }

    public string PropertyName { get; private set; } = string.Empty;

    public string PropertyValue { get; private set; } = string.Empty;

    private void Add_Click(object sender, RoutedEventArgs eventArgs)
    {
        string name = NameTextBox.Text.Trim();
        if (!IsValidXmlName(name))
        {
            ErrorText.Text =
                "Enter a valid XML property name, such as Opacity or ImagePath.";
            return;
        }

        PropertyName = name;
        PropertyValue = ValueTextBox.Text;
        DialogResult = true;
    }

    private static bool IsValidXmlName(string name) =>
        name.Length > 0 &&
        (char.IsLetter(name[0]) || name[0] is '_' or ':') &&
        name.All(character =>
            char.IsLetterOrDigit(character) ||
            character is '_' or '-' or ':' or '.');
}
