using System.Windows;
using XuiEditor.Wpf.Services;

namespace XuiEditor.Wpf;

public partial class App : Application
{
    internal EditorSettings? Settings { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        Settings = EditorSettingsStore.Load();
        UiLocalization.Apply(Settings.UiLanguage);
        base.OnStartup(e);
    }
}
