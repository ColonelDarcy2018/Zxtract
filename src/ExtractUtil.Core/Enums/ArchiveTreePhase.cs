namespace ExtractUtil.Core.Enums;

public enum ArchiveTreePhase
{
    Scanning = 0,
    Discovered = 1,
    Blocked = 2,
    Queued = 3,
    Staging = 4,
    TryingPassword = 5,
    Extracting = 6,
    Committing = 7,
    Completed = 8,
    Failed = 9,
    Canceled = 10,
    SkippedLimit = 11,
    SkippedCycle = 12
}
