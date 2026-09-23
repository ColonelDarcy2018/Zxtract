using ExtractUtil.Core.Enums;

namespace ExtractUtil.Core.Models;

// 解压任务实体：保存路径、选项与时间状态。
public sealed class ExtractJob
{
    public ExtractJob(string archivePath, ExtractOptions options)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            throw new ArgumentException("Archive path is required.", nameof(archivePath));
        }

        ArchivePath = archivePath;
        Options = options ?? throw new ArgumentNullException(nameof(options));
        DisplayName = Path.GetFileName(archivePath);
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string ArchivePath { get; }
    public string DisplayName { get; }
    public ExtractOptions Options { get; }

    public ExtractJobStatus Status { get; set; } = ExtractJobStatus.Queued;
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
