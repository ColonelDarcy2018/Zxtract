using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using ExtractUtil.App.ViewModels;
using ExtractUtil.Core.Enums;

namespace ExtractUtil.App.Presentation;

/// <summary>
/// Read-only presentation of the selected workflow. It never dispatches work or
/// changes the task collection, filters, passwords, or extraction settings.
/// </summary>
public sealed class LunarStatusPresenter : INotifyPropertyChanged, IDisposable
{
    private readonly MainViewModel _viewModel;
    private readonly HashSet<JobItemViewModel> _subscribedJobs = new();
    private bool _disposed;
    private double _progress;
    private bool _isIndeterminate;
    private string _title = string.Empty;
    private string _detail = string.Empty;
    private string _tooltipDetails = string.Empty;
    private string _optionsSummary = string.Empty;

    public LunarStatusPresenter(MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        _viewModel.PropertyChanged += OnViewModelChanged;
        _viewModel.Jobs.CollectionChanged += OnJobsChanged;
        SynchronizeSubscriptions();
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public bool IsIndeterminate { get => _isIndeterminate; private set => Set(ref _isIndeterminate, value); }
    public string Title { get => _title; private set => Set(ref _title, value); }
    public string Detail { get => _detail; private set => Set(ref _detail, value); }
    public string TooltipDetails { get => _tooltipDetails; private set => Set(ref _tooltipDetails, value); }
    public string OptionsSummary { get => _optionsSummary; private set => Set(ref _optionsSummary, value); }
    public string SourceLabel => _viewModel.SelectedWorkflowIndex == 1 ? _viewModel.ScanRootDisplay : "压缩文件队列 · 支持拖入文件";
    public string SummaryMain => $"{_viewModel.OutputModeDisplay} · 密码：{_viewModel.PasswordSummary} · " +
        (_viewModel.SelectedWorkflowIndex == 1 && _viewModel.DeleteSourceAfterSuccess ? "成功后删除原包" : "保留原包");
    public string SummarySecondary => (_viewModel.SelectedConflictPolicy switch
    {
        ConflictPolicy.RenameExisting => "重命名已有文件",
        ConflictPolicy.Overwrite => "覆盖同名文件",
        ConflictPolicy.Skip => "跳过同名文件",
        _ => "未知冲突策略"
    }) + $" · {_viewModel.MaxParallelJobs} 项并行" + (_viewModel.SelectedWorkflowIndex == 1
        ? $" · 嵌套解压{(_viewModel.RecursiveExtractionEnabled ? "开" : "关")} · 跨目录分卷{(_viewModel.GroupDistributedVolumes ? "开" : "关")}" : "");

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName is
            nameof(MainViewModel.SelectedWorkflowIndex) or nameof(MainViewModel.IsBusy) or
            nameof(MainViewModel.IsPaused) or nameof(MainViewModel.OverallStatus) or
            nameof(MainViewModel.ScannedFileCount) or nameof(MainViewModel.ScannedDirectoryCount) or
            nameof(MainViewModel.SelectedOutputMode) or nameof(MainViewModel.SelectedConflictPolicy) or
            nameof(MainViewModel.DeleteSourceAfterSuccess) or nameof(MainViewModel.MaxParallelJobs) or
            nameof(MainViewModel.PreserveRelativeDirectories) or nameof(MainViewModel.RecursiveExtractionEnabled) or
            nameof(MainViewModel.GroupDistributedVolumes) or nameof(MainViewModel.PasswordSummary) or
            nameof(MainViewModel.ScanRootDirectory))
        {
            Refresh();
        }
    }

    private void OnJobsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Reset (including Clear) carries no old items. Compare the actual collection
        // so removed/replaced rows cannot retain this presenter through event handlers.
        SynchronizeSubscriptions();
        Refresh();
    }

    private void SynchronizeSubscriptions()
    {
        var current = _viewModel.Jobs.ToHashSet();
        foreach (var removed in _subscribedJobs.Except(current).ToArray())
        {
            removed.PropertyChanged -= OnJobChanged;
            _subscribedJobs.Remove(removed);
        }
        foreach (var added in current.Except(_subscribedJobs))
        {
            added.PropertyChanged += OnJobChanged;
            _subscribedJobs.Add(added);
        }
    }

    private void OnJobChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName is
            nameof(JobItemViewModel.Status) or nameof(JobItemViewModel.Phase) or
            nameof(JobItemViewModel.ProgressPercent) or nameof(JobItemViewModel.CurrentFile))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        if (_disposed) return;

        var folderWorkflow = _viewModel.SelectedWorkflowIndex == 1;
        var jobs = _viewModel.Jobs.Where(job => job.IsTreeItem == folderWorkflow).ToArray();
        var completed = jobs.Count(job => job.IsCompleted);
        var running = jobs.Where(job => job.IsRunning).ToArray();
        var attention = jobs.Count(NeedsAttention);
        var queued = jobs.Count(job => job.Status == ExtractJobStatus.Queued && !NeedsAttention(job));
        var password = running.Count(job => job.Phase == ArchiveTreePhase.TryingPassword);
        var measurable = running.Where(job => job.ShowProgress || job.Phase == ArchiveTreePhase.Committing).ToArray();
        var scanning = _viewModel.IsBusy && folderWorkflow && running.Length == 0 &&
            (_viewModel.OverallStatus.StartsWith("正在扫描", StringComparison.Ordinal) ||
             _viewModel.OverallStatus.StartsWith("正在整理分卷", StringComparison.Ordinal) || jobs.Length == 0);
        var canceling = _viewModel.IsBusy &&
            _viewModel.OverallStatus.StartsWith("正在取消", StringComparison.Ordinal);
        var sessionFailed = !_viewModel.IsBusy && _viewModel.OverallStatus is "扫描失败" or "解压失败";
        var sessionCanceled = !_viewModel.IsBusy && _viewModel.OverallStatus is "已取消" or "扫描已取消";

        // A task only contributes its measured extraction fraction until completion.
        // Password candidate counts and staging/commit time are not progress estimates.
        var advancement = completed + measurable.Sum(job => Math.Clamp(job.ProgressPercent / 100d, 0, .99));
        Progress = jobs.Length == 0 ? (_viewModel.IsBusy ? 0 : 100) : advancement / jobs.Length * 100;
        IsIndeterminate = advancement == 0 &&
            (scanning || running.Length > 0 && measurable.Length == 0 || _viewModel.IsBusy && jobs.Length == 0);

        Title = canceling ? "正在取消"
            : _viewModel.IsPaused ? "暂停调度"
            : sessionFailed ? _viewModel.OverallStatus
            : sessionCanceled ? "已取消"
            : scanning ? "扫描中"
            : running.Length > 0 ? (password == running.Length ? "验证密码" : "正在解压")
            : attention > 0 ? "部分任务需处理"
            : queued > 0 ? "等待开始"
            : jobs.Length > 0 && completed == jobs.Length ? "已完成"
            : "就绪";

        if (jobs.Length == 0)
        {
            Detail = sessionFailed ? "未能完成本次操作，请查看日志后重试"
                : sessionCanceled ? "本次操作已取消，可以重新选择任务"
                : scanning
                ? $"已扫描 {_viewModel.ScannedFileCount} 个文件 · {_viewModel.ScannedDirectoryCount} 个目录"
                : folderWorkflow ? "选择文件夹，扫描其中的压缩包" : "拖入压缩文件，或选择要解压的文件";
        }
        else
        {
            var parts = new List<string> { $"已完成 {completed} / {jobs.Length} 项" };
            if (running.Length > 0) parts.Add($"处理中 {running.Length} 项");
            if (attention > 0) parts.Add($"需处理 {attention} 项");
            if (queued > 0 && running.Length == 0) parts.Add($"等待 {queued} 项");
            var active = running.FirstOrDefault(job => job.ShowProgress) ?? running.FirstOrDefault();
            if (active is not null)
            {
                // Show only a file name, never the candidate password or raw log message.
                var file = Path.GetFileName(!active.ShowProgress || string.IsNullOrWhiteSpace(active.CurrentFile)
                    ? active.DisplayName : active.CurrentFile);
                if (!string.IsNullOrWhiteSpace(file)) parts.Add(file);
            }
            if (_viewModel.IsPaused && running.Length > 0) parts.Add("当前任务仍会安全完成");
            Detail = string.Join(" · ", parts);
        }

        if (jobs.Length == 0 && !_viewModel.IsBusy)
        {
            TooltipDetails = "尚无任务，月亮为静态背景";
        }
        else
        {
            // Keep percentages in the tooltip only. Recursive discovery may increase
            // the denominator, so this must not be described as byte-level progress.
            var lines = new List<string>
            {
                IsIndeterminate ? "当前阶段暂无法计算进度" : $"当前已发现任务进度 {Math.Floor(Progress * 10) / 10:0.#}%",
                $"已完成 {completed} / {jobs.Length} 项 · 处理中 {running.Length} 项 · 需处理 {attention} 项",
                "按当前已发现任务等权汇总，非字节进度"
            };
            if (folderWorkflow && _viewModel.RecursiveExtractionEnabled)
                lines.Add("递归发现新任务时，总数增加，汇总进度可能回退");
            if (scanning) lines.Add("仍在扫描，最终任务总数尚未确定");
            if (password > 0) lines.Add($"{password} 项正在验证密码，验证次数不计作解压进度");
            if (running.Any(job => job.Phase == ArchiveTreePhase.Committing))
                lines.Add("正在写入输出目录，确认任务成功后才计为完成");
            if (attention > 0) lines.Add("失败、取消、缺卷及跳过的任务不计为完成");
            if (_viewModel.IsPaused) lines.Add("已暂停新任务调度，正在运行的任务仍可继续");
            TooltipDetails = string.Join(Environment.NewLine, lines);
        }

        OptionsSummary = BuildOptionsSummary(folderWorkflow);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SourceLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SummaryMain)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SummarySecondary)));
    }

    private string BuildOptionsSummary(bool folderWorkflow)
    {
        var parts = new List<string>
        {
            _viewModel.OutputModeDisplay,
            $"{_viewModel.MaxParallelJobs} 项并行",
            _viewModel.SelectedConflictPolicy switch
            {
                ConflictPolicy.RenameExisting => "重名时保留两份",
                ConflictPolicy.Overwrite => "重名时覆盖",
                ConflictPolicy.Skip => "重名时跳过",
                _ => "未知重名策略"
            },
            _viewModel.DeleteSourceAfterSuccess ? "注意：成功后删除源文件" : "保留源文件"
        };
        if (folderWorkflow)
        {
            parts.Add(_viewModel.RecursiveExtractionEnabled ? "递归解压开启" : "递归解压关闭");
            parts.Add(_viewModel.GroupDistributedVolumes ? "跨目录分卷归组开启" : "跨目录分卷归组关闭");
            if (_viewModel.SelectedOutputMode == ArchiveOutputMode.CustomRoot)
                parts.Add(_viewModel.PreserveRelativeDirectories ? "保留相对目录" : "不保留相对目录");
        }
        return string.Join(" · ", parts);
    }

    private static bool NeedsAttention(JobItemViewModel job) => job.NeedsAttention ||
        job.Phase is ArchiveTreePhase.SkippedCycle or ArchiveTreePhase.SkippedLimit;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _viewModel.PropertyChanged -= OnViewModelChanged;
        _viewModel.Jobs.CollectionChanged -= OnJobsChanged;
        foreach (var job in _subscribedJobs) job.PropertyChanged -= OnJobChanged;
        _subscribedJobs.Clear();
    }
}
