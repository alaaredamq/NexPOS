using Application.DTOs.Backup;
using Application.IServices.Backup;
using Core.Auditing;
using Core.Entities.Backup;
using Core.Entities.Logging;
using Core.IRepositories.Backup;
using Core.Time;
using Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackupEntity = Core.Entities.Backup.Backup;

namespace Application.Services.Backup
{
    public class BackupService : IBackupService
    {
        private readonly IBackupRepository _backupRepository;
        private readonly IBackupScheduleRepository _scheduleRepository;
        private readonly ErpDbContext _context;
        private readonly IAuditTrail _auditTrail;
        private readonly IAuditContext _auditContext;
        private static readonly Dictionary<string, BackupProgressDTO> _activeBackups = new();
        private static readonly Dictionary<string, BackupProgressDTO> _activeRestores = new();

        public BackupService(
            IBackupRepository backupRepository,
            IBackupScheduleRepository scheduleRepository,
            ErpDbContext context,
            IAuditTrail auditTrail,
            IAuditContext auditContext)
        {
            _backupRepository = backupRepository;
            _scheduleRepository = scheduleRepository;
            _context = context;
            _auditTrail = auditTrail;
            _auditContext = auditContext;
        }

        #region Backup CRUD Operations

        public async Task<IEnumerable<BackupEntity>> GetAllAsync()
        {
            return await _backupRepository.GetAllAsync();
        }

        public async Task<BackupEntity> GetByIdAsync(int id)
        {
            return await _backupRepository.GetByIdAsync(id);
        }

        public async Task<BackupEntity> CreateAsync(BackupEntity entity)
        {
            entity.CreatedAt = DateTime.UtcNow;
            entity.Status = BackupStatus.InProgress;
            return await _backupRepository.SaveAsync(entity);
        }

        public async Task<BackupEntity> UpdateAsync(BackupEntity entity)
        {
            return await _backupRepository.SaveAsync(entity);
        }

        public async Task DeleteAsync(int id)
        {
            await _backupRepository.DeleteAsync(id);
        }

        #endregion

        #region Backup Operations

        public async Task<BackupDTO> CreateBackupAsync(BackupRequest request)
        {
            try
            {
                // Ensure backup directory exists
                await EnsureBackupDirectoryExistsAsync();

                // Generate backup file name and path
                var fileName = await GenerateBackupFileNameAsync(request.BackupType, DateTime.UtcNow);
                var backupPath = Path.Combine(await GetBackupDirectoryAsync(), fileName);

                // Create backup entity - ensure no nulls for required DB columns
                var backup = new BackupEntity
                {
                    // A blank name (an API call with no body) would list as an unnamed backup.
                    Name = string.IsNullOrWhiteSpace(request.Name) ? $"Backup_{DateTime.Now:yyyyMMdd_HHmmss}" : request.Name.Trim(),
                    Description = request.Description ?? string.Empty,
                    FilePath = backupPath,
                    FileName = fileName ?? string.Empty,
                    BackupType = ParseBackupType(request.BackupType ?? "Full"),
                    Status = BackupStatus.InProgress,
                    CreatedBy = "System", // TODO: Get from current user
                    IsEncrypted = request.IsEncrypted,
                    CompressionType = request.CompressionType ?? "None",
                    TablesIncluded = request.TablesToInclude != null ? JsonSerializer.Serialize(request.TablesToInclude) : "[]",
                    TablesExcluded = request.TablesToExclude != null ? JsonSerializer.Serialize(request.TablesToExclude) : "[]",
                    Metadata = string.Empty,
                    ErrorMessage = string.Empty,
                    Checksum = string.Empty,
                    DeletedBy = string.Empty,
                    CreatedAt = DateTime.UtcNow
                };

                var savedBackup = await CreateAsync(backup);
                var backupId = savedBackup.Id.ToString();

                // Execute backup synchronously to avoid DbContext concurrency issues
                // For a production system, use a proper background job queue (Hangfire, etc.)
                await PerformBackupAsync(backupId, request, backupPath);

                // Return the final state (Completed/Failed + error) rather than the initial InProgress snapshot
                var finalBackup = await _backupRepository.GetByIdAsync(savedBackup.Id) ?? savedBackup;

                var succeeded = finalBackup.Status == BackupStatus.Completed;
                _auditTrail.LogSystem(
                    succeeded ? AuditActions.BackupCreated : AuditActions.BackupFailed,
                    succeeded
                        ? $"Backup '{finalBackup.Name}' created ({finalBackup.FileName}, {FormatSize(finalBackup.FileSizeBytes)})"
                        : $"Backup '{finalBackup.Name}' did not complete: {finalBackup.ErrorMessage}",
                    "Backup",
                    succeeded ? AuditOutcome.Success : AuditOutcome.Failure,
                    succeeded ? AuditLogSeverity.Info : AuditLogSeverity.Error,
                    new
                    {
                        backupId = finalBackup.Id,
                        name = finalBackup.Name,
                        file = finalBackup.FileName,
                        sizeBytes = finalBackup.FileSizeBytes,
                        type = finalBackup.BackupType.ToString(),
                        status = finalBackup.Status.ToString(),
                        error = string.IsNullOrEmpty(finalBackup.ErrorMessage) ? null : finalBackup.ErrorMessage
                    });

                return MapToDTO(finalBackup);
            }
            catch (Exception ex)
            {
                _auditTrail.LogSystem(AuditActions.BackupFailed, $"Backup could not be created: {ex.Message}", "Backup",
                    AuditOutcome.Failure, AuditLogSeverity.Error, new { name = request.Name, type = request.BackupType, error = ex.GetType().Name });
                throw new Exception($"Failed to create backup: {ex.Message}", ex);
            }
        }

        private static string FormatSize(long bytes)
            => bytes >= 1_048_576 ? $"{bytes / 1_048_576.0:F1} MB" : $"{bytes / 1024.0:F0} KB";

        public async Task<BackupProgressDTO> GetBackupProgressAsync(string backupId)
        {
            if (_activeBackups.TryGetValue(backupId, out var progress))
            {
                return progress;
            }

            var backup = await _backupRepository.GetByIdAsync(int.Parse(backupId));
            if (backup != null)
            {
                return new BackupProgressDTO
                {
                    BackupId = backupId,
                    Status = backup.Status.ToString(),
                    ProgressPercentage = backup.Status == BackupStatus.Completed ? 100 : 0,
                    StartedAt = backup.CreatedAt,
                    ErrorMessage = backup.ErrorMessage
                };
            }

            return new BackupProgressDTO
            {
                BackupId = backupId,
                Status = "NotFound",
                ProgressPercentage = 0
            };
        }

        public async Task<bool> CancelBackupAsync(string backupId)
        {
            if (_activeBackups.TryGetValue(backupId, out var progress))
            {
                progress.Status = "Cancelled";
                await _backupRepository.UpdateBackupStatusAsync(int.Parse(backupId), BackupStatus.Cancelled);
                return true;
            }
            return false;
        }

        public async Task<BackupValidationDTO> ValidateBackupAsync(int backupId)
        {
            var backup = await _backupRepository.GetByIdAsync(backupId);
            if (backup == null)
            {
                return new BackupValidationDTO
                {
                    IsValid = false,
                    Errors = new List<string> { "Backup not found" }
                };
            }

            var validation = await ValidateBackupFileAsync(backup.FilePath);

            // The file alone can't say what it is; the catalog record can.
            if (validation.BackupInfo is { } info)
            {
                var dto = MapToDTO(backup);
                info.Name = dto.Name;
                info.BackupType = dto.BackupType;
                info.CreatedAt = backup.CreatedAt;
                info.IsEncrypted = backup.IsEncrypted;
                info.CompressionType = backup.CompressionType ?? string.Empty;
                info.TablesIncluded = dto.TablesIncluded;
                info.TableCount = dto.TablesIncluded.Count;
            }

            return validation;
        }

