namespace ExtractUtil.Core.Models;

/// <summary>
/// One physical file that belongs to a logical archive task.
/// </summary>
public sealed class ArchiveSourcePart
{
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// One-based logical volume number, or null for a regular single-file archive.
    /// </summary>
    public int? VolumeNumber { get; init; }

    public long Length { get; init; }
}
