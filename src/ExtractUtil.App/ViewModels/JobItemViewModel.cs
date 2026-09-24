using System.IO;
using ExtractUtil.Core.Enums;
using ExtractUtil.Core.Models;

namespace ExtractUtil.App.ViewModels;

// 任务行同时承载扫描阶段信息和传统单文件任务信息。
public sealed class JobItemViewModel : ObservableObject
{
    private ExtractJobStatus _status;
    private ArchiveTreePhase _phase;
    private int _progressPercent;
    private string _currentFile = string.Empty;
    private string _message = string.Empty;
    private int _passwordAttempt;
    private int _passwordAttemptCount;
    private int _discoveryPass;

    public JobItemViewModel(ArchiveWorkItem item, int discoveryPass = 0, int nestingDepth = 0, Guid? parentId = null)
    {
        WorkItem = item ?? throw new ArgumentNullException(nameof(item));
        _status = ExtractJobStatus.Queued;
        _phase = item.CanExtract ? ArchiveTreePhase.Discovered : ArchiveTreePhase.Blocked;
        _discoveryPass = discoveryPass;
        NestingDepth = nestingDepth;
        ParentId = parentId;
    }

    // 兼容资源管理器右键菜单和旧的“添加文件”工作流。
    public JobItemViewModel(ExtractJob job)
    {
        Job = job ?? throw new ArgumentNullException(nameof(job));
        _status = job.Status;
        _phase = job.Status == ExtractJobStatus.Queued ? ArchiveTreePhase.Queued : ArchiveTreePhase.Discovered;
    }

    public ExtractJob? Job { get; }
    public ArchiveWorkItem? WorkItem { get; }
    public bool IsTreeItem => WorkItem is not null;

