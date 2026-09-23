using System.Runtime.InteropServices;
using ExtractUtil.Core.Models;

namespace ExtractUtil.Core.Services;

public sealed class ArchiveVolumeStager
{
    public async Task<StagedArchiveLease> StageIfNeededAsync(
        ArchiveWorkItem item,
        string stagingRoot,
        CancellationToken cancellationToken)
    {
        var sourceDirectories = item.SourceParts
            .Select(part => Path.GetDirectoryName(Path.GetFullPath(part.Path)) ?? string.Empty)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (sourceDirectories.Length <= 1)
        {
            return StagedArchiveLease.NotStaged(item.EntryPath);
        }

        var ownedRoot = Path.GetFullPath(stagingRoot);
        Directory.CreateDirectory(ownedRoot);
        var stageDirectory = Path.Combine(ownedRoot, item.Id.ToString("N"));
        Directory.CreateDirectory(stageDirectory);

        var usedCopyFallback = false;
        try
        {
            foreach (var part in item.SourceParts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = Path.Combine(stageDirectory, Path.GetFileName(part.Path));
                if (!TryCreateHardLink(target, part.Path))
                {
                    usedCopyFallback = true;
                    await CopyFileAsync(part.Path, target, cancellationToken).ConfigureAwait(false);
                }
            }

            var stagedEntry = Path.Combine(stageDirectory, Path.GetFileName(item.EntryPath));
            return new StagedArchiveLease(stagedEntry, stageDirectory, ownedRoot, usedCopyFallback);
        }
        catch
        {
            SafeDeleteOwnedDirectory(stageDirectory, ownedRoot);
            throw;
        }
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await input.CopyToAsync(output, 1024 * 1024, cancellationToken).ConfigureAwait(false);
    }

    private static bool TryCreateHardLink(string linkPath, string existingPath)
    {
        return OperatingSystem.IsWindows() && CreateHardLink(linkPath, existingPath, IntPtr.Zero);
    }

    internal static void SafeDeleteOwnedDirectory(string path, string ownedRoot)
    {
        try
        {
            var root = Path.GetFullPath(ownedRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var target = Path.GetFullPath(path);

            if (target.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }
        }
        catch (IOException)
        {
            // A failed cleanup is reported by the caller's run log on the next startup/run.
        }
        catch (UnauthorizedAccessException)
        {
            // Keep the owned staging directory rather than expanding cleanup scope.
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);
}

public sealed class StagedArchiveLease : IAsyncDisposable
{
    private readonly string? _stageDirectory;
    private readonly string? _ownedRoot;

    internal StagedArchiveLease(
        string inputPath,
        string? stageDirectory,
        string? ownedRoot,
        bool usedCopyFallback)
    {
        InputPath = inputPath;
        _stageDirectory = stageDirectory;
        _ownedRoot = ownedRoot;
        UsedCopyFallback = usedCopyFallback;
    }

    public string InputPath { get; }
    public bool WasStaged => _stageDirectory is not null;
    public bool UsedCopyFallback { get; }

    internal static StagedArchiveLease NotStaged(string inputPath)
    {
        return new StagedArchiveLease(inputPath, null, null, false);
    }

    public ValueTask DisposeAsync()
    {
        if (_stageDirectory is not null && _ownedRoot is not null)
        {
            ArchiveVolumeStager.SafeDeleteOwnedDirectory(_stageDirectory, _ownedRoot);
        }

        return ValueTask.CompletedTask;
    }
}
