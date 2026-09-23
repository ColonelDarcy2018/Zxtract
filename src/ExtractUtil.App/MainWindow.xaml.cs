using System.Windows;
using ExtractUtil.App.ViewModels;

namespace ExtractUtil.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        // 初始化 UI 并绑定主 ViewModel。
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    private void OnPreviewDragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            ? System.Windows.DragDropEffects.Copy
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel ||
            !e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop) ||
            e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        viewModel.AddPathsFromDrop(paths);
        e.Handled = true;
    }
}
