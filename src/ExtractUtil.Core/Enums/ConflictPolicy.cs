namespace ExtractUtil.Core.Enums;

// 文件冲突策略（解压时遇到同名文件）。
public enum ConflictPolicy
{
    RenameExisting = 0,
    Overwrite = 1,
    Skip = 2
}
