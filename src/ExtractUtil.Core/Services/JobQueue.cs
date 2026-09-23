using ExtractUtil.Core.Models;

namespace ExtractUtil.Core.Services;

// 控制并发数量的解压队列，避免同时启动过多进程。
public sealed class JobQueue
{
    private readonly IArchiveExtractor _extractor;
    private readonly ILogSink _log;
    private readonly SemaphoreSlim _semaphore;

    public JobQueue(IArchiveExtractor extractor, ILogSink log, int maxParallel)
    {
        if (maxParallel < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxParallel));
        }

        _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _semaphore = new SemaphoreSlim(maxParallel, maxParallel);
    }

    // 包裹一次解压：先占用并发名额，完成后释放。
    public async Task<ExtractResult> RunAsync(
        ExtractJob job,
        IProgress<ExtractProgress> progress,
        CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await _extractor.ExtractAsync(job, progress, _log, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
