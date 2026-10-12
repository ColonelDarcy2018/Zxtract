using System.Windows;
using System.Windows.Controls;

namespace ExtractUtil.App.Controls;

public partial class NightOptionsPanel : System.Windows.Controls.UserControl
{
    public event EventHandler? CloseRequested;
    public event EventHandler? PasswordsRequested;
    public NightOptionsPanel() => InitializeComponent();
    private void OnClose(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    private void OnPasswords(object sender, RoutedEventArgs e) => PasswordsRequested?.Invoke(this, EventArgs.Empty);
}
