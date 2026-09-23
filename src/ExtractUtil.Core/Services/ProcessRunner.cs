using System.Diagnostics;

namespace ExtractUtil.Core.Services;

// 执行外部进程并实时读取输出，用于与 7z.exe 交互。
public sealed class ProcessRunner
{
    public async Task<int> RunAsync(
        ProcessStartInfo startInfo,
        Action<string> onStdOut,
        Action<string> onStdErr,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        process.Start();

        // 取消时尽量终止进程，避免僵尸进程残留。
        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch
            {
                // Ignore cancellation races.
            }
        });

        // 独立任务读取标准输出，避免阻塞主线程。
        var stdOutTask = Task.Run(async () =>
        {
            while (!process.StandardOutput.EndOfStream)
            {
                var line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
                if (line is not null)
                {
                    onStdOut(line);
                }
            }
        }, CancellationToken.None);

        // 独立任务读取标准错误，便于捕获失败原因。
        var stdErrTask = Task.Run(async () =>
        {
            while (!process.StandardError.EndOfStream)
            {
                var line = await process.StandardError.ReadLineAsync().ConfigureAwait(false);
                if (line is not null)
                {
                    onStdErr(line);
                }
            }
        }, CancellationToken.None);

        // 同步等待：进程结束 + 输出读取完成。
        await Task.WhenAll(
                process.WaitForExitAsync(cancellationToken),
                stdOutTask,
                stdErrTask)
            .ConfigureAwait(false);

        return process.ExitCode;
    }
}
