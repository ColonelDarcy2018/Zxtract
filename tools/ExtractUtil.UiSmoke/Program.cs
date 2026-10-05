using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Reflection;
using System.Collections.Specialized;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using ExtractUtil.App;
using ExtractUtil.App.ViewModels;
using ExtractUtil.Core.Enums;
using ExtractUtil.Core.Models;
using ExtractUtil.Core.Services;

internal static class Program
{
    // Exercise the real XAML template without opening a window or extracting files.
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var window = new MainWindow();
            var root = (FrameworkElement)window.Content;
            var grid = Descendants<DataGrid>(root).Single();
            var template = ((DataGridTemplateColumn)grid.Columns[1]).CellTemplate;
            var archivePath = Path.Combine(Path.GetTempPath(), "zxtract-ui-smoke.7z");
            var fileRow = new JobItemViewModel(new ExtractJob(archivePath, new ExtractOptions()));
            VerifyProgress(template, fileRow,
                () => fileRow.ApplyLegacyProgress(new ExtractProgress { Percentage = 42, CurrentFile = "example.txt" }), 42,
                () => fileRow.SetLegacyState(ExtractJobStatus.Completed, ArchiveTreePhase.Completed, "Completed"));

            var folderRow = new JobItemViewModel(new ArchiveWorkItem
            {
                EntryPath = archivePath,
                DisplayName = "zxtract-ui-smoke.7z",
                CanExtract = true
            });
            VerifyProgress(template, folderRow, () => folderRow.ApplyTreeProgress(new ArchiveTreeProgress
            {
                Phase = ArchiveTreePhase.Extracting,
                ExtractionProgress = new ExtractProgress { Percentage = 63, CurrentFile = "example.txt" }
            }), 63, () => folderRow.ApplyTreeProgress(new ArchiveTreeProgress { Phase = ArchiveTreePhase.Completed }));
            Console.WriteLine("PASS: real task-row template binds queued, running and completed file/folder progress.");

            if (args.Length == 2 && args[0] == "--preview")
            {
                RenderPreviews(window, args[1]);
            }
            else if (args.Length == 1)
            {
                var scan = new ArchiveScanner().ScanAsync(new ArchiveScanOptions
                {
                    RootDirectory = args[0],
                    EnableSiblingVolumeGrouping = true
                }).GetAwaiter().GetResult();
                var model = (MainViewModel)window.DataContext;
                model.SelectedWorkflowIndex = 1;
                foreach (var item in scan.Items)
                {
                    var row = new JobItemViewModel(item);
                    model.Jobs.Add(row);
                    VerifyProgress(template, row, () => { }, 0);
                }
                Layout(root);
                if (grid.Items.OfType<JobItemViewModel>().Count() != scan.Items.Count ||
                    (scan.Items.Count > 0 && !Descendants<DataGridRow>(grid).Any()))
                {
                    throw new InvalidOperationException($"Scanned tasks were not rendered in the real task list: scanned={scan.Items.Count}, jobs={model.Jobs.Count}, typedItems={grid.Items.OfType<JobItemViewModel>().Count()}, items={grid.Items.Count}, rows={Descendants<DataGridRow>(grid).Count()}.");
                }
                Console.WriteLine($"PASS: read-only folder scan and task-list layout; tasks={scan.Items.Count}, blocked={scan.BlockedCount}.");
            }

