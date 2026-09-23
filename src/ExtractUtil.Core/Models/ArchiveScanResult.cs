namespace ExtractUtil.Core.Models;

/// <summary>
/// Immutable summary returned after a directory scan completes.
/// </summary>
public sealed class ArchiveScanResult
{
    public string RootDirectory { get; init; } = string.Empty;

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset CompletedAt { get; init; }

    public IReadOnlyList<ArchiveWorkItem> Items { get; init; } = Array.Empty<ArchiveWorkItem>();

    public int FilesScanned { get; init; }

    public int DirectoriesScanned { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public int ExtractableCount => Items.Count(item => item.CanExtract);

    public int BlockedCount => Items.Count - ExtractableCount;
}
