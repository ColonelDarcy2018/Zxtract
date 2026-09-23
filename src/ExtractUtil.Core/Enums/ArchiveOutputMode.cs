namespace ExtractUtil.Core.Enums;

/// <summary>
/// Controls where a discovered archive is extracted.
/// </summary>
public enum ArchiveOutputMode
{
    /// <summary>Extract to a same-named child directory beside the archive.</summary>
    ArchiveSubdirectory = 0,

    /// <summary>Extract directly into the directory that contains the archive entry file.</summary>
    ArchiveDirectory = 1,

    /// <summary>Extract below a custom root while preserving the archive's relative directory.</summary>
    CustomRoot = 2
}