        public async Task<BackupValidationDTO> ValidateBackupFileAsync(string filePath)
        {
            var validation = new BackupValidationDTO { IsValid = true };

            if (!File.Exists(filePath))
            {
                validation.IsValid = false;
                validation.Errors.Add("Backup file does not exist");
                return validation;
            }

            try
            {
                var fileInfo = new FileInfo(filePath);
                var backupInfo = new BackupInfoDTO
                {
                    FileSizeBytes = fileInfo.Length,
                    FileSizeFormatted = FormatFileSize(fileInfo.Length),
                    CreatedAt = fileInfo.CreationTimeUtc,
                    Checksum = await CalculateBackupChecksumAsync(filePath)
                };

                validation.BackupInfo = backupInfo;
            }
            catch (Exception ex)
            {
                validation.IsValid = false;
                validation.Errors.Add($"Failed to validate backup file: {ex.Message}");
            }

            return validation;
        }

        #endregion

        #region Restore Operations

        public async Task<bool> RestoreBackupAsync(RestoreRequest request)
        {
            try
            {
                var backup = await _backupRepository.GetByIdAsync(request.BackupId);
                if (backup == null)
                {
                    throw new Exception("Backup not found");
                }

                var restoreId = Guid.NewGuid().ToString();
                var progress = new BackupProgressDTO
                {
                    BackupId = restoreId,
                    Status = "InProgress",
                    StartedAt = DateTime.UtcNow
                };

                _activeRestores[restoreId] = progress;

                // Execute restore synchronously to avoid DbContext concurrency issues
                // For a production system, use a proper background job queue (Hangfire, etc.)
                await PerformRestoreAsync(restoreId, request, backup.FilePath);

                // A restore replaces the whole database, including the Backups table, so any
                // backups taken after the restored point vanish from the list. Rebuild the
                // catalog from the .bak files (and their sidecars) still present on disk.
                await ReconcileBackupCatalogAsync();

                // The audit trail lives in its own database file, so this restore did not touch it: the history up to
                // this moment is intact, and this entry marks the point where the business data jumped back in time.
                _auditTrail.LogSystem(AuditActions.BackupRestored,
                    $"Database restored from backup '{backup.Name}' ({backup.FileName}, taken {backup.CreatedAt:yyyy-MM-dd HH:mm} UTC). " +
                    "Business data changed after that moment was replaced.",
                    "Backup", AuditOutcome.Success, AuditLogSeverity.Warning,
                    new
                    {
                        backupId = backup.Id,
                        name = backup.Name,
                        file = backup.FileName,
                        backupTakenAtUtc = backup.CreatedAt,
                        createdBackupBeforeRestore = request.CreateBackupBeforeRestore
                    });

                return true;
            }
            catch (Exception ex)
            {
                _auditTrail.LogSystem(AuditActions.BackupRestoreFailed, $"Restore from backup #{request.BackupId} failed: {ex.Message}", "Backup",
                    AuditOutcome.Failure, AuditLogSeverity.Critical, new { backupId = request.BackupId, error = ex.GetType().Name });
                throw new Exception($"Failed to start restore: {ex.Message}", ex);
            }
        }

        public async Task<BackupProgressDTO> GetRestoreProgressAsync(string restoreId)
        {
            if (_activeRestores.TryGetValue(restoreId, out var progress))
            {
                return progress;
            }

            return new BackupProgressDTO
            {
                BackupId = restoreId,
                Status = "NotFound",
                ProgressPercentage = 0
            };
        }

        public async Task<bool> CancelRestoreAsync(string restoreId)
        {
            if (_activeRestores.TryGetValue(restoreId, out var progress))
            {
                progress.Status = "Cancelled";
                return true;
            }
            return false;
        }

        #endregion

        #region Backup Management

        public async Task<List<BackupDTO>> GetBackupsAsync(BackupFilterRequest request)
        {
            try
            {
                // Re-register any backup files on disk that are missing from the table
                // (e.g. after a restore reverted the Backups table).
                await ReconcileBackupCatalogAsync();

                // Get all backups - handle null
                var allBackups = await _backupRepository.GetAllAsync();
                IEnumerable<BackupEntity> backups = allBackups ?? new List<BackupEntity>();

                // If no backups, return empty list
                if (!backups.Any())
                {
                    return new List<BackupDTO>();
                }

                // Apply filters
                if (!string.IsNullOrEmpty(request.BackupType))
                {
                    var type = ParseBackupType(request.BackupType);
                    backups = backups.Where(b => b.BackupType == type);
                }

                if (!string.IsNullOrEmpty(request.Status))
                {
                    var status = ParseBackupStatus(request.Status);
                    backups = backups.Where(b => b.Status == status);
                }

                if (request.FromDate.HasValue)
                    backups = backups.Where(b => b.CreatedAt >= request.FromDate.Value);

                if (request.ToDate.HasValue)
                    backups = backups.Where(b => b.CreatedAt <= request.ToDate.Value);

                if (!string.IsNullOrEmpty(request.CreatedBy))
                    backups = backups.Where(b => b.CreatedBy == request.CreatedBy);

                if (request.IsEncrypted.HasValue)
                    backups = backups.Where(b => b.IsEncrypted == request.IsEncrypted.Value);

                // Apply sorting
                var orderedBackups = request.SortBy.ToLower() switch
                {
                    "name" => request.SortDirection == "asc" ? backups.OrderBy(b => b.Name) : backups.OrderByDescending(b => b.Name),
                    "size" => request.SortDirection == "asc" ? backups.OrderBy(b => b.FileSizeBytes) : backups.OrderByDescending(b => b.FileSizeBytes),
                    _ => request.SortDirection == "asc" ? backups.OrderBy(b => b.CreatedAt) : backups.OrderByDescending(b => b.CreatedAt)
                };

                // Apply pagination
                var pagedBackups = orderedBackups
                    .Skip((request.Page - 1) * request.PageSize)
                    .Take(request.PageSize)
                    .ToList();

                // Map to DTOs - handle null
                var result = pagedBackups.Select(MapToDTO).Where(dto => dto != null).ToList();
                return result ?? new List<BackupDTO>();
            }
            catch (Exception ex)
            {
                // Log error and return empty list instead of throwing
                Console.WriteLine($"Error in GetBackupsAsync: {ex.Message}");
                return new List<BackupDTO>();
            }
        }

        public async Task<BackupStatsDTO> GetBackupStatsAsync()
        {
            try
            {
                await ReconcileBackupCatalogAsync();

                var backups = await _backupRepository.GetAllAsync();
                var schedules = await _scheduleRepository.GetActiveSchedulesAsync();

                var backupList = backups?.ToList() ?? new List<BackupEntity>();
                var scheduleList = schedules?.ToList() ?? new List<Core.Entities.Backup.BackupSchedule>();

                var stats = new BackupStatsDTO
                {
                    TotalBackups = backupList.Count,
                    SuccessfulBackups = backupList.Count(b => b.Status == BackupStatus.Completed),
                    FailedBackups = backupList.Count(b => b.Status == BackupStatus.Failed),
                    ScheduledBackups = scheduleList.Count,
                    TotalBackupSize = backupList.Sum(b => b.FileSizeBytes),
                    TotalBackupSizeFormatted = FormatFileSize(backupList.Sum(b => b.FileSizeBytes)),
                    LastBackupDate = backupList.Where(b => b.Status == BackupStatus.Completed && b.CompletedAt.HasValue).Max(b => b.CompletedAt),
                    NextScheduledBackup = scheduleList.Where(s => s.NextRun.HasValue).Min(s => s.NextRun),
                    RecentBackups = backupList.OrderByDescending(b => b.CreatedAt).Take(5).Select(MapToDTO).ToList(),
                    ActiveSchedules = scheduleList.Select(MapScheduleToDTO).ToList(),
                    BackupTypeCounts = await _backupRepository.GetBackupTypeCountsAsync() ?? new Dictionary<string, int>(),
                    StorageUsageByMonth = await _backupRepository.GetStorageUsageByMonthAsync() ?? new Dictionary<string, long>()
                };

                return stats;
            }
            catch (Exception ex)
            {
                // Return empty stats if there's an error
                return new BackupStatsDTO
                {
                    TotalBackups = 0,
                    SuccessfulBackups = 0,
                    FailedBackups = 0,
                    ScheduledBackups = 0,
                    TotalBackupSize = 0,
                    TotalBackupSizeFormatted = "0 B",
                    LastBackupDate = null,
                    NextScheduledBackup = null,
                    RecentBackups = new List<BackupDTO>(),
                    ActiveSchedules = new List<BackupScheduleDTO>(),
                    BackupTypeCounts = new Dictionary<string, int>(),
                    StorageUsageByMonth = new Dictionary<string, long>()
                };
            }
        }

