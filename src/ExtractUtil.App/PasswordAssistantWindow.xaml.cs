using System.Windows;

namespace ExtractUtil.App;

public partial class PasswordAssistantWindow : Window
{
    public PasswordAssistantWindow()
    {
        InitializeComponent();
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
