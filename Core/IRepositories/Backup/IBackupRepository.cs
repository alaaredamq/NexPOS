using Core.Entities.Backup;
using Core.IRepositories.Base;

namespace Core.IRepositories.Backup
{
    public interface IBackupRepository : IBaseRepository<Core.Entities.Backup.Backup, int>
    {
        Task<IEnumerable<Core.Entities.Backup.Backup>> GetBackupsByTypeAsync(BackupType type);
        Task<IEnumerable<Core.Entities.Backup.Backup>> GetBackupsByStatusAsync(BackupStatus status);
        Task<IEnumerable<Core.Entities.Backup.Backup>> GetBackupsByDateRangeAsync(DateTime fromDate, DateTime toDate);
        Task<IEnumerable<Core.Entities.Backup.Backup>> GetBackupsByUserAsync(string createdBy);
        Task<Core.Entities.Backup.Backup> GetLatestBackupAsync();
        Task<Core.Entities.Backup.Backup> GetLatestBackupByTypeAsync(BackupType type);
        Task<IEnumerable<Core.Entities.Backup.Backup>> GetOldBackupsAsync(int daysOld);
        Task<long> GetTotalBackupSizeAsync();
        Task<long> GetBackupSizeByTypeAsync(BackupType type);
        Task<IEnumerable<Core.Entities.Backup.Backup>> GetFailedBackupsAsync();
        Task<bool> DeleteBackupFileAsync(int id);
        Task<bool> UpdateBackupStatusAsync(int id, BackupStatus status, string errorMessage = null);
        Task<bool> UpdateBackupProgressAsync(int id, long bytesProcessed, string currentOperation);
        Task<IEnumerable<Core.Entities.Backup.Backup>> SearchBackupsAsync(string searchTerm);
        Task<Dictionary<string, int>> GetBackupTypeCountsAsync();
        Task<Dictionary<string, long>> GetStorageUsageByMonthAsync();
    }

    public interface IBackupScheduleRepository : IBaseRepository<BackupSchedule, int>
    {
        Task<IEnumerable<BackupSchedule>> GetActiveSchedulesAsync();
        Task<IEnumerable<BackupSchedule>> GetSchedulesByFrequencyAsync(string frequency);
        Task<BackupSchedule> GetNextScheduledBackupAsync();
        Task<bool> UpdateLastRunAsync(int id, DateTime lastRun);
        Task<bool> UpdateNextRunAsync(int id, DateTime nextRun);
        Task<IEnumerable<BackupSchedule>> GetOverdueSchedulesAsync();
    }
}
