using System.IO;

namespace ExtractUtil.App.Services;

// 来自命令行/右键菜单的启动模式。
public enum ShellMode
{
    None = 0,
    ExtractHere = 1,
    ExtractToFolder = 2,
    OpenInApp = 3,
    RegisterContextMenu = 4,
    UnregisterContextMenu = 5
}

// 解析后的启动参数。
public sealed class ShellArguments
{
    public ShellArguments(ShellMode mode, IReadOnlyList<string> archivePaths, bool autoStart)
    {
        Mode = mode;
        ArchivePaths = archivePaths;
        AutoStart = autoStart;
    }

    public ShellMode Mode { get; }
    public IReadOnlyList<string> ArchivePaths { get; }
    public bool AutoStart { get; }
}

// 解析命令行参数，支持右键菜单传入的模式与路径。
public static class ShellArgumentParser
{
    public static ShellArguments Parse(string[] args)
    {
        var mode = ShellMode.None;
        var paths = new List<string>();

        foreach (var arg in args)
        {
            switch (arg)
            {
                case "--shell-extract-here":
                    mode = ShellMode.ExtractHere;
                    break;
                case "--shell-extract-to-folder":
                    mode = ShellMode.ExtractToFolder;
                    break;
                case "--shell-open":
                    mode = ShellMode.OpenInApp;
                    break;
                case "--register-context-menu":
                    mode = ShellMode.RegisterContextMenu;
                    break;
                case "--unregister-context-menu":
                    mode = ShellMode.UnregisterContextMenu;
                    break;
                default:
                    paths.Add(arg);
                    break;
            }
        }

        // 过滤不存在的路径，避免无效输入。
        var existing = paths.Where(File.Exists).ToList();
        // 右键“直接解压”模式下自动开始。
        var autoStart = mode is ShellMode.ExtractHere or ShellMode.ExtractToFolder;

        return new ShellArguments(mode, existing, autoStart);
    }
}
