using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using ExtractUtil.App.Services;
using ExtractUtil.Core.Enums;
using ExtractUtil.Core.Models;
using ExtractUtil.Core.Services;
using WinForms = System.Windows.Forms;
using WpfDialogs = Microsoft.Win32;

namespace ExtractUtil.App.ViewModels;

/// <summary>
/// Main application state. Folder workflows are handled by ArchiveTreeCoordinator;
/// direct files added from Explorer remain supported through the legacy queue path.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly SynchronizationContext? _uiContext;
    private readonly InMemoryLogSink _logSink;
    private readonly IArchiveExtractor _extractor;
    private readonly IArchiveScanner _scanner;
    private readonly ArchiveTreeCoordinator _treeCoordinator;
    private readonly LocalPasswordSettingsStore _passwordSettingsStore;
    private readonly Dictionary<string, JobItemViewModel> _jobsByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<RelayCommand> _commands = new();
    private IReadOnlyList<string> _customPasswordRules = Array.Empty<string>();

    private CancellationTokenSource? _runCts;
    private PauseController? _pauseController;
    private bool _isBusy;
    private bool _isPaused;
    private string _overallStatus = "就绪";
    private string _scanRootDirectory = string.Empty;
    private bool _recursiveExtractionEnabled = true;
    private bool _groupDistributedVolumes = true;
    private bool _inferPasswordsFromPath = true;
    private string _passwordCandidatesText = string.Empty;
    private string _customPasswordRulesText = string.Empty;
    private string _newSavedPassword = string.Empty;
    private string _passwordSettingsStatus = "尚未保存自定义规则";
    private string _outputBaseDirectory = string.Empty;
    private bool _preserveRelativeDirectories = true;
    private ArchiveOutputMode _selectedOutputMode = ArchiveOutputMode.ArchiveSubdirectory;
    private ConflictPolicy _selectedConflictPolicy = ConflictPolicy.RenameExisting;
    private bool _deleteSourceAfterSuccess;
    private int _maxParallelJobs;
    private int _maxScanPasses = 10;
    private int _currentPass;
    private int _scannedFileCount;
    private int _scannedDirectoryCount;
    private int _discoveredJobCount;
    private int _completedCount;
    private int _failedCount;
    private int _incompleteCount;
    private int _selectedWorkflowIndex;
    private JobItemViewModel? _selectedJob;

    public MainViewModel()
    {
        _uiContext = SynchronizationContext.Current;
        _logSink = new InMemoryLogSink();
        _logSink.EntryAdded += OnLogEntryAdded;
        _extractor = new SevenZipExtractor(new ProcessRunner());
        _scanner = new ArchiveScanner();
        _treeCoordinator = new ArchiveTreeCoordinator(
            _scanner,
            _extractor,
            new ArchiveVolumeStager(),
            new OutputCommitter(),
            _logSink);
        _passwordSettingsStore = new LocalPasswordSettingsStore();

        Jobs = new ObservableCollection<JobItemViewModel>();
        LogLines = new ObservableCollection<string>();
        SavedPasswords = new ObservableCollection<SavedPasswordViewModel>();
        ConflictPolicies = Enum.GetValues<ConflictPolicy>();
        OutputModes = Enum.GetValues<ArchiveOutputMode>();
        _maxParallelJobs = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 2));

        var passwordSettings = _passwordSettingsStore.Load();
        _customPasswordRulesText = string.Join(Environment.NewLine, passwordSettings.CustomInferenceRules);
        _customPasswordRules = PasswordCandidateResolver.ParseRuleTemplates(_customPasswordRulesText);
        foreach (var password in passwordSettings.SavedPasswords)
        {
            SavedPasswords.Add(new SavedPasswordViewModel(password));
        }
        _passwordSettingsStatus = _customPasswordRules.Count == 0
            ? "使用内置推断规则"
            : $"已加载 {_customPasswordRules.Count} 条自定义规则";

        AddFilesCommand = Register(new RelayCommand(_ => AddFiles(), _ => !IsBusy));
        AddFolderCommand = Register(new RelayCommand(_ => AddFolder(), _ => !IsBusy));
        BrowseScanRootCommand = Register(new RelayCommand(_ => BrowseScanRoot(), _ => !IsBusy));
        BrowseOutputCommand = Register(new RelayCommand(_ => BrowseOutputFolder(), _ => !IsBusy));
        ScanOnlyCommand = Register(new RelayCommand(_ => _ = ScanOnlyAsync(), _ => !IsBusy));
        ScanAndExtractCommand = Register(new RelayCommand(_ => _ = ScanAndExtractAsync(), _ => !IsBusy));
        StartCommand = Register(new RelayCommand(_ => StartExtraction(), _ => !IsBusy && QueuedFileCount > 0));
        RetrySelectedCommand = Register(new RelayCommand(_ => RetrySelectedJob(), _ => CanRetrySelectedJob));
        CopySelectedPathCommand = Register(new RelayCommand(_ => CopySelectedPath(), _ => SelectedJob is not null));
        SavePasswordRulesCommand = Register(new RelayCommand(_ => SavePasswordSettings()));
        SaveCandidatePasswordsCommand = Register(new RelayCommand(_ => SaveCandidatePasswords(), _ => PasswordCandidateResolver.ParseMultiline(PasswordCandidatesText).Count > 0));
        AddSavedPasswordCommand = Register(new RelayCommand(_ => AddSavedPassword(), _ => !string.IsNullOrWhiteSpace(NewSavedPassword)));
        UseSavedPasswordCommand = Register(new RelayCommand(UseSavedPassword, parameter => parameter is SavedPasswordViewModel));
        DeleteSavedPasswordCommand = Register(new RelayCommand(DeleteSavedPassword, parameter => parameter is SavedPasswordViewModel));
        PauseResumeCommand = Register(new RelayCommand(_ => TogglePause(), _ => CanPause));
        CancelCommand = Register(new RelayCommand(_ => CancelExtraction(), _ => IsBusy));
        ExportLogCommand = Register(new RelayCommand(_ => ExportLog()));
        RegisterContextMenuCommand = Register(new RelayCommand(_ => RegisterContextMenu()));
        UnregisterContextMenuCommand = Register(new RelayCommand(_ => UnregisterContextMenu()));
    }

    public ObservableCollection<JobItemViewModel> Jobs { get; }
    public ObservableCollection<string> LogLines { get; }
    public ObservableCollection<SavedPasswordViewModel> SavedPasswords { get; }
    public Array ConflictPolicies { get; }
    public Array OutputModes { get; }

    public int SelectedWorkflowIndex
    {
        get => _selectedWorkflowIndex;
        set => SetProperty(ref _selectedWorkflowIndex, Math.Clamp(value, 0, 1));
    }

    public JobItemViewModel? SelectedJob
    {
        get => _selectedJob;
        set
        {
            if (SetProperty(ref _selectedJob, value))
            {
                OnPropertyChanged(nameof(CanRetrySelectedJob));
                RaiseCommandStates();
            }
        }
    }

    public string ScanRootDirectory
    {
        get => _scanRootDirectory;
        set => SetProperty(ref _scanRootDirectory, value);
    }

    public bool RecursiveExtractionEnabled
    {
        get => _recursiveExtractionEnabled;
        set => SetProperty(ref _recursiveExtractionEnabled, value);
    }

    public bool GroupDistributedVolumes
    {
        get => _groupDistributedVolumes;
        set => SetProperty(ref _groupDistributedVolumes, value);
    }

    public bool InferPasswordsFromPath
    {
        get => _inferPasswordsFromPath;
        set => SetProperty(ref _inferPasswordsFromPath, value);
    }

    public string PasswordCandidatesText
    {
        get => _passwordCandidatesText;
        set
        {
            value ??= string.Empty;
            if (SetProperty(ref _passwordCandidatesText, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string CustomPasswordRulesText
    {
        get => _customPasswordRulesText;
        set
        {
            value ??= string.Empty;
            if (SetProperty(ref _customPasswordRulesText, value))
            {
                _customPasswordRules = PasswordCandidateResolver.ParseRuleTemplates(value);
                var enteredCount = value.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                    .Count(line => !string.IsNullOrWhiteSpace(line));
                var invalidCount = Math.Max(0, enteredCount - _customPasswordRules.Count);
                PasswordSettingsStatus = invalidCount == 0
                    ? $"{_customPasswordRules.Count} 条有效规则，尚未保存"
                    : $"{_customPasswordRules.Count} 条有效，{invalidCount} 条缺少 {{password}}";
            }
        }
    }

    public string NewSavedPassword
    {
        get => _newSavedPassword;
        set
        {
            value ??= string.Empty;
            if (SetProperty(ref _newSavedPassword, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string PasswordSettingsStatus
    {
        get => _passwordSettingsStatus;
        private set => SetProperty(ref _passwordSettingsStatus, value);
    }

    // Kept for compatibility with the original single-password API.
    public string? Password
    {
        get => PasswordCandidateResolver.ParseMultiline(PasswordCandidatesText).FirstOrDefault();
        set
        {
            if (value is not null)
            {
                PasswordCandidatesText = value;
            }
        }
    }

    public bool UseCustomOutputDirectory
    {
        get => SelectedOutputMode == ArchiveOutputMode.CustomRoot;
        set
        {
            if (value && SelectedOutputMode != ArchiveOutputMode.CustomRoot)
            {
                SelectedOutputMode = ArchiveOutputMode.CustomRoot;
            }
            else if (!value && SelectedOutputMode == ArchiveOutputMode.CustomRoot)
            {
                SelectedOutputMode = ArchiveOutputMode.ArchiveSubdirectory;
            }
        }
    }

    public string OutputBaseDirectory
    {
        get => _outputBaseDirectory;
        set => SetProperty(ref _outputBaseDirectory, value);
    }

    public bool PreserveRelativeDirectories
    {
        get => _preserveRelativeDirectories;
        set => SetProperty(ref _preserveRelativeDirectories, value);
    }

    public ArchiveOutputMode SelectedOutputMode
    {
        get => _selectedOutputMode;
        set
        {
            if (SetProperty(ref _selectedOutputMode, value))
            {
                OnPropertyChanged(nameof(UseCustomOutputDirectory));
            }
        }
    }

    public ConflictPolicy SelectedConflictPolicy
    {
        get => _selectedConflictPolicy;
        set => SetProperty(ref _selectedConflictPolicy, value);
    }

    public bool DeleteSourceAfterSuccess
    {
        get => _deleteSourceAfterSuccess;
        set => SetProperty(ref _deleteSourceAfterSuccess, value);
    }

    public int MaxParallelJobs
    {
        get => _maxParallelJobs;
        set
        {
            var normalized = Math.Clamp(value, 1, 32);
            if (SetProperty(ref _maxParallelJobs, normalized))
            {
                OnPropertyChanged(nameof(MaxParallelJobsText));
            }
        }
    }

    public string MaxParallelJobsText
    {
        get => _maxParallelJobs.ToString(CultureInfo.InvariantCulture);
        set
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                MaxParallelJobs = parsed;
            }
        }
    }

    public int MaxScanPasses
    {
        get => _maxScanPasses;
        set
        {
            var normalized = Math.Clamp(value, 0, 100);
            if (SetProperty(ref _maxScanPasses, normalized))
            {
                OnPropertyChanged(nameof(MaxScanPassesText));
            }
        }
    }

    public string MaxScanPassesText
    {
        get => _maxScanPasses.ToString(CultureInfo.InvariantCulture);
        set
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                MaxScanPasses = parsed;
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseCommandStates();
                OnPropertyChanged(nameof(PauseButtonText));
            }
        }
    }

    public bool IsPaused
    {
        get => _isPaused;
        private set
        {
            if (SetProperty(ref _isPaused, value))
            {
                OnPropertyChanged(nameof(PauseButtonText));
                OnPropertyChanged(nameof(OverallStatus));
            }
        }
    }

    public string PauseButtonText => IsPaused ? "继续" : "暂停";

    public bool CanPause => IsBusy && _pauseController is not null;

    public int QueuedFileCount => Jobs.Count(job => job.Job is not null && job.Status == ExtractJobStatus.Queued);

    public string DirectFileStatus => QueuedFileCount == 0
        ? "尚未添加文件"
        : $"{QueuedFileCount} 个待解压文件";

    public bool CanRetrySelectedJob => !IsBusy &&
        SelectedJob is { Job: not null } job &&
        job.Status is ExtractJobStatus.Failed or ExtractJobStatus.Canceled &&
        File.Exists(job.ArchivePath);

    public string OverallStatus
    {
        get => _overallStatus;
        private set => SetProperty(ref _overallStatus, value);
    }

    public int CurrentPass
    {
        get => _currentPass;
        private set => SetProperty(ref _currentPass, value);
    }

    public int ScannedFileCount
    {
        get => _scannedFileCount;
        private set => SetProperty(ref _scannedFileCount, value);
    }

    public int ScannedDirectoryCount
    {
        get => _scannedDirectoryCount;
        private set => SetProperty(ref _scannedDirectoryCount, value);
    }

    public int DiscoveredJobCount
    {
        get => _discoveredJobCount;
        private set => SetProperty(ref _discoveredJobCount, value);
    }

    public int CompletedCount
    {
        get => _completedCount;
        private set => SetProperty(ref _completedCount, value);
    }

    public int FailedCount
    {
        get => _failedCount;
        private set => SetProperty(ref _failedCount, value);
    }

    public int IncompleteCount
    {
        get => _incompleteCount;
        private set => SetProperty(ref _incompleteCount, value);
    }

    public ICommand AddFilesCommand { get; }
    public ICommand AddFolderCommand { get; }
    public ICommand BrowseScanRootCommand { get; }
    public ICommand BrowseOutputCommand { get; }
    public ICommand ScanOnlyCommand { get; }
    public ICommand ScanAndExtractCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand RetrySelectedCommand { get; }
    public ICommand CopySelectedPathCommand { get; }
    public ICommand SavePasswordRulesCommand { get; }
    public ICommand SaveCandidatePasswordsCommand { get; }
    public ICommand AddSavedPasswordCommand { get; }
    public ICommand UseSavedPasswordCommand { get; }
    public ICommand DeleteSavedPasswordCommand { get; }
    public ICommand PauseResumeCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ExportLogCommand { get; }
    public ICommand RegisterContextMenuCommand { get; }
    public ICommand UnregisterContextMenuCommand { get; }

    public void AddArchivesFromShell(IEnumerable<string> paths, ShellMode mode)
    {
        AddLegacyArchives(paths, mode);
    }

    public void AddPathsFromDrop(IEnumerable<string> paths)
    {
        var pathList = paths.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
        var directories = pathList.Where(Directory.Exists).ToArray();
        var files = pathList.Where(File.Exists).ToArray();
        SelectedWorkflowIndex = directories.Length > 0 ? 1 : 0;

        if (directories.Length > 0)
        {
            ScanRootDirectory = directories[0];
            Log(LogLevel.Info, $"已选择扫描目录：{ScanRootDirectory}");
            _ = ScanOnlyAsync();
        }

        if (files.Length > 0)
        {
            AddLegacyArchives(files, ShellMode.None);
        }
    }

    public void StartExtraction()
    {
        SelectedWorkflowIndex = 0;
        if (IsBusy)
        {
            return;
        }

        if (Jobs.Any(job => job.Job is not null && job.Status == ExtractJobStatus.Queued))
        {
            _ = RunLegacyJobsAsync();
            return;
        }

        Log(LogLevel.Warning, "没有待解压的普通文件，请先点击“添加压缩文件”。");
    }

    private void RetrySelectedJob()
    {
        var item = SelectedJob;
        if (item?.Job is null ||
            item.Status is not (ExtractJobStatus.Failed or ExtractJobStatus.Canceled))
        {
            return;
        }

        if (!File.Exists(item.ArchivePath))
        {
            Log(LogLevel.Error, $"无法重试，源文件已不存在：{item.ArchivePath}");
            RaiseCommandStates();
            return;
        }

        item.ResetForRetry();
        RecalculateCounts();
        OverallStatus = $"准备重试：{item.DisplayName}";
        Log(LogLevel.Info, $"重新使用原路径执行：{item.ArchivePath}");
        _ = RunLegacyJobsAsync(new[] { item });
    }

    private void CopySelectedPath()
    {
        if (SelectedJob is not { } item)
        {
            return;
        }

        var paths = item.VolumePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (paths.Length == 0)
        {
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(string.Join(Environment.NewLine, paths));
            OverallStatus = paths.Length == 1 ? "已复制文件路径" : $"已复制 {paths.Length} 个分卷路径";
            Log(LogLevel.Info, paths.Length == 1 ? "已复制所选任务的文件路径。" : $"已复制所选任务的 {paths.Length} 个源文件路径。");
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, $"复制路径失败：{ex.Message}");
        }
    }

    private void SavePasswordSettings()
    {
        PersistPasswordSettings($"已保存 {_customPasswordRules.Count} 条有效自定义规则");
    }

    private void SaveCandidatePasswords()
    {
        var added = 0;
        foreach (var password in PasswordCandidateResolver.ParseMultiline(PasswordCandidatesText))
        {
            if (SavedPasswords.Any(item => string.Equals(item.Value, password, StringComparison.Ordinal)))
            {
                continue;
            }

            SavedPasswords.Add(new SavedPasswordViewModel(password));
            added++;
        }

        PersistPasswordSettings(added == 0 ? "候选密码已在本地密码库中" : $"已保存 {added} 个候选密码");
    }

    private void AddSavedPassword()
    {
        var password = NewSavedPassword.Trim();
        if (password.Length == 0)
        {
            return;
        }

        if (SavedPasswords.Any(item => string.Equals(item.Value, password, StringComparison.Ordinal)))
        {
            PasswordSettingsStatus = "该密码已存在于本地密码库";
            return;
        }

        SavedPasswords.Add(new SavedPasswordViewModel(password));
        NewSavedPassword = string.Empty;
        PersistPasswordSettings("密码已加密保存到本机");
    }

    private void UseSavedPassword(object? parameter)
    {
        if (parameter is not SavedPasswordViewModel item)
        {
            return;
        }

        var candidates = PasswordCandidateResolver.ParseMultiline(PasswordCandidatesText).ToList();
        if (!candidates.Contains(item.Value, StringComparer.Ordinal))
        {
            candidates.Add(item.Value);
            PasswordCandidatesText = string.Join(Environment.NewLine, candidates);
        }

        PasswordSettingsStatus = "已加入本次密码候选";
    }

    private void DeleteSavedPassword(object? parameter)
    {
        if (parameter is not SavedPasswordViewModel item || !SavedPasswords.Remove(item))
        {
            return;
        }

        PersistPasswordSettings("已从本地密码库删除");
    }

    private void PersistPasswordSettings(string successMessage)
    {
        try
        {
            var enteredRules = CustomPasswordRulesText
                .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(rule => rule.Trim())
                .Where(rule => rule.Length > 0)
                .ToArray();
            _passwordSettingsStore.Save(enteredRules, SavedPasswords.Select(item => item.Value));
            PasswordSettingsStatus = successMessage;
            Log(LogLevel.Info, $"密码助手设置已保存到本机：{_passwordSettingsStore.SettingsPath}");
        }
        catch (Exception ex)
        {
            PasswordSettingsStatus = "本地保存失败";
            Log(LogLevel.Error, $"保存密码助手设置失败：{ex.Message}");
        }
    }

    private RelayCommand Register(RelayCommand command)
    {
        _commands.Add(command);
        return command;
    }

    private async Task ScanOnlyAsync()
    {
        SelectedWorkflowIndex = 1;
        if (IsBusy)
        {
            return;
        }

        if (!TryGetScanRoot(out var root))
        {
            return;
        }

        BeginRun("正在扫描");
        try
        {
            var result = await _scanner.ScanAsync(
                    BuildScanOptions(root),
                    new Progress<ArchiveScanProgress>(OnScanProgress),
                    _runCts!.Token)
                .ConfigureAwait(false);

            PostToUi(() =>
            {
                ReplaceTreeJobs(result.Items, 0);
                ScannedFileCount = result.FilesScanned;
                ScannedDirectoryCount = result.DirectoriesScanned;
                OverallStatus = $"扫描完成：发现 {result.Items.Count} 个逻辑任务";
                Log(LogLevel.Info,
                    $"扫描完成：{result.FilesScanned:n0} 个文件，{result.Items.Count} 个逻辑压缩任务，" +
                    $"可解压 {result.ExtractableCount}，需处理 {result.BlockedCount}。");
                foreach (var warning in result.Warnings.Take(50))
                {
                    Log(LogLevel.Warning, warning);
                }
            });
        }
        catch (OperationCanceledException)
        {
            PostToUi(() => OverallStatus = "扫描已取消");
        }
        catch (Exception ex)
        {
            PostToUi(() =>
            {
                OverallStatus = "扫描失败";
                Log(LogLevel.Error, $"扫描失败：{ex.Message}");
            });
        }
        finally
        {
            EndRun();
        }
    }

    private async Task ScanAndExtractAsync()
    {
        SelectedWorkflowIndex = 1;
        if (IsBusy)
        {
            return;
        }

        if (!TryGetScanRoot(out var root))
        {
            return;
        }

        if (SelectedOutputMode == ArchiveOutputMode.CustomRoot &&
            string.IsNullOrWhiteSpace(OutputBaseDirectory))
        {
            Log(LogLevel.Warning, "已选择自定义输出目录，请先填写输出根目录。");
            return;
        }

        BeginRun("正在扫描并解压");
        var pause = new PauseController();
        _pauseController = pause;
        PostToUi(() =>
        {
            OnPropertyChanged(nameof(CanPause));
            RaiseCommandStates();
        });
        try
        {
            var options = new ArchiveTreeRunOptions
            {
                RootDirectory = root,
                OutputMode = SelectedOutputMode,
                CustomOutputRoot = OutputBaseDirectory,
                PasswordCandidates = PasswordCandidateResolver.ParseMultiline(PasswordCandidatesText),
                CustomPasswordInferenceRules = _customPasswordRules,
                InferPasswordsFromPath = InferPasswordsFromPath,
                Recursive = RecursiveExtractionEnabled,
                EnableSiblingVolumeGrouping = GroupDistributedVolumes,
                PreserveRelativeDirectories = PreserveRelativeDirectories,
                DeleteSourceAfterSuccess = DeleteSourceAfterSuccess,
                ConflictPolicy = SelectedConflictPolicy,
                EnableMultiThread = true,
                MaxParallelJobs = MaxParallelJobs,
                MaxDepth = MaxScanPasses,
                MaxTasks = 10_000
            };

            var result = await _treeCoordinator.RunAsync(
                    options,
                    pause,
                    new Progress<ArchiveTreeProgress>(OnTreeProgress),
                    _runCts!.Token)
                .ConfigureAwait(false);

            PostToUi(() =>
            {
                CompletedCount = result.CompletedCount;
                FailedCount = result.FailedCount;
                IncompleteCount = Jobs.Count(job => !job.CanExtract || job.Phase == ArchiveTreePhase.Blocked);
                OverallStatus = result.WasCanceled
                    ? "已取消"
                    : result.FailedCount == 0 ? $"完成：成功 {result.CompletedCount} 个" : $"完成：成功 {result.CompletedCount}，失败 {result.FailedCount} 个";
                Log(result.FailedCount == 0 ? LogLevel.Info : LogLevel.Warning,
                    $"递归会话结束：成功 {result.CompletedCount}，失败 {result.FailedCount}，任务总数 {result.Outcomes.Count}。");
            });
        }
        catch (OperationCanceledException)
        {
            PostToUi(() => OverallStatus = "已取消");
        }
        catch (Exception ex)
        {
            PostToUi(() =>
            {
                OverallStatus = "解压失败";
                Log(LogLevel.Error, $"递归会话失败：{ex.Message}");
            });
        }
        finally
        {
            _pauseController = null;
            IsPaused = false;
            PostToUi(() => OnPropertyChanged(nameof(CanPause)));
            EndRun();
        }
    }

    private async Task RunLegacyJobsAsync(IReadOnlyCollection<JobItemViewModel>? requestedJobs = null)
    {
        var pending = (requestedJobs ?? Jobs)
            .Where(job => job.Job is not null && job.Status == ExtractJobStatus.Queued)
            .ToArray();
        if (pending.Length == 0 || IsBusy)
        {
            return;
        }

        BeginRun("正在解压已添加的文件");
        try
        {
            var queue = new JobQueue(_extractor, _logSink, MaxParallelJobs);
            var passwordCandidates = PasswordCandidateResolver.ParseMultiline(PasswordCandidatesText);
            var tasks = pending.Select(job => RunLegacyJobAsync(queue, job, passwordCandidates, _runCts!.Token)).ToArray();
            await Task.WhenAll(tasks).ConfigureAwait(false);
            PostToUi(() =>
            {
                RecalculateCounts();
                OverallStatus = "文件解压完成";
            });
        }
        catch (OperationCanceledException)
        {
            PostToUi(() => OverallStatus = "已取消");
        }
        finally
        {
            EndRun();
        }
    }

    private async Task RunLegacyJobAsync(
        JobQueue queue,
        JobItemViewModel item,
        IReadOnlyList<string> explicitPasswords,
        CancellationToken token)
    {
        PostToUi(() => item.SetLegacyState(ExtractJobStatus.Running, ArchiveTreePhase.Extracting, "准备解压"));
        var progress = new Progress<ExtractProgress>(value => PostToUi(() => item.ApplyLegacyProgress(value)));
        var source = item.Job!;
        var passwords = PasswordCandidateResolver.Resolve(
            source.ArchivePath,
            explicitPasswords,
            InferPasswordsFromPath,
            customInferenceRules: _customPasswordRules);
        var attempts = passwords.Count == 0 ? new string?[] { null } : passwords.Cast<string?>().ToArray();
        var sourceDirectory = Path.GetDirectoryName(source.ArchivePath) ?? Environment.CurrentDirectory;
        var directWorkRoot = Path.Combine(
            sourceDirectory,
            ".extractutil_work",
            "direct",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directWorkRoot);

        try
        {
            ExtractResult? lastResult = null;
            for (var attemptIndex = 0; attemptIndex < attempts.Length; attemptIndex++)
            {
                token.ThrowIfCancellationRequested();
                var attemptNumber = attemptIndex + 1;
                var attemptDirectory = Path.Combine(directWorkRoot, attemptNumber.ToString(CultureInfo.InvariantCulture));
                Directory.CreateDirectory(attemptDirectory);
                PostToUi(() => item.SetLegacyState(
                    ExtractJobStatus.Running,
                    ArchiveTreePhase.TryingPassword,
                    $"密码尝试 {attemptNumber}/{attempts.Length}"));
                Log(LogLevel.Info, $"普通文件密码尝试 {attemptNumber}/{attempts.Length}：{item.DisplayName}");

                var options = new ExtractOptions
                {
                    OutputDirectory = attemptDirectory,
                    ConflictPolicy = ConflictPolicy.Overwrite,
                    Password = attempts[attemptIndex],
                    EnableMultiThread = true
                };
                var runJob = new ExtractJob(source.ArchivePath, options);
                lastResult = await queue.RunAsync(runJob, progress, token).ConfigureAwait(false);

                if (!lastResult.Success)
                {
                    TryDeleteOwnedDirectory(attemptDirectory, directWorkRoot);
                    if (attemptIndex + 1 < attempts.Length &&
                        lastResult.FailureKind is ExtractFailureKind.WrongPassword or ExtractFailureKind.CorruptArchive or ExtractFailureKind.Unknown)
                    {
                        continue;
                    }

                    break;
                }

                PostToUi(() => item.SetLegacyState(
                    ExtractJobStatus.Running,
                    ArchiveTreePhase.Committing,
                    "正在提交解压结果"));
                await new OutputCommitter().CommitAsync(
                    attemptDirectory,
                    source.Options.OutputDirectory,
                    SelectedConflictPolicy,
                    token).ConfigureAwait(false);

                var message = lastResult.ExitCode == 1 ? "完成（有警告）" : "完成";
                if (DeleteSourceAfterSuccess && lastResult.ExitCode == 0)
                {
                    message += TryDeleteLegacySources(source.ArchivePath)
                        ? "；源压缩文件已删除"
                        : "；部分源文件删除失败";
                }

                PostToUi(() => item.SetLegacyState(
                    ExtractJobStatus.Completed,
                    ArchiveTreePhase.Completed,
                    message));
                return;
            }

            PostToUi(() => item.SetLegacyState(
                ExtractJobStatus.Failed,
                ArchiveTreePhase.Failed,
                SummarizeLegacyError(lastResult)));
        }
        catch (OperationCanceledException)
        {
            PostToUi(() => item.SetLegacyState(ExtractJobStatus.Canceled, ArchiveTreePhase.Canceled, "已取消"));
            throw;
        }
        catch (Exception ex)
        {
            PostToUi(() => item.SetLegacyState(ExtractJobStatus.Failed, ArchiveTreePhase.Failed, ex.Message));
        }
        finally
        {
            TryDeleteOwnedDirectory(directWorkRoot, Path.Combine(sourceDirectory, ".extractutil_work", "direct"));
        }
    }

    private void OnTreeProgress(ArchiveTreeProgress progress)
    {
        PostToUi(() =>
        {
            if (progress.FilesScanned > 0)
            {
                ScannedFileCount = Math.Max(ScannedFileCount, progress.FilesScanned);
            }

            if (progress.DirectoriesScanned > 0)
            {
                ScannedDirectoryCount = Math.Max(ScannedDirectoryCount, progress.DirectoriesScanned);
            }

            CurrentPass = Math.Max(CurrentPass, progress.Depth + 1);
            if (progress.Item is not null)
            {
                var item = GetOrAddTreeJob(progress.Item, progress.Depth);
                item.ApplyTreeProgress(progress);
            }

            if (progress.Phase == ArchiveTreePhase.Scanning)
            {
                OverallStatus = progress.Message.StartsWith("正在扫描", StringComparison.Ordinal)
                    ? progress.Message
                    : "正在扫描和整理分卷";
            }
            else if (progress.Phase == ArchiveTreePhase.TryingPassword)
            {
                OverallStatus = progress.Message;
            }

            RecalculateCounts();
        });
    }

    private void OnScanProgress(ArchiveScanProgress progress)
    {
        PostToUi(() =>
        {
            ScannedFileCount = Math.Max(ScannedFileCount, progress.FilesScanned);
            ScannedDirectoryCount = Math.Max(ScannedDirectoryCount, progress.DirectoriesScanned);
            OverallStatus = progress.IsGrouping ? "正在整理分卷" : "正在扫描：" + (progress.CurrentPath ?? string.Empty);
        });
    }

    private JobItemViewModel GetOrAddTreeJob(ArchiveWorkItem item, int depth)
    {
        var key = BuildItemKey(item);
        if (_jobsByKey.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var viewModel = new JobItemViewModel(item, depth, depth);
        _jobsByKey[key] = viewModel;
        Jobs.Add(viewModel);
        return viewModel;
    }

    private void ReplaceTreeJobs(IEnumerable<ArchiveWorkItem> items, int pass)
    {
        foreach (var old in Jobs.Where(job => job.IsTreeItem).ToArray())
        {
            Jobs.Remove(old);
        }

        foreach (var pair in _jobsByKey.Where(pair => pair.Value.IsTreeItem).ToArray())
        {
            _jobsByKey.Remove(pair.Key);
        }

        foreach (var item in items)
        {
            var viewModel = GetOrAddTreeJob(item, pass);
            viewModel.DiscoveryPass = pass;
            if (!item.CanExtract)
            {
                viewModel.ApplyTreeProgress(new ArchiveTreeProgress
                {
                    Phase = ArchiveTreePhase.Blocked,
                    Item = item,
                    Depth = pass,
                    Message = viewModel.BlockingReasons
                });
            }
        }

        RecalculateCounts();
    }

    private void RecalculateCounts()
    {
        DiscoveredJobCount = Jobs.Count;
        CompletedCount = Jobs.Count(job => job.Status == ExtractJobStatus.Completed);
        FailedCount = Jobs.Count(job => job.Status == ExtractJobStatus.Failed);
        IncompleteCount = Jobs.Count(job => !job.CanExtract || job.Phase == ArchiveTreePhase.Blocked);
        OnPropertyChanged(nameof(QueuedFileCount));
        OnPropertyChanged(nameof(DirectFileStatus));
        RaiseCommandStates();
    }

    private ArchiveScanOptions BuildScanOptions(string root)
    {
        return new ArchiveScanOptions
        {
            RootDirectory = root,
            OutputMode = SelectedOutputMode,
            CustomOutputRoot = OutputBaseDirectory,
            RecurseSubdirectories = true,
            EnableSiblingVolumeGrouping = GroupDistributedVolumes,
            PreserveRelativeDirectories = PreserveRelativeDirectories,
            ProgressReportInterval = 250
        };
    }

    private bool TryGetScanRoot(out string root)
    {
        root = string.Empty;
        if (string.IsNullOrWhiteSpace(ScanRootDirectory))
        {
            Log(LogLevel.Warning, "请先选择要扫描的根目录。");
            return false;
        }

        if (!Directory.Exists(ScanRootDirectory))
        {
            Log(LogLevel.Warning, $"目录不存在：{ScanRootDirectory}");
            return false;
        }

        root = Path.GetFullPath(ScanRootDirectory);
        return true;
    }

    private void BeginRun(string status)
    {
        IsBusy = true;
        IsPaused = false;
        CurrentPass = 0;
        ScannedFileCount = 0;
        ScannedDirectoryCount = 0;
        OverallStatus = status;
        _runCts = new CancellationTokenSource();
        OnPropertyChanged(nameof(CanPause));
        RaiseCommandStates();
    }

    private void EndRun()
    {
        PostToUi(() =>
        {
            _runCts?.Dispose();
            _runCts = null;
            IsBusy = false;
            IsPaused = false;
            _pauseController = null;
            OnPropertyChanged(nameof(CanPause));
            RaiseCommandStates();
        });
    }

    private void TogglePause()
    {
        if (_pauseController is null || !IsBusy)
        {
            return;
        }

        if (IsPaused)
        {
            _pauseController.Resume();
            IsPaused = false;
            OverallStatus = "继续运行";
            Log(LogLevel.Info, "已继续：允许派发新任务和下一次密码尝试。");
        }
        else
        {
            _pauseController.Pause();
            IsPaused = true;
            OverallStatus = "已暂停（正在运行的任务完成后保持暂停）";
            Log(LogLevel.Info, "已请求暂停：当前 7-Zip 进程会安全完成，不再开始新任务。");
        }
    }

    private void CancelExtraction()
    {
        if (!IsBusy)
        {
            return;
        }

        OverallStatus = "正在取消";
        _pauseController?.Resume();
        _runCts?.Cancel();
        Log(LogLevel.Warning, "正在取消当前会话；临时目录会在退出时清理。");
    }

    private void AddFiles()
    {
        SelectedWorkflowIndex = 0;
        var dialog = new WpfDialogs.OpenFileDialog
        {
            Filter = "压缩文件 (*.zip;*.7z;*.7z.???;*.rar;*.tar;*.gz;*.tgz;*.bz2;*.xz)|*.zip;*.7z;*.7z.???;*.rar;*.tar;*.gz;*.tgz;*.bz2;*.xz|所有文件 (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            AddLegacyArchives(dialog.FileNames, ShellMode.None);
        }
    }

    private void AddFolder()
    {
        SelectedWorkflowIndex = 1;
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "选择要递归扫描的文件夹",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            ScanRootDirectory = dialog.SelectedPath;
            _ = ScanOnlyAsync();
        }
    }

    private void BrowseScanRoot()
    {
        SelectedWorkflowIndex = 1;
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "选择要递归扫描的根目录",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            ScanRootDirectory = dialog.SelectedPath;
        }
    }

    private void BrowseOutputFolder()
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "选择自定义输出根目录",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            OutputBaseDirectory = dialog.SelectedPath;
            UseCustomOutputDirectory = true;
        }
    }

    private void AddLegacyArchives(IEnumerable<string> paths, ShellMode mode)
    {
        var added = 0;
        var duplicate = 0;
        var existing = new HashSet<string>(Jobs.Where(job => job.Job is not null).Select(job => job.Key), StringComparer.OrdinalIgnoreCase);

        foreach (var rawPath in paths)
        {
            if (!File.Exists(rawPath))
            {
                continue;
            }

            var path = Path.GetFullPath(rawPath);
            var fileName = Path.GetFileName(path);
            if (fileName.EndsWith(".7z.002", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".7z.003", StringComparison.OrdinalIgnoreCase))
            {
                var first = Path.Combine(Path.GetDirectoryName(path)!, fileName[..^3] + "001");
                if (File.Exists(first))
                {
                    path = first;
                }
            }

            if (!existing.Add(path))
            {
                duplicate++;
                continue;
            }

            var outputDirectory = ResolveLegacyOutput(path, mode);
            var job = new ExtractJob(path, new ExtractOptions
            {
                OutputDirectory = outputDirectory,
                ConflictPolicy = SelectedConflictPolicy,
                Password = PasswordCandidateResolver.ParseMultiline(PasswordCandidatesText).FirstOrDefault(),
                EnableMultiThread = true,
                DeleteSourceAfterSuccess = DeleteSourceAfterSuccess
            });
            var row = new JobItemViewModel(job);
            Jobs.Add(row);
            _jobsByKey[row.Key] = row;
            added++;
        }

        RecalculateCounts();
        Log(LogLevel.Info, $"已添加 {added} 个文件任务。" + (duplicate > 0 ? $"跳过重复 {duplicate} 个。" : string.Empty));
    }

    private string ResolveLegacyOutput(string archivePath, ShellMode mode)
    {
        var directory = Path.GetDirectoryName(archivePath) ?? Environment.CurrentDirectory;
        var name = Path.GetFileName(archivePath);
        foreach (var suffix in new[] { ".7z.001", ".zip.001", ".tar.gz", ".tar.bz2", ".tar.xz", ".zip", ".7z", ".rar", ".tar", ".tgz", ".gz", ".bz2", ".xz" })
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^suffix.Length];
                break;
            }
        }

        return mode == ShellMode.ExtractHere
            ? directory
            : mode == ShellMode.ExtractToFolder || SelectedOutputMode == ArchiveOutputMode.ArchiveSubdirectory
                ? Path.Combine(directory, name)
                : SelectedOutputMode == ArchiveOutputMode.ArchiveDirectory
                    ? directory
                    : Path.Combine(OutputBaseDirectory, name);
    }

    private bool TryDeleteLegacySources(string archivePath)
    {
        var paths = ResolveLegacySourceParts(archivePath);
        var allDeleted = true;
        foreach (var path in paths)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    Log(LogLevel.Info, $"已删除源压缩文件：{path}");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                allDeleted = false;
                Log(LogLevel.Warning, $"删除源压缩文件失败：{path}；{ex.Message}");
            }
        }

        return allDeleted;
    }

    private static IReadOnlyList<string> ResolveLegacySourceParts(string archivePath)
    {
        var fullPath = Path.GetFullPath(archivePath);
        var directory = Path.GetDirectoryName(fullPath);
        var fileName = Path.GetFileName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return new[] { fullPath };
        }

        var lastDot = fileName.LastIndexOf('.');
        if (lastDot > 0 &&
            fileName[(lastDot + 1)..] is { Length: 3 } numericSuffix &&
            numericSuffix.All(char.IsDigit) &&
            (fileName[..lastDot].EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ||
             fileName[..lastDot].EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
        {
            var prefix = fileName[..lastDot];
            return Directory.EnumerateFiles(directory, prefix + ".???", SearchOption.TopDirectoryOnly)
                .Where(path =>
                {
                    var suffix = Path.GetExtension(path).TrimStart('.');
                    return suffix.Length == 3 && suffix.All(char.IsDigit);
                })
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var partMarker = fileName.LastIndexOf(".part1.rar", StringComparison.OrdinalIgnoreCase);
        if (partMarker > 0 && partMarker + ".part1.rar".Length == fileName.Length)
        {
            var stem = fileName[..partMarker];
            return Directory.EnumerateFiles(directory, stem + ".part*.rar", SearchOption.TopDirectoryOnly)
                .Where(path =>
                {
                    var name = Path.GetFileName(path);
                    var middle = name[stem.Length..^4];
                    return middle.StartsWith(".part", StringComparison.OrdinalIgnoreCase) &&
                           middle[5..].All(char.IsDigit);
                })
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        if (fileName.EndsWith(".rar", StringComparison.OrdinalIgnoreCase))
        {
            var stem = Path.GetFileNameWithoutExtension(fileName);
            var parts = new List<string> { fullPath };
            parts.AddRange(Directory.EnumerateFiles(directory, stem + ".r??", SearchOption.TopDirectoryOnly)
                .Where(path =>
                {
                    var extension = Path.GetExtension(path);
                    return extension.Length == 4 &&
                           extension[1] is 'r' or 'R' &&
                           extension[2..].All(char.IsDigit);
                }));
            return parts.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        return new[] { fullPath };
    }

    private static string SummarizeLegacyError(ExtractResult? result)
    {
        if (result is null)
        {
            return "解压失败";
        }

        if (string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            return result.FailureKind.ToString();
        }

        var line = result.ErrorMessage
            .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .LastOrDefault(value =>
                value.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("archive", StringComparison.OrdinalIgnoreCase))
            ?? result.ErrorMessage.Trim();
        return line.Length <= 300 ? line : line[..300] + "…";
    }

    private static void TryDeleteOwnedDirectory(string path, string ownedRoot)
    {
        try
        {
            var normalizedRoot = Path.GetFullPath(ownedRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var normalizedPath = Path.GetFullPath(path);
            if (normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(normalizedPath))
            {
                Directory.Delete(normalizedPath, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The directory is owned by this run. A later run can safely leave or clean it.
        }
    }

    private void ExportLog()
    {
        var dialog = new WpfDialogs.SaveFileDialog
        {
            Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            FileName = $"extractutil-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        using var writer = new StreamWriter(dialog.FileName);
        foreach (var entry in _logSink.Snapshot())
        {
            writer.WriteLine($"{entry.Timestamp:O} [{entry.Level}] {entry.Message}");
        }

        Log(LogLevel.Info, $"日志已导出：{dialog.FileName}");
    }

    private void RegisterContextMenu()
    {
        try
        {
            ContextMenuRegistration.Register(Environment.ProcessPath ?? string.Empty);
            System.Windows.MessageBox.Show("已为当前用户注册资源管理器右键菜单。", "ExtractUtil");
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"注册失败：{ex.Message}", "ExtractUtil");
        }
    }

    private void UnregisterContextMenu()
    {
        try
        {
            ContextMenuRegistration.Unregister();
            System.Windows.MessageBox.Show("已卸载资源管理器右键菜单。", "ExtractUtil");
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"卸载失败：{ex.Message}", "ExtractUtil");
        }
    }

    private void OnLogEntryAdded(object? sender, LogEntry entry)
    {
        var line = $"{entry.Timestamp:O} [{entry.Level}] {entry.Message}";
        PostToUi(() =>
        {
            LogLines.Add(line);
            while (LogLines.Count > 10_000)
            {
                LogLines.RemoveAt(0);
            }
        });
    }

    private void Log(LogLevel level, string message) => _logSink.Append(level, message);

    private void PostToUi(Action action)
    {
        if (_uiContext is null)
        {
            action();
        }
        else
        {
            _uiContext.Post(_ => action(), null);
        }
    }

    private void RaiseCommandStates()
    {
        foreach (var command in _commands)
        {
            command.RaiseCanExecuteChanged();
        }
    }

    private static string BuildItemKey(ArchiveWorkItem item)
    {
        return string.Join("|", item.SourceParts
            .Select(part => Path.GetFullPath(part.Path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
    }
}
