using ExtractUtil.Core.Enums;

namespace ExtractUtil.Core.Models;

/// <summary>
/// Options for turning the contents of a directory tree into logical archive work items.
/// </summary>
public sealed class ArchiveScanOptions
{
    public string RootDirectory { get; init; } = string.Empty;

    public ArchiveOutputMode OutputMode { get; init; } = ArchiveOutputMode.ArchiveSubdirectory;

    public string? CustomOutputRoot { get; init; }

    public bool RecurseSubdirectories { get; init; } = true;

    public bool EnableSiblingVolumeGrouping { get; init; } = true;

    /// <summary>When using CustomRoot, preserve the source directory hierarchy below it.</summary>
    public bool PreserveRelativeDirectories { get; init; } = true;

    /// <summary>
    /// Number of visited files between progress notifications. Values below one are normalized to one.
    /// </summary>
    public int ProgressReportInterval { get; init; } = 250;
}
