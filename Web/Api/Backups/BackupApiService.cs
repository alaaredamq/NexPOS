using Application.DTOs.Backup;
using Application.IServices.Backup;
using Core.ICache;
using Web.Api.Backups.Contracts;
using Web.Api.Infrastructure;
using Web.Services;

namespace Web.Api.Backups;

public interface IBackupApiService
{
    Task<BackupSummaryDto> SummaryAsync();

    /// <summary>One page of backups. <c>sort</c>: <c>createdAt</c> (default, newest first), <c>name</c>, <c>size</c>. <paramref name="status"/> is one of <see cref="BackupApiService.Statuses"/> or null.</summary>
    Task<PagedResult<BackupDto>> ListAsync(PagedQuery query, string? status);

    /// <summary>Null when the backup does not exist.</summary>
    Task<BackupDto?> GetAsync(int id);

    /// <summary>Takes the snapshot now and returns the finished record. Throws <see cref="BackupOperationException"/> when it did not complete.</summary>
    Task<BackupDto> CreateAsync(BackupCreateRequest request);

    /// <summary>Null when the backup does not exist.</summary>
    Task<BackupValidationDto?> ValidateAsync(int id);

    /// <summary>False when the backup does not exist. Throws <see cref="BackupOperationException"/> when the file or record could not be removed.</summary>
    Task<bool> DeleteAsync(int id);

    Task<BackupBulkDeleteResult> BulkDeleteAsync(IReadOnlyList<int> ids);

    Task<BackupBulkValidateResult> BulkValidateAsync(IReadOnlyList<int> ids);

    /// <summary>The file to send for a download, or null when the backup, or its file, is gone.</summary>
    Task<BackupFile?> FindFileAsync(int id);

    /// <summary>
    /// Replaces the live database with the backup. False when the backup does not exist. Throws <see cref="BackupOperationException"/>
    /// when the file is missing, is not a backup of this database, or the restore failed part-way.
    /// </summary>
    Task<bool> RestoreAsync(int id, BackupRestoreRequest request);

    Task CleanupAsync(int daysToKeep);

    Task<BackupConnectionDto> TestConnectionAsync();
}

/// <summary>A backup file on disk, ready to be streamed.</summary>
public sealed record BackupFile(string Path, string FileName);

/// <summary>A backup, restore or clean-up that was refused or failed; <see cref="Code"/> is the stable API code.</summary>
public sealed class BackupOperationException : Exception
{
    public string Code { get; }

    public BackupOperationException(string code, string message) : base(message) => Code = code;
}

/// <summary>
/// Maps <see cref="IBackupService"/> to the API's DTOs. The backup engine (snapshot, restore, catalog, retention) stays in that service,
/// shared with the Razor page and the scheduler; what is new is the shape of the answers and the paging.
/// </summary>
public sealed class BackupApiService : IBackupApiService
{
    public static readonly string[] Statuses = { "Completed", "Failed", "InProgress", "Cancelled" };
    private static readonly string[] SortColumns = { "createdAt", "name", "size" };

    private readonly IBackupService _backups;
    private readonly ICacheService _cache;
    private readonly IRoleAccessService _access;

    public BackupApiService(IBackupService backups, ICacheService cache, IRoleAccessService access)
    {
        _backups = backups;
        _cache = cache;
        _access = access;
    }

    public async Task<BackupSummaryDto> SummaryAsync()
    {
        var stats = await _backups.GetBackupStatsAsync();
        var newest = stats.RecentBackups.FirstOrDefault();

        return new BackupSummaryDto
        {
            TotalBackups = stats.TotalBackups,
            SuccessfulBackups = stats.SuccessfulBackups,
            FailedBackups = stats.FailedBackups,
            TotalSizeBytes = stats.TotalBackupSize,
            ScheduledCount = stats.ScheduledBackups,
            NextScheduledAt = stats.NextScheduledBackup,
            LastBackupAt = stats.LastBackupDate,
            LastBackupStatus = newest?.Status,
            BackupDirectory = await _backups.GetBackupDirectoryAsync(),
            AvailableDiskBytes = await SafeAsync(_backups.GetAvailableDiskSpaceAsync),
            DatabaseSizeBytes = await SafeAsync(_backups.GetTotalDatabaseSizeAsync),
        };
    }

