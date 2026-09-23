using ExtractUtil.Core.Enums;

namespace ExtractUtil.Core.Models;

// 单次解压的选项配置。
public sealed class ExtractOptions
{
    public string OutputDirectory { get; init; } = string.Empty;
    public ConflictPolicy ConflictPolicy { get; init; } = ConflictPolicy.RenameExisting;
    public string? Password { get; init; }
    public bool EnableMultiThread { get; init; } = true;
    public bool DeleteSourceAfterSuccess { get; init; }
}
