using ExtractUtil.Core.Enums;

namespace ExtractUtil.Core.Models;

public sealed class ArchiveTreeRunOptions
{
    public string RootDirectory { get; init; } = string.Empty;
    public ArchiveOutputMode OutputMode { get; init; } = ArchiveOutputMode.ArchiveSubdirectory;
    public string? CustomOutputRoot { get; init; }
    public IReadOnlyList<string> PasswordCandidates { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> CustomPasswordInferenceRules { get; init; } = Array.Empty<string>();
    public bool InferPasswordsFromPath { get; init; } = true;
    public bool Recursive { get; init; } = true;
    public bool EnableSiblingVolumeGrouping { get; init; } = true;
    public bool PreserveRelativeDirectories { get; init; } = true;
    public bool DeleteSourceAfterSuccess { get; init; }
    public ConflictPolicy ConflictPolicy { get; init; } = ConflictPolicy.RenameExisting;
    public bool EnableMultiThread { get; init; } = true;
    public int MaxParallelJobs { get; init; } = 1;
    public int MaxDepth { get; init; } = 10;
    public int MaxTasks { get; init; } = 10_000;
}
