using Application.DTOs.Backup;
using Application.IServices.Backup;
using Microsoft.Extensions.Localization;
using Web.Api.Backups.Contracts;

namespace Web.Api.Backups;

public interface IBackupScheduleApiService
{
    Task<IReadOnlyList<BackupScheduleDto>> ListAsync();

    /// <summary>Throws <see cref="BackupScheduleRuleException"/> for a weekly schedule without a day of the week or a monthly one without a day of the month.</summary>
    Task<BackupScheduleDto> CreateAsync(BackupScheduleRequest request);

    /// <summary>Null when the schedule does not exist. Same rules as <see cref="CreateAsync"/>.</summary>
    Task<BackupScheduleDto?> UpdateAsync(int id, BackupScheduleRequest request);

    /// <summary>False when the schedule does not exist.</summary>
    Task<bool> DeleteAsync(int id);

    /// <summary>Null when the schedule does not exist.</summary>
    Task<BackupScheduleDto?> SetActiveAsync(int id, bool isActive);

    /// <summary>Takes this schedule's backup now. False when the schedule does not exist; throws <see cref="BackupOperationException"/> when it failed.</summary>
    Task<bool> RunNowAsync(int id);
}

/// <summary>A field of the request breaks a rule the DTO attributes cannot judge; <see cref="Field"/> is the request property.</summary>
public sealed class BackupScheduleRuleException : Exception
{
    public string Field { get; }

    public BackupScheduleRuleException(string field, string message) : base(message) => Field = field;
}

/// <summary>Maps the shared backup service's schedules to the API's DTOs. When a run is due, and how it is cleaned up, stays in that service and the scheduler.</summary>
public sealed class BackupScheduleApiService : IBackupScheduleApiService
{
    private readonly IBackupService _backups;
    private readonly IStringLocalizer<SharedResource> _text;

    public BackupScheduleApiService(IBackupService backups, IStringLocalizer<SharedResource> text)
    {
        _backups = backups;
        _text = text;
    }

    public async Task<IReadOnlyList<BackupScheduleDto>> ListAsync()
        => (await _backups.GetSchedulesAsync()).OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).Select(ToDto).ToList();

    public async Task<BackupScheduleDto> CreateAsync(BackupScheduleRequest request)
        => ToDto(await _backups.CreateScheduleAsync(ToService(0, request)));

    public async Task<BackupScheduleDto?> UpdateAsync(int id, BackupScheduleRequest request)
    {
        if (await FindAsync(id) is null) return null;

        return ToDto(await _backups.UpdateScheduleAsync(ToService(id, request)));
    }

    public async Task<bool> DeleteAsync(int id)
        => await FindAsync(id) is not null && await _backups.DeleteScheduleAsync(id);

    public async Task<BackupScheduleDto?> SetActiveAsync(int id, bool isActive)
    {
        if (await FindAsync(id) is null) return null;

        // Enabling works out the next run again; disabling clears it.
        if (isActive) await _backups.EnableScheduleAsync(id);
        else await _backups.DisableScheduleAsync(id);

        return await FindAsync(id);
    }

    public async Task<bool> RunNowAsync(int id)
    {
        if (await FindAsync(id) is null) return false;

        if (!await _backups.RunScheduleNowAsync(id))
        {
            throw new BackupOperationException("backup_failed", "Failed to start backup");
        }

        return true;
    }

    private async Task<BackupScheduleDto?> FindAsync(int id)
        => (await _backups.GetSchedulesAsync()).Where(s => s.Id == id).Select(ToDto).FirstOrDefault();

    private BackupScheduleDTO ToService(int id, BackupScheduleRequest request)
    {
        var frequency = char.ToUpperInvariant(request.Frequency[0]) + request.Frequency[1..].ToLowerInvariant();
        if (frequency == "Weekly" && request.DayOfWeek is null) throw new BackupScheduleRuleException("dayOfWeek", _text["Backup_Err_DayOfWeekRequired"]);
        if (frequency == "Monthly" && request.DayOfMonth is null) throw new BackupScheduleRuleException("dayOfMonth", _text["Backup_Err_DayOfMonthRequired"]);

        return new BackupScheduleDTO
        {
            Id = id,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim() ?? string.Empty,
            // Every backup is a full snapshot; the type column only survives in the table.
            BackupType = "Full",
            ScheduleFrequency = frequency,
            ScheduleTime = TimeSpan.ParseExact(request.Time, @"hh\:mm", null),
            // A day that does not belong to the frequency is dropped, not stored.
            ScheduleDayOfWeek = frequency == "Weekly" ? (DayOfWeek)request.DayOfWeek!.Value : null,
            ScheduleDayOfMonth = frequency == "Monthly" ? request.DayOfMonth : null,
            IsActive = request.IsActive,
            RetentionDays = request.RetentionDays,
        };
    }

    private static BackupScheduleDto ToDto(BackupScheduleDTO schedule) => new()
    {
        Id = schedule.Id,
        Name = schedule.Name,
        Description = string.IsNullOrWhiteSpace(schedule.Description) ? null : schedule.Description,
        Frequency = schedule.ScheduleFrequency,
        Time = schedule.ScheduleTime.ToString(@"hh\:mm"),
        DayOfWeek = schedule.ScheduleDayOfWeek is { } day ? (int)day : null,
        DayOfMonth = schedule.ScheduleDayOfMonth,
        IsActive = schedule.IsActive,
        RetentionDays = schedule.RetentionDays is > 0 ? schedule.RetentionDays : null,
        LastRunAt = schedule.LastRun,
        NextRunAt = schedule.NextRun,
    };
}
