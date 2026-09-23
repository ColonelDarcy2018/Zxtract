namespace ExtractUtil.Core.Models;

// 进度回报对象：用于 UI 展示。
public sealed class ExtractProgress
{
    public int Percentage { get; init; }
    public string? CurrentFile { get; init; }
    public string? RawLine { get; init; }
}
