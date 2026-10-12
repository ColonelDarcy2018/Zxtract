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
using ExtractUtil.App.Themes;
using ExtractUtil.App.Controls;
using ExtractUtil.App.Presentation;
using System.Diagnostics;

internal static class Program
{
    // Exercise the real XAML template without opening a window or extracting files.
    [STAThread]
    private static int Main(string[] args)
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "zxtract-themes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        var bindingTrace = new BindingTrace();
        PresentationTraceSources.DataBindingSource.Listeners.Add(bindingTrace);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        try
        {
            var application = new App();
            application.InitializeComponent();
            ThemeManager.Initialize(application.Resources, new AppearanceSettingsStore(Path.Combine(temporaryDirectory, "appearance.json")));
            System.Threading.SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
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
            VerifyTaskRowStages(template);
            VerifyThemeSettings(temporaryDirectory);
            VerifyLunarSkin(window);
            VerifyStoneComposition(window, args.Length == 2 && args[0] == "--preview" ? args[1] : temporaryDirectory);

            if (args.Length == 2 && args[0] == "--moon-preview") RenderMoonFinishPreview(args[1]);
            if (args.Length == 2 && args[0] == "--docs") RenderDocumentationPreviews(window, args[1]);

            if (args.Length == 1 && args[0] == "--integration")
            {
                VerifyRealExtraction(temporaryDirectory);
            }

            if (args.Length == 2 && args[0] == "--preview")
            {
                RenderPreviews(window, args[1]);
                VerifyNativeFlyoutsAndMotion(window, args[1]);
            }
            else if (args.Length == 1 && args[0] != "--integration")
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
            if (bindingTrace.Messages.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, bindingTrace.Messages));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(bindingTrace);
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    // Documentation assets use the shipping controls with explicit sample data.
    // The phase replay below uses the exact production interpolation and renderer.
    private static void RenderDocumentationPreviews(MainWindow window, string directory)
    {
        Directory.CreateDirectory(directory);
        var model = (MainViewModel)window.DataContext;
        var themes = ThemeManager.Current;
        var original = themes.SelectedTheme;
        try
        {
            model.Jobs.Clear();
            model.SelectedWorkflowIndex = 0;
            model.SavedPasswords.Clear();
            model.SavedPasswords.Add(new SavedPasswordViewModel("sample-password"));
            model.SavedPasswords.Add(new SavedPasswordViewModel("demo-archive-key"));
            typeof(MainViewModel).GetField("_pauseController", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(model, new PauseController());
            var completed = new JobItemViewModel(new ExtractJob(@"D:\Archives\照片归档.7z", new ExtractOptions()));
            var active = new JobItemViewModel(new ExtractJob(@"D:\Archives\项目备份.zip", new ExtractOptions()));
            var queued = new JobItemViewModel(new ExtractJob(@"D:\Archives\设计资源.rar", new ExtractOptions()));
            model.Jobs.Add(completed); model.Jobs.Add(active); model.Jobs.Add(queued);
            completed.SetLegacyState(ExtractJobStatus.Completed, ArchiveTreePhase.Completed, "已完成");
            active.ApplyLegacyProgress(new ExtractProgress { Percentage = 63, CurrentFile = "年度项目备份.txt" });
            model.SelectedJob = active;
            typeof(MainViewModel).GetMethod("BeginRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, new object[] { "正在解压" });
            foreach (var theme in themes.AvailableThemes)
            {
                themes.SelectedTheme = theme;
                SavePreview(window, Path.Combine(directory, theme.Id + ".png"), 1200, 950);
            }
            themes.SelectedTheme = themes.AvailableThemes.Single(t => t.Id == "night-voyage");
            SavePreview(window, Path.Combine(directory, "file-workflow.png"), 1200, 950);
            typeof(MainViewModel).GetMethod("EndRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, null);
            var assistant = new PasswordAssistantWindow { DataContext = model };
            SaveWindowPreview(assistant, Path.Combine(directory, "password-assistant.png"), 960, 760);
            assistant.Close();

            model.Jobs.Clear();
            model.SelectedWorkflowIndex = 1;
            model.ScanRootDirectory = @"D:\Archives\待解压";
            foreach (var (name, phase) in new[] { ("项目备份.zip", ArchiveTreePhase.Extracting), ("素材包.7z.001", ArchiveTreePhase.Queued), ("照片归档.7z", ArchiveTreePhase.Completed), ("加密资料.rar", ArchiveTreePhase.Failed) })
            {
                var row = new JobItemViewModel(new ArchiveWorkItem
                {
                    EntryPath = @"D:\Archives\待解压\" + name, DisplayName = name,
                    CanExtract = true, OutputDirectory = @"D:\Archives\解压结果",
                    SourceParts = name.EndsWith(".001")
                        ? Enumerable.Range(1, 3).Select(i => new ArchiveSourcePart { Path = @"D:\Archives\待解压\素材包.7z." + i.ToString("000") }).ToArray()
                        : new[] { new ArchiveSourcePart { Path = @"D:\Archives\待解压\" + name } }
                });
                model.Jobs.Add(row);
                row.ApplyTreeProgress(new ArchiveTreeProgress
                {
                    Phase = phase, Message = phase == ArchiveTreePhase.Failed ? "密码不匹配，请修改密码后重试" : string.Empty,
                    ExtractionProgress = phase == ArchiveTreePhase.Extracting ? new ExtractProgress { Percentage = 42, CurrentFile = "项目文档.txt" } : null
                });
            }
            model.SelectedJob = model.Jobs[0];
            typeof(MainViewModel).GetMethod("BeginRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, new object[] { "正在扫描并解压" });
            themes.SelectedTheme = themes.AvailableThemes.Single(t => t.Id == "lunar-stone");
            SavePreview(window, Path.Combine(directory, "folder-workflow.png"), 1200, 950);
            typeof(MainViewModel).GetMethod("EndRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, null);
            model.Jobs[0].ApplyTreeProgress(new ArchiveTreeProgress { Phase = ArchiveTreePhase.Completed });
            model.SelectedTaskFilterIndex = 2;
            themes.SelectedTheme = themes.AvailableThemes.Single(t => t.Id == "lunar-paper");
            SavePreview(window, Path.Combine(directory, "attention-state.png"), 1200, 950);
            model.SelectedTaskFilterIndex = 0;
            themes.SelectedTheme = themes.AvailableThemes.Single(t => t.Id == "lunar-aurora");
            ((System.Windows.Controls.Button)window.FindName("NightOptionsButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            SavePreview(window, Path.Combine(directory, "extraction-options.png"), 1200, 950);
            typeof(MainWindow).GetMethod("CloseNightOptions", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);

            themes.SelectedTheme = themes.AvailableThemes.Single(t => t.Id == "night-voyage");
            var framesDirectory = Path.Combine(directory, "animation-frames");
            Directory.CreateDirectory(framesDirectory);
            var scene = new LunarScene { Width = 1024, Height = 350, IsAnimationEnabled = false };
            var interpolate = typeof(LunarScene).GetMethod("InterpolatePhase", BindingFlags.Static | BindingFlags.NonPublic)!;
            for (var i = 0; i <= 48; i++)
            {
                scene.Progress = (double)interpolate.Invoke(null, new object[] { 0d, 1d, i / 48d })! * 100;
                scene.Measure(new Size(1024, 350)); scene.Arrange(new Rect(0, 0, 1024, 350)); scene.UpdateLayout();
                var bitmap = new RenderTargetBitmap(1024, 350, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(scene);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(framesDirectory, $"{i:000}.png")); encoder.Save(stream);
            }
            Console.WriteLine("PASS: documentation screenshots and phase replay rendered from production WPF controls with sample tasks.");
        }
        finally { themes.SelectedTheme = original; }
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
        VerifyLiveThemeSwitch(window, rows[1], outputDirectory);
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
        VerifyThemePanel(window, outputDirectory, "options");
        model.IsPasswordPanelOpen = true;
        Layout((FrameworkElement)window.Content);
        if (optionsToggle.IsChecked == true) throw new InvalidOperationException("Opening passwords left both panels open.");
        SavePreview(window, Path.Combine(outputDirectory, "password-1024.png"), 1024, 610);
        VerifyThemePanel(window, outputDirectory, "password");
        optionsToggle.IsChecked = true;
        Layout((FrameworkElement)window.Content);
        if (passwordToggle.IsChecked == true) throw new InvalidOperationException("Opening options left both panels open.");
        typeof(MainViewModel).GetMethod("EndRun", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, null);
        Console.WriteLine("PASS: status hierarchy, selection/refresh stability, unique rows, pause, search, filters and panel toggles; rendered 1200/1024 previews.");

        optionsToggle.IsChecked = false;
        model.SelectedWorkflowIndex = 0;
        model.Jobs.Add(new JobItemViewModel(new ExtractJob(@"D:\资料\很长的压缩包名称-年度项目备份与设计素材版本归档.zip", new ExtractOptions())));
        SavePreview(window, Path.Combine(outputDirectory, "file-1024.png"), 1024, 610);
    }

    private static void VerifyLiveThemeSwitch(MainWindow window, JobItemViewModel selected, string outputDirectory)
    {
        var model = (MainViewModel)window.DataContext;
        var view = model.FilteredJobs;
        var rowCount = view.Cast<object>().Count();
        var picker = (System.Windows.Controls.ComboBox)window.FindName("ThemePicker");
        var themes = ThemeManager.Current;
        var original = themes.SelectedTheme;
        var dictionaryCount = System.Windows.Application.Current.Resources.MergedDictionaries.Count;
        var resets = 0;
        NotifyCollectionChangedEventHandler handler = (_, e) => { if (e.Action == NotifyCollectionChangedAction.Reset) resets++; };
        view.CollectionChanged += handler;
        try
        {
            foreach (var theme in themes.AvailableThemes)
            {
                picker.SelectedItem = theme; // Exercise the real two-way UI binding.
                var minimumHeight = theme.HasLunarScene ? 720 : 610;
                SavePreview(window, Path.Combine(outputDirectory, theme.Id + "-1024.png"), 1024, minimumHeight);
                if (theme.HasLunarScene && ((LunarScene)window.FindName("NightScene")).ArtworkThemeId != theme.Id)
                    throw new InvalidOperationException("Lunar artwork did not follow the active skin: " + theme.Id);
                if (themes.SelectedTheme != theme || !ReferenceEquals(model, window.DataContext) ||
                    !ReferenceEquals(view, model.FilteredJobs) || !ReferenceEquals(selected, model.SelectedJob) ||
                    view.Cast<object>().Count() != rowCount || !model.IsBusy || selected.ProgressPercent != 63 || resets != 0 ||
                    System.Windows.Application.Current.Resources.MergedDictionaries.Count != dictionaryCount)
                    throw new InvalidOperationException("Changing skin changed the extraction state or failed to update the picker.");
                var surface = (SolidColorBrush)window.FindResource("WindowBrush");
                if (((SolidColorBrush)window.Background).Color != surface.Color)
                    throw new InvalidOperationException("Window palette did not refresh live.");
                var popup = (Popup)window.FindName("LogPopup");
                var logPanel = (Border)popup.Child;
                if (((SolidColorBrush)logPanel.Background).Color != ((SolidColorBrush)window.FindResource("SurfaceBrush")).Color)
                    throw new InvalidOperationException("Log panel did not inherit the active skin.");
                var copy = (System.Windows.Controls.Button)window.FindName("FocusCopyButton");
                var point = copy.TranslatePoint(new System.Windows.Point(), (FrameworkElement)window.Content);
                if (!theme.HasLunarScene && (copy.ActualHeight < 30 || point.Y < 0 || point.Y + copy.ActualHeight > minimumHeight))
                    throw new InvalidOperationException("Focused-task action is clipped at the minimum window size.");
                var progress = (ProgressBar)window.FindName("FocusProgressBar");
                var progressPoint = progress.TranslatePoint(new System.Windows.Point(), (FrameworkElement)window.Content);
                if (progress.Value != 63 || (!theme.HasLunarScene && progressPoint.Y + progress.ActualHeight > point.Y))
                    throw new InvalidOperationException("Focused-task progress is obscured by fixed actions.");
                if (theme.HasLunarScene && !HasCollapsedAncestor(copy))
                    throw new InvalidOperationException("Night skin retained the redundant focused-task sidebar.");
                SaveWindowPreview(window, Path.Combine(outputDirectory, theme.Id + "-150dpi.png"), 1024, minimumHeight, 1.5);
                if (theme.HasLunarScene)
                {
                    SavePreview(window, Path.Combine(outputDirectory, theme.Id + "-1200.png"), 1200, 820);
                    if (theme.Id == "night-voyage") VerifyNightComposition(window, outputDirectory);
                }
                var assistant = new PasswordAssistantWindow { DataContext = model };
                SaveWindowPreview(assistant, Path.Combine(outputDirectory, theme.Id + "-assistant.png"), 680, 530);
                if (((SolidColorBrush)assistant.Background).Color != surface.Color)
                    throw new InvalidOperationException("Password assistant did not inherit the active skin.");
                themes.SelectedTheme = themes.AvailableThemes.First(t => t != theme);
                Layout((FrameworkElement)assistant.Content);
                if (((SolidColorBrush)assistant.Background).Color == surface.Color)
                    throw new InvalidOperationException("An already-open assistant did not refresh its skin.");
                assistant.Close();
                themes.SelectedTheme = theme;
                model.PauseResumeCommand.Execute(null);
                themes.SelectedTheme = themes.AvailableThemes.First(t => t != theme);
                if (!model.IsPaused || !ReferenceEquals(model.SelectedJob, selected))
                    throw new InvalidOperationException("Changing skin resumed scheduling or lost selection.");
                model.PauseResumeCommand.Execute(null);
                themes.SelectedTheme = theme;
            }
        }
        finally
        {
            themes.SelectedTheme = original;
            view.CollectionChanged -= handler;
        }
        Console.WriteLine("PASS: live skin picker, active/paused queue stability, selection, collection identity and password assistant theming.");
    }

    private static void VerifyStoneComposition(MainWindow window, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var themes = ThemeManager.Current;
        var original = themes.SelectedTheme;
        var model = (MainViewModel)window.DataContext;
        var view = model.FilteredJobs;
        var root = (FrameworkElement)window.Content;
        var grid = (DataGrid)window.FindName("TaskGrid");
        var hero = (FrameworkElement)window.FindName("NightHero");
        var settings = (ToggleButton)window.FindName("SettingsToggle");
        var picker = (ComboBox)window.FindName("ThemePicker");
        try
        {
            foreach (var theme in themes.AvailableThemes)
            {
                settings.IsChecked = true;
                picker.SelectedItem = themes.AvailableThemes.First(t => t != theme);
                settings.IsChecked = true;
                picker.SelectedItem = theme;
                root.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                if (settings.IsChecked == true || ((Popup)window.FindName("SettingsPopup")).IsOpen)
                    throw new InvalidOperationException("Theme selection did not dismiss appearance settings: " + theme.Id);
            }
            themes.SelectedTheme = themes.AvailableThemes.Single(t => t.Id == "lunar-stone");
            foreach (var size in new[] { (1024, 720), (1200, 860), (1440, 1000) })
            {
                SavePreview(window, Path.Combine(outputDirectory, $"stone-layout-{size.Item1}.png"), size.Item1, size.Item2);
                var scene = (LunarScene)window.FindName("NightScene");
                if (scene.ArtworkThemeId != "lunar-stone" || Math.Abs(scene.MoonBounds.Width - 103) > .01 || Math.Abs(scene.MoonBounds.Y - 85) > .01)
                    throw new InvalidOperationException("Stone artwork or fixed moon placement does not match its scenic rail.");
                var hp = hero.TranslatePoint(new Point(), root);
                var gp = grid.TranslatePoint(new Point(), root);
                var button = (FrameworkElement)window.FindName("NightOptionsButton");
                var bp = button.TranslatePoint(new Point(), root);
                if (Math.Abs(hero.ActualWidth - 296) > 1 || hp.X > 1 || gp.X < 322 || gp.Y > 151 ||
                    hero.ActualHeight < size.Item2 - 85 || bp.X + button.ActualWidth > 296 || bp.Y + button.ActualHeight > size.Item2 - 40 ||
                    !ReferenceEquals(view, grid.ItemsSource) || Descendants<DataGrid>(root).Count() != 1)
                    throw new InvalidOperationException("Stone left rail/right queue geometry or shared-control contract failed.");
            }
            ((System.Windows.Controls.Button)window.FindName("NightOptionsButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            SavePreview(window, Path.Combine(outputDirectory, "stone-options.png"), 1024, 720);
            typeof(MainWindow).GetMethod("CloseNightOptions", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
            themes.SelectedTheme = themes.AvailableThemes.Single(t => t.Id == "night-voyage");
            SavePreview(window, Path.Combine(outputDirectory, "stone-back-to-night.png"), 1024, 950);
            if (Math.Abs(grid.TranslatePoint(new Point(), root).X - 36) > 1 || Math.Abs(hero.ActualHeight - 350) > 1 ||
                !ReferenceEquals(model, window.DataContext) || !ReferenceEquals(view, model.FilteredJobs))
                throw new InvalidOperationException("Leaving stone failed to restore the normal composition or task state.");
        }
        finally { settings.IsChecked = false; themes.SelectedTheme = original; }
        Console.WriteLine("PASS: stone 296px scenic rail/right shared queue, 1024/1200/1440 layouts, options and theme-picker auto-dismiss for every skin.");
    }

    private static void VerifyNightComposition(MainWindow window, string outputDirectory)
    {
        var root = (FrameworkElement)window.Content;
        var model = (MainViewModel)window.DataContext;
        SavePreview(window, Path.Combine(outputDirectory, "night-layout-1024.png"), 1024, 950);
        FrameworkElement Element(string name) => (FrameworkElement)window.FindName(name);
        System.Windows.Point Position(string name) => Element(name).TranslatePoint(new System.Windows.Point(), root);
        var grid = (DataGrid)window.FindName("TaskGrid");
        if (Math.Abs(Element("NightHero").ActualHeight - 350) > 1 ||
            Math.Abs(Position("NightTabs").Y - 52) > 1 ||
            Math.Abs(Position("NightActions").Y - 246) > 1 ||
            Math.Abs(Position("NightActions").X - 36) > 1 ||
            Math.Abs(Position("TaskGrid").Y - 448) > 1 ||
            Math.Abs(Position("TaskGrid").X - 36) > 1 || grid.RowHeight != 60 ||
            !HasCollapsedAncestor(Element("PasswordToggle")) || !HasCollapsedAncestor(Element("FocusPanel")))
            throw new InvalidOperationException("Night composition drifted from the approved 42px caption / 350px scene / 36px inset / 60px rows.");
        var button = (System.Windows.Controls.Button)window.FindName("NightOptionsButton");
        button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        SavePreview(window, Path.Combine(outputDirectory, "night-layout-options-1024.png"), 1024, 950);
        var overlay = Element("NightOptionsOverlay");
        var panel = (ExtractUtil.App.Controls.NightOptionsPanel)window.FindName("NightOptions");
        if (overlay.Visibility != Visibility.Visible || Element("WorkspaceRoot").IsHitTestVisible ||
            Math.Abs(panel.ActualWidth - 530) > 1 || Math.Abs(Position("NightOptions").X - 247) > 1 ||
            ((FrameworkElement)panel.FindName("EditableOptions")).IsEnabled || !ReferenceEquals(panel.DataContext, model))
            throw new InvalidOperationException("Centered options dialog failed width, read-only or binding contract.");
        SaveWindowPreview(window, Path.Combine(outputDirectory, "night-layout-options-150dpi.png"), 1024, 720, 1.5);
        typeof(MainWindow).GetMethod("CloseNightOptions", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
        if (overlay.Visibility != Visibility.Collapsed || !Element("WorkspaceRoot").IsHitTestVisible)
            throw new InvalidOperationException("Closing night settings left the workspace blocked.");
        Console.WriteLine("PASS: approved night composition geometry, compact rows, centered 530px read-only options, shared bindings and modal dismissal.");
    }

    private static void VerifyThemePanel(MainWindow window, string outputDirectory, string panel)
    {
        var themes = ThemeManager.Current;
        themes.SelectedTheme = themes.AvailableThemes[1];
        SavePreview(window, Path.Combine(outputDirectory, "clear-light-" + panel + ".png"), 1024, 610);
        themes.SelectedTheme = themes.AvailableThemes[0];
    }

    private static void VerifyThemeSettings(string temporaryDirectory)
    {
        var path = Path.Combine(temporaryDirectory, "test-appearance.json");
        var store = new AppearanceSettingsStore(path);
        var manager = new ThemeManager(new ResourceDictionary(), store);
        if (manager.SelectedTheme.Id != "ink-flow") throw new InvalidOperationException("Missing settings did not default to Ink Flow.");
        manager.SelectedTheme = manager.AvailableThemes[1];
        if (new ThemeManager(new ResourceDictionary(), store).SelectedTheme.Id != "clear-light")
            throw new InvalidOperationException("Skin preference was not restored.");
        File.WriteAllText(path, "{not-json");
        if (new ThemeManager(new ResourceDictionary(), store).SelectedTheme.Id != "ink-flow")
            throw new InvalidOperationException("Corrupt settings did not fall back safely.");
        store.TrySave("removed-theme");
        if (new ThemeManager(new ResourceDictionary(), store).SelectedTheme.Id != "ink-flow")
            throw new InvalidOperationException("Unknown skin did not fall back safely.");
        var blocked = new ThemeManager(new ResourceDictionary(), new AppearanceSettingsStore(temporaryDirectory));
        blocked.SelectedTheme = blocked.AvailableThemes[1];
        if (blocked.SelectedTheme.Id != "clear-light" || !blocked.StatusText.Contains("无法保存"))
            throw new InvalidOperationException("Failed preference persistence blocked runtime switching or was not reported.");
        var dictionaries = manager.AvailableThemes.Select(theme => new ResourceDictionary
        { Source = new Uri($"/Zxtract;component/{theme.ResourcePath}", UriKind.Relative) }).ToArray();
        if (dictionaries.Skip(1).Any(dictionary => !dictionaries[0].Keys.Cast<object>().ToHashSet().SetEquals(dictionary.Keys.Cast<object>())))
            throw new InvalidOperationException("Skins have different resource contracts.");
        foreach (var theme in manager.AvailableThemes)
        {
            manager.SelectedTheme = theme;
            if (new ThemeManager(new ResourceDictionary(), store).SelectedTheme.Id != theme.Id)
                throw new InvalidOperationException($"Skin preference was not restored for {theme.Id}.");
        }
        Console.WriteLine("PASS: skin resource contract, persistence, missing/corrupt/unknown settings and read/write failure fallback.");
    }

    private sealed class BindingTrace : TraceListener
    {
        public List<string> Messages { get; } = new();
        public override void Write(string? message) { if (message is not null) Messages.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }

    private static void VerifyNativeFlyoutsAndMotion(MainWindow window, string outputDirectory)
    {
        var themes = ThemeManager.Current;
        var original = themes.SelectedTheme;
        var scene = (LunarScene)window.FindName("NightScene");
        themes.SelectedTheme = themes.AvailableThemes.Single(t => t.Id == "night-voyage");
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -10000;
        window.Top = -10000;
        window.Show();
        void Pump(int milliseconds)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }
        try
        {
            Pump(80);
            var nightOptionsButton = (System.Windows.Controls.Button)window.FindName("NightOptionsButton");
            var nightOverlay = (FrameworkElement)window.FindName("NightOptionsOverlay");
            var nightPanel = (NightOptionsPanel)window.FindName("NightOptions");
            var nightModel = (MainViewModel)window.DataContext;
            nightOptionsButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(60);
            if (!nightPanel.IsKeyboardFocusWithin || !((FrameworkElement)nightPanel.FindName("EditableOptions")).IsEnabled)
                throw new InvalidOperationException("Idle night settings did not receive keyboard focus or remain editable.");
            var originalInfer = nightModel.InferPasswordsFromPath;
            var inference = Descendants<CheckBox>(nightPanel).Single(c => c.Content?.ToString() == "从文件名和路径推断");
            inference.IsChecked = !originalInfer;
            Pump(30);
            if (nightModel.InferPasswordsFromPath == originalInfer || !window.LunarStatus!.SummaryMain.Contains(nightModel.PasswordSummary))
                throw new InvalidOperationException("Night option edits did not update the real settings and summary.");
            inference.IsChecked = originalInfer;
            var modalEscape = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(nightPanel)!, 0, System.Windows.Input.Key.Escape) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
            nightPanel.RaiseEvent(modalEscape);
            Pump(30);
            if (!modalEscape.Handled || nightOverlay.Visibility != Visibility.Collapsed || !nightOptionsButton.IsKeyboardFocused)
                throw new InvalidOperationException("Night dialog Escape failed to dismiss and restore focus.");
            Console.WriteLine("PASS: live centered dialog focus, real option editing/summary synchronization, Escape and focus restoration.");
            var settings = (ToggleButton)window.FindName("SettingsToggle");
            var options = (ToggleButton)window.FindName("OptionsToggle");
            var password = (ToggleButton)window.FindName("PasswordToggle");
            foreach (var (toggle, name, file) in new[] { (settings, "SettingsPopup", "settings"), (options, "OptionsPopup", "options"), (password, "PasswordPopup", "password") })
            {
                toggle.IsChecked = true;
                Pump(80);
                var popup = (Popup)window.FindName(name);
                if (!popup.IsOpen || new[] { settings, options, password }.Count(t => t.IsChecked == true) != 1)
                    throw new InvalidOperationException("Flyout failed to open exclusively: " + name);
                var child = (FrameworkElement)popup.Child;
                child.UpdateLayout();
                if (child.ActualWidth <= 0 || child.ActualHeight <= 0)
                    throw new InvalidOperationException("Empty flyout: " + name);
                if (name != "SettingsPopup" && !ReferenceEquals(child.DataContext, window.DataContext))
                    throw new InvalidOperationException("Flyout lost its task bindings: " + name);
                var bmp = new RenderTargetBitmap((int)Math.Ceiling(child.ActualWidth), (int)Math.Ceiling(child.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bmp.Render(child);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bmp));
                using (var stream = File.Create(Path.Combine(outputDirectory, "night-voyage-" + file + "-flyout.png"))) encoder.Save(stream);
                var escape = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                    PresentationSource.FromVisual(child)!, 0, System.Windows.Input.Key.Escape) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
                typeof(MainWindow).GetMethod("OnFlyoutKeyDown", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, new object[] { child, escape });
                Pump(30);
                if (popup.IsOpen || !escape.Handled) throw new InvalidOperationException("Escape did not dismiss flyout: " + name);
            }
            var model = (MainViewModel)window.DataContext;
            model.SelectedWorkflowIndex = 0;
            model.Jobs.Clear();
            var row = new JobItemViewModel(new ExtractJob(@"C:\smoke\moon-runtime.7z", new ExtractOptions()));
            model.Jobs.Add(row);
            Pump(720);
            row.ApplyLegacyProgress(new ExtractProgress { Percentage = 70 });
            Pump(150);
            var activity = Descendants<TaskActivityIndicator>((FrameworkElement)window.Content).Single(a => ReferenceEquals(a.DataContext, row));
            var rowBar = Descendants<ProgressBar>((FrameworkElement)window.Content).Single(b => b.Name == "NightRowProgress" && ReferenceEquals(b.DataContext, row));
            if (!activity.IsActive || HasCollapsedAncestor(rowBar) || rowBar.Value != 70 ||
                SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast && !activity.IsAnimating)
                throw new InvalidOperationException("Running row lost its visible percentage/progress or live activity cue.");
            if (SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast && (!scene.IsAnimating || scene.RenderedProgress <= 0 || scene.RenderedProgress >= 70))
                throw new InvalidOperationException("Real progress did not animate through intermediate phases.");
            Pump(1500);
            if (scene.IsAnimating || Math.Abs(scene.RenderedProgress - 70) > .001)
                throw new InvalidOperationException("Moon did not settle on measured progress.");
            var nativePicker = (ComboBox)window.FindName("ThemePicker");
            var nativeSettingsPopup = (Popup)window.FindName("SettingsPopup");
            window.Width = 1024;
            window.Height = 720;
            foreach (var theme in themes.AvailableThemes.Where(t => t.HasLunarScene).Reverse())
            {
                settings.IsChecked = true;
                Pump(50);
                if (!nativeSettingsPopup.IsOpen) throw new InvalidOperationException("Settings did not open before native theme selection.");
                nativePicker.SelectedItem = theme;
                Pump(100);
                if (nativeSettingsPopup.IsOpen || settings.IsChecked == true || scene.ArtworkThemeId != theme.Id ||
                    !ReferenceEquals(model.Jobs.Single(), row) || row.ProgressPercent != 70)
                    throw new InvalidOperationException("Native theme selection failed dismissal, artwork or task preservation: " + theme.Id);
                var content = (FrameworkElement)window.Content;
                if (theme.Id == "lunar-stone")
                {
                    var taskGrid = (DataGrid)window.FindName("TaskGrid");
                    if (Math.Abs(taskGrid.Columns.Sum(c => c.ActualWidth) - taskGrid.ActualWidth) > 3)
                        throw new InvalidOperationException($"Stone columns do not fill the queue at the minimum native window size: grid={taskGrid.ActualWidth}, columns={string.Join(',', taskGrid.Columns.Select(c => c.ActualWidth))}, specs={string.Join(',', taskGrid.Columns.Select(c => c.Width))}.");
                }
                SavePreview(window, Path.Combine(outputDirectory, theme.Id + "-native.png"), (int)content.ActualWidth, (int)content.ActualHeight);
            }
            Console.WriteLine("PASS: native theme selection closes the open popup and refreshes all four lunar scenes without losing the running task.");
            var renders = scene.RenderCount;
            scene.Focus(); Pump(100);
            scene.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent });
            Pump(100);
            if (scene.IsAnimating || scene.RenderedProgress != 70 || scene.RenderCount != renders)
                throw new InvalidOperationException("Hover/focus changed the lunar painting or animation.");
            row.ApplyLegacyProgress(new ExtractProgress { Percentage = 80 });
            window.WindowState = WindowState.Minimized; Pump(80);
            if (scene.IsAnimating) throw new InvalidOperationException("Minimized moon keeps animating.");
            if (activity.IsAnimating) throw new InvalidOperationException("Minimized row keeps animating.");
            window.WindowState = WindowState.Normal; Pump(80);
            row.ApplyLegacyProgress(new ExtractProgress { Percentage = 90 });
            themes.SelectedTheme = original; Pump(80);
            if (scene.IsAnimating) throw new InvalidOperationException("Inactive skin keeps animating.");
            if (activity.IsAnimating) throw new InvalidOperationException("Inactive row skin keeps animating.");
            themes.SelectedTheme = themes.AvailableThemes.Single(t => t.Id == "night-voyage"); Pump(80);
            activity.IsAnimationEnabled = false; Pump(40);
            if (activity.IsAnimating) throw new InvalidOperationException("Reduced row motion did not stop.");
            activity.IsAnimationEnabled = true;
            row.SetLegacyState(ExtractJobStatus.Running, ArchiveTreePhase.Committing, "写入输出目录"); Pump(60);
            if (rowBar.Visibility != Visibility.Collapsed || !activity.IsActive)
                throw new InvalidOperationException("Committing row retained a misleading percentage or lost activity.");
            row.SetLegacyState(ExtractJobStatus.Completed, ArchiveTreePhase.Completed, "Completed"); Pump(60);
            if (activity.IsAnimating || activity.IsActive) throw new InvalidOperationException("Completed row keeps animating.");
            var completionBounds = scene.MoonBounds;
            Pump(220);
            if (scene.Progress != 100 || SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast &&
                (!scene.IsAnimating || scene.RenderedProgress >= 100 || scene.RenderedProgress < 90))
                throw new InvalidOperationException("Completion snapped the moon instead of easing its final phase.");
            Pump(1200);
            if (scene.IsAnimating || Math.Abs(scene.RenderedProgress - 100) > .001 || scene.MoonBounds != completionBounds)
                throw new InvalidOperationException("Completion failed to settle at full moon with an unchanged disk size.");
            themes.SelectedTheme = original;
            Console.WriteLine("PASS: live task-row activity, visible actual progress, commit without fake percent, completion/reduced-motion/minimized/inactive shutdown.");
            Console.WriteLine("PASS: native flyout bindings, exclusivity/Escape, real progress transition, no hover repaint and minimized/inactive timer shutdown.");
        }
        finally { window.Hide(); themes.SelectedTheme = original; }
    }

    private static void VerifyLunarSkin(MainWindow window)
    {
        var themes = ThemeManager.Current;
        var original = themes.SelectedTheme;
        var lunarTheme = themes.AvailableThemes.Single(theme => theme.Id == "night-voyage");
        var lunarThemes = themes.AvailableThemes.Where(theme => theme.HasLunarScene).ToArray();
        if (!lunarTheme.HasLunarScene || lunarThemes.Length < 4)
            throw new InvalidOperationException("The four lunar skins are not registered.");
        try
        {
            LunarScene? scene = null;
            foreach (var theme in lunarThemes)
            {
                themes.SelectedTheme = theme;
                Layout((FrameworkElement)window.Content);
                scene = (LunarScene)window.FindName("NightScene");
                if (HasCollapsedAncestor(scene) || scene.ActualWidth <= 0 || scene.ActualHeight <= 0 ||
                    !scene.Focusable || scene.ToolTip is null)
                    throw new InvalidOperationException($"Lunar skin {theme.Id} is missing its visible, keyboard-accessible scene and tooltip.");
                if (window.FindName("SettingsToggle") is not ToggleButton || window.FindName("ThemePicker") is not ComboBox)
                    throw new InvalidOperationException("Appearance settings are not available from the settings control.");
                var model = (MainViewModel)window.DataContext;
                if (window.LunarStatus is null || scene.Progress != window.LunarStatus.Progress ||
                    scene.IsIndeterminate != window.LunarStatus.IsIndeterminate)
                    throw new InvalidOperationException("The lunar scene is not bound to the real presentation observer.");
            }
            themes.SelectedTheme = themes.AvailableThemes.First(theme => !theme.HasLunarScene);
            Layout((FrameworkElement)window.Content);
            if (scene is null || !HasCollapsedAncestor(scene) || scene.IsAnimating)
                throw new InvalidOperationException("Inactive lunar skin retained its scene or animation work.");
        }
        finally { themes.SelectedTheme = original; }

        VerifyLunarPresentation();
        VerifyLunarPixels();
        VerifyLunarFinishContinuity();
        Console.WriteLine("PASS: lunar skin capability, real status binding, settings access, tooltip focus, pure workflow observer and opaque limb-bounded phase rendering.");
    }

    private static void VerifyLunarPresentation()
    {
        var model = new MainViewModel();
        using var presenter = new LunarStatusPresenter(model);
        if (presenter.Progress != 100 || presenter.IsIndeterminate)
            throw new InvalidOperationException("An idle empty moon must remain decorative, not pretend to be an active task.");
        var first = new JobItemViewModel(new ExtractJob(@"C:\smoke\first.7z", new ExtractOptions()));
        var second = new JobItemViewModel(new ExtractJob(@"C:\smoke\second.7z", new ExtractOptions()));
        var folder = new JobItemViewModel(new ArchiveWorkItem { EntryPath = @"C:\smoke\folder.7z", DisplayName = "folder.7z", CanExtract = true });
        model.Jobs.Add(first);
        model.Jobs.Add(second);
        model.Jobs.Add(folder);
        folder.ApplyTreeProgress(new ArchiveTreeProgress { Phase = ArchiveTreePhase.Completed });
        if (presenter.Progress != 0) throw new InvalidOperationException("A hidden workflow changed the moon's progress.");
        first.ApplyLegacyProgress(new ExtractProgress { Percentage = 63, CurrentFile = "example.txt" });
        second.SetLegacyState(ExtractJobStatus.Completed, ArchiveTreePhase.Completed, "Completed");
        if (Math.Abs(presenter.Progress - 81.5) > .0001 || presenter.IsIndeterminate)
            throw new InvalidOperationException("Task-weighted moon progress ignored measured work or completed tasks.");
        first.SetLegacyState(ExtractJobStatus.Failed, ArchiveTreePhase.Failed, "Failed");
        if (presenter.Progress != 50) throw new InvalidOperationException("A failed task incorrectly became successful lunar progress.");
        model.SelectedWorkflowIndex = 1;
        if (presenter.Progress != 100) throw new InvalidOperationException("Switching workflow did not change the observed lunar progress.");
        folder.ApplyTreeProgress(new ArchiveTreeProgress { Phase = ArchiveTreePhase.TryingPassword, PasswordAttempt = 1, PasswordAttemptCount = 1 });
        typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsBusy))!.SetValue(model, true);
        if (!presenter.IsIndeterminate || presenter.Progress != 0)
            throw new InvalidOperationException("Password validation manufactured a determinate percentage.");
        folder.ApplyTreeProgress(new ArchiveTreeProgress { Phase = ArchiveTreePhase.Extracting, ExtractionProgress = new ExtractProgress { Percentage = 100 } });
        if (presenter.Progress >= 100 || presenter.IsIndeterminate)
            throw new InvalidOperationException("An in-flight job became a completed full moon before commit.");
        folder.ApplyTreeProgress(new ArchiveTreeProgress { Phase = ArchiveTreePhase.Completed });
        if (presenter.Progress != 100) throw new InvalidOperationException("Completion did not produce the full moon.");
        model.Jobs.Clear();
        if (!presenter.IsIndeterminate || presenter.Progress != 0)
            throw new InvalidOperationException("Scanning an empty queue manufactured progress.");
        typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsBusy))!.SetValue(model, false);
        if (presenter.Progress != 100 || presenter.IsIndeterminate || first.ProgressPercent != 63 || first.Status != ExtractJobStatus.Failed)
            throw new InvalidOperationException("The presentation observer mutated task state or did not reset after scanning.");
        var afterDispose = presenter.Progress;
        presenter.Dispose();
        model.Jobs.Add(new JobItemViewModel(new ExtractJob(@"C:\smoke\after-dispose.7z", new ExtractOptions())));
        if (presenter.Progress != afterDispose) throw new InvalidOperationException("Disposed lunar presentation kept observing jobs.");
    }

    private static void VerifyLunarPixels()
    {
        const int width = 900, height = 286;
        var scene = new LunarScene { Width = width, Height = height, IsAnimationEnabled = false };
        var frames = new List<byte[]>();
        byte[]? originalLunarAlpha = null;
        foreach (var phase in new[] { 0.0, 16, 50, 100 })
        {
            scene.Progress = phase;
            scene.Measure(new Size(width, height));
            scene.Arrange(new Rect(0, 0, width, height));
            scene.InvalidateVisual();
            scene.UpdateLayout();
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(scene);
            var pixels = new byte[width * height * 4];
            bitmap.CopyPixels(pixels, width * 4, 0);
            frames.Add(pixels);
            if (Math.Abs(scene.RenderedProgress - phase) > .001 || scene.IsAnimating)
                throw new InvalidOperationException("Reduced-motion moon failed to snap to real progress.");
            // Read the moon-only raster: landscape exposure is intentionally phase-dependent,
            // but neither lunar transparency nor any external glow may depend on phase.
            var lunar = (WriteableBitmap)typeof(LunarScene).GetField("_moonBitmap", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
            var rasterBounds = (Rect)typeof(LunarScene).GetField("_bitmapBounds", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
            var lunarPixels = new byte[lunar.PixelWidth * lunar.PixelHeight * 4];
            lunar.CopyPixels(lunarPixels, lunar.PixelWidth * 4, 0);
            var alpha = Enumerable.Range(0, lunar.PixelWidth * lunar.PixelHeight).Select(index => lunarPixels[index * 4 + 3]).ToArray();
            if (originalLunarAlpha is not null && !alpha.SequenceEqual(originalLunarAlpha))
                throw new InvalidOperationException("Lunar alpha changes with phase, producing a see-through shadow.");
            originalLunarAlpha ??= alpha;
            var moon = scene.MoonBounds;
            for (var y = 0; y < lunar.PixelHeight; y++)
            for (var x = 0; x < lunar.PixelWidth; x++)
            {
                var px = rasterBounds.X + (x + .5) * rasterBounds.Width / lunar.PixelWidth;
                var py = rasterBounds.Y + (y + .5) * rasterBounds.Height / lunar.PixelHeight;
                var nx = (px - moon.X - moon.Width / 2) / (moon.Width / 2);
                var ny = (py - moon.Y - moon.Height / 2) / (moon.Height / 2);
                var radius = Math.Sqrt(nx * nx + ny * ny);
                if (radius < .97 && alpha[y * lunar.PixelWidth + x] != 255)
                    throw new InvalidOperationException("The lunar body is not fully opaque.");
                if (radius > 1 + 1.5 / (moon.Width / 2) && alpha[y * lunar.PixelWidth + x] != 0)
                    throw new InvalidOperationException("Moon-only raster emits outside its antialiased limb.");
            }
        }
        var bounds = scene.MoonBounds;
        if (bounds.IsEmpty || bounds.Width < 20 || bounds.Left < 0 || bounds.Right > width || bounds.Top < 0 || bounds.Bottom > height)
            throw new InvalidOperationException("Lunar scene has invalid or clipped moon geometry.");
        bounds.Inflate(2, 2);
        var changingPixels = 0;
        for (var i = 0; i < width * height; i++)
        {
            var offset = i * 4;
            var point = new Point(i % width + .5, i / width + .5);
            if (frames.Any(frame => frame[offset + 3] != 255))
                throw new InvalidOperationException("Moon phase rendering introduced transparency into the landscape.");
            var changed = frames.Skip(1).Any(frame => Enumerable.Range(0, 3).Any(channel => frame[offset + channel] != frames[0][offset + channel]));
            if (!changed) continue;
            if (bounds.Contains(point)) changingPixels++;
        }
        if (changingPixels < bounds.Width * bounds.Height * .15)
            throw new InvalidOperationException("The moon did not visibly change phase.");
    }

    private static void VerifyLunarFinishContinuity()
    {
        var interpolate = typeof(LunarScene).GetMethod("InterpolatePhase", BindingFlags.Static | BindingFlags.NonPublic)!;
        var scene = new LunarScene { Width = 1024, Height = 350, IsAnimationEnabled = false };
        foreach (var start in new[] { 0d, .5, .9, .94, .99 })
        {
            byte[]? previous = null;
            byte[]? alpha = null;
            Rect? originalBounds = null;
            var last = start;
            var deltas = new List<double>();
            for (var frame = 0; frame <= 40; frame++)
            {
                var phase = (double)interpolate.Invoke(null, new object[] { start, 1d, frame / 40d })!;
                if (phase < last - 1e-10 || phase > 1 || frame == 40 && Math.Abs(phase - 1) > 1e-10)
                    throw new InvalidOperationException("Moon completion interpolation is not monotonic/exact.");
                last = phase;
                scene.Progress = phase * 100;
                scene.Measure(new Size(1024, 350)); scene.Arrange(new Rect(0, 0, 1024, 350)); scene.UpdateLayout();
                new RenderTargetBitmap(1024, 350, 96, 96, PixelFormats.Pbgra32).Render(scene);
                var lunar = (WriteableBitmap)typeof(LunarScene).GetField("_moonBitmap", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
                var pixels = new byte[lunar.PixelWidth * lunar.PixelHeight * 4]; lunar.CopyPixels(pixels, lunar.PixelWidth * 4, 0);
                var currentAlpha = Enumerable.Range(0, lunar.PixelWidth * lunar.PixelHeight).Select(i => pixels[i * 4 + 3]).ToArray();
                if (alpha is not null && !alpha.SequenceEqual(currentAlpha) || originalBounds is not null && originalBounds.Value != scene.MoonBounds)
                    throw new InvalidOperationException("Moon completion changes disk radius or alpha.");
                alpha ??= currentAlpha; originalBounds ??= scene.MoonBounds;
                if (previous is not null)
                    deltas.Add(pixels.Select((b, i) => i % 4 == 3 ? 0 : Math.Abs(b - previous[i])).Average());
                previous = pixels;
            }
            if (deltas.Last() > .05 || deltas.TakeLast(4).Max() > deltas.Max() * .12 + .02)
                throw new InvalidOperationException("The final lunar frames still produce a brightness/area pop.");
            Console.WriteLine($"PASS: lunar finish {start * 100:0}->100%, fixed radius/alpha, last frame RGB delta={deltas.Last():F4}, peak={deltas.Max():F4}.");
        }
    }

    private static void RenderMoonFinishPreview(string directory)
    {
        Directory.CreateDirectory(directory);
        var scene = new LunarScene { Width = 1024, Height = 350, IsAnimationEnabled = false };
        var phases = new[] { 90d, 94, 96, 98, 99, 99.5, 99.9, 100 };
        var sheet = new DrawingVisual();
        var previous = Array.Empty<byte>();
        using (var drawing = sheet.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(11, 25, 30)), null, new Rect(0, 0, 800, 360));
            for (var i = 0; i < phases.Length; i++)
            {
                scene.Progress = phases[i];
                scene.Measure(new Size(1024, 350)); scene.Arrange(new Rect(0, 0, 1024, 350)); scene.UpdateLayout();
                var bitmap = new RenderTargetBitmap(1024, 350, 96, 96, PixelFormats.Pbgra32); bitmap.Render(scene);
                var bounds = scene.MoonBounds; bounds.Inflate(20, 20);
                var crop = new CroppedBitmap(bitmap, new Int32Rect((int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height));
                drawing.DrawImage(crop, new Rect(i % 4 * 200 + 28, i / 4 * 180 + 12, 144, 144));
                drawing.DrawText(new FormattedText(phases[i] + "%", System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Consolas"), 13, Brushes.White, 1), new Point(i % 4 * 200 + 75, i / 4 * 180 + 155));
                var pixels = new byte[crop.PixelWidth * crop.PixelHeight * 4]; crop.CopyPixels(pixels, crop.PixelWidth * 4, 0);
                if (previous.Length == pixels.Length)
                    Console.WriteLine($"MOON {phases[i - 1]}->{phases[i]}: mean RGB delta {pixels.Select((b, index) => index % 4 == 3 ? 0 : Math.Abs(b - previous[index])).Average():F3}");
                previous = pixels;
            }
        }
        var output = new RenderTargetBitmap(800, 360, 96, 96, PixelFormats.Pbgra32); output.Render(sheet);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(output));
        using var stream = File.Create(Path.Combine(directory, "moon-finish-phases.png")); encoder.Save(stream);
    }

    private static void VerifyRealExtraction(string temporaryDirectory)
    {
        var source = Path.Combine(temporaryDirectory, "fixture-input");
        var archives = Path.Combine(temporaryDirectory, ".rosso", "0new", "解压密码：xcys");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(archives);
        const string payload = "Zxtract 墨流换肤测试：解压内容必须保持一致。";
        var input = Path.Combine(source, "验证内容.txt");
        File.WriteAllText(input, string.Join(Environment.NewLine, Enumerable.Repeat(payload, 6000)));
        var sevenZip = Path.Combine(AppContext.BaseDirectory, "7z.exe");
        if (!File.Exists(sevenZip)) throw new InvalidOperationException("Bundled 7-Zip is required for integration tests.");
        var archive = Path.Combine(archives, "加密资料.7z");
        var start = new ProcessStartInfo(sevenZip) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "a", "-t7z", "-mx=0", "-pxcys", "-mhe=on", archive, input }) start.ArgumentList.Add(argument);
        using (var process = Process.Start(start)!)
        {
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(15000)) { process.Kill(); throw new TimeoutException("Fixture archive creation timed out."); }
            if (process.ExitCode != 0) throw new InvalidOperationException(output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
        }

        var window = new MainWindow();
        var model = (MainViewModel)window.DataContext;
        model.InferPasswordsFromPath = true;
        model.OutputToCustomRoot = true;
        model.OutputBaseDirectory = Path.Combine(temporaryDirectory, "direct-output");
        model.AddArchivesFromShell(new[] { archive }, ExtractUtil.App.Services.ShellMode.None);
        model.SelectedJob = model.Jobs.Single();
        model.StartCommand.Execute(null);
        PumpExtraction(model);
        if (model.Jobs.Single().Status != ExtractJobStatus.Completed ||
            !File.ReadAllText(Path.Combine(model.Jobs.Single().OutputDirectory, "验证内容.txt")).Contains(payload))
            throw new InvalidOperationException("Real file extraction failed during skin switching: " + string.Join(";", model.LogLines));

        // Exercise the workflows independently: legacy and tree queues have separate UI contracts.
        window.Close();
        window = new MainWindow();
        model = (MainViewModel)window.DataContext;
        model.InferPasswordsFromPath = true;
        model.OutputToCustomRoot = true;
        model.ScanRootDirectory = archives;
        model.OutputBaseDirectory = Path.Combine(temporaryDirectory, "folder-output");
        model.RecursiveExtractionEnabled = false;
        model.DeleteSourceAfterSuccess = false;
        model.ScanAndExtractCommand.Execute(null);
        PumpExtraction(model);
        var folderJobs = model.Jobs.Where(row => row.IsTreeItem).ToArray();
        if (folderJobs.Length != 1 || folderJobs[0].Status != ExtractJobStatus.Completed ||
            !File.ReadAllText(Path.Combine(folderJobs[0].OutputDirectory, "验证内容.txt")).Contains(payload) || !File.Exists(archive))
            throw new InvalidOperationException($"Real folder extraction failed during skin switching: count={folderJobs.Length}; states={string.Join(',', folderJobs.Select(row => row.Status))}; sourceExists={File.Exists(archive)}; outputs={string.Join(';', folderJobs.Select(row => row.OutputDirectory))}; " + string.Join(";", model.LogLines));
        window.Close();
        Console.WriteLine("PASS: real encrypted 7-Zip file/folder extraction during live skin switching; Unicode/password path inference, verified output and retained source.");
    }

    private static void PumpExtraction(MainViewModel model)
    {
        if (!model.IsBusy) throw new InvalidOperationException("Extraction command did not start.");
        var frame = new DispatcherFrame();
        var stopwatch = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        var switches = 0;
        timer.Tick += (_, _) =>
        {
            if (!model.IsBusy || stopwatch.Elapsed > TimeSpan.FromSeconds(30)) { frame.Continue = false; return; }
            var selected = model.SelectedJob;
            var jobs = model.Jobs;
            var themes = ThemeManager.Current;
            themes.SelectedTheme = themes.AvailableThemes[(switches + 1) % themes.AvailableThemes.Count];
            if (!ReferenceEquals(jobs, model.Jobs) || !ReferenceEquals(selected, model.SelectedJob))
                throw new InvalidOperationException("Live extraction state changed when switching skin.");
            switches++;
        };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        if (model.IsBusy) { model.CancelCommand.Execute(null); throw new TimeoutException("Extraction integration test timed out."); }
        if (switches == 0) throw new InvalidOperationException("No skin change occurred during real extraction.");
        ThemeManager.Current.SelectedTheme = ThemeManager.Current.AvailableThemes[0];
    }

    private static void SavePreview(MainWindow window, string path, int width, int height)
        => SaveWindowPreview(window, path, width, height);

    private static void SaveWindowPreview(Window window, string path, int width, int height, double scale = 1)
    {
        var root = (FrameworkElement)window.Content;
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)(width * scale), (int)(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
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
        var bars = Descendants<ProgressBar>(content).ToArray();
        if (bars.Length != 2 || bars.Any(bar => bar.Value != 0 || bar.Visibility != Visibility.Collapsed))
        {
            throw new InvalidOperationException("Queued progress should start at zero and remain hidden.");
        }
        update();
        Layout(content);
        if (bars.Any(bar => bar.Value != expected || bar.Visibility != (row.ShowProgress ? Visibility.Visible : Visibility.Collapsed)))
        {
            throw new InvalidOperationException($"A skin's row progress did not update to {expected}.");
        }
        if (complete is not null)
        {
            complete();
            Layout(content);
            if (bars.Any(bar => bar.Value != 100 || bar.Visibility != Visibility.Collapsed))
            {
                throw new InvalidOperationException("Completed progress should reach 100 and become hidden.");
            }
        }
    }

    private static void VerifyTaskRowStages(DataTemplate template)
    {
        var row = new JobItemViewModel(new ArchiveWorkItem { EntryPath = @"C:\smoke\activity.7z", DisplayName = "activity.7z", CanExtract = true });
        var content = (FrameworkElement)template.LoadContent();
        content.DataContext = row;
        foreach (var phase in new[] { ArchiveTreePhase.Queued, ArchiveTreePhase.TryingPassword, ArchiveTreePhase.Staging,
            ArchiveTreePhase.Extracting, ArchiveTreePhase.Committing, ArchiveTreePhase.Completed, ArchiveTreePhase.Failed, ArchiveTreePhase.Canceled })
        {
            row.ApplyTreeProgress(new ArchiveTreeProgress { Phase = phase,
                ExtractionProgress = phase == ArchiveTreePhase.Extracting ? new ExtractProgress { Percentage = 63 } : null });
            Layout(content);
            var percent = Descendants<TextBlock>(content).Single(t => t.Name == "NightRowPercent");
            var bar = Descendants<ProgressBar>(content).Single(t => t.Name == "NightRowProgress");
            var activity = Descendants<TaskActivityIndicator>(content).Single();
            var measurable = phase == ArchiveTreePhase.Extracting;
            var active = phase is ArchiveTreePhase.TryingPassword or ArchiveTreePhase.Staging or ArchiveTreePhase.Extracting or ArchiveTreePhase.Committing;
            if ((percent.Visibility == Visibility.Visible) != measurable || (bar.Visibility == Visibility.Visible) != measurable ||
                measurable && (percent.Text != "63%" || bar.Value != 63) || activity.IsActive != active)
                throw new InvalidOperationException("Task row manufactured progress or misrepresented activity: " + phase);
        }
        Console.WriteLine("PASS: per-file numeric/bar progress and truthful activity across queued/password/staging/extracting/commit/completed/failed/canceled states.");
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

    private static bool HasCollapsedAncestor(DependencyObject element)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is UIElement visual && visual.Visibility == Visibility.Collapsed) return true;
        return false;
    }
}
