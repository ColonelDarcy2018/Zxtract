namespace ExtractUtil.Core.Enums;

// 7-Zip 失败的粗粒度分类，便于上层决定是否继续尝试下一个密码。
public enum ExtractFailureKind
{
    None = 0,
    WrongPassword = 1,
    MissingVolume = 2,
    InvalidArchive = 3,
    CorruptArchive = 4,
    AccessDenied = 5,
    ToolNotFound = 6,
    Canceled = 7,
    Unknown = 8
}
