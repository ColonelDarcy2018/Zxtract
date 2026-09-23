using ExtractUtil.Core.Enums;
using ExtractUtil.Core.Models;
using ExtractUtil.Core.Services;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: scan <root> | run <root> [password] [maxTasks]");
    return 2;
}

var mode = args[0].ToLowerInvariant();
var root = Path.GetFullPath(args[1]);
var password = args.Length > 2 ? args[2] : string.Empty;
var maxTasks = args.Length > 3 && int.TryParse(args[3], out var parsed) ? parsed : 100;
var log = new InMemoryLogSink();
log.EntryAdded += (_, entry) => Console.WriteLine($"[{entry.Level}] {entry.Message}");
var scanner = new ArchiveScanner();
var scanProgress = new Progress<ArchiveScanProgress>(p =>
{
    if (p.IsGrouping || p.FilesScanned % 1000 == 0)
    {
        Console.WriteLine($"SCAN files={p.FilesScanned} dirs={p.DirectoriesScanned} candidates={p.ArchiveCandidatesFound} grouping={p.IsGrouping}");
    }
});

if (mode == "scan")
{
    var scan = await scanner.ScanAsync(new ArchiveScanOptions
    {
        RootDirectory = root,
        OutputMode = ArchiveOutputMode.ArchiveSubdirectory,
        EnableSiblingVolumeGrouping = true,
        ProgressReportInterval = 1000
    }, scanProgress);
    PrintScan(scan);
    return 0;
}

if (mode != "run")
{
    Console.Error.WriteLine($"Unknown mode: {mode}");
    return 2;
}

var coordinator = new ArchiveTreeCoordinator(
    scanner,
    new SevenZipExtractor(new ProcessRunner(), ResolveSevenZip()),
    new ArchiveVolumeStager(),
    new OutputCommitter(),
    log);
var pause = new PauseController();
var progress = new Progress<ArchiveTreeProgress>(p =>
{
    var name = p.Item?.DisplayName ?? "(scan)";
    Console.WriteLine($"{p.Phase} depth={p.Depth} {name} {p.Message} {p.PasswordAttempt}/{p.PasswordAttemptCount}");
});
var result = await coordinator.RunAsync(new ArchiveTreeRunOptions
{
    RootDirectory = root,
    PasswordCandidates = PasswordCandidateResolver.ParseMultiline(password),
    InferPasswordsFromPath = true,
    Recursive = true,
    EnableSiblingVolumeGrouping = true,
    MaxParallelJobs = 1,
    MaxDepth = 4,
    MaxTasks = maxTasks,
    ConflictPolicy = ConflictPolicy.RenameExisting
}, pause, progress);

Console.WriteLine($"RESULT completed={result.CompletedCount} failed={result.FailedCount} canceled={result.WasCanceled}");
foreach (var outcome in result.Outcomes)
{
    Console.WriteLine($"OUTCOME {outcome.Phase} {outcome.Item.DisplayName} attempts={outcome.PasswordAttempts} message={outcome.Message}");
}
return result.FailedCount == 0 ? 0 : 1;

static void PrintScan(ArchiveScanResult scan)
{
    Console.WriteLine($"RESULT files={scan.FilesScanned} dirs={scan.DirectoriesScanned} items={scan.Items.Count} extractable={scan.ExtractableCount} blocked={scan.BlockedCount}");
    foreach (var item in scan.Items)
    {
        Console.WriteLine($"ITEM {item.Kind} {item.DisplayName} parts={item.SourceParts.Count} cross={item.IsCrossDirectoryVolumeSet} can={item.CanExtract} output={item.OutputDirectory}");
        if (!item.CanExtract)
        {
            Console.WriteLine($"  BLOCK {string.Join(" | ", item.BlockingReasons)}");
        }
    }
    foreach (var warning in scan.Warnings.Take(20))
    {
        Console.WriteLine($"WARN {warning}");
    }
}

static string ResolveSevenZip()
{
    var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "7zip", "7z.exe"));
    return File.Exists(path) ? path : throw new FileNotFoundException("Bundled 7z.exe not found", path);
}
