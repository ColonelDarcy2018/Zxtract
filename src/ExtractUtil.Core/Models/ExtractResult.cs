using ExtractUtil.Core.Enums;

namespace ExtractUtil.Core.Models;

// 解压结果对象：用于汇总是否成功与错误信息。
public sealed class ExtractResult
{
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? WarningMessage { get; init; }
    public ExtractFailureKind FailureKind { get; init; }
}