            window.Close();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void RenderPreviews(MainWindow window, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var model = (MainViewModel)window.DataContext;
        model.SelectedWorkflowIndex = 1;
        model.ScanRootDirectory = @"D:\资料\待解压";
        var names = new[] { "课程资料.7z", "项目备份.zip", "照片归档.7z", "素材包.7z.001", "文档合集.rar", "设计资源.7z" };
        var rows = names.Select(name => new JobItemViewModel(new ArchiveWorkItem
        {
            EntryPath = @"D:\资料\待解压\" + name,
            DisplayName = name,
            OutputDirectory = @"D:\资料\解压结果",
            RelativeDirectory = "待解压",
            CanExtract = true,
            SourceParts = new[] { new ArchiveSourcePart { Path = @"D:\资料\待解压\" + name } }
        })).ToArray();
        foreach (var row in rows) model.Jobs.Add(row);
        rows[0].ApplyTreeProgress(new ArchiveTreeProgress
        {
            Phase = ArchiveTreePhase.TryingPassword,
            PasswordAttempt = 1,
            PasswordAttemptCount = 1,
            Message = "密码尝试 1/1"
        });
        rows[1].ApplyTreeProgress(new ArchiveTreeProgress
        {
            Phase = ArchiveTreePhase.Extracting,
            ExtractionProgress = new ExtractProgress { Percentage = 63, CurrentFile = "年度项目备份.txt" }
        });
        rows[2].ApplyTreeProgress(new ArchiveTreeProgress { Phase = ArchiveTreePhase.Completed });
        rows[4].ApplyTreeProgress(new ArchiveTreeProgress { Phase = ArchiveTreePhase.Failed, Message = "密码不匹配，请修改密码后重试" });
        typeof(MainViewModel).GetField("_pauseController", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(model, new PauseController());
        typeof(MainViewModel).GetMethod("BeginRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, new object[] { "正在扫描并解压" });
        model.SelectedJob = rows[1];
        if (model.SessionStatusText != "正在解压" || model.SessionDetailText.Contains("密码尝试") ||
            rows[0].StatusDetail != "正在验证候选密码" || model.FilteredJobCount != rows.Length)
        {
            throw new InvalidOperationException("Session and per-job status did not remain separate, or task rows were duplicated.");
        }

        SavePreview(window, Path.Combine(outputDirectory, "folder-1200.png"), 1200, 730);
        SavePreview(window, Path.Combine(outputDirectory, "folder-1024.png"), 1024, 610);
        var resets = 0;
        model.FilteredJobs.CollectionChanged += (_, e) => { if (e.Action == NotifyCollectionChangedAction.Reset) resets++; };
        rows[1].ApplyTreeProgress(new ArchiveTreeProgress { Phase = ArchiveTreePhase.Committing });
        typeof(MainViewModel).GetMethod("RecalculateCounts", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, null);
        Layout((FrameworkElement)window.Content);
        if (!ReferenceEquals(model.SelectedJob, rows[1])) throw new InvalidOperationException("Status update cleared the selected task.");
        if (resets != 0) throw new InvalidOperationException("Progress update rebuilt the entire task list.");
        model.PauseResumeCommand.Execute(null);
        if (model.SessionStatusText != "已暂停") throw new InvalidOperationException("Pause feedback did not update.");
        SavePreview(window, Path.Combine(outputDirectory, "paused.png"), 1200, 730);
        model.PauseResumeCommand.Execute(null);
        model.JobSearchText = "does-not-exist";
        if (model.FilteredJobCount != 0 || model.EmptyListTitle != "没有匹配的文件") throw new InvalidOperationException("Search empty state did not update.");
        SavePreview(window, Path.Combine(outputDirectory, "search-empty.png"), 1024, 610);
        model.JobSearchText = string.Empty;
        model.SelectedTaskFilterIndex = 2;
        if (model.FilteredJobCount != 1) throw new InvalidOperationException("Attention filter returned the wrong tasks.");
        SavePreview(window, Path.Combine(outputDirectory, "attention.png"), 1200, 730);
        model.SelectedTaskFilterIndex = 0;
        var optionsToggle = (ToggleButton)window.FindName("OptionsToggle");
        var passwordToggle = (ToggleButton)window.FindName("PasswordToggle");
        optionsToggle.IsChecked = true;
        SavePreview(window, Path.Combine(outputDirectory, "options-1024.png"), 1024, 610);
        model.IsPasswordPanelOpen = true;
        Layout((FrameworkElement)window.Content);
        if (optionsToggle.IsChecked == true) throw new InvalidOperationException("Opening passwords left both panels open.");
        SavePreview(window, Path.Combine(outputDirectory, "password-1024.png"), 1024, 610);
        optionsToggle.IsChecked = true;
        Layout((FrameworkElement)window.Content);
        if (passwordToggle.IsChecked == true) throw new InvalidOperationException("Opening options left both panels open.");
        typeof(MainViewModel).GetMethod("EndRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, null);
        Console.WriteLine("PASS: status hierarchy, selection/refresh stability, unique rows, pause, search, filters and panel toggles; rendered 1200/1024 previews.");
    }

    private static void SavePreview(MainWindow window, string path, int width, int height)
    {
        var root = (FrameworkElement)window.Content;
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void VerifyProgress(DataTemplate template, JobItemViewModel row, Action update, int expected, Action? complete = null)
    {
        var content = (FrameworkElement)template.LoadContent();
        content.DataContext = row;
        Layout(content);
        var bar = Descendants<ProgressBar>(content).Single();
        if (bar.Value != 0 || bar.Visibility != Visibility.Collapsed)
        {
            throw new InvalidOperationException("Queued progress should start at zero and remain hidden.");
        }
        update();
        Layout(content);
        if (bar.Value != expected || bar.Visibility != (row.ShowProgress ? Visibility.Visible : Visibility.Collapsed))
        {
            throw new InvalidOperationException($"Progress did not update: expected {expected}, got {bar.Value}.");
        }
        if (complete is not null)
        {
            complete();
            Layout(content);
            if (bar.Value != 100 || bar.Visibility != Visibility.Collapsed)
            {
                throw new InvalidOperationException("Completed progress should reach 100 and become hidden.");
            }
        }
    }

    private static void Layout(FrameworkElement content)
    {
        content.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        content.Measure(new Size(1200, 760));
        content.Arrange(new Rect(0, 0, 1200, 760));
        content.UpdateLayout();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
