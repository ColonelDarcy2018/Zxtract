namespace ExtractUtil.Core.Models;

/// <summary>
/// Periodic scan progress. File discovery has no reliable percentage because directory
/// enumeration does not know the total in advance.
/// </summary>
public sealed class ArchiveScanProgress
{
    public string? CurrentPath { get; init; }

    public int FilesScanned { get; init; }

    public int DirectoriesScanned { get; init; }

    public int ArchiveCandidatesFound { get; init; }

    public bool IsGrouping { get; init; }
}
