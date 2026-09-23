using ExtractUtil.Core.Enums;

namespace ExtractUtil.Core.Models;

public sealed class ArchiveTreeProgress
{
    public ArchiveTreePhase Phase { get; init; }
    public ArchiveWorkItem? Item { get; init; }
    public string Message { get; init; } = string.Empty;
    public int Depth { get; init; }
    public int PasswordAttempt { get; init; }
    public int PasswordAttemptCount { get; init; }
    public int FilesScanned { get; init; }
    public int DirectoriesScanned { get; init; }
    public ExtractProgress? ExtractionProgress { get; init; }
}
