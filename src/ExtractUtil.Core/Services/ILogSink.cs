using ExtractUtil.Core.Enums;

namespace ExtractUtil.Core.Services;

// 日志汇聚接口，UI 或其他模块可订阅日志输出。
public interface ILogSink
{
    void Append(LogLevel level, string message);
}
