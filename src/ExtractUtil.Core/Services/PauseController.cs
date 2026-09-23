namespace ExtractUtil.Core.Services;

// 协作式暂停门：正在运行的 7-Zip 会自然完成，新的任务、扫描和密码尝试会等待继续。
public sealed class PauseController
{
    private readonly object _sync = new();
    private TaskCompletionSource<bool> _resumeSignal = CreateCompletedSignal();
    private bool _isPaused;

    public event EventHandler<bool>? StateChanged;

    public bool IsPaused
    {
        get
        {
            lock (_sync)
            {
                return _isPaused;
            }
        }
    }

    public void Pause()
    {
        var changed = false;

        lock (_sync)
        {
            if (!_isPaused)
            {
                _isPaused = true;
                _resumeSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                changed = true;
            }
        }

        if (changed)
        {
            StateChanged?.Invoke(this, true);
        }
    }

    public void Resume()
    {
        TaskCompletionSource<bool>? signal = null;

        lock (_sync)
        {
            if (_isPaused)
            {
                _isPaused = false;
                signal = _resumeSignal;
            }
        }

        if (signal is not null)
        {
            signal.TrySetResult(true);
            StateChanged?.Invoke(this, false);
        }
    }

    public async Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        Task waitTask;

        lock (_sync)
        {
            if (!_isPaused)
            {
                return;
            }

            waitTask = _resumeSignal.Task;
        }

        await waitTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static TaskCompletionSource<bool> CreateCompletedSignal()
    {
        var signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        signal.SetResult(true);
        return signal;
    }
}
