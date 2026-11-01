using System.ComponentModel.DataAnnotations;

namespace Web.Api.Backups.Contracts;

/// <summary>
/// One backup: a complete, consistent snapshot of the database (SQLite <c>VACUUM INTO</c>) kept in the <c>Backups</c> folder
/// next to the database file. The catalog is rebuilt from the files on disk, so a restore that rolls the table back never loses one.
/// </summary>
public sealed record BackupDto
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>The file on disk (not the full path): enough to recognise it, nothing that tells a client how the server's folders are laid out.</summary>
    public required string FileName { get; init; }

    public required long FileSizeBytes { get; init; }

    /// <summary><c>Completed</c>, <c>Failed</c>, <c>InProgress</c> or <c>Cancelled</c>.</summary>
    public required string Status { get; init; }

    /// <summary>UTC.</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>UTC; null while the backup is still running.</summary>
    public DateTime? CompletedAt { get; init; }

    /// <summary>How long the snapshot took, in seconds; null while it runs.</summary>
    public double? DurationSeconds { get; init; }

    /// <summary>Why a failed backup failed; empty otherwise.</summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>The tiles above the Backup page, plus where the files go and how much room is left for them.</summary>
public sealed record BackupSummaryDto
{
    public required int TotalBackups { get; init; }

    public required int SuccessfulBackups { get; init; }

    public required int FailedBackups { get; init; }

    /// <summary>Size of all backup files together.</summary>
    public required long TotalSizeBytes { get; init; }

    /// <summary>Active schedules.</summary>
    public required int ScheduledCount { get; init; }

    /// <summary>UTC; the soonest run of any active schedule.</summary>
    public DateTime? NextScheduledAt { get; init; }

    /// <summary>UTC; when the newest completed backup finished.</summary>
    public DateTime? LastBackupAt { get; init; }

    /// <summary>Status of the newest backup of any kind, so a failure right now is not hidden by an older success.</summary>
    public string? LastBackupStatus { get; init; }

    /// <summary>Folder the backups are written to (shown to the administrator, who needs to know where to copy them from).</summary>
    public required string BackupDirectory { get; init; }

    /// <summary>Free space on the drive that holds that folder.</summary>
    public required long AvailableDiskBytes { get; init; }

    /// <summary>The live database file.</summary>
    public required long DatabaseSizeBytes { get; init; }
}

/// <summary>Body of "create a backup". Both fields are optional: a blank name becomes <c>Backup_yyyyMMdd_HHmmss</c>.</summary>
public sealed record BackupCreateRequest
{
    [StringLength(200)]
    public string? Name { get; init; }

    [StringLength(1000)]
    public string? Description { get; init; }
}

/// <summary>Body of "restore this backup".</summary>
public sealed record BackupRestoreRequest
{
    /// <summary>Take a backup of the current data first (default <c>true</c>), so a wrong restore can itself be undone.</summary>
    public bool CreateBackupBeforeRestore { get; init; } = true;
}

/// <summary>The outcome of checking one backup file.</summary>
public sealed record BackupValidationDto
{
    public required bool IsValid { get; init; }

    public required IReadOnlyList<string> Errors { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }

    public string? Checksum { get; init; }

    public long FileSizeBytes { get; init; }
}

/// <summary>Body of the bulk calls.</summary>
public sealed record BackupIdsRequest
{
    [Required]
    [MinLength(1)]
    [MaxLength(500)]
    public required IReadOnlyList<int> Ids { get; init; }
}

/// <summary>How many of the requested backups were removed (ids that no longer exist are skipped, not errors).</summary>
public sealed record BackupBulkDeleteResult
{
    public required int Deleted { get; init; }
}

/// <summary>Per backup: does the file exist and is it non-empty. <c>AllValid</c> is the old "all backups validated" verdict.</summary>
public sealed record BackupBulkValidateResult
{
    public required bool AllValid { get; init; }

    public required IReadOnlyList<BackupCheckDto> Results { get; init; }
}

public sealed record BackupCheckDto
{
    public required int Id { get; init; }

    public required bool IsValid { get; init; }
}

/// <summary>Body of "delete backups older than ..." (the retention clean-up a schedule also runs after each backup).</summary>
public sealed record BackupCleanupRequest
{
    [Range(1, 3650)]
    public int DaysToKeep { get; init; } = 30;
}

/// <summary>Whether the app can open its database for a backup and for a restore.</summary>
public sealed record BackupConnectionDto
{
    public required bool BackupConnection { get; init; }

    public required bool RestoreConnection { get; init; }

    public bool Ok => BackupConnection && RestoreConnection;
}

/// <summary>A recurring backup. Times are the shop's local clock; the next/last run are UTC instants.</summary>
public sealed record BackupScheduleDto
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary><c>Daily</c>, <c>Weekly</c> or <c>Monthly</c>.</summary>
    public required string Frequency { get; init; }

    /// <summary>Local time of day, <c>HH:mm</c>.</summary>
    public required string Time { get; init; }

    /// <summary>0 = Sunday ... 6 = Saturday; weekly schedules only.</summary>
    public int? DayOfWeek { get; init; }

    /// <summary>1 to 28; monthly schedules only.</summary>
    public int? DayOfMonth { get; init; }

    public required bool IsActive { get; init; }

    /// <summary>Backups older than this many days are deleted after each scheduled run; null keeps everything.</summary>
    public int? RetentionDays { get; init; }

    /// <summary>UTC.</summary>
    public DateTime? LastRunAt { get; init; }

    /// <summary>UTC; null while the schedule is paused.</summary>
    public DateTime? NextRunAt { get; init; }
}

/// <summary>Body of "create a schedule" and "change a schedule".</summary>
public sealed record BackupScheduleRequest
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public required string Name { get; init; }

    [StringLength(1000)]
    public string? Description { get; init; }

    /// <summary><c>Daily</c>, <c>Weekly</c> or <c>Monthly</c>.</summary>
    [Required]
    [RegularExpression("(?i)^(daily|weekly|monthly)$")]
    public required string Frequency { get; init; }

    /// <summary>Local time of day, <c>HH:mm</c> (24-hour).</summary>
    [Required]
    [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$")]
    public required string Time { get; init; }

    /// <summary>Weekly only: 0 = Sunday ... 6 = Saturday.</summary>
    [Range(0, 6)]
    public int? DayOfWeek { get; init; }

    /// <summary>Monthly only: 1 to 28 (every month has those days).</summary>
    [Range(1, 28)]
    public int? DayOfMonth { get; init; }

    public bool IsActive { get; init; } = true;

    /// <summary>Delete backups older than this many days after each run; omit or null to keep everything.</summary>
    [Range(1, 3650)]
    public int? RetentionDays { get; init; }
}

/// <summary>Body of "turn a schedule on / off".</summary>
public sealed record BackupScheduleActiveRequest
{
    public required bool IsActive { get; init; }
}
