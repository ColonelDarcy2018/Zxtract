namespace ExtractUtil.Core.Enums;

/// <summary>
/// Describes the physical layout of a logical archive task.
/// </summary>
public enum ArchiveKind
{
    Regular = 0,
    SevenZipSplit = 1,
    ZipSplit = 2,
    PartRar = 3,
    LegacyRar = 4
}
