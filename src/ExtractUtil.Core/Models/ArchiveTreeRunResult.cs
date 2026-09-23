namespace ExtractUtil.Core.Models;

public sealed class ArchiveTreeRunResult
{
    public required ArchiveScanResult InitialScan { get; init; }
    public required IReadOnlyList<ArchiveTaskOutcome> Outcomes { get; init; }
    public bool WasCanceled { get; init; }

    public int CompletedCount => Outcomes.Count(item => item.Phase == Enums.ArchiveTreePhase.Completed);
    public int FailedCount => Outcomes.Count(item => item.Phase == Enums.ArchiveTreePhase.Failed);
}
