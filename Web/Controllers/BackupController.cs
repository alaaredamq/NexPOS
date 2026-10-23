using Application.DTOs.Backup;
using Application.IServices.Backup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.Attributes;
using Web.Helpers;

namespace Web.Controllers
{
    [Authorize]
    [RequirePermission(PermissionHelper.Permissions.Backup_View)]
    public class BackupController : BaseController
    {
        private readonly IBackupService _backupService;

        public BackupController(IBackupService backupService)
        {
            _backupService = backupService;
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                var stats = await _backupService.GetBackupStatsAsync();
                return View(stats);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error loading backup dashboard: {ex.Message}";
                return View(new BackupStatsDTO());
            }
        }

        [HttpGet("test-db")]
        public async Task<IActionResult> TestDatabase()
        {
            try
            {
                // Test 1: Can we access the service?
                var serviceType = _backupService.GetType().Name;
                
                // Test 2: Can we query all backups directly?
                var allBackups = await _backupService.GetAllAsync();
                var backupCount = allBackups?.Count() ?? 0;
                
                // Test 3: Get first few backups to inspect
                var sampleBackups = allBackups?.Take(3).Select(b => new {
                    b.Id,
                    b.Name,
                    b.Status,
                    b.IsDeleted,
                    b.CreatedAt
                }).ToList();
                
                // Test 4: Can we get stats?
                var stats = await _backupService.GetBackupStatsAsync();
                
                return Json(new { 
                    success = true, 
                    serviceType,
                    backupCount,
                    sampleBackups,
                    statsTotal = stats.TotalBackups,
                    message = $"Service OK. Found {backupCount} backups in database."
                });
            }
            catch (Exception ex)
            {
                return Json(new { 
                    success = false, 
                    message = ex.Message,
                    stackTrace = ex.StackTrace
                });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetBackups([FromQuery] BackupFilterRequest request)
        {
            try
            {
                // Ensure request is not null
                request ??= new BackupFilterRequest();
                
                var backups = await _backupService.GetBackupsAsync(request);
                
                // Ensure backups is never null
                var safeBackups = backups ?? new List<Application.DTOs.Backup.BackupDTO>();
                
                return Json(new { success = true, data = safeBackups });
            }
            catch (Exception ex)
            {
                // Log the error
                Console.WriteLine($"GetBackups error: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                
                return Json(new { success = false, message = ex.Message, data = new List<Application.DTOs.Backup.BackupDTO>() });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetBackupStats()
        {
            try
            {
                var stats = await _backupService.GetBackupStatsAsync();
                return Json(new { success = true, data = stats });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [RequirePermission(PermissionHelper.Permissions.Backup_Create)]
        public async Task<IActionResult> CreateBackup([FromBody] BackupRequest request)
        {
            try
            {
                var backup = await _backupService.CreateBackupAsync(request);
                if (backup.Status == "Failed")
                {
                    return Json(new { success = false, data = backup, message = string.IsNullOrEmpty(backup.ErrorMessage) ? "Backup failed" : backup.ErrorMessage });
                }
                return Json(new { success = true, data = backup, message = "Backup completed successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetBackupProgress(string backupId)
        {
            try
            {
                var progress = await _backupService.GetBackupProgressAsync(backupId);
                return Json(new { success = true, data = progress });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CancelBackup([FromBody] string backupId)
        {
            try
            {
                var result = await _backupService.CancelBackupAsync(backupId);
                return Json(new { success = result, message = result ? "Backup cancelled" : "Failed to cancel backup" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [RequirePermission(PermissionHelper.Permissions.Backup_Restore)]
        public async Task<IActionResult> RestoreBackup([FromBody] RestoreRequest request)
        {
            try
            {
                var result = await _backupService.RestoreBackupAsync(request);
                return Json(new { success = result, message = result ? "Restore started successfully" : "Failed to start restore" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetRestoreProgress(string restoreId)
        {
            try
            {
                var progress = await _backupService.GetRestoreProgressAsync(restoreId);
                return Json(new { success = true, data = progress });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CancelRestore([FromBody] string restoreId)
        {
            try
            {
                var result = await _backupService.CancelRestoreAsync(restoreId);
                return Json(new { success = result, message = result ? "Restore cancelled" : "Failed to cancel restore" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [RequirePermission(PermissionHelper.Permissions.Backup_Manage)]
        public async Task<IActionResult> ValidateBackup([FromBody] int backupId)
        {
            try
            {
                var validation = await _backupService.ValidateBackupAsync(backupId);
                return Json(new { success = true, data = validation });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [RequirePermission(PermissionHelper.Permissions.Backup_Manage)]
        public async Task<IActionResult> DeleteBackup([FromBody] int backupId)
        {
            try
            {
                var result = await _backupService.DeleteBackupAsync(backupId);
                return Json(new { success = result, message = result ? "Backup deleted successfully" : "Failed to delete backup" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        [RequirePermission(PermissionHelper.Permissions.Backup_View)]
        public async Task<IActionResult> DownloadBackup(int id)
        {
            try
            {
                var backup = await _backupService.GetByIdAsync(id);
                if (backup == null)
                {
                    return NotFound(new { success = false, message = "Backup not found" });
                }

                // Check if file exists
                if (!string.IsNullOrEmpty(backup.FilePath) && System.IO.File.Exists(backup.FilePath))
                {
                    var fileName = backup.FileName ?? $"backup_{id}.bak";
                    var fileBytes = await System.IO.File.ReadAllBytesAsync(backup.FilePath);
                    return File(fileBytes, "application/octet-stream", fileName);
                }

                // File doesn't exist, try to generate it
                var tempPath = Path.Combine(Path.GetTempPath(), $"backup_{id}_{DateTime.Now:yyyyMMddHHmmss}.bak");
                var result = await _backupService.DownloadBackupAsync(id, tempPath);
                
                if (result && System.IO.File.Exists(tempPath))
                {
                    var fileName = backup.FileName ?? $"backup_{id}.bak";
                    var fileBytes = await System.IO.File.ReadAllBytesAsync(tempPath);
                    
                    // Clean up temp file
                    System.IO.File.Delete(tempPath);
                    
                    return File(fileBytes, "application/octet-stream", fileName);
                }
                
                return Json(new { success = false, message = "Backup file not found" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [RequirePermission(PermissionHelper.Permissions.Backup_View)]
        public async Task<IActionResult> DownloadBackupPost([FromBody] DownloadBackupRequest request)
        {
            return await DownloadBackup(request.BackupId);
        }

        [HttpGet]
        public async Task<IActionResult> GetDatabaseTables()
        {
            try
            {
                var tables = await _backupService.GetDatabaseTablesAsync();
                return Json(new { success = true, data = tables });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetSystemInfo()
        {
            try
            {
                var info = await _backupService.GetSystemInfoAsync();
                return Json(new { success = true, data = info });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> TestConnections()
        {
            try
            {
                var backupTest = await _backupService.TestBackupConnectionAsync();
                var restoreTest = await _backupService.TestRestoreConnectionAsync();
                
                return Json(new { 
                    success = backupTest && restoreTest, 
                    backupConnection = backupTest,
                    restoreConnection = restoreTest,
                    message = backupTest && restoreTest ? "All connections OK" : "Some connections failed"
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetOperationStatus(string operationId)
        {
            try
            {
                // Try backup progress first
                var backupProgress = await _backupService.GetBackupProgressAsync(operationId);
                if (backupProgress != null && backupProgress.Status != "NotFound")
                {
                    return Json(new { 
                        success = true, 
                        type = "backup",
                        status = backupProgress.Status,
                        progress = backupProgress.ProgressPercentage,
                        message = string.IsNullOrEmpty(backupProgress.ErrorMessage) ? backupProgress.CurrentOperation : backupProgress.ErrorMessage,
                        isCompleted = backupProgress.Status == "Completed" || backupProgress.Status == "Failed" || backupProgress.Status == "Cancelled",
                        isSuccess = backupProgress.Status == "Completed"
                    });
                }

                // Try restore progress
                var restoreProgress = await _backupService.GetRestoreProgressAsync(operationId);
                if (restoreProgress != null && restoreProgress.Status != "NotFound")
                {
                    return Json(new { 
                        success = true, 
                        type = "restore",
                        status = restoreProgress.Status,
                        progress = restoreProgress.ProgressPercentage,
                        message = string.IsNullOrEmpty(restoreProgress.ErrorMessage) ? restoreProgress.CurrentOperation : restoreProgress.ErrorMessage,
                        isCompleted = restoreProgress.Status == "Completed" || restoreProgress.Status == "Failed" || restoreProgress.Status == "Cancelled",
                        isSuccess = restoreProgress.Status == "Completed"
                    });
                }

                return Json(new { success = false, message = "Operation not found" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CancelOperation([FromBody] string operationId)
        {
            try
            {
                // Try to cancel backup first
                var backupCancelled = await _backupService.CancelBackupAsync(operationId);
                if (backupCancelled)
                {
                    return Json(new { success = true, message = "Backup operation cancelled" });
                }

                // Try to cancel restore
                var restoreCancelled = await _backupService.CancelRestoreAsync(operationId);
                if (restoreCancelled)
                {
                    return Json(new { success = true, message = "Restore operation cancelled" });
                }

                return Json(new { success = false, message = "Operation not found or already completed" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> TestConnection()
        {
            try
            {
                var backupTest = await _backupService.TestBackupConnectionAsync();
                var restoreTest = await _backupService.TestRestoreConnectionAsync();
                
                return Json(new { 
                    success = backupTest && restoreTest, 
                    data = new { 
                        BackupConnection = backupTest, 
                        RestoreConnection = restoreTest 
                    },
                    message = backupTest && restoreTest ? "All connections successful" : "Connection test failed"
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [RequirePermission(PermissionHelper.Permissions.Backup_Manage)]
        public async Task<IActionResult> CleanupOldBackups([FromBody] int daysToKeep = 30)
        {
            try
            {
                var result = await _backupService.CleanupOldBackupsAsync(daysToKeep);
                return Json(new { success = result, message = result ? "Cleanup completed successfully" : "Cleanup failed" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [RequirePermission(PermissionHelper.Permissions.Backup_Create)]
        public async Task<IActionResult> CreateEmergencyBackup()
        {
            try
            {
                var result = await _backupService.CreateEmergencyBackupAsync();
                return Json(new { success = result, message = result ? "Emergency backup created successfully" : "Failed to create emergency backup" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetEmergencyRestoreOptions()
        {
            try
            {
                var options = await _backupService.GetEmergencyRestoreOptionsAsync();
                return Json(new { success = true, data = options });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [RequirePermission(PermissionHelper.Permissions.Backup_Restore)]
        public async Task<IActionResult> EmergencyRestore([FromBody] string backupFilePath)
        {
            try
            {
                var result = await _backupService.EmergencyRestoreAsync(backupFilePath);
                return Json(new { success = result, message = result ? "Emergency restore completed" : "Emergency restore failed" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetBackupHistory([FromQuery] int days = 30)
        {
            try
            {
                var history = await _backupService.GetBackupHistoryAsync(days);
                return Json(new { success = true, data = history });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        [RequirePermission(PermissionHelper.Permissions.Reports_View)]
        public async Task<IActionResult> GenerateReport([FromQuery] DateTime fromDate, [FromQuery] DateTime toDate)
        {
            try
            {
                var report = await _backupService.GenerateBackupReportAsync(fromDate, toDate);
                return Json(new { success = true, data = report });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        [RequirePermission(PermissionHelper.Permissions.Backup_View)]
        public async Task<IActionResult> ExportMetadata([FromQuery] int backupId)
        {
            try
            {
                var metadata = await _backupService.ExportBackupMetadataAsync(backupId);
                return Json(new { success = true, data = metadata });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [RequirePermission(PermissionHelper.Permissions.Backup_Manage)]
        public async Task<IActionResult> BulkDeleteBackups([FromBody] List<int> backupIds)
        {
            try
            {
                var result = await _backupService.BulkDeleteBackupsAsync(backupIds);
                return Json(new { success = true, data = result, message = $"{result.Count} backups deleted successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [RequirePermission(PermissionHelper.Permissions.Backup_Manage)]
        public async Task<IActionResult> BulkValidateBackups([FromBody] List<int> backupIds)
        {
            try
            {
                var result = await _backupService.BulkValidateBackupsAsync(backupIds);
                return Json(new { success = result, message = result ? "All backups validated successfully" : "Some backups failed validation" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        [RequirePermission(PermissionHelper.Permissions.Backup_View)]
        public async Task<IActionResult> DebugDirectories()
        {
            try
            {
                var backupDirectory = await _backupService.GetBackupDirectoryAsync();
                var exists = await _backupService.EnsureBackupDirectoryExistsAsync();
                var diskSpace = await _backupService.GetAvailableDiskSpaceAsync();
                var backups = await _backupService.GetBackupsAsync(new BackupFilterRequest { PageSize = 100 });
                
                return Json(new
                {
                    success = true,
                    backupDirectory = backupDirectory,
                    directoryExists = exists,
                    availableDiskSpaceBytes = diskSpace,
                    availableDiskSpaceGB = Math.Round(diskSpace / 1073741824.0, 2),
                    backupCount = backups?.Count ?? 0,
                    backups = backups?.Select(b => new { b.Id, b.Name, b.FileName, b.FilePath, b.Status, b.FileSizeBytes }).ToList()
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message, stackTrace = ex.StackTrace });
            }
        }

        // Schedule Management Methods
        [HttpGet]
        [RequirePermission(PermissionHelper.Permissions.Backup_View)]
        public async Task<IActionResult> GetSchedules()
        {
            try
            {
                var schedules = await _backupService.GetSchedulesAsync();
                return Json(new { success = true, data = schedules });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        [RequirePermission(PermissionHelper.Permissions.Backup_View)]
        public async Task<IActionResult> GetActiveSchedules()
        {
            try
            {
                var schedules = await _backupService.GetActiveSchedulesAsync();
                return Json(new { success = true, data = schedules });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [RequirePermission(PermissionHelper.Permissions.Backup_Manage)]
        public async Task<IActionResult> CreateSchedule([FromBody] BackupScheduleDTO schedule)
        {
            try
            {
                var result = await _backupService.CreateScheduleAsync(schedule);
                return Json(new { success = true, data = result, message = "Schedule created successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPut]
        [RequirePermission(PermissionHelper.Permissions.Backup_Manage)]
        public async Task<IActionResult> UpdateSchedule([FromBody] BackupScheduleDTO schedule)
        {
            try
            {
                var result = await _backupService.UpdateScheduleAsync(schedule);
                return Json(new { success = true, data = result, message = "Schedule updated successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpDelete]
        [RequirePermission(PermissionHelper.Permissions.Backup_Manage)]
        public async Task<IActionResult> DeleteSchedule([FromBody] int scheduleId)
        {
            try
            {
                var result = await _backupService.DeleteScheduleAsync(scheduleId);
                return Json(new { success = result, message = result ? "Schedule deleted successfully" : "Failed to delete schedule" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [RequirePermission(PermissionHelper.Permissions.Backup_Manage)]
        public async Task<IActionResult> EnableSchedule([FromBody] int scheduleId)
        {
            try
            {
                var result = await _backupService.EnableScheduleAsync(scheduleId);
                return Json(new { success = result, message = result ? "Schedule enabled successfully" : "Failed to enable schedule" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [RequirePermission(PermissionHelper.Permissions.Backup_Manage)]
        public async Task<IActionResult> DisableSchedule([FromBody] int scheduleId)
        {
            try
            {
                var result = await _backupService.DisableScheduleAsync(scheduleId);
                return Json(new { success = result, message = result ? "Schedule disabled successfully" : "Failed to disable schedule" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [RequirePermission(PermissionHelper.Permissions.Backup_Create)]
        public async Task<IActionResult> RunScheduleNow([FromBody] int scheduleId)
        {
            try
            {
                var result = await _backupService.RunScheduleNowAsync(scheduleId);
                return Json(new { success = result, message = result ? "Backup started successfully" : "Failed to start backup" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }

    public class DownloadBackupRequest
    {
        public int BackupId { get; set; }
    }
}