        public async Task<bool> DeleteBackupAsync(int backupId)
        {
            try
            {
                // Capture the file path before the record is gone, so we can also
                // remove the metadata sidecar and stop reconciliation from re-adding it.
                string? filePath = null;
                try { filePath = (await _backupRepository.GetByIdAsync(backupId))?.FilePath; }
                catch { /* record may not exist */ }

                // Delete the file first
                await _backupRepository.DeleteBackupFileAsync(backupId);

                // Then delete the record
                await _backupRepository.DeleteAsync(backupId);

                if (!string.IsNullOrEmpty(filePath))
                {
                    DeleteBackupSidecar(filePath);
                }

                // Removing the .bak file from disk is not a database change, so it needs its own entry.
                _auditTrail.LogSystem(AuditActions.BackupDeleted,
                    $"Backup #{backupId} deleted ({(string.IsNullOrEmpty(filePath) ? "file unknown" : Path.GetFileName(filePath))})",
                    "Backup", details: new { backupId, file = filePath is null ? null : Path.GetFileName(filePath) });

                return true;
            }
            catch (Exception ex)
            {
                _auditTrail.LogSystem(AuditActions.BackupDeleted, $"Backup #{backupId} could not be deleted: {ex.Message}", "Backup",
                    AuditOutcome.Failure, AuditLogSeverity.Warning, new { backupId, error = ex.GetType().Name });
                return false;
            }
        }

        public async Task<bool> DeleteBackupFileAsync(int backupId)
        {
            return await _backupRepository.DeleteBackupFileAsync(backupId);
        }

