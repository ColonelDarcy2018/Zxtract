using ExtractUtil.Core.Models;

namespace ExtractUtil.Core.Services;

public interface IArchiveScanner
{
    Task<ArchiveScanResult> ScanAsync(
        ArchiveScanOptions options,
        IProgress<ArchiveScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
