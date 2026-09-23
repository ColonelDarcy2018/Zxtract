using ExtractUtil.Core.Enums;

namespace ExtractUtil.Core.Models;

// 单条日志记录。
public sealed class LogEntry
{
    public LogEntry(LogLevel level, string message)
    {
        Timestamp = DateTimeOffset.UtcNow;
        Level = level;
        Message = message;
    }

    public DateTimeOffset Timestamp { get; }
    public LogLevel Level { get; }
    public string Message { get; }
}
