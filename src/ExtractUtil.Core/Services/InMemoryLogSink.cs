using ExtractUtil.Core.Enums;
using ExtractUtil.Core.Models;

namespace ExtractUtil.Core.Services;

// 简单的内存日志实现：存储日志并通过事件通知 UI。
public sealed class InMemoryLogSink : ILogSink
{
    private readonly object _gate = new();
    private readonly List<LogEntry> _entries = new();

    public event EventHandler<LogEntry>? EntryAdded;

    // 添加日志条目并触发事件。
    public void Append(LogLevel level, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var entry = new LogEntry(level, message);

        lock (_gate)
        {
            _entries.Add(entry);
        }

        EntryAdded?.Invoke(this, entry);
    }

    // 获取日志快照，供导出使用。
    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_gate)
        {
            return _entries.ToArray();
        }
    }
}
