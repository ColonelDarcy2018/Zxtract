using ExtractUtil.Core.Enums;

namespace ExtractUtil.Core.Models;

/// <summary>
/// A logical archive discovered by a scan. A work item can also represent a blocked
/// volume set so the UI can explain missing, duplicate, or ambiguous input.
/// </summary>
public sealed class ArchiveWorkItem
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public ArchiveKind Kind { get; init; }

    public string LogicalKey { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// File passed to the extraction engine. For blocked items without a first volume,
    /// this is the lowest numbered available part and must not be executed.
    /// </summary>
    public string EntryPath { get; init; } = string.Empty;

    public string OutputDirectory { get; init; } = string.Empty;

    /// <summary>Directory of the entry file relative to the scan root; empty at the root.</summary>
    public string RelativeDirectory { get; init; } = string.Empty;

    public IReadOnlyList<ArchiveSourcePart> SourceParts { get; init; } = Array.Empty<ArchiveSourcePart>();

    public long TotalSourceBytes { get; init; }

    public bool IsCrossDirectoryVolumeSet { get; init; }

    public bool CanExtract { get; init; }

    public IReadOnlyList<string> BlockingReasons { get; init; } = Array.Empty<string>();
}
