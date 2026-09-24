using Microsoft.Win32;

namespace ExtractUtil.App.Services;

// 通过写入 HKCU 注册表为资源管理器添加右键菜单。
public static class ContextMenuRegistration
{
    private static readonly string[] SupportedExtensions =
    {
        ".zip",
        ".7z",
        ".001",
        ".rar",
        ".tar",
        ".gz",
        ".tgz",
        ".bz2",
        ".xz",
        ".tar.gz",
        ".tar.bz2",
        ".tar.xz"
    };

    // 注册菜单项：解压到此处 / 解压到同名文件夹 / 在应用中打开。
    public static void Register(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
        {
            throw new ArgumentException("Executable path is required.", nameof(exePath));
        }

        foreach (var extension in SupportedExtensions)
        {
            var shellRoot = $@"Software\Classes\SystemFileAssociations\{extension}\shell";

            DeleteMenuItem(shellRoot, "ExtractUtil.ExtractHere");
            DeleteMenuItem(shellRoot, "ExtractUtil.ExtractToFolder");
            DeleteMenuItem(shellRoot, "ExtractUtil.OpenInApp");
            CreateMenuItem(shellRoot, "Zxtract.ExtractHere", "Zxtract：解压到此处", exePath, "--shell-extract-here");
            CreateMenuItem(shellRoot, "Zxtract.ExtractToFolder", "Zxtract：解压到同名文件夹", exePath, "--shell-extract-to-folder");
            CreateMenuItem(shellRoot, "Zxtract.OpenInApp", "使用 Zxtract 打开", exePath, "--shell-open");
        }
    }

    // 移除菜单项（仅影响当前用户）。
    public static void Unregister()
    {
        foreach (var extension in SupportedExtensions)
        {
            var shellRoot = $@"Software\Classes\SystemFileAssociations\{extension}\shell";

            DeleteMenuItem(shellRoot, "ExtractUtil.ExtractHere");
            DeleteMenuItem(shellRoot, "ExtractUtil.ExtractToFolder");
            DeleteMenuItem(shellRoot, "ExtractUtil.OpenInApp");
            DeleteMenuItem(shellRoot, "Zxtract.ExtractHere");
            DeleteMenuItem(shellRoot, "Zxtract.ExtractToFolder");
            DeleteMenuItem(shellRoot, "Zxtract.OpenInApp");
        }
    }

    // 生成单个菜单项以及其 command 子键。
    private static void CreateMenuItem(string shellRoot, string keyName, string title, string exePath, string args)
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"{shellRoot}\{keyName}");
        key?.SetValue("MUIVerb", title);
        key?.SetValue("Icon", exePath);
        // 允许多选文件时只触发一次命令（%1 仍为当前文件）。
        key?.SetValue("MultiSelectModel", "Player");

        using var commandKey = key?.CreateSubKey("command");
        commandKey?.SetValue(string.Empty, $"\"{exePath}\" {args} \"%1\"");
    }

    private static void DeleteMenuItem(string shellRoot, string keyName)
    {
        var keyPath = $@"{shellRoot}\{keyName}";
        Registry.CurrentUser.DeleteSubKeyTree(keyPath, false);
    }
}
