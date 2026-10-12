using System.Windows;
using ExtractUtil.App.Themes;

namespace ExtractUtil.App;

public partial class PasswordAssistantWindow : Window
{
    public PasswordAssistantWindow()
    {
        _ = ThemeManager.Current;
        InitializeComponent();
        SourceInitialized += (_, _) => ThemeManager.Current.ApplyWindowChrome(this);
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
