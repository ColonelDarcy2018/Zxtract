namespace ExtractUtil.Core.Enums;

// 任务在队列中的状态。
public enum ExtractJobStatus
{
    Queued = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Canceled = 4
}