    public string Key => WorkItem is not null
        ? string.Join("|", WorkItem.SourceParts.Select(part => Path.GetFullPath(part.Path)).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        : Path.GetFullPath(Job!.ArchivePath);

    public string ArchivePath => WorkItem?.EntryPath ?? Job!.ArchivePath;
    public string DisplayName => WorkItem?.DisplayName ?? Job!.DisplayName;
    public string RelativeDirectory => WorkItem?.RelativeDirectory ?? (Path.GetDirectoryName(ArchivePath) ?? string.Empty);
    public string ArchiveKind => WorkItem?.Kind.ToString() ?? "Regular";
    public string VolumeSummary => WorkItem is null
        ? "1"
        : WorkItem.SourceParts.Count == 1
            ? "单文件"
            : $"{WorkItem.SourceParts.Count} 卷" + (WorkItem.IsCrossDirectoryVolumeSet ? " · 跨目录" : string.Empty);
    public int VolumeCount => WorkItem?.SourceParts.Count ?? 1;
    public IReadOnlyList<string> VolumePaths => WorkItem?.SourceParts.Select(part => part.Path).ToArray() ?? new[] { ArchivePath };
    public bool IsDistributed => WorkItem?.IsCrossDirectoryVolumeSet ?? false;
    public bool CanExtract => WorkItem?.CanExtract ?? true;
    public string BlockingReasons => WorkItem is null ? string.Empty : string.Join("；", WorkItem.BlockingReasons);
    public string OutputDirectory => WorkItem?.OutputDirectory ?? Job!.Options.OutputDirectory;

    public int DiscoveryPass
    {
        get => _discoveryPass;
        set => SetProperty(ref _discoveryPass, value);
    }

    public int NestingDepth { get; }
    public Guid? ParentId { get; }

    public ArchiveTreePhase Phase
    {
        get => _phase;
        private set
        {
            if (SetProperty(ref _phase, value))
            {
                NotifyPresentationProperties();
            }
        }
    }

    public ExtractJobStatus Status
    {
        get => _status;
        private set
        {
            if (SetProperty(ref _status, value))
            {
                NotifyPresentationProperties();
            }
        }
    }

    public int ProgressPercent
    {
        get => _progressPercent;
        private set => SetProperty(ref _progressPercent, value);
    }

    public string CurrentFile
    {
        get => _currentFile;
        private set => SetProperty(ref _currentFile, value);
    }

    public string Message
    {
        get => _message;
        private set
        {
            if (SetProperty(ref _message, value))
            {
                OnPropertyChanged(nameof(StatusDetail));
                OnPropertyChanged(nameof(RetryRequiresPassword));
                OnPropertyChanged(nameof(RetryActionText));
            }
        }
    }

    public bool IsRunning => Status == ExtractJobStatus.Running;
    public bool IsCompleted => Status == ExtractJobStatus.Completed;
    public bool NeedsAttention => !CanExtract || Phase == ArchiveTreePhase.Blocked ||
                                  Status is ExtractJobStatus.Failed or ExtractJobStatus.Canceled;
    public bool CanRetry => Status is ExtractJobStatus.Failed or ExtractJobStatus.Canceled ||
                            Phase == ArchiveTreePhase.Blocked;
    public bool RetryRequiresPassword => Status == ExtractJobStatus.Failed &&
        (Message.Contains("密码", StringComparison.OrdinalIgnoreCase) ||
         Message.Contains("password", StringComparison.OrdinalIgnoreCase));

    public string StatusText => Phase switch
    {
        ArchiveTreePhase.TryingPassword => "正在尝试密码",
        ArchiveTreePhase.Staging => "正在整理分卷",
        ArchiveTreePhase.Extracting => Status == ExtractJobStatus.Running ? "正在解压" : Status.ToString(),
        ArchiveTreePhase.Committing => "正在写入文件",
        ArchiveTreePhase.Completed => "已完成",
        ArchiveTreePhase.Canceled => "已取消",
        ArchiveTreePhase.Blocked => "需处理",
        ArchiveTreePhase.SkippedCycle => "已跳过循环",
        ArchiveTreePhase.SkippedLimit => "已达到上限",
        ArchiveTreePhase.Failed => "解压失败",
        _ => Status switch
        {
            ExtractJobStatus.Queued => "等待开始",
            ExtractJobStatus.Running => "处理中",
            ExtractJobStatus.Completed => "已完成",
            ExtractJobStatus.Failed => "解压失败",
            ExtractJobStatus.Canceled => "已取消",
            _ => Status.ToString()
        }
    };

    public string SourceSummary => WorkItem is null
        ? ArchivePath
        : WorkItem.SourceParts.Count > 1
            ? $"{VolumeSummary} · {RelativeDirectory}"
            : ArchivePath;

    public string StatusDetail => string.IsNullOrWhiteSpace(Message)
        ? ProgressPercent > 0 && ProgressPercent < 100 ? $"{ProgressPercent}%" : string.Empty
        : Message;

    public string RetryActionText => Phase == ArchiveTreePhase.Blocked
        ? "重新扫描并解压"
        : Status == ExtractJobStatus.Canceled
            ? "重试"
            : RetryRequiresPassword
                ? "修改密码并重试"
                : "重试";

    private void NotifyPresentationProperties()
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(NeedsAttention));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(RetryRequiresPassword));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusDetail));
        OnPropertyChanged(nameof(RetryActionText));
    }

    public int PasswordAttempt
    {
        get => _passwordAttempt;
        private set => SetProperty(ref _passwordAttempt, value);
    }

    public int PasswordAttemptCount
    {
        get => _passwordAttemptCount;
        private set => SetProperty(ref _passwordAttemptCount, value);
    }

    public void ApplyTreeProgress(ArchiveTreeProgress progress)
    {
        Phase = progress.Phase;
        if (!string.IsNullOrWhiteSpace(progress.Message))
        {
            Message = progress.Message;
        }

        if (progress.PasswordAttemptCount > 0)
        {
            PasswordAttempt = progress.PasswordAttempt;
            PasswordAttemptCount = progress.PasswordAttemptCount;
        }

        if (progress.ExtractionProgress is { } extraction)
        {
            ProgressPercent = Math.Clamp(extraction.Percentage, 0, 100);
            CurrentFile = extraction.CurrentFile ?? string.Empty;
        }

        Status = progress.Phase switch
        {
            ArchiveTreePhase.Completed => ExtractJobStatus.Completed,
            ArchiveTreePhase.Failed => ExtractJobStatus.Failed,
            ArchiveTreePhase.Canceled => ExtractJobStatus.Canceled,
            ArchiveTreePhase.Blocked or ArchiveTreePhase.SkippedLimit or ArchiveTreePhase.SkippedCycle => ExtractJobStatus.Queued,
            ArchiveTreePhase.Queued or ArchiveTreePhase.Discovered => ExtractJobStatus.Queued,
            _ => ExtractJobStatus.Running
        };

        if (progress.Phase == ArchiveTreePhase.Completed)
        {
            ProgressPercent = 100;
        }
    }

    public void ApplyLegacyProgress(ExtractProgress progress)
    {
        ProgressPercent = Math.Clamp(progress.Percentage, 0, 100);
        CurrentFile = progress.CurrentFile ?? string.Empty;
        Phase = ArchiveTreePhase.Extracting;
        Status = ExtractJobStatus.Running;
    }

    public void SetLegacyState(ExtractJobStatus status, ArchiveTreePhase phase, string message)
    {
        Status = status;
        Phase = phase;
        Message = message;
        if (Job is not null)
        {
            Job.Status = status;
            if (status == ExtractJobStatus.Running)
            {
                Job.StartedAt ??= DateTimeOffset.UtcNow;
            }
            else if (status is ExtractJobStatus.Completed or ExtractJobStatus.Failed or ExtractJobStatus.Canceled)
            {
                Job.CompletedAt = DateTimeOffset.UtcNow;
            }
        }

        if (status == ExtractJobStatus.Completed)
        {
            ProgressPercent = 100;
        }
    }

    public void ResetForRetry()
    {
        if (Job is null)
        {
            throw new InvalidOperationException("Only ordinary file tasks can be retried directly.");
        }

        Job.Status = ExtractJobStatus.Queued;
        Job.StartedAt = null;
        Job.CompletedAt = null;
        Status = ExtractJobStatus.Queued;
        Phase = ArchiveTreePhase.Queued;
        ProgressPercent = 0;
        CurrentFile = string.Empty;
        Message = "等待重试";
        PasswordAttempt = 0;
        PasswordAttemptCount = 0;
    }
}
