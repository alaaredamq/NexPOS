namespace Application.DTOs.Backup
{
    public class BackupDTO
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public string FileSizeFormatted { get; set; } = string.Empty;
        public string BackupType { get; set; } = string.Empty; // Full, Incremental, Differential
        public string Status { get; set; } = string.Empty; // InProgress, Completed, Failed, Scheduled
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public TimeSpan? Duration { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public bool IsEncrypted { get; set; }
        public string CompressionType { get; set; } = string.Empty; // None, GZip, Zip
        public Dictionary<string, object> Metadata { get; set; } = new();
        public List<string> TablesIncluded { get; set; } = new();
        public List<string> TablesExcluded { get; set; } = new();
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public class BackupRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string BackupType { get; set; } = "Full"; // Full, Incremental, Differential
        public bool IncludeData { get; set; } = true;
        public bool IncludeSchema { get; set; } = true;
        public bool IncludeUsers { get; set; } = false;
        public bool IsEncrypted { get; set; } = false;
        public string CompressionType { get; set; } = "GZip"; // None, GZip, Zip
        public List<string> TablesToInclude { get; set; } = new();
        public List<string> TablesToExclude { get; set; } = new();
        public bool ScheduleBackup { get; set; } = false;
        public DateTime? ScheduledTime { get; set; }
        public string ScheduleFrequency { get; set; } = string.Empty; // Daily, Weekly, Monthly
    }

    public class RestoreRequest
    {
        public int BackupId { get; set; }
        public string BackupFilePath { get; set; } = string.Empty;
        public bool RestoreData { get; set; } = true;
        public bool RestoreSchema { get; set; } = true;
        public bool RestoreUsers { get; set; } = false;
        public bool OverwriteExisting { get; set; } = false;
        public List<string> TablesToRestore { get; set; } = new();
        public List<string> TablesToSkip { get; set; } = new();
        public bool CreateBackupBeforeRestore { get; set; } = true;
        public string RestoreDescription { get; set; } = string.Empty;
    }

    public class BackupScheduleDTO
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string BackupType { get; set; } = string.Empty;
        public string ScheduleFrequency { get; set; } = string.Empty;
        public TimeSpan ScheduleTime { get; set; }
        public DayOfWeek? ScheduleDayOfWeek { get; set; }
        public int? ScheduleDayOfMonth { get; set; }
        public bool IsActive { get; set; }
        public DateTime? LastRun { get; set; }
        public DateTime? NextRun { get; set; }
        /// <summary>
        /// Number of days to retain backups. Backups older than this will be automatically deleted.
        /// Null or 0 means no automatic deletion.
        /// </summary>
        public int? RetentionDays { get; set; }
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public Dictionary<string, object> Settings { get; set; } = new();
    }

    public class BackupStatsDTO
    {
        public int TotalBackups { get; set; }
        public int SuccessfulBackups { get; set; }
        public int FailedBackups { get; set; }
        public int ScheduledBackups { get; set; }
        public long TotalBackupSize { get; set; }
        public string TotalBackupSizeFormatted { get; set; } = string.Empty;
        public DateTime? LastBackupDate { get; set; }
        public DateTime? NextScheduledBackup { get; set; }
        public List<BackupDTO> RecentBackups { get; set; } = new();
        public List<BackupScheduleDTO> ActiveSchedules { get; set; } = new();
        public Dictionary<string, int> BackupTypeCounts { get; set; } = new();
        public Dictionary<string, long> StorageUsageByMonth { get; set; } = new();
    }

    public class BackupProgressDTO
    {
        public string BackupId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int ProgressPercentage { get; set; }
        public string CurrentOperation { get; set; } = string.Empty;
        public string CurrentTable { get; set; } = string.Empty;
        public long BytesProcessed { get; set; }
        public long TotalBytes { get; set; }
        public TimeSpan ElapsedTime { get; set; }
        public TimeSpan EstimatedTimeRemaining { get; set; }
        public DateTime StartedAt { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public class BackupValidationDTO
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public BackupInfoDTO BackupInfo { get; set; } = new();
    }

    public class BackupInfoDTO
    {
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string BackupType { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public string FileSizeFormatted { get; set; } = string.Empty;
        public string DatabaseVersion { get; set; } = string.Empty;
        public List<string> TablesIncluded { get; set; } = new();
        public int TableCount { get; set; }
        public bool IsEncrypted { get; set; }
        public string CompressionType { get; set; } = string.Empty;
        public string Checksum { get; set; } = string.Empty;
    }

    public class BackupFilterRequest
    {
        public string? BackupType { get; set; }
        public string? Status { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public string? CreatedBy { get; set; }
        public bool? IsEncrypted { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string SortBy { get; set; } = "CreatedAt";
        public string SortDirection { get; set; } = "desc";
    }
}
