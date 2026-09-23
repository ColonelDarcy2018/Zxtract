using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using ExtractUtil.Core.Enums;
using ExtractUtil.Core.Models;

namespace ExtractUtil.Core.Services;

// 管理一次“扫描 -> 解压 -> 扫描本次产物”的动态会话。
public sealed class ArchiveTreeCoordinator
{
    private readonly IArchiveScanner _scanner;
    private readonly IArchiveExtractor _extractor;
    private readonly ArchiveVolumeStager _stager;
    private readonly OutputCommitter _committer;
    private readonly ILogSink _log;

    public ArchiveTreeCoordinator(
        IArchiveScanner scanner,
        IArchiveExtractor extractor,
        ArchiveVolumeStager stager,
        OutputCommitter committer,
        ILogSink log)
    {
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
        _stager = stager ?? throw new ArgumentNullException(nameof(stager));
        _committer = committer ?? throw new ArgumentNullException(nameof(committer));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task<ArchiveTreeRunResult> RunAsync(
        ArchiveTreeRunOptions options,
        PauseController pauseController,
        IProgress<ArchiveTreeProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(pauseController);
        ValidateOptions(options);

        var root = Path.GetFullPath(options.RootDirectory);
        var runId = Guid.NewGuid().ToString("N");
        var runsRoot = Path.Combine(root, ".extractutil_work", "runs");
        var runRoot = Path.Combine(runsRoot, runId);
        var stagingRoot = Path.Combine(runRoot, "staging");
        var attemptsRoot = Path.Combine(runRoot, "attempts");
        Directory.CreateDirectory(stagingRoot);
        Directory.CreateDirectory(attemptsRoot);

        var outcomes = new ConcurrentBag<ArchiveTaskOutcome>();
        var scheduled = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        var successfulPasswords = new ConcurrentQueue<string>();
        var successfulPasswordSet = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        var wasCanceled = false;
        ArchiveScanResult? initialScan = null;

        try
        {
            await pauseController.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);
            initialScan = await ScanAsync(CreateScanOptions(options, root, nested: false), 0, progress, cancellationToken)
                .ConfigureAwait(false);

            var channel = Channel.CreateUnbounded<WorkEnvelope>(new UnboundedChannelOptions
            {
                SingleReader = options.MaxParallelJobs == 1,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

            var outstanding = 0;
            var acceptedTasks = 0;

            void QueueItems(
                IEnumerable<ArchiveWorkItem> items,
                int depth,
                Guid? parentId,
                IReadOnlySet<string> ancestorFingerprints)
            {
                foreach (var item in items)
                {
                    Report(progress, ArchiveTreePhase.Discovered, item, depth, "已发现压缩任务");

                    if (!item.CanExtract)
                    {
                        var reason = item.BlockingReasons.Count == 0
                            ? "任务不可解压"
                            : string.Join("；", item.BlockingReasons);
                        outcomes.Add(new ArchiveTaskOutcome
                        {
                            Item = item,
                            Phase = ArchiveTreePhase.Blocked,
                            Depth = depth,
                            Message = reason
                        });
                        Report(progress, ArchiveTreePhase.Blocked, item, depth, reason);
                        continue;
                    }

                    var fingerprint = BuildContentFingerprint(item);
                    if (ancestorFingerprints.Contains(fingerprint))
                    {
                        const string reason = "检测到与父级链相同的压缩内容，已跳过以避免递归循环";
                        outcomes.Add(new ArchiveTaskOutcome
                        {
                            Item = item,
                            Phase = ArchiveTreePhase.SkippedCycle,
                            Depth = depth,
                            Message = reason
                        });
                        Report(progress, ArchiveTreePhase.SkippedCycle, item, depth, reason);
                        continue;
                    }

                    var identity = BuildSourceIdentity(item);
                    if (!scheduled.TryAdd(identity, 0))
                    {
                        continue;
                    }

                    var taskNumber = Interlocked.Increment(ref acceptedTasks);
                    if (taskNumber > options.MaxTasks)
                    {
                        outcomes.Add(new ArchiveTaskOutcome
                        {
                            Item = item,
                            Phase = ArchiveTreePhase.SkippedLimit,
                            Depth = depth,
                            Message = $"达到任务上限 {options.MaxTasks}"
                        });
                        Report(progress, ArchiveTreePhase.SkippedLimit, item, depth, $"达到任务上限 {options.MaxTasks}");
                        continue;
                    }

                    Interlocked.Increment(ref outstanding);
                    if (!channel.Writer.TryWrite(new WorkEnvelope(
                            item,
                            depth,
                            parentId,
                            fingerprint,
                            ancestorFingerprints)))
                    {
                        Interlocked.Decrement(ref outstanding);
                        throw new InvalidOperationException("Unable to queue archive work item.");
                    }

                    Report(progress, ArchiveTreePhase.Queued, item, depth, "等待解压");
                }
            }

            QueueItems(initialScan.Items, 0, null, new HashSet<string>(StringComparer.Ordinal));
            if (Volatile.Read(ref outstanding) == 0)
            {
                channel.Writer.TryComplete();
            }

            async Task WorkerAsync()
            {
                await foreach (var envelope in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    try
                    {
                        await pauseController.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);
                        var outcome = await ExecuteItemAsync(
                                envelope,
                                options,
                                pauseController,
                                successfulPasswords,
                                successfulPasswordSet,
                                stagingRoot,
                                attemptsRoot,
                                progress,
                                cancellationToken)
                            .ConfigureAwait(false);
                        outcomes.Add(outcome);

                        if (outcome.Phase == ArchiveTreePhase.Completed &&
                            options.Recursive &&
                            envelope.Depth < options.MaxDepth &&
                            Directory.Exists(envelope.Item.OutputDirectory))
                        {
                            await pauseController.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);
                            var nestedScan = await ScanAsync(
                                    CreateScanOptions(options, envelope.Item.OutputDirectory, nested: true),
                                    envelope.Depth + 1,
                                    progress,
                                    cancellationToken)
                                .ConfigureAwait(false);
                            var nestedAncestors = new HashSet<string>(envelope.AncestorFingerprints, StringComparer.Ordinal)
                            {
                                envelope.ContentFingerprint
                            };
                            QueueItems(nestedScan.Items, envelope.Depth + 1, envelope.Item.Id, nestedAncestors);
                        }
                        else if (outcome.Phase == ArchiveTreePhase.Completed &&
                                 options.Recursive &&
                                 envelope.Depth >= options.MaxDepth)
                        {
                            Report(progress, ArchiveTreePhase.SkippedLimit, envelope.Item, envelope.Depth,
                                $"已达到递归深度上限 {options.MaxDepth}");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        wasCanceled = true;
                        Report(progress, ArchiveTreePhase.Canceled, envelope.Item, envelope.Depth, "已取消");
                        throw;
                    }
                    catch (Exception ex)
                    {
                        outcomes.Add(new ArchiveTaskOutcome
                        {
                            Item = envelope.Item,
                            Phase = ArchiveTreePhase.Failed,
                            Depth = envelope.Depth,
                            ExitCode = -1,
                            Message = ex.Message
                        });
                        _log.Append(LogLevel.Error, $"Task failed: {envelope.Item.DisplayName}; {ex.Message}");
                        Report(progress, ArchiveTreePhase.Failed, envelope.Item, envelope.Depth, ex.Message);
                    }
                    finally
                    {
                        if (Interlocked.Decrement(ref outstanding) == 0)
                        {
                            channel.Writer.TryComplete();
                        }
                    }
                }
            }

            var workers = Enumerable.Range(0, Math.Max(1, options.MaxParallelJobs))
                .Select(_ => WorkerAsync())
                .ToArray();

            try
            {
                await Task.WhenAll(workers).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                wasCanceled = true;
                channel.Writer.TryComplete();
            }
        }
        catch (OperationCanceledException)
        {
            wasCanceled = true;
        }
        finally
        {
            ArchiveVolumeStager.SafeDeleteOwnedDirectory(runRoot, runsRoot);
        }

        return new ArchiveTreeRunResult
        {
            InitialScan = initialScan ?? EmptyScanResult(root),
            Outcomes = outcomes.OrderBy(item => item.Depth).ThenBy(item => item.Item.EntryPath).ToArray(),
            WasCanceled = wasCanceled
        };
    }

    private async Task<ArchiveTaskOutcome> ExecuteItemAsync(
        WorkEnvelope envelope,
        ArchiveTreeRunOptions sessionOptions,
        PauseController pauseController,
        ConcurrentQueue<string> successfulPasswords,
        ConcurrentDictionary<string, byte> successfulPasswordSet,
        string stagingRoot,
        string attemptsRoot,
        IProgress<ArchiveTreeProgress>? progress,
        CancellationToken cancellationToken)
    {
        var item = envelope.Item;
        await using var staged = await _stager.StageIfNeededAsync(item, stagingRoot, cancellationToken)
            .ConfigureAwait(false);

        if (staged.WasStaged)
        {
            var mode = staged.UsedCopyFallback ? "复制" : "硬链接";
            _log.Append(LogLevel.Info, $"Staged {item.SourceParts.Count} volume(s) using {mode}: {item.DisplayName}");
            Report(progress, ArchiveTreePhase.Staging, item, envelope.Depth, $"已用{mode}临时归并 {item.SourceParts.Count} 个分卷");
        }

        var resolvedPasswords = PasswordCandidateResolver.Resolve(
            item.EntryPath,
            sessionOptions.PasswordCandidates,
            sessionOptions.InferPasswordsFromPath,
            successfulPasswords.ToArray(),
            sessionOptions.CustomPasswordInferenceRules);
        var attempts = resolvedPasswords.Count == 0
            ? new string?[] { null }
            : resolvedPasswords.Cast<string?>().ToArray();

        ExtractResult? lastResult = null;

        for (var attemptIndex = 0; attemptIndex < attempts.Length; attemptIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await pauseController.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);

            var attemptNumber = attemptIndex + 1;
            var attemptDirectory = Path.Combine(attemptsRoot, item.Id.ToString("N"), attemptNumber.ToString());
            Directory.CreateDirectory(attemptDirectory);
            Report(progress, ArchiveTreePhase.TryingPassword, item, envelope.Depth,
                $"密码尝试 {attemptNumber}/{attempts.Length}", attemptNumber, attempts.Length);
            _log.Append(LogLevel.Info, $"Password attempt {attemptNumber}/{attempts.Length}: {item.DisplayName}");

            try
            {
                var runJob = new ExtractJob(staged.InputPath, new ExtractOptions
                {
                    OutputDirectory = attemptDirectory,
                    ConflictPolicy = ConflictPolicy.Overwrite,
                    Password = attempts[attemptIndex],
                    EnableMultiThread = sessionOptions.EnableMultiThread,
                    DeleteSourceAfterSuccess = false
                });
                var itemProgress = new Progress<ExtractProgress>(value =>
                {
                    Report(progress, ArchiveTreePhase.Extracting, item, envelope.Depth,
                        value.CurrentFile ?? "正在解压", attemptNumber, attempts.Length, value);
                });

                Report(progress, ArchiveTreePhase.Extracting, item, envelope.Depth,
                    "正在解压到隔离目录", attemptNumber, attempts.Length);
                lastResult = await _extractor.ExtractAsync(runJob, itemProgress, _log, cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                if (!lastResult.Success)
                {
                    _log.Append(LogLevel.Warning,
                        $"Attempt {attemptNumber}/{attempts.Length} failed ({lastResult.FailureKind}): {item.DisplayName}");
                    if (!CanRetryWithAnotherPassword(lastResult.FailureKind))
                    {
                        break;
                    }

                    continue;
                }

                await pauseController.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);
                Report(progress, ArchiveTreePhase.Committing, item, envelope.Depth, "正在提交已验证的解压结果",
                    attemptNumber, attempts.Length);
                await _committer.CommitAsync(
                        attemptDirectory,
                        item.OutputDirectory,
                        sessionOptions.ConflictPolicy,
                        cancellationToken)
                    .ConfigureAwait(false);

                var password = attempts[attemptIndex];
                if (!string.IsNullOrEmpty(password) && successfulPasswordSet.TryAdd(password, 0))
                {
                    successfulPasswords.Enqueue(password);
                }

                var deleteWarning = sessionOptions.DeleteSourceAfterSuccess && lastResult.ExitCode == 0
                    ? DeleteSources(item)
                    : null;
                var message = lastResult.ExitCode == 1 ? "解压完成（7-Zip 有警告）" : "解压完成";
                if (!string.IsNullOrWhiteSpace(deleteWarning))
                {
                    message += $"；{deleteWarning}";
                }

                _log.Append(lastResult.ExitCode == 1 ? LogLevel.Warning : LogLevel.Info,
                    $"Completed: {item.DisplayName}");
                Report(progress, ArchiveTreePhase.Completed, item, envelope.Depth, message,
                    attemptNumber, attempts.Length);
                return new ArchiveTaskOutcome
                {
                    Item = item,
                    Phase = ArchiveTreePhase.Completed,
                    Depth = envelope.Depth,
                    ExitCode = lastResult.ExitCode,
                    PasswordAttempts = attemptNumber,
                    Message = message
                };
            }
            finally
            {
                var itemAttemptsRoot = Path.Combine(attemptsRoot, item.Id.ToString("N"));
                ArchiveVolumeStager.SafeDeleteOwnedDirectory(attemptDirectory, itemAttemptsRoot);
            }
        }

        var error = SummarizeError(lastResult?.ErrorMessage, lastResult?.FailureKind ?? ExtractFailureKind.Unknown);
        _log.Append(LogLevel.Error, $"Failed after {attempts.Length} attempt(s): {item.DisplayName}; {error}");
        Report(progress, ArchiveTreePhase.Failed, item, envelope.Depth, error, attempts.Length, attempts.Length);
        return new ArchiveTaskOutcome
        {
            Item = item,
            Phase = ArchiveTreePhase.Failed,
            Depth = envelope.Depth,
            ExitCode = lastResult?.ExitCode ?? -1,
            PasswordAttempts = attempts.Length,
            Message = error
        };
    }

    private async Task<ArchiveScanResult> ScanAsync(
        ArchiveScanOptions scanOptions,
        int depth,
        IProgress<ArchiveTreeProgress>? progress,
        CancellationToken cancellationToken)
    {
        Report(progress, ArchiveTreePhase.Scanning, null, depth, $"正在扫描：{scanOptions.RootDirectory}");
        var scanProgress = new Progress<ArchiveScanProgress>(value =>
        {
            progress?.Report(new ArchiveTreeProgress
            {
                Phase = ArchiveTreePhase.Scanning,
                Message = value.IsGrouping ? "正在整理分卷" : value.CurrentPath ?? string.Empty,
                Depth = depth,
                FilesScanned = value.FilesScanned,
                DirectoriesScanned = value.DirectoriesScanned
            });
        });

        return await _scanner.ScanAsync(scanOptions, scanProgress, cancellationToken).ConfigureAwait(false);
    }

    private static ArchiveScanOptions CreateScanOptions(
        ArchiveTreeRunOptions options,
        string scanRoot,
        bool nested)
    {
        var outputMode = nested && options.OutputMode == ArchiveOutputMode.CustomRoot
            ? ArchiveOutputMode.ArchiveSubdirectory
            : options.OutputMode;

        return new ArchiveScanOptions
        {
            RootDirectory = scanRoot,
            OutputMode = outputMode,
            CustomOutputRoot = outputMode == ArchiveOutputMode.CustomRoot ? options.CustomOutputRoot : null,
            RecurseSubdirectories = true,
            EnableSiblingVolumeGrouping = options.EnableSiblingVolumeGrouping,
            PreserveRelativeDirectories = options.PreserveRelativeDirectories,
            ProgressReportInterval = 250
        };
    }

    private string? DeleteSources(ArchiveWorkItem item)
    {
        var failed = 0;
        foreach (var part in item.SourceParts)
        {
            try
            {
                if (File.Exists(part.Path))
                {
                    File.Delete(part.Path);
                    _log.Append(LogLevel.Info, $"Deleted source archive: {part.Path}");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed++;
                _log.Append(LogLevel.Warning, $"Failed to delete source archive {part.Path}: {ex.Message}");
            }
        }

        return failed == 0 ? null : $"{failed} 个源文件删除失败";
    }

    private static bool CanRetryWithAnotherPassword(ExtractFailureKind failureKind)
    {
        return failureKind is ExtractFailureKind.WrongPassword
            or ExtractFailureKind.CorruptArchive
            or ExtractFailureKind.Unknown;
    }

    private static string BuildSourceIdentity(ArchiveWorkItem item)
    {
        return string.Join("|", item.SourceParts
            .Select(part => Path.GetFullPath(part.Path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
    }

    // Full hashing a 25 GiB volume set would make discovery unnecessarily slow. A length plus
    // first/last 64 KiB fingerprint is sufficient for ancestor-loop protection; it is never used
    // to suppress equivalent archives in unrelated branches.
    private static string BuildContentFingerprint(ArchiveWorkItem item)
    {
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var part in item.SourceParts.OrderBy(part => part.VolumeNumber).ThenBy(part => part.Path, StringComparer.OrdinalIgnoreCase))
            {
                var header = Encoding.UTF8.GetBytes($"{part.VolumeNumber}:{part.Length}:");
                hash.AppendData(header);
                using var stream = new FileStream(part.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                AppendSample(hash, stream, 64 * 1024);
                if (stream.Length > 64 * 1024)
                {
                    stream.Seek(Math.Max(0, stream.Length - 64 * 1024), SeekOrigin.Begin);
                    AppendSample(hash, stream, 64 * 1024);
                }
            }

            return Convert.ToHexString(hash.GetHashAndReset());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "PATH:" + BuildSourceIdentity(item);
        }
    }

    private static void AppendSample(IncrementalHash hash, Stream stream, int maximumBytes)
    {
        var buffer = new byte[16 * 1024];
        var remaining = maximumBytes;
        while (remaining > 0)
        {
            var read = stream.Read(buffer, 0, Math.Min(buffer.Length, remaining));
            if (read <= 0)
            {
                break;
            }

            hash.AppendData(buffer, 0, read);
            remaining -= read;
        }
    }

    private static string SummarizeError(string? message, ExtractFailureKind failureKind)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return failureKind.ToString();
        }

        var meaningful = message.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .LastOrDefault(line =>
                line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("archive", StringComparison.OrdinalIgnoreCase))
            ?? message.Trim();

        return meaningful.Length <= 300 ? meaningful : meaningful[..300] + "…";
    }

    private static void Report(
        IProgress<ArchiveTreeProgress>? progress,
        ArchiveTreePhase phase,
        ArchiveWorkItem? item,
        int depth,
        string message,
        int passwordAttempt = 0,
        int passwordAttemptCount = 0,
        ExtractProgress? extractionProgress = null)
    {
        progress?.Report(new ArchiveTreeProgress
        {
            Phase = phase,
            Item = item,
            Depth = depth,
            Message = message,
            PasswordAttempt = passwordAttempt,
            PasswordAttemptCount = passwordAttemptCount,
            ExtractionProgress = extractionProgress
        });
    }

    private static void ValidateOptions(ArchiveTreeRunOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RootDirectory) || !Directory.Exists(options.RootDirectory))
        {
            throw new DirectoryNotFoundException(options.RootDirectory);
        }

        if (options.OutputMode == ArchiveOutputMode.CustomRoot && string.IsNullOrWhiteSpace(options.CustomOutputRoot))
        {
            throw new ArgumentException("Custom output root is required.", nameof(options));
        }

        if (options.MaxDepth < 0 || options.MaxTasks < 1 || options.MaxParallelJobs < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }
    }

    private static ArchiveScanResult EmptyScanResult(string root)
    {
        var now = DateTimeOffset.UtcNow;
        return new ArchiveScanResult
        {
            RootDirectory = root,
            StartedAt = now,
            CompletedAt = now,
            Items = Array.Empty<ArchiveWorkItem>(),
            Warnings = Array.Empty<string>()
        };
    }

    private sealed record WorkEnvelope(
        ArchiveWorkItem Item,
        int Depth,
        Guid? ParentId,
        string ContentFingerprint,
        IReadOnlySet<string> AncestorFingerprints);
}
