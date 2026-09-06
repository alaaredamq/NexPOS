using Application.DTOs.Backup;
using Core.Entities.Backup;
using BackupEntity = Core.Entities.Backup.Backup;

namespace Application.IServices.Backup
{
    public interface IBackupService
    {
        // Backup CRUD operations
        Task<IEnumerable<BackupEntity>> GetAllAsync();
        Task<BackupEntity> GetByIdAsync(int id);
        Task<BackupEntity> CreateAsync(BackupEntity entity);
        Task<BackupEntity> UpdateAsync(BackupEntity entity);
        Task DeleteAsync(int id);

        // Backup operations
        Task<BackupDTO> CreateBackupAsync(BackupRequest request);
        Task<BackupProgressDTO> GetBackupProgressAsync(string backupId);
        Task<bool> CancelBackupAsync(string backupId);
        Task<BackupValidationDTO> ValidateBackupAsync(int backupId);
        Task<BackupValidationDTO> ValidateBackupFileAsync(string filePath);

        // Restore operations
        Task<bool> RestoreBackupAsync(RestoreRequest request);
        Task<BackupProgressDTO> GetRestoreProgressAsync(string restoreId);
        Task<bool> CancelRestoreAsync(string restoreId);

        // Backup management
        Task<List<BackupDTO>> GetBackupsAsync(BackupFilterRequest request);
        Task<BackupStatsDTO> GetBackupStatsAsync();
        Task<bool> DeleteBackupAsync(int backupId);
        Task<bool> DeleteBackupFileAsync(int backupId);
        Task<bool> DownloadBackupAsync(int backupId, string downloadPath);

        // Backup scheduling
        Task<BackupScheduleDTO> CreateScheduleAsync(BackupScheduleDTO schedule);
        Task<BackupScheduleDTO> UpdateScheduleAsync(BackupScheduleDTO schedule);
        Task<bool> DeleteScheduleAsync(int scheduleId);
        Task<List<BackupScheduleDTO>> GetSchedulesAsync();
        Task<List<BackupScheduleDTO>> GetActiveSchedulesAsync();
        Task<bool> EnableScheduleAsync(int scheduleId);
        Task<bool> DisableScheduleAsync(int scheduleId);
        Task<bool> RunScheduleNowAsync(int scheduleId);

        // Backup maintenance
        Task<bool> CleanupOldBackupsAsync(int daysToKeep = 30);
        Task<bool> CompressBackupAsync(int backupId);
        Task<bool> EncryptBackupAsync(int backupId, string password);
        Task<bool> DecryptBackupAsync(int backupId, string password);

        // Database operations
        Task<List<string>> GetDatabaseTablesAsync();
        Task<long> GetTableSizeAsync(string tableName);
        Task<long> GetTotalDatabaseSizeAsync();
        Task<List<string>> GetDatabaseUsersAsync();

        // File operations
        Task<string> GenerateBackupFileNameAsync(string backupType, DateTime timestamp);
        Task<string> GetBackupDirectoryAsync();
        Task<bool> EnsureBackupDirectoryExistsAsync();
        Task<long> GetAvailableDiskSpaceAsync();
        Task<bool> CheckDiskSpaceAsync(long requiredSpace);

        // Backup verification
        Task<bool> VerifyBackupIntegrityAsync(int backupId);
        Task<string> CalculateBackupChecksumAsync(string filePath);
        Task<bool> CompareBackupChecksumsAsync(int backupId1, int backupId2);

        // System operations
        Task<bool> TestBackupConnectionAsync();
        Task<bool> TestRestoreConnectionAsync();
        Task<List<string>> GetBackupHistoryAsync(int days = 30);
        Task<Dictionary<string, object>> GetSystemInfoAsync();

        // Bulk operations
        Task<List<BackupDTO>> BulkDeleteBackupsAsync(List<int> backupIds);
        Task<bool> BulkRestoreBackupsAsync(List<RestoreRequest> requests);
        Task<bool> BulkValidateBackupsAsync(List<int> backupIds);

        // Export/Import
        Task<string> ExportBackupMetadataAsync(int backupId);
        Task<bool> ImportBackupMetadataAsync(string metadataJson);
        Task<string> GenerateBackupReportAsync(DateTime fromDate, DateTime toDate);

        // Emergency operations
        Task<bool> CreateEmergencyBackupAsync();
        Task<List<string>> GetEmergencyRestoreOptionsAsync();
        Task<bool> EmergencyRestoreAsync(string backupFilePath);
    }
}