using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

            if (args.Length == 1)
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
        if (bar.Value != expected || bar.Visibility != (row.IsRunning ? Visibility.Visible : Visibility.Collapsed))
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
