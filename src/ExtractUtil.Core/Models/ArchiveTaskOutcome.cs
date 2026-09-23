using ExtractUtil.Core.Enums;

namespace ExtractUtil.Core.Models;

public sealed class ArchiveTaskOutcome
{
    public required ArchiveWorkItem Item { get; init; }
    public required ArchiveTreePhase Phase { get; init; }
    public int Depth { get; init; }
    public int ExitCode { get; init; }
    public int PasswordAttempts { get; init; }
    public string? Message { get; init; }
}
