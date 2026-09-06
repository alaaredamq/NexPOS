using Core.Entities.Backup;
using Core.IRepositories.Backup;
using Infrastructure.Data;
using Infrastructure.Repositories.Base;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using BackupEntity = Core.Entities.Backup.Backup;

namespace Infrastructure.Repositories.Backup
{
    public class BackupRepository : BaseRepository<BackupEntity, int>, IBackupRepository
    {
        public BackupRepository(ErpDbContext context, IServiceProvider serviceProvider) : base(context, serviceProvider)
        {
        }

        public async Task<IEnumerable<BackupEntity>> GetBackupsByTypeAsync(BackupType type)
        {
            return await _Entities
                .Where(b => b.BackupType == type && !b.IsDeleted)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<BackupEntity>> GetBackupsByStatusAsync(BackupStatus status)
        {
            return await _Entities
                .Where(b => b.Status == status && !b.IsDeleted)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<BackupEntity>> GetBackupsByDateRangeAsync(DateTime fromDate, DateTime toDate)
        {
            return await _Entities
                .Where(b => b.CreatedAt >= fromDate && b.CreatedAt <= toDate && !b.IsDeleted)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<BackupEntity>> GetBackupsByUserAsync(string createdBy)
        {
            return await _Entities
                .Where(b => b.CreatedBy == createdBy && !b.IsDeleted)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();
        }

        public async Task<BackupEntity> GetLatestBackupAsync()
        {
            return await _Entities
                .Where(b => !b.IsDeleted && b.Status == BackupStatus.Completed)
                .OrderByDescending(b => b.CreatedAt)
                .FirstOrDefaultAsync();
        }

        public async Task<BackupEntity> GetLatestBackupByTypeAsync(BackupType type)
        {
            return await _Entities
                .Where(b => b.BackupType == type && !b.IsDeleted && b.Status == BackupStatus.Completed)
                .OrderByDescending(b => b.CreatedAt)
                .FirstOrDefaultAsync();
        }

        public async Task<IEnumerable<BackupEntity>> GetOldBackupsAsync(int daysOld)
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-daysOld);
            return await _Entities
                .Where(b => b.CreatedAt < cutoffDate && !b.IsDeleted)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();
        }

        public async Task<long> GetTotalBackupSizeAsync()
        {
            return await _Entities
                .Where(b => !b.IsDeleted)
                .SumAsync(b => b.FileSizeBytes);
        }

        public async Task<long> GetBackupSizeByTypeAsync(BackupType type)
        {
            return await _Entities
                .Where(b => b.BackupType == type && !b.IsDeleted)
                .SumAsync(b => b.FileSizeBytes);
        }

        public async Task<IEnumerable<BackupEntity>> GetFailedBackupsAsync()
        {
            return await _Entities
                .Where(b => b.Status == BackupStatus.Failed && !b.IsDeleted)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();
        }

        public async Task<bool> DeleteBackupFileAsync(int id)
        {
            var backup = await GetByIdAsync(id);
            if (backup != null)
            {
                try
                {
                    if (File.Exists(backup.FilePath))
                    {
                        File.Delete(backup.FilePath);
                    }
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            return false;
        }

        public async Task<bool> UpdateBackupStatusAsync(int id, BackupStatus status, string errorMessage = null)
        {
            var backup = await GetByIdAsync(id);
            if (backup != null)
            {
                backup.Status = status;
                if (status == BackupStatus.Completed || status == BackupStatus.Failed)
                {
                    backup.CompletedAt = DateTime.UtcNow;
                    if (backup.CreatedAt != default)
                    {
                        backup.Duration = backup.CompletedAt - backup.CreatedAt;
                    }
                }
                if (!string.IsNullOrEmpty(errorMessage))
                {
                    backup.ErrorMessage = errorMessage;
                }
                await SaveAsync(backup);
                return true;
            }
            return false;
        }

        public async Task<bool> UpdateBackupProgressAsync(int id, long bytesProcessed, string currentOperation)
        {
            var backup = await GetByIdAsync(id);
            if (backup != null)
            {
                var metadata = new Dictionary<string, object>();
                if (!string.IsNullOrEmpty(backup.Metadata))
                {
                    metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(backup.Metadata) ?? new();
                }
                metadata["bytesProcessed"] = bytesProcessed;
                metadata["currentOperation"] = currentOperation;
                backup.Metadata = JsonSerializer.Serialize(metadata);
                await SaveAsync(backup);
                return true;
            }
            return false;
        }

        public async Task<IEnumerable<BackupEntity>> SearchBackupsAsync(string searchTerm)
        {
            var term = searchTerm.ToLower();
            return await _Entities
                .Where(b => !b.IsDeleted && 
                           (b.Name.ToLower().Contains(term) || 
                            b.Description.ToLower().Contains(term) ||
                            b.FileName.ToLower().Contains(term)))
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();
        }

        public async Task<Dictionary<string, int>> GetBackupTypeCountsAsync()
        {
            var result = await _Entities
                .Where(b => !b.IsDeleted)
                .GroupBy(b => b.BackupType)
                .ToDictionaryAsync(g => g.Key.ToString(), g => g.Count());
            
            return result ?? new Dictionary<string, int>();
        }

        public async Task<Dictionary<string, long>> GetStorageUsageByMonthAsync()
        {
            var result = await _Entities
                .Where(b => !b.IsDeleted)
                .GroupBy(b => new { b.CreatedAt.Year, b.CreatedAt.Month })
                .ToDictionaryAsync(g => $"{g.Key.Year}-{g.Key.Month:D2}", g => g.Sum(b => b.FileSizeBytes));
            
            return result ?? new Dictionary<string, long>();
        }
    }

    public class BackupScheduleRepository : BaseRepository<Core.Entities.Backup.BackupSchedule, int>, IBackupScheduleRepository
    {
        public BackupScheduleRepository(ErpDbContext context, IServiceProvider serviceProvider) : base(context, serviceProvider)
        {
        }

        public async Task<IEnumerable<Core.Entities.Backup.BackupSchedule>> GetActiveSchedulesAsync()
        {
            return await _Entities
                .Where(s => s.IsActive && !s.IsDeleted)
                .OrderBy(s => s.NextRun)
                .ToListAsync();
        }

        public async Task<IEnumerable<Core.Entities.Backup.BackupSchedule>> GetSchedulesByFrequencyAsync(string frequency)
        {
            return await _Entities
                .Where(s => s.ScheduleFrequency == frequency && !s.IsDeleted)
                .OrderBy(s => s.NextRun)
                .ToListAsync();
        }

        public async Task<Core.Entities.Backup.BackupSchedule> GetNextScheduledBackupAsync()
        {
            return await _Entities
                .Where(s => s.IsActive && !s.IsDeleted && s.NextRun.HasValue)
                .OrderBy(s => s.NextRun)
                .FirstOrDefaultAsync();
        }

        public async Task<bool> UpdateLastRunAsync(int id, DateTime lastRun)
        {
            var schedule = await GetByIdAsync(id);
            if (schedule != null)
            {
                schedule.LastRun = lastRun;
                await SaveAsync(schedule);
                return true;
            }
            return false;
        }

        public async Task<bool> UpdateNextRunAsync(int id, DateTime nextRun)
        {
            var schedule = await GetByIdAsync(id);
            if (schedule != null)
            {
                schedule.NextRun = nextRun;
                await SaveAsync(schedule);
                return true;
            }
            return false;
        }

        public async Task<IEnumerable<Core.Entities.Backup.BackupSchedule>> GetOverdueSchedulesAsync()
        {
            var now = DateTime.UtcNow;
            return await _Entities
                .Where(s => s.IsActive && !s.IsDeleted && s.NextRun.HasValue && s.NextRun < now)
                .OrderBy(s => s.NextRun)
                .ToListAsync();
        }
    }
}