    public async Task<PagedResult<BackupDto>> ListAsync(PagedQuery query, string? status)
    {
        var sort = SortColumns.FirstOrDefault(c => string.Equals(c, query.Sort, StringComparison.OrdinalIgnoreCase)) ?? "createdAt";
        // Newest first unless told otherwise; names read better A-Z.
        var descending = query.Dir is null ? sort != "name" : query.Descending;

        // The catalog is a few dozen rows: take them all (the service filters and sorts) and page here, so the answer carries a total.
        var all = await _backups.GetBackupsAsync(new BackupFilterRequest
        {
            Status = status,
            Page = 1,
            PageSize = int.MaxValue,
            SortBy = sort,
            SortDirection = descending ? "desc" : "asc",
        });

        IEnumerable<BackupDto> rows = all.Select(b => ToDto(b));
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            rows = rows.Where(b =>
                b.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.FileName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (b.Description?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        return PagedResult<BackupDto>.From(rows, query);
    }

    public async Task<BackupDto?> GetAsync(int id)
        => await FindAsync(id) is { } backup ? ToDto(backup) : null;

    public async Task<BackupDto> CreateAsync(BackupCreateRequest request)
    {
        BackupDTO created;
        try
        {
            // SQLite has no incremental or differential backups: every backup is a full snapshot, and nothing is compressed or encrypted.
            created = await _backups.CreateBackupAsync(new BackupRequest
            {
                Name = request.Name ?? string.Empty,
                Description = request.Description ?? string.Empty,
                BackupType = "Full",
                CompressionType = "None",
            });
        }
        catch (Exception ex)
        {
            throw new BackupOperationException("backup_failed", ex.Message);
        }

        // The service records a failed snapshot instead of throwing; the caller must still be told.
        if (created.Status == "Failed")
        {
            throw new BackupOperationException("backup_failed", string.IsNullOrWhiteSpace(created.ErrorMessage) ? "Backup failed" : created.ErrorMessage);
        }

        return ToDto(created);
    }

    public async Task<BackupValidationDto?> ValidateAsync(int id)
    {
        if (await FindAsync(id) is null) return null;

        var result = await _backups.ValidateBackupAsync(id);
        return new BackupValidationDto
        {
            IsValid = result.IsValid,
            Errors = result.Errors,
            Warnings = result.Warnings,
            Checksum = string.IsNullOrEmpty(result.BackupInfo.Checksum) ? null : result.BackupInfo.Checksum,
            FileSizeBytes = result.BackupInfo.FileSizeBytes,
        };
    }

    public async Task<bool> DeleteAsync(int id)
    {
        if (await FindAsync(id) is null) return false;

        if (!await _backups.DeleteBackupAsync(id))
        {
            throw new BackupOperationException("backup_delete_failed", "Failed to delete backup");
        }

        return true;
    }

    public async Task<BackupBulkDeleteResult> BulkDeleteAsync(IReadOnlyList<int> ids)
    {
        // The service throws on an id that is not there; the old page never sent one, but a second tab can delete it first.
        var known = new List<int>();
        foreach (var id in ids.Distinct())
        {
            if (await FindAsync(id) is not null) known.Add(id);
        }

        var deleted = await _backups.BulkDeleteBackupsAsync(known);
        return new BackupBulkDeleteResult { Deleted = deleted.Count };
    }

    public async Task<BackupBulkValidateResult> BulkValidateAsync(IReadOnlyList<int> ids)
    {
        var results = new List<BackupCheckDto>();
        foreach (var id in ids.Distinct())
        {
            results.Add(new BackupCheckDto { Id = id, IsValid = await _backups.VerifyBackupIntegrityAsync(id) });
        }

        return new BackupBulkValidateResult { AllValid = results.All(r => r.IsValid), Results = results };
    }

    public async Task<BackupFile?> FindFileAsync(int id)
    {
        var backup = await FindAsync(id);
        if (backup is null || string.IsNullOrEmpty(backup.FilePath) || !File.Exists(backup.FilePath)) return null;

        return new BackupFile(backup.FilePath, string.IsNullOrWhiteSpace(backup.FileName) ? $"backup_{id}.bak" : backup.FileName);
    }

    public async Task<bool> RestoreAsync(int id, BackupRestoreRequest request)
    {
        if (await FindAsync(id) is null) return false;

        try
        {
            await _backups.RestoreBackupAsync(new RestoreRequest
            {
                BackupId = id,
                RestoreData = true,
                RestoreSchema = true,
                CreateBackupBeforeRestore = request.CreateBackupBeforeRestore,
            });
        }
        catch (Exception ex)
        {
            // The service has already written BackupRestoreFailed to the audit trail.
            throw new BackupOperationException("restore_failed", ex.Message);
        }
        finally
        {
            // The database may have been rewritten under the app's feet (a restore can fail after the copy, in the schema upgrade): whatever
            // the app remembered about products, categories, dashboards ... and about who may do what (users and roles come back with a
            // restore) is from before. Forget it, so the next request reads the data that is there now and open sessions are judged
            // against the restored users.
            _cache.Clear();
            _access.InvalidateAccess();
        }

        return true;
    }

    public async Task CleanupAsync(int daysToKeep)
    {
        if (!await _backups.CleanupOldBackupsAsync(daysToKeep))
        {
            throw new BackupOperationException("cleanup_failed", "Cleanup failed");
        }
    }

    public async Task<BackupConnectionDto> TestConnectionAsync() => new()
    {
        BackupConnection = await _backups.TestBackupConnectionAsync(),
        RestoreConnection = await _backups.TestRestoreConnectionAsync(),
    };

    private async Task<Core.Entities.Backup.Backup?> FindAsync(int id)
    {
        try
        {
            return await _backups.GetByIdAsync(id);
        }
        catch (ArgumentNullException)
        {
            // The shared repository reports "no such row" as an exception.
            return null;
        }
    }

    private static async Task<long> SafeAsync(Func<Task<long>> read)
    {
        try
        {
            return await read();
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // An unreadable drive (a disconnected share) must not take the whole page down: show 0 and carry on.
            return 0;
        }
    }

    internal static BackupDto ToDto(BackupDTO backup) => new()
    {
        Id = backup.Id,
        Name = backup.Name,
        Description = string.IsNullOrWhiteSpace(backup.Description) ? null : backup.Description,
        FileName = backup.FileName,
        FileSizeBytes = backup.FileSizeBytes,
        Status = backup.Status,
        CreatedAt = backup.CreatedAt,
        CompletedAt = backup.CompletedAt,
        DurationSeconds = backup.Duration?.TotalSeconds,
        ErrorMessage = string.IsNullOrWhiteSpace(backup.ErrorMessage) ? null : backup.ErrorMessage,
    };

    private static BackupDto ToDto(Core.Entities.Backup.Backup backup) => new()
    {
        Id = backup.Id,
        Name = string.IsNullOrWhiteSpace(backup.Name) ? Path.GetFileNameWithoutExtension(backup.FileName ?? string.Empty) : backup.Name,
        Description = string.IsNullOrWhiteSpace(backup.Description) ? null : backup.Description,
        FileName = backup.FileName ?? string.Empty,
        FileSizeBytes = backup.FileSizeBytes,
        Status = backup.Status.ToString(),
        CreatedAt = backup.CreatedAt,
        CompletedAt = backup.CompletedAt,
        DurationSeconds = backup.Duration?.TotalSeconds,
        ErrorMessage = string.IsNullOrWhiteSpace(backup.ErrorMessage) ? null : backup.ErrorMessage,
    };
}
