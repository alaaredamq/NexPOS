using Application.IServices.Backup;
using Application.DTOs.Backup;
using Core.Entities.Backup;
using Core.IRepositories.Backup;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Services.Backup;

public class BackupSchedulingService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BackupSchedulingService> _logger;
    private Timer? _timer;
    private static readonly SemaphoreSlim _executionLock = new(1, 1);
    private static bool _isExecuting = false;

    public BackupSchedulingService(
        IServiceProvider serviceProvider,
        ILogger<BackupSchedulingService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Backup Scheduling Service started at {Time} (Local)", DateTime.Now);

        // Delay startup significantly to avoid DbContext concurrency issues during app initialization
        // This gives time for users to login and initial cache operations to complete
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        
        if (stoppingToken.IsCancellationRequested)
            return;

        _logger.LogInformation("Backup Scheduling Service starting timer at {Time} (Local)", DateTime.Now);

        // Check every minute for scheduled backups (more responsive)
        _timer = new Timer(
            CheckAndExecuteScheduledBackups,
            null,
            TimeSpan.FromSeconds(5), // Start after 5 seconds (after initial delay)
            TimeSpan.FromMinutes(1)); // Check every minute
    }

    private async void CheckAndExecuteScheduledBackups(object? state)
    {
        // Prevent concurrent execution
        if (_isExecuting)
        {
            _logger.LogDebug("Skipping backup check - previous execution still running");
            return;
        }

        bool lockAcquired;
        try
        {
            // Try to acquire lock without blocking
            lockAcquired = await _executionLock.WaitAsync(0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error acquiring backup scheduling lock");
            return;
        }

        if (!lockAcquired)
        {
            _logger.LogDebug("Could not acquire execution lock - skipping this cycle");
            return;
        }

        try
        {
            _isExecuting = true;

            using var scope = _serviceProvider.CreateScope();

            // No user is behind this work: the audit trail files everything below (backups, schedule updates, any
            // error it logs) under the "BackupScheduler" system actor instead of "nobody".
            using var systemActor = scope.ServiceProvider.GetRequiredService<Core.Auditing.IAuditContext>()
                .BeginSystemScope("BackupScheduler");

            var backupScheduleRepo = scope.ServiceProvider.GetRequiredService<IBackupScheduleRepository>();
            var backupService = scope.ServiceProvider.GetRequiredService<IBackupService>();

            // Use local time for comparison since schedules are set in local time
            var now = DateTime.Now;
            _logger.LogDebug("Checking for scheduled backups at {Time} (Local)", now);

            var activeSchedules = await backupScheduleRepo.GetActiveSchedulesAsync();

            foreach (var schedule in activeSchedules)
            {
                _logger.LogDebug("Schedule '{Name}': NextRun={NextRun}, Now={Now}, IsDue={IsDue}", 
                    schedule.Name, schedule.NextRun, now, schedule.NextRun.HasValue && schedule.NextRun <= now);
                
                // Check if backup is due
                if (schedule.NextRun.HasValue && schedule.NextRun <= now)
                {
                    _logger.LogInformation("Executing scheduled backup: {ScheduleName} (was due at {DueTime})", 
                        schedule.Name, schedule.NextRun);

                    try
                    {
                        // Execute backup with the schedule's backup type
                        var backupRequest = new BackupRequest
                        {
                            Name = $"Scheduled_{schedule.Name}_{DateTime.Now:yyyyMMdd_HHmmss}",
                            Description = $"Automatic backup from schedule: {schedule.Name}",
                            BackupType = schedule.BackupType.ToString(),
                            IncludeData = true,
                            IncludeSchema = true,
                            IncludeUsers = false,
                            IsEncrypted = false,
                            CompressionType = "GZip"
                        };
                        
                        var backup = await backupService.CreateBackupAsync(backupRequest);

                        // Update schedule with new next run time (use local time)
                        schedule.LastRun = now;
                        schedule.NextRun = CalculateNextRun(schedule);
                        await backupScheduleRepo.SaveAsync(schedule);

                        _logger.LogInformation("Scheduled backup completed: {ScheduleName}. Next run: {NextRun}",
                            schedule.Name, schedule.NextRun);

                        // Run cleanup if retention days is set
                        if (schedule.RetentionDays.HasValue && schedule.RetentionDays.Value > 0)
                        {
                            _logger.LogInformation("Running backup cleanup for schedule '{ScheduleName}': deleting backups older than {RetentionDays} days",
                                schedule.Name, schedule.RetentionDays.Value);
                            
                            try
                            {
                                var cleanupResult = await backupService.CleanupOldBackupsAsync(schedule.RetentionDays.Value);
                                if (cleanupResult)
                                {
                                    _logger.LogInformation("Backup cleanup completed successfully for schedule '{ScheduleName}'", schedule.Name);
                                }
                                else
                                {
                                    _logger.LogWarning("Backup cleanup returned false for schedule '{ScheduleName}'", schedule.Name);
                                }
                            }
                            catch (Exception cleanupEx)
                            {
                                _logger.LogError(cleanupEx, "Error during backup cleanup for schedule '{ScheduleName}'", schedule.Name);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error executing scheduled backup: {ScheduleName}", schedule.Name);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in backup scheduling service");
        }
        finally
        {
            _isExecuting = false;
            _executionLock.Release();
        }
    }

    /// <summary>
    /// Calculates the next run time based on the schedule's frequency, time, and day settings
    /// Uses local time since schedules are set in local time by users
    /// </summary>
    private DateTime? CalculateNextRun(BackupSchedule schedule)
    {
        var now = DateTime.Now; // Use local time
        var time = schedule.ScheduleTime;

        return schedule.ScheduleFrequency?.ToLower() switch
        {
            "daily" => CalculateDailyNextRun(now, time),
            "weekly" => CalculateWeeklyNextRun(now, time, schedule.ScheduleDayOfWeek),
            "monthly" => CalculateMonthlyNextRun(now, time, schedule.ScheduleDayOfMonth),
            _ => CalculateDailyNextRun(now, time)
        };
    }

    /// <summary>
    /// Calculates the next daily run time
    /// </summary>
    private DateTime CalculateDailyNextRun(DateTime now, TimeSpan time)
    {
        var nextRun = now.Date.Add(time);
        
        // If the time has already passed today, schedule for tomorrow
        if (nextRun <= now)
        {
            nextRun = nextRun.AddDays(1);
        }
        
        return nextRun;
    }

    /// <summary>
    /// Calculates the next weekly run time based on the specified day of week
    /// </summary>
    private DateTime CalculateWeeklyNextRun(DateTime now, TimeSpan time, DayOfWeek? dayOfWeek)
    {
        var targetDay = dayOfWeek ?? DayOfWeek.Sunday;
        var nextRun = now.Date.Add(time);
        
        // Calculate days until target day of week
        int daysUntilTarget = ((int)targetDay - (int)now.DayOfWeek + 7) % 7;
        
        // If target is today but time has passed, go to next week
        if (daysUntilTarget == 0 && nextRun <= now)
        {
            daysUntilTarget = 7;
        }
        
        return nextRun.AddDays(daysUntilTarget);
    }

    /// <summary>
    /// Calculates the next monthly run time based on the specified day of month
    /// </summary>
    private DateTime CalculateMonthlyNextRun(DateTime now, TimeSpan time, int? dayOfMonth)
    {
        var targetDay = dayOfMonth ?? 1;
        
        // Ensure target day is valid (1-28 to be safe across all months)
        targetDay = Math.Max(1, Math.Min(28, targetDay));
        
        // Try this month first
        var daysInCurrentMonth = DateTime.DaysInMonth(now.Year, now.Month);
        var actualDay = Math.Min(targetDay, daysInCurrentMonth);
        var nextRun = new DateTime(now.Year, now.Month, actualDay).Add(time);
        
        // If the date has already passed this month, go to next month
        if (nextRun <= now)
        {
            var nextMonth = now.AddMonths(1);
            var daysInNextMonth = DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month);
            actualDay = Math.Min(targetDay, daysInNextMonth);
            nextRun = new DateTime(nextMonth.Year, nextMonth.Month, actualDay).Add(time);
        }
        
        return nextRun;
    }

    public override async Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Backup Scheduling Service stopping");
        _timer?.Change(Timeout.Infinite, 0);
        await base.StopAsync(stoppingToken);
    }

    public override void Dispose()
    {
        _timer?.Dispose();
        base.Dispose();
    }
}