        public async Task<bool> DownloadBackupAsync(int backupId, string downloadPath)
        {
            try
            {
                var backup = await _backupRepository.GetByIdAsync(backupId);
                if (backup == null || !File.Exists(backup.FilePath))
                {
                    return false;
                }

                File.Copy(backup.FilePath, downloadPath, true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Backup Scheduling

        public async Task<BackupScheduleDTO> CreateScheduleAsync(BackupScheduleDTO schedule)
        {
            var entity = new Core.Entities.Backup.BackupSchedule
            {
                Name = schedule.Name,
                Description = schedule.Description,
                BackupType = ParseBackupType(schedule.BackupType),
                ScheduleFrequency = schedule.ScheduleFrequency,
                ScheduleTime = schedule.ScheduleTime,
                ScheduleDayOfWeek = schedule.ScheduleDayOfWeek,
                ScheduleDayOfMonth = schedule.ScheduleDayOfMonth,
                IsActive = schedule.IsActive,
                RetentionDays = schedule.RetentionDays,
                CreatedBy = "System", // TODO: Get from current user
                Settings = JsonSerializer.Serialize(schedule.Settings),
                CreatedAt = DateTime.UtcNow
            };

            entity.NextRun = CalculateNextRunTime(entity);
            var savedSchedule = await _scheduleRepository.SaveAsync(entity);
            return MapScheduleToDTO(savedSchedule);
        }

        public async Task<BackupScheduleDTO> UpdateScheduleAsync(BackupScheduleDTO schedule)
        {
            var entity = await _scheduleRepository.GetByIdAsync(schedule.Id);
            if (entity != null)
            {
                entity.Name = schedule.Name;
                entity.Description = schedule.Description;
                entity.BackupType = ParseBackupType(schedule.BackupType);
                entity.ScheduleFrequency = schedule.ScheduleFrequency;
                entity.ScheduleTime = schedule.ScheduleTime;
                entity.ScheduleDayOfWeek = schedule.ScheduleDayOfWeek;
                entity.ScheduleDayOfMonth = schedule.ScheduleDayOfMonth;
                entity.IsActive = schedule.IsActive;
                entity.RetentionDays = schedule.RetentionDays;
                entity.Settings = JsonSerializer.Serialize(schedule.Settings);
                entity.NextRun = CalculateNextRunTime(entity);

                var updatedSchedule = await _scheduleRepository.SaveAsync(entity);
                return MapScheduleToDTO(updatedSchedule);
            }

            throw new Exception("Schedule not found");
        }

        public async Task<bool> DeleteScheduleAsync(int scheduleId)
        {
            await _scheduleRepository.DeleteAsync(scheduleId);
            return true;
        }

        public async Task<List<BackupScheduleDTO>> GetSchedulesAsync()
        {
            var schedules = await _scheduleRepository.GetAllAsync();
            return schedules.Select(MapScheduleToDTO).ToList();
        }

        public async Task<List<BackupScheduleDTO>> GetActiveSchedulesAsync()
        {
            var schedules = await _scheduleRepository.GetActiveSchedulesAsync();
            return schedules.Select(MapScheduleToDTO).ToList();
        }

        public async Task<bool> EnableScheduleAsync(int scheduleId)
        {
            var schedule = await _scheduleRepository.GetByIdAsync(scheduleId);
            if (schedule != null)
            {
                schedule.IsActive = true;
                schedule.NextRun = CalculateNextRunTime(schedule);
                await _scheduleRepository.SaveAsync(schedule);
                return true;
            }
            return false;
        }

        public async Task<bool> DisableScheduleAsync(int scheduleId)
        {
            var schedule = await _scheduleRepository.GetByIdAsync(scheduleId);
            if (schedule != null)
            {
                schedule.IsActive = false;
                schedule.NextRun = null;
                await _scheduleRepository.SaveAsync(schedule);
                return true;
            }
            return false;
        }

        public async Task<bool> RunScheduleNowAsync(int scheduleId)
        {
            var schedule = await _scheduleRepository.GetByIdAsync(scheduleId);
            if (schedule == null)
            {
                return false;
            }

            try
            {
                // Create backup based on schedule settings
                var backupRequest = new BackupRequest
                {
                    Name = $"Manual_{schedule.Name}_{DateTime.Now:yyyyMMdd_HHmmss}",
                    Description = $"Manually triggered backup from schedule: {schedule.Name}",
                    BackupType = schedule.BackupType.ToString(),
                    IncludeData = true,
                    IncludeSchema = true,
                    IncludeUsers = false,
                    IsEncrypted = false,
                    CompressionType = "GZip"
                };

                await CreateBackupAsync(backupRequest);

                // Update last run time but keep the next run as scheduled
                schedule.LastRun = DateTime.UtcNow;
                await _scheduleRepository.SaveAsync(schedule);

                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Backup Maintenance

        public async Task<bool> CleanupOldBackupsAsync(int daysToKeep = 30)
        {
            try
            {
                var oldBackups = await _backupRepository.GetOldBackupsAsync(daysToKeep);
                var removed = 0;
                foreach (var backup in oldBackups)
                {
                    if (await DeleteBackupAsync(backup.Id)) removed++;
                }

                _auditTrail.LogSystem(AuditActions.BackupCleanup,
                    $"Backup cleanup: {removed} backup(s) older than {daysToKeep} days removed", "Backup",
                    details: new { daysToKeep, removed });
                return true;
            }
            catch (Exception ex)
            {
                _auditTrail.LogSystem(AuditActions.BackupCleanup, $"Backup cleanup failed: {ex.Message}", "Backup",
                    AuditOutcome.Failure, AuditLogSeverity.Warning, new { daysToKeep, error = ex.GetType().Name });
                return false;
            }
        }

        public async Task<bool> CompressBackupAsync(int backupId)
        {
            try
            {
                var backup = await _backupRepository.GetByIdAsync(backupId);
                if (backup == null || !File.Exists(backup.FilePath))
                {
                    return false;
                }

                var compressedPath = backup.FilePath + ".gz";
                using (var sourceStream = new FileStream(backup.FilePath, FileMode.Open))
                using (var compressedStream = new FileStream(compressedPath, FileMode.Create))
                using (var gzipStream = new GZipStream(compressedStream, CompressionMode.Compress))
                {
                    await sourceStream.CopyToAsync(gzipStream);
                }

                // Update backup record
                backup.FilePath = compressedPath;
                backup.FileName = Path.GetFileName(compressedPath);
                backup.CompressionType = "GZip";
                backup.FileSizeBytes = new FileInfo(compressedPath).Length;
                await _backupRepository.SaveAsync(backup);

                // Delete original file
                File.Delete(backup.FilePath);

                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> EncryptBackupAsync(int backupId, string password)
        {
            // Implementation would encrypt the backup file
            // This is a placeholder for the encryption logic
            return await Task.FromResult(true);
        }

        public async Task<bool> DecryptBackupAsync(int backupId, string password)
        {
            // Implementation would decrypt the backup file
            // This is a placeholder for the decryption logic
            return await Task.FromResult(true);
        }

        #endregion

        #region Database Operations

        public async Task<List<string>> GetDatabaseTablesAsync()
        {
            var tables = new List<string>();
            using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name";

            await _context.Database.OpenConnectionAsync();
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }

            return tables;
        }

        public async Task<long> GetTableSizeAsync(string tableName)
        {
            using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM \"{tableName.Replace("\"", "\"\"")}\"";
            
            await _context.Database.OpenConnectionAsync();
            var result = await command.ExecuteScalarAsync();
            
            return Convert.ToInt64(result);
        }

        public async Task<long> GetTotalDatabaseSizeAsync()
        {
            var dbFile = GetDatabaseFilePath();
            return await Task.FromResult(File.Exists(dbFile) ? new FileInfo(dbFile).Length : 0L);
        }

        public async Task<List<string>> GetDatabaseUsersAsync()
        {
            // SQLite has no database-level principals; application users live in the Users table.
            return await Task.FromResult(new List<string>());
        }

        #endregion

        #region File Operations

        public async Task<string> GenerateBackupFileNameAsync(string backupType, DateTime timestamp)
        {
            var type = backupType.ToLower();
            var dateStr = timestamp.ToString("yyyyMMdd_HHmmss");
            var extension = "bak";
            
            return $"ERP_{type}_backup_{dateStr}.{extension}";
        }

        public async Task<string> GetBackupDirectoryAsync()
        {
            // Backups live in a "Backups" folder next to the SQLite database file, i.e. in the
            // per-user app-data folder, which is always writable (the install dir may not be).
            var dbDir = Path.GetDirectoryName(Path.GetFullPath(GetDatabaseFilePath()));
            return await Task.FromResult(Path.Combine(dbDir ?? AppDomain.CurrentDomain.BaseDirectory, "Backups"));
        }

        public async Task<bool> EnsureBackupDirectoryExistsAsync()
        {
            try
            {
                var backupDir = await GetBackupDirectoryAsync();
                if (!Directory.Exists(backupDir))
                {
                    Directory.CreateDirectory(backupDir);
                }
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                // If we can't create the directory, SQL Server might still be able to write there
                // (e.g., SQL Server's default backup path)
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating backup directory: {ex.Message}");
                return false;
            }
        }

        public async Task<long> GetAvailableDiskSpaceAsync()
        {
            var backupDir = await GetBackupDirectoryAsync();
            var drive = new DriveInfo(Path.GetPathRoot(backupDir));
            return drive.AvailableFreeSpace;
        }

        public async Task<bool> CheckDiskSpaceAsync(long requiredSpace)
        {
            var availableSpace = await GetAvailableDiskSpaceAsync();
            return availableSpace >= requiredSpace;
        }

        #endregion

        #region Backup Verification

        public async Task<bool> VerifyBackupIntegrityAsync(int backupId)
        {
            var backup = await _backupRepository.GetByIdAsync(backupId);
            if (backup == null || !File.Exists(backup.FilePath))
            {
                return false;
            }

            try
            {
                // Basic file integrity check
                var fileInfo = new FileInfo(backup.FilePath);
                return fileInfo.Length > 0 && fileInfo.Exists;
            }
            catch
            {
                return false;
            }
        }

        public async Task<string> CalculateBackupChecksumAsync(string filePath)
        {
            using var md5 = MD5.Create();
            using var stream = File.OpenRead(filePath);
            var hash = await Task.Run(() => md5.ComputeHash(stream));
            return Convert.ToBase64String(hash);
        }

        public async Task<bool> CompareBackupChecksumsAsync(int backupId1, int backupId2)
        {
            var backup1 = await _backupRepository.GetByIdAsync(backupId1);
            var backup2 = await _backupRepository.GetByIdAsync(backupId2);

            if (backup1 == null || backup2 == null)
                return false;

            var checksum1 = await CalculateBackupChecksumAsync(backup1.FilePath);
            var checksum2 = await CalculateBackupChecksumAsync(backup2.FilePath);

            return checksum1 == checksum2;
        }

        #endregion

        #region System Operations

        public async Task<bool> TestBackupConnectionAsync()
        {
            try
            {
                await _context.Database.OpenConnectionAsync();
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                await _context.Database.CloseConnectionAsync();
            }
        }

        public async Task<bool> TestRestoreConnectionAsync()
        {
            // Similar to backup connection test
            return await TestBackupConnectionAsync();
        }

        public async Task<List<string>> GetBackupHistoryAsync(int days = 30)
        {
            var fromDate = DateTime.UtcNow.AddDays(-days);
            var backups = await _backupRepository.GetBackupsByDateRangeAsync(fromDate, DateTime.UtcNow);
            
            return backups.Select(b => $"{b.Name} - {b.CreatedAt.UtcToLocal():yyyy-MM-dd HH:mm} - {b.Status}").ToList();
        }

        public async Task<Dictionary<string, object>> GetSystemInfoAsync()
        {
            var info = new Dictionary<string, object>
            {
                ["DatabaseVersion"] = _context.Database.ProviderName,
                ["BackupDirectory"] = await GetBackupDirectoryAsync(),
                ["AvailableDiskSpace"] = await GetAvailableDiskSpaceAsync(),
                ["TotalBackups"] = (await _backupRepository.GetAllAsync()).Count(),
                ["ActiveSchedules"] = (await _scheduleRepository.GetActiveSchedulesAsync()).Count()
            };

            return info;
        }

        #endregion

        #region Bulk Operations

        public async Task<List<BackupDTO>> BulkDeleteBackupsAsync(List<int> backupIds)
        {
            var deletedBackups = new List<BackupDTO>();
            
            foreach (var id in backupIds)
            {
                var backup = await _backupRepository.GetByIdAsync(id);
                if (backup != null)
                {
                    await DeleteBackupAsync(id);
                    deletedBackups.Add(MapToDTO(backup));
                }
            }

            return deletedBackups;
        }

        public async Task<bool> BulkRestoreBackupsAsync(List<RestoreRequest> requests)
        {
            var results = new List<bool>();
            
            foreach (var request in requests)
            {
                try
                {
                    results.Add(await RestoreBackupAsync(request));
                }
                catch
                {
                    results.Add(false);
                }
            }

            return results.All(r => r);
        }

        public async Task<bool> BulkValidateBackupsAsync(List<int> backupIds)
        {
            var results = new List<bool>();
            
            foreach (var id in backupIds)
            {
                results.Add(await VerifyBackupIntegrityAsync(id));
            }

            return results.All(r => r);
        }

        #endregion

        #region Export/Import

        public async Task<string> ExportBackupMetadataAsync(int backupId)
        {
            var backup = await _backupRepository.GetByIdAsync(backupId);
            if (backup == null)
            {
                return string.Empty;
            }

            var metadata = new
            {
                backup.Name,
                backup.Description,
                backup.FileName,
                backup.FilePath,
                backup.FileSizeBytes,
                backup.BackupType,
                backup.Status,
                backup.CreatedAt,
                backup.CompletedAt,
                backup.CreatedBy,
                backup.IsEncrypted,
                backup.CompressionType,
                TablesIncluded = JsonSerializer.Deserialize<List<string>>(backup.TablesIncluded),
                TablesExcluded = JsonSerializer.Deserialize<List<string>>(backup.TablesExcluded)
            };

            return JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        }

        public async Task<bool> ImportBackupMetadataAsync(string metadataJson)
        {
            try
            {
                var metadata = JsonSerializer.Deserialize<dynamic>(metadataJson);
                // Implementation would import the metadata
                return await Task.FromResult(true);
            }
            catch
            {
                return false;
            }
        }

        public async Task<string> GenerateBackupReportAsync(DateTime fromDate, DateTime toDate)
        {
            var backups = await _backupRepository.GetBackupsByDateRangeAsync(fromDate, toDate);
            
            var report = new StringBuilder();
            report.AppendLine($"Backup Report - {fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd}");
            report.AppendLine("=" + new string('=', 50));
            report.AppendLine($"Total Backups: {backups.Count()}");
            report.AppendLine($"Successful: {backups.Count(b => b.Status == BackupStatus.Completed)}");
            report.AppendLine($"Failed: {backups.Count(b => b.Status == BackupStatus.Failed)}");
            report.AppendLine($"Total Size: {FormatFileSize(backups.Sum(b => b.FileSizeBytes))}");
            report.AppendLine();
            
            foreach (var backup in backups.OrderByDescending(b => b.CreatedAt))
            {
                report.AppendLine($"{backup.Name} - {backup.Status} - {backup.CreatedAt.UtcToLocal():yyyy-MM-dd HH:mm}");
            }

            return report.ToString();
        }

        #endregion

        #region Emergency Operations

        public async Task<bool> CreateEmergencyBackupAsync()
        {
            var request = new BackupRequest
            {
                Name = $"Emergency Backup - {DateTime.Now:yyyy-MM-dd HH:mm}",
                Description = "Emergency backup created automatically",
                BackupType = "Full",
                CompressionType = "GZip"
            };

            try
            {
                await CreateBackupAsync(request);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<List<string>> GetEmergencyRestoreOptionsAsync()
        {
            var backups = await _backupRepository.GetAllAsync();
            return backups
                .Where(b => b.Status == BackupStatus.Completed)
                .OrderByDescending(b => b.CreatedAt)
                .Take(10)
                .Select(b => $"{b.Name} ({b.CreatedAt.UtcToLocal():yyyy-MM-dd HH:mm})")
                .ToList();
        }

        public async Task<bool> EmergencyRestoreAsync(string backupFilePath)
        {
            var request = new RestoreRequest
            {
                BackupFilePath = backupFilePath,
                RestoreData = true,
                RestoreSchema = true,
                CreateBackupBeforeRestore = false
            };

            try
            {
                return await RestoreBackupAsync(request);
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Private Helper Methods

        private async Task PerformBackupAsync(string backupId, BackupRequest request, string backupPath)
        {
            var progress = new BackupProgressDTO
            {
                BackupId = backupId,
                Status = "InProgress",
                StartedAt = DateTime.UtcNow
            };

            _activeBackups[backupId] = progress;

            try
            {
                progress.CurrentOperation = "Preparing backup";
                progress.ProgressPercentage = 10;

                var actualBackupPath = await GetValidBackupPathAsync(backupPath);

                progress.CurrentOperation = "Backing up database";
                progress.ProgressPercentage = 30;

                // SQLite has no differential/incremental backups: every backup is a full,
                // consistent snapshot taken with VACUUM INTO (safe while the app is running).
                // VACUUM INTO refuses to overwrite an existing file.
                if (File.Exists(actualBackupPath))
                {
                    File.Delete(actualBackupPath);
                }

                progress.CurrentOperation = "Writing database snapshot";
                progress.ProgressPercentage = 50;

                // Separate connection so the snapshot doesn't interfere with EF's connection.
                using (var backupConnection = new SqliteConnection(_context.Database.GetDbConnection().ConnectionString))
                {
                    await backupConnection.OpenAsync();

                    using var command = backupConnection.CreateCommand();
                    command.CommandText = "VACUUM INTO $path";
                    command.Parameters.AddWithValue("$path", actualBackupPath);
                    command.CommandTimeout = 600;

                    await command.ExecuteNonQueryAsync();
                }

                // Update the backup record with the actual path used
                var backup = await _backupRepository.GetByIdAsync(int.Parse(backupId));
                if (backup != null && actualBackupPath != backupPath)
                {
                    backup.FilePath = actualBackupPath;
                    backup.FileName = Path.GetFileName(actualBackupPath);
                    await _backupRepository.SaveAsync(backup);
                }

                progress.CurrentOperation = "Verifying backup";
                progress.ProgressPercentage = 90;

                // Verify the backup file was actually created and get file size
                long fileSizeBytes = 0;
                var verifiedPath = await FindBackupFileAsync(actualBackupPath);
                
                if (string.IsNullOrEmpty(verifiedPath))
                {
                    throw new Exception($"Backup command completed but file was not created at: {actualBackupPath}. Check folder permissions.");
                }
                
                try
                {
                    var fileInfo = new FileInfo(verifiedPath);
                    fileSizeBytes = fileInfo.Length;
                    
                    if (fileSizeBytes == 0)
                    {
                        throw new Exception("Backup file was created but is empty (0 bytes). Check folder permissions and free disk space.");
                    }
                }
                catch (Exception ex) when (!(ex.Message.Contains("Backup")))
                {
                    // File access error but backup might still be valid
                    Console.WriteLine($"Warning: Could not verify backup file size: {ex.Message}");
                }
                
                // Update the backup record
                backup = await _backupRepository.GetByIdAsync(int.Parse(backupId));
                if (backup != null)
                {
                    backup.FilePath = verifiedPath; // Use the verified path
                    backup.FileName = Path.GetFileName(verifiedPath);
                    backup.FileSizeBytes = fileSizeBytes;
                    backup.CompletedAt = DateTime.UtcNow;
                    backup.Duration = backup.CompletedAt.Value - backup.CreatedAt;
                    await _backupRepository.SaveAsync(backup);
                }
                
                await _backupRepository.UpdateBackupStatusAsync(int.Parse(backupId), BackupStatus.Completed);

                // Persist metadata alongside the .bak file so the backup list can be rebuilt
                // even after a restore reverts the Backups table to an older state.
                if (backup != null)
                {
                    backup.Status = BackupStatus.Completed;
                    await WriteBackupSidecarAsync(backup);
                }

                progress.Status = "Completed";
                progress.ProgressPercentage = 100;
                progress.CurrentOperation = $"Backup completed successfully. File: {verifiedPath} ({FormatFileSize(fileSizeBytes)})";
            }
            catch (Exception ex)
            {
                progress.Status = "Failed";
                progress.ErrorMessage = ex.Message;
                await _backupRepository.UpdateBackupStatusAsync(int.Parse(backupId), BackupStatus.Failed, ex.Message);
            }
            finally
            {
                _activeBackups.Remove(backupId);
            }
        }

        private async Task PerformRestoreAsync(string restoreId, RestoreRequest request, string backupPath)
        {
            var progress = _activeRestores[restoreId];

            try
            {
                progress.CurrentOperation = "Preparing restore";
                progress.ProgressPercentage = 10;

                // Try to find the backup file - it might be in a different location
                var actualBackupPath = await FindBackupFileAsync(backupPath);
                if (string.IsNullOrEmpty(actualBackupPath))
                {
                    throw new FileNotFoundException($"Backup file not found: {backupPath}. Please ensure the backup file exists and is accessible.");
                }

                if (request.CreateBackupBeforeRestore)
                {
                    progress.CurrentOperation = "Creating backup before restore";
                    progress.ProgressPercentage = 20;
                    await CreateEmergencyBackupAsync();
                }

                progress.CurrentOperation = "Restoring database";
                progress.ProgressPercentage = 50;

                // Overwrite the live SQLite database with the backup
                await RestoreDatabaseFromFileAsync(actualBackupPath, request);

                progress.CurrentOperation = "Finalizing restore";
                progress.ProgressPercentage = 90;

                progress.Status = "Completed";
                progress.ProgressPercentage = 100;
                progress.CurrentOperation = "Restore completed successfully";
            }
            catch (Exception ex)
            {
                progress.Status = "Failed";
                progress.ErrorMessage = ex.Message;
                throw; // Re-throw to propagate the error
            }
            finally
            {
                _activeRestores.Remove(restoreId);
            }
        }

        /// <summary>
        /// Gets a valid, writable backup path
        /// </summary>
        private async Task<string> GetValidBackupPathAsync(string requestedPath)
        {
            var fileName = Path.GetFileName(requestedPath);
            
            // Preferred: the Backups folder next to the database file
            try
            {
                var backupDir = await GetBackupDirectoryAsync();
                Directory.CreateDirectory(backupDir);
                return Path.Combine(backupDir, fileName);
            }
            catch
            {
                // Can't use the default folder, fall through to the alternatives below
            }

            // Try common backup locations
            var commonPaths = new[]
            {
                @"C:\SQLBackups\ERP",
                @"C:\Backups\ERP",
                @"C:\Temp\Backups",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ERP", "Backups")
            };
            
            foreach (var basePath in commonPaths)
            {
                try
                {
                    if (!Directory.Exists(basePath))
                    {
                        Directory.CreateDirectory(basePath);
                    }
                    
                    // Test if we can write to this directory
                    var testFile = Path.Combine(basePath, ".write_test");
                    File.WriteAllText(testFile, "test");
                    File.Delete(testFile);
                    
                    return Path.Combine(basePath, fileName);
                }
                catch
                {
                    // Can't write to this directory, try next
                }
            }
            
            // Last resort: use the original requested path
            var requestedDir = Path.GetDirectoryName(requestedPath);
            if (!string.IsNullOrEmpty(requestedDir) && !Directory.Exists(requestedDir))
            {
                try
                {
                    Directory.CreateDirectory(requestedDir);
                }
                catch
                {
                    // Ignore directory creation errors
                }
            }
            
            return requestedPath;
        }

        /// <summary>
        /// Tries to find the backup file in various locations
        /// </summary>
        private async Task<string?> FindBackupFileAsync(string originalPath)
        {
            // 1. Check the original path first
            if (File.Exists(originalPath))
            {
                return originalPath;
            }

            var fileName = Path.GetFileName(originalPath);

            // 2. Check in the default backup directory (next to the database file)
            try
            {
                var defaultPath = Path.Combine(await GetBackupDirectoryAsync(), fileName);
                if (File.Exists(defaultPath))
                {
                    return defaultPath;
                }
            }
            catch
            {
                // Ignore errors when resolving the default backup directory
            }

            // 3. Check in application's backup directory
            var appBackupDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backups");
            var appPath = Path.Combine(appBackupDir, fileName);
            if (File.Exists(appPath))
            {
                return appPath;
            }

            // 4. Check common backup locations
            var commonPaths = new[]
            {
                @"C:\Backups",
                @"C:\SQLBackups",
                @"D:\Backups",
                @"D:\SQLBackups"
            };

            foreach (var basePath in commonPaths)
            {
                var checkPath = Path.Combine(basePath, fileName);
                if (File.Exists(checkPath))
                {
                    return checkPath;
                }
                
                var erpCheckPath = Path.Combine(basePath, "ERP", fileName);
                if (File.Exists(erpCheckPath))
                {
                    return erpCheckPath;
                }
            }

            // File not found anywhere
            return null;
        }

        #region Backup Catalog Reconciliation

        // The Backups table lives inside the ERP database itself, so a database restore
        // rolls it back to an older state and "loses" newer backups. These helpers keep a
        // JSON sidecar next to every .bak file and rebuild missing table rows from disk.

        private sealed class BackupSidecar
        {
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string BackupType { get; set; } = "Full";
            public string CreatedBy { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
            public DateTime? CompletedAt { get; set; }
            public long FileSizeBytes { get; set; }
            public bool IsEncrypted { get; set; }
            public string CompressionType { get; set; } = "None";
            public string TablesIncluded { get; set; } = "[]";
            public string TablesExcluded { get; set; } = "[]";
        }

        private static string GetSidecarPath(string backupFilePath) => backupFilePath + ".meta.json";

        // Description prefix stamped on rows that were rebuilt from a file on disk rather than
        // created through CreateBackupAsync. Such rows are re-validated on every reconcile and
        // dropped (row only, never the file) if they turn out not to belong to this database.
        private const string AutoRecoveredMarker = "[auto-recovered]";

        private static bool IsAutoRecovered(BackupEntity b) =>
            !string.IsNullOrEmpty(b.Description) &&
            (b.Description.StartsWith(AutoRecoveredMarker, StringComparison.Ordinal) ||
             // legacy marker from the first version of the reconciler
             b.Description.StartsWith("Recovered from backup file on disk", StringComparison.Ordinal));

        /// <summary>
        /// True only if the .bak file is a backup of the database this app is connected to.
        /// A sidecar next to the file proves this ERP instance wrote it; otherwise the file
        /// header is inspected. If the header can't be read the file is treated as foreign,
        /// so backups belonging to other databases/projects are never imported.
        /// </summary>
        private async Task<bool> BelongsToThisDatabaseAsync(string filePath)
        {
            if (File.Exists(GetSidecarPath(filePath)))
                return true;

            try
            {
                // Must be a SQLite file that carries this app's EF migrations history table.
                var builder = new SqliteConnectionStringBuilder
                {
                    DataSource = filePath,
                    Mode = SqliteOpenMode.ReadOnly,
                    Pooling = false
                };

                using var conn = new SqliteConnection(builder.ConnectionString);
                await conn.OpenAsync();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '__EFMigrationsHistory'";
                return Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
            }
            catch
            {
                // Unreadable file (permissions, corrupt, not a SQLite database) -> treat as foreign.
            }

            return false;
        }

        private async Task WriteBackupSidecarAsync(BackupEntity backup)
        {
            try
            {
                if (string.IsNullOrEmpty(backup.FilePath))
                    return;

                var sidecar = new BackupSidecar
                {
                    Name = backup.Name,
                    Description = backup.Description,
                    BackupType = backup.BackupType.ToString(),
                    CreatedBy = backup.CreatedBy,
                    CreatedAt = backup.CreatedAt,
                    CompletedAt = backup.CompletedAt,
                    FileSizeBytes = backup.FileSizeBytes,
                    IsEncrypted = backup.IsEncrypted,
                    CompressionType = string.IsNullOrEmpty(backup.CompressionType) ? "None" : backup.CompressionType,
                    TablesIncluded = string.IsNullOrEmpty(backup.TablesIncluded) ? "[]" : backup.TablesIncluded,
                    TablesExcluded = string.IsNullOrEmpty(backup.TablesExcluded) ? "[]" : backup.TablesExcluded
                };

                var json = JsonSerializer.Serialize(sidecar, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(GetSidecarPath(backup.FilePath), json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WriteBackupSidecarAsync failed: {ex.Message}");
            }
        }

        private async Task<BackupSidecar?> ReadBackupSidecarAsync(string backupFilePath)
        {
            try
            {
                var path = GetSidecarPath(backupFilePath);
                if (!File.Exists(path))
                    return null;

                var json = await File.ReadAllTextAsync(path);
                return JsonSerializer.Deserialize<BackupSidecar>(json);
            }
            catch
            {
                return null;
            }
        }

        private void DeleteBackupSidecar(string backupFilePath)
        {
            try
            {
                var path = GetSidecarPath(backupFilePath);
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DeleteBackupSidecar failed: {ex.Message}");
            }
        }

        private async Task<List<string>> GetBackupSearchDirectoriesAsync()
        {
            var dirs = new List<string>();

            try { dirs.Add(await GetBackupDirectoryAsync()); } catch { /* ignore */ }

            // Directories of backups already known to the table
            try
            {
                var existing = await _backupRepository.GetAllAsync();
                if (existing != null)
                {
                    foreach (var b in existing)
                    {
                        if (string.IsNullOrEmpty(b.FilePath)) continue;
                        var d = Path.GetDirectoryName(b.FilePath);
                        if (!string.IsNullOrEmpty(d)) dirs.Add(d);
                    }
                }
            }
            catch { /* ignore */ }

            dirs.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backups"));

            return dirs
                .Where(d => !string.IsNullOrEmpty(d) && Directory.Exists(d))
                .Select(d => Path.GetFullPath(d))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task<BackupEntity?> BuildBackupFromDiskAsync(string filePath)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists || fileInfo.Length == 0)
                    return null;

                var sidecar = await ReadBackupSidecarAsync(filePath);

                var createdAt = sidecar?.CreatedAt ?? fileInfo.CreationTimeUtc;
                var completedAt = sidecar?.CompletedAt ?? fileInfo.LastWriteTimeUtc;

                return new BackupEntity
                {
                    Name = sidecar?.Name ?? Path.GetFileNameWithoutExtension(filePath),
                    Description = !string.IsNullOrEmpty(sidecar?.Description)
                        ? sidecar!.Description
                        : $"{AutoRecoveredMarker} rebuilt from backup file on disk",
                    FilePath = filePath,
                    FileName = Path.GetFileName(filePath),
                    FileSizeBytes = fileInfo.Length,
                    BackupType = ParseBackupType(sidecar?.BackupType ?? "Full"),
                    Status = BackupStatus.Completed,
                    CreatedAt = createdAt,
                    CompletedAt = completedAt,
                    Duration = completedAt > createdAt ? completedAt - createdAt : TimeSpan.Zero,
                    CreatedBy = string.IsNullOrEmpty(sidecar?.CreatedBy) ? "System" : sidecar!.CreatedBy,
                    IsEncrypted = sidecar?.IsEncrypted ?? false,
                    CompressionType = sidecar?.CompressionType ?? "None",
                    TablesIncluded = sidecar?.TablesIncluded ?? "[]",
                    TablesExcluded = sidecar?.TablesExcluded ?? "[]",
                    Metadata = string.Empty,
                    ErrorMessage = string.Empty,
                    Checksum = string.Empty,
                    DeletedBy = string.Empty,
                    IsDeleted = false
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Keeps the Backups table in sync with the .bak files on disk. Inserts rows for this
        /// database's backup files that have no record, and removes previously auto-recovered
        /// rows that turn out to belong to another database. Safe to call often; never throws.
        /// Only table rows are added or removed here - backup files are never touched.
        /// </summary>
        private async Task ReconcileBackupCatalogAsync()
        {
            try
            {
                var existing = (await _backupRepository.GetAllAsync()) ?? new List<BackupEntity>();

                // 1. Drop auto-recovered rows that no longer point to a backup of THIS database
                //    (e.g. another project's .bak imported by an earlier, over-broad scan).
                foreach (var b in existing.Where(IsAutoRecovered).ToList())
                {
                    var valid = !string.IsNullOrEmpty(b.FilePath)
                                && File.Exists(b.FilePath)
                                && await BelongsToThisDatabaseAsync(b.FilePath);

                    if (!valid)
                    {
                        try { await _backupRepository.DeleteAsync(b.Id); } catch { /* ignore */ }
                    }
                    else if (!File.Exists(GetSidecarPath(b.FilePath)))
                    {
                        // Confirmed ours - drop a sidecar so future reconciles skip the header read.
                        await WriteBackupSidecarAsync(b);
                    }
                }

                var refreshed = (await _backupRepository.GetAllAsync()) ?? new List<BackupEntity>();
                var knownFileNames = new HashSet<string>(
                    refreshed.Select(b => b.FileName).Where(n => !string.IsNullOrEmpty(n)),
                    StringComparer.OrdinalIgnoreCase);

                // 2. Add rows for this database's backup files that have no record.
                foreach (var dir in await GetBackupSearchDirectoriesAsync())
                {
                    string[] files;
                    try { files = Directory.GetFiles(dir, "*.bak"); }
                    catch { continue; }

                    foreach (var file in files)
                    {
                        var fileName = Path.GetFileName(file);
                        if (knownFileNames.Contains(fileName))
                            continue;

                        if (!await BelongsToThisDatabaseAsync(file))
                            continue;

                        var recovered = await BuildBackupFromDiskAsync(file);
                        if (recovered == null)
                            continue;

                        await _backupRepository.SaveAsync(recovered);
                        knownFileNames.Add(fileName);
                    }
                }

                await ResolveStuckBackupsAsync(refreshed);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ReconcileBackupCatalogAsync failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Backups left "InProgress" by an interrupted operation or an app restart never
        /// recover on their own (the progress tracker is in-memory only). Resolve them:
        /// mark Completed if a valid .bak exists, otherwise Failed.
        /// </summary>
        private async Task ResolveStuckBackupsAsync(IEnumerable<BackupEntity> existing)
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-30);
            var stuck = existing
                .Where(b => b.Status == BackupStatus.InProgress
                            && b.CreatedAt < cutoff
                            && !_activeBackups.ContainsKey(b.Id.ToString()))
                .ToList();

            foreach (var b in stuck)
            {
                try
                {
                    string? verified = string.IsNullOrEmpty(b.FilePath)
                        ? null
                        : await FindBackupFileAsync(b.FilePath);

                    if (!string.IsNullOrEmpty(verified))
                    {
                        var fi = new FileInfo(verified);
                        if (fi.Exists && fi.Length > 0)
                        {
                            b.FilePath = verified;
                            b.FileName = Path.GetFileName(verified);
                            b.FileSizeBytes = fi.Length;
                            b.Status = BackupStatus.Completed;
                            b.CompletedAt = DateTime.UtcNow;
                            b.Duration = b.CompletedAt.Value - b.CreatedAt;
                            await _backupRepository.SaveAsync(b);
                            await WriteBackupSidecarAsync(b);
                            continue;
                        }
                    }

                    b.Status = BackupStatus.Failed;
                    if (string.IsNullOrEmpty(b.ErrorMessage))
                    {
                        b.ErrorMessage = "Backup did not finish (operation interrupted or application restarted).";
                    }
                    await _backupRepository.SaveAsync(b);
                }
                catch
                {
                    // Ignore individual failures; other rows still get processed.
                }
            }
        }

        #endregion

        private async Task<byte[]> GenerateBackupDataAsync(BackupRequest request)
        {
            // Not used: backups are SQLite VACUUM INTO snapshots. Kept for compatibility.
            var data = $"Backup: {request.Name}\nCreated: {DateTime.UtcNow}\nType: {request.BackupType}";
            return Encoding.UTF8.GetBytes(data);
        }

        private async Task RestoreDatabaseFromFileAsync(string backupPath, RestoreRequest request)
        {
            if (!await BelongsToThisDatabaseAsync(backupPath))
            {
                throw new InvalidOperationException("The selected file is not a valid backup of this application's database.");
            }

            var liveConnectionString = _context.Database.GetDbConnection().ConnectionString;

            // Release EF's connection so the backup API can take the write lock.
            await _context.Database.CloseConnectionAsync();

            var sourceBuilder = new SqliteConnectionStringBuilder
            {
                DataSource = backupPath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            };

            // SQLite online backup API, source = backup file, destination = live database.
            // It rewrites the live database in place, so no file swapping or app restart is needed.
            using (var source = new SqliteConnection(sourceBuilder.ConnectionString))
            using (var destination = new SqliteConnection(liveConnectionString))
            {
                await source.OpenAsync();
                await destination.OpenAsync();
                source.BackupDatabase(destination);
            }

            // Drop pooled connections that may hold stale page caches of the pre-restore file.
            SqliteConnection.ClearAllPools();

            // A backup taken before a schema change is restored with the old schema; bring it up to date.
            // The migration's own statements are not events anyone investigates.
            using (_auditContext.SuppressDataChanges("restore schema upgrade"))
            {
                await _context.Database.MigrateAsync();
            }
        }

        private string GetDatabaseFilePath()
        {
            var dataSource = new SqliteConnectionStringBuilder(_context.Database.GetDbConnection().ConnectionString).DataSource;
            return Path.GetFullPath(dataSource);
        }

        /// <summary>
        /// Calculates the next run time. Users pick the schedule on their LOCAL clock ("02:00"), so the
        /// day/time rules run on local time; the result is returned as UTC, which is how NextRun is stored.
        /// </summary>
        private DateTime? CalculateNextRunTime(BackupSchedule schedule)
        {
            var now = DateTime.Now; // local wall clock
            var nextRun = now.Date.Add(schedule.ScheduleTime);

            DateTime? nextLocal = schedule.ScheduleFrequency.ToLower() switch
            {
                "daily" => nextRun > now ? nextRun : nextRun.AddDays(1),
                "weekly" => CalculateWeeklyNextRun(schedule, now),
                "monthly" => CalculateMonthlyNextRun(schedule, now),
                _ => null
            };
            return nextLocal.LocalToUtc();
        }

        private DateTime CalculateWeeklyNextRun(BackupSchedule schedule, DateTime now)
        {
            var nextRun = now.Date.Add(schedule.ScheduleTime);
            var daysUntilNext = ((int)schedule.ScheduleDayOfWeek - (int)now.DayOfWeek + 7) % 7;
            
            // If target is today but time has passed, go to next week
            if (daysUntilNext == 0 && nextRun <= now)
            {
                daysUntilNext = 7;
            }
            
            return nextRun.AddDays(daysUntilNext);
        }

        private DateTime CalculateMonthlyNextRun(BackupSchedule schedule, DateTime now)
        {
            var dayOfMonth = schedule.ScheduleDayOfMonth ?? 1;
            
            // Try this month first
            var daysInCurrentMonth = DateTime.DaysInMonth(now.Year, now.Month);
            var actualDay = Math.Min(dayOfMonth, daysInCurrentMonth);
            var nextRun = new DateTime(now.Year, now.Month, actualDay).Add(schedule.ScheduleTime);
            
            // If the date has already passed this month, go to next month
            if (nextRun <= now)
            {
                var nextMonth = now.AddMonths(1);
                var daysInNextMonth = DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month);
                actualDay = Math.Min(dayOfMonth, daysInNextMonth);
                nextRun = new DateTime(nextMonth.Year, nextMonth.Month, actualDay).Add(schedule.ScheduleTime);
            }
            
            return nextRun;
        }

        private BackupType ParseBackupType(string type)
        {
            return type.ToLower() switch
            {
                "full" => BackupType.Full,
                "incremental" => BackupType.Incremental,
                "differential" => BackupType.Differential,
                _ => BackupType.Full
            };
        }

        private BackupStatus ParseBackupStatus(string status)
        {
            return status.ToLower() switch
            {
                "inprogress" => BackupStatus.InProgress,
                "completed" => BackupStatus.Completed,
                "failed" => BackupStatus.Failed,
                "cancelled" => BackupStatus.Cancelled,
                _ => BackupStatus.InProgress
            };
        }

        private string FormatFileSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            return $"{number:n1} {suffixes[counter]}";
        }

        private BackupDTO MapToDTO(BackupEntity backup)
        {
            return new BackupDTO
            {
                Id = backup.Id,
                // Backups made before a blank name was defaulted are listed by their file name.
                Name = string.IsNullOrWhiteSpace(backup.Name) ? Path.GetFileNameWithoutExtension(backup.FileName ?? string.Empty) : backup.Name,
                Description = backup.Description,
                FilePath = backup.FilePath,
                FileName = backup.FileName,
                FileSizeBytes = backup.FileSizeBytes,
                FileSizeFormatted = FormatFileSize(backup.FileSizeBytes),
                BackupType = backup.BackupType.ToString(),
                Status = backup.Status.ToString(),
                CreatedAt = backup.CreatedAt,
                CompletedAt = backup.CompletedAt,
                Duration = backup.Duration,
                CreatedBy = backup.CreatedBy,
                IsEncrypted = backup.IsEncrypted,
                CompressionType = backup.CompressionType,
                TablesIncluded = DeserializeJsonList(backup.TablesIncluded),
                TablesExcluded = DeserializeJsonList(backup.TablesExcluded),
                ErrorMessage = backup.ErrorMessage
            };
        }

        private List<string> DeserializeJsonList(string jsonString)
        {
            if (string.IsNullOrEmpty(jsonString))
                return new List<string>();
            
            try
            {
                return JsonSerializer.Deserialize<List<string>>(jsonString) ?? new List<string>();
            }
            catch (JsonException)
            {
                return new List<string>();
            }
        }

        private BackupScheduleDTO MapScheduleToDTO(BackupSchedule schedule)
        {
            return new BackupScheduleDTO
            {
                Id = schedule.Id,
                Name = schedule.Name,
                Description = schedule.Description,
                BackupType = schedule.BackupType.ToString(),
                ScheduleFrequency = schedule.ScheduleFrequency,
                ScheduleTime = schedule.ScheduleTime,
                ScheduleDayOfWeek = schedule.ScheduleDayOfWeek,
                ScheduleDayOfMonth = schedule.ScheduleDayOfMonth,
                IsActive = schedule.IsActive,
                LastRun = schedule.LastRun,
                NextRun = schedule.NextRun,
                RetentionDays = schedule.RetentionDays,
                CreatedAt = schedule.CreatedAt,
                CreatedBy = schedule.CreatedBy,
                Settings = JsonSerializer.Deserialize<Dictionary<string, object>>(schedule.Settings) ?? new()
            };
        }

        #endregion
    }
}