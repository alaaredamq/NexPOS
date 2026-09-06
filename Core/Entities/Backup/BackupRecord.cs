namespace Core.Entities.Backup;

public class BackupRecord
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public BackupType Type { get; set; }
    public BackupStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public string? Description { get; set; }
    public string? ErrorMessage { get; set; }
}

public enum BackupType
{
    Full = 0,
    Incremental = 1,
    Differential = 2
}

public enum BackupStatus
{
    InProgress = 0,
    Completed = 1,
    Failed = 2,
    Cancelled = 3
}
