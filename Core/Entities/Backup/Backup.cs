using Core.Entities.Base;
using System.ComponentModel.DataAnnotations;

namespace Core.Entities.Backup
{
    public class Backup : BaseEntity, IEntityBase<int>
    {
        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string Description { get; set; } = string.Empty;

        [Required]
        [MaxLength(500)]
        public string FilePath { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        public string FileName { get; set; } = string.Empty;

        public long FileSizeBytes { get; set; }

        [Required]
        public BackupType BackupType { get; set; }

        [Required]
        public BackupStatus Status { get; set; }

        public DateTime? CompletedAt { get; set; }

        public TimeSpan? Duration { get; set; }

        [MaxLength(100)]
        public string CreatedBy { get; set; } = string.Empty;

        public bool IsEncrypted { get; set; }

        [MaxLength(50)]
        public string CompressionType { get; set; } = "None";

        public string Metadata { get; set; } = string.Empty; // JSON

        public string TablesIncluded { get; set; } = string.Empty; // JSON array

        public string TablesExcluded { get; set; } = string.Empty; // JSON array

        public string ErrorMessage { get; set; } = string.Empty;

        public string Checksum { get; set; } = string.Empty;

        // Present in the real database but missing here until A15's reconciliation - added to match.
        public string? Settings { get; set; }

        public bool IsDeleted { get; set; } = false;

        public DateTime? DeletedAt { get; set; }

        [MaxLength(100)]
        public string DeletedBy { get; set; } = string.Empty;
    }

    public class BackupSchedule : BaseEntity, IEntityBase<int>
    {
        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string Description { get; set; } = string.Empty;

        [Required]
        public BackupType BackupType { get; set; }

        [Required]
        [MaxLength(50)]
        public string ScheduleFrequency { get; set; } = string.Empty; // Daily, Weekly, Monthly

        public TimeSpan ScheduleTime { get; set; }

        public DayOfWeek? ScheduleDayOfWeek { get; set; }

        public int? ScheduleDayOfMonth { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime? LastRun { get; set; }

        public DateTime? NextRun { get; set; }

        /// <summary>
        /// Number of days to retain backups. Backups older than this will be automatically deleted.
        /// Null or 0 means no automatic deletion.
        /// </summary>
        public int? RetentionDays { get; set; }

        [MaxLength(100)]
        public string CreatedBy { get; set; } = string.Empty;

        public string Settings { get; set; } = string.Empty; // JSON

        public bool IsDeleted { get; set; } = false;

        public DateTime? DeletedAt { get; set; }

        [MaxLength(100)]
        public string DeletedBy { get; set; } = string.Empty;
    }
}
