using ExtractUtil.Core.Models;

namespace ExtractUtil.Core.Services;

// 抽象的解压接口，便于替换不同的解压引擎。
public interface IArchiveExtractor
{
    Task<ExtractResult> ExtractAsync(
        ExtractJob job,
        IProgress<ExtractProgress> progress,
        ILogSink log,
        CancellationToken cancellationToken);
}
