using System.Windows;
using ExtractUtil.App.Services;
using ExtractUtil.App.ViewModels;

namespace ExtractUtil.App;

// 应用入口：处理右键菜单注册、解析启动参数。
public partial class App : System.Windows.Application
{
    private void OnStartup(object sender, StartupEventArgs e)
    {
        var shellArgs = ShellArgumentParser.Parse(e.Args);

        // 注册/卸载右键菜单后直接退出。
        if (shellArgs.Mode == ShellMode.RegisterContextMenu)
        {
            ContextMenuRegistration.Register(Environment.ProcessPath ?? string.Empty);
            Shutdown();
            return;
        }

        if (shellArgs.Mode == ShellMode.UnregisterContextMenu)
        {
            ContextMenuRegistration.Unregister();
            Shutdown();
            return;
        }

        var window = new MainWindow();
        var viewModel = (MainViewModel)window.DataContext;

        // 右键菜单传入的文件路径会自动加入队列。
        if (shellArgs.ArchivePaths.Count > 0)
        {
            viewModel.AddArchivesFromShell(shellArgs.ArchivePaths, shellArgs.Mode);

            // “解压到此处/解压到文件夹”模式下自动开始。
            if (shellArgs.AutoStart)
            {
                viewModel.StartExtraction();
            }
        }

        window.Show();
    }
}
