using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.Backups.Contracts;
using Web.Api.Infrastructure;
using Web.Attributes;
using Web.Helpers;

namespace Web.Api.Backups;

/// <summary>
/// Backups: list, create, restore, download, check, delete. Mirrors the Razor <c>BackupController</c>, whose class-level
/// <c>Backup.View</c> applies to every action in addition to the one named on the action.
/// <para>
/// Not behind <c>[RequireSalesLicense]</c>: a shop whose licence has lapsed must still be able to take and download its backups.
/// </para>
/// </summary>
[Route("api/v1/backups")]
[RequirePermission(PermissionHelper.Permissions.Backup_View)]
public sealed class BackupsController : ApiControllerBase
{
    private readonly IBackupApiService _backups;

    public BackupsController(IBackupApiService backups) => _backups = backups;

    /// <summary>The tiles of the page, where the files live and how much room is left. Also re-registers any backup file found on disk but missing from the list.</summary>
    [HttpGet("summary")]
    [ProducesResponseType<BackupSummaryDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getBackupSummary")]
    public async Task<BackupSummaryDto> Summary() => await _backups.SummaryAsync();

    /// <summary>
    /// One page of backups, newest first. <c>sort</c>: <c>createdAt</c> (default), <c>name</c>, <c>size</c>; <c>q</c> matches the name,
    /// file name and description; <c>status</c>: <c>Completed</c>, <c>Failed</c>, <c>InProgress</c> or <c>Cancelled</c>.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<BackupDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "listBackups")]
    public async Task<ActionResult<PagedResult<BackupDto>>> List([FromQuery] PagedQuery query, [FromQuery] string? status = null)
    {
        if (!string.IsNullOrEmpty(status) && !BackupApiService.Statuses.Contains(status, StringComparer.OrdinalIgnoreCase))
        {
            return ValidationFailure("status", string.Join(", ", BackupApiService.Statuses));
        }

        return await _backups.ListAsync(query, string.IsNullOrEmpty(status) ? null : status);
    }

    /// <summary>One backup.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<BackupDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "getBackup")]
    public async Task<ActionResult<BackupDto>> Get(int id) => await _backups.GetAsync(id) is { } backup ? backup : NotFound();

    /// <summary>
    /// Takes a snapshot now. The request returns when the file is written (a few seconds for a shop-sized database), so the UI shows a
    /// busy state meanwhile. A snapshot that fails is still recorded in the list, and answers 500 <c>backup_failed</c> with the reason.
    /// </summary>
    [HttpPost]
    [RequirePermission(PermissionHelper.Permissions.Backup_Create)]
    [ProducesResponseType<BackupDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status500InternalServerError, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "createBackup")]
    public async Task<ActionResult<BackupDto>> Create(BackupCreateRequest request)
    {
        try
        {
            var created = await _backups.CreateAsync(request);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }
        catch (BackupOperationException ex)
        {
            return Failure(StatusCodes.Status500InternalServerError, ex.Code, ex.Message);
        }
    }

    /// <summary>
    /// Replaces ALL business data with the backup's, then reconciles the list. By default a backup of the current data is taken first.
    /// 409 <c>restore_failed</c> when the file is missing or is not a backup of this database. The caller's own session may not survive
    /// (the users table is replaced too), so the UI reloads the session afterwards.
    /// </summary>
    [HttpPost("{id:int}/restore")]
    [RequirePermission(PermissionHelper.Permissions.Backup_Restore)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "restoreBackup")]
    public async Task<IActionResult> Restore(int id, BackupRestoreRequest? request)
    {
        try
        {
            return await _backups.RestoreAsync(id, request ?? new BackupRestoreRequest()) ? NoContent() : NotFound();
        }
        catch (BackupOperationException ex)
        {
            return ConflictFailure(ex.Code, ex.Message);
        }
    }

    /// <summary>Checks that the file exists and reads its size and checksum.</summary>
    [HttpPost("{id:int}/validate")]
    [RequirePermission(PermissionHelper.Permissions.Backup_Manage)]
    [ProducesResponseType<BackupValidationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "validateBackup")]
    public async Task<ActionResult<BackupValidationDto>> Validate(int id)
        => await _backups.ValidateAsync(id) is { } result ? result : NotFound();

    /// <summary>
    /// The backup file itself (<c>application/octet-stream</c>, streamed, resumable). A plain GET so a link or the browser's download
    /// manager can use it. 404 <c>backup_file_missing</c> when the file was removed from the folder.
    /// </summary>
    [HttpGet("{id:int}/download")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FileResult))]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "downloadBackup")]
    public async Task<IActionResult> Download(int id)
    {
        if (await _backups.FindFileAsync(id) is not { } file)
        {
            return Failure(StatusCodes.Status404NotFound, "backup_file_missing");
        }

        return PhysicalFile(file.Path, "application/octet-stream", file.FileName, enableRangeProcessing: true);
    }

    /// <summary>Deletes the file and its record. 404 for an unknown id.</summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Backup_Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status500InternalServerError, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "deleteBackup")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            return await _backups.DeleteAsync(id) ? NoContent() : NotFound();
        }
        catch (BackupOperationException ex)
        {
            return Failure(StatusCodes.Status500InternalServerError, ex.Code, ex.Message);
        }
    }

    /// <summary>Deletes several backups; ids that no longer exist are skipped.</summary>
    [HttpPost("bulk-delete")]
    [RequirePermission(PermissionHelper.Permissions.Backup_Manage)]
    [ProducesResponseType<BackupBulkDeleteResult>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "bulkDeleteBackups")]
    public async Task<BackupBulkDeleteResult> BulkDelete(BackupIdsRequest request) => await _backups.BulkDeleteAsync(request.Ids);

    /// <summary>Checks that each file exists and is not empty.</summary>
    [HttpPost("bulk-validate")]
    [RequirePermission(PermissionHelper.Permissions.Backup_Manage)]
    [ProducesResponseType<BackupBulkValidateResult>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "bulkValidateBackups")]
    public async Task<BackupBulkValidateResult> BulkValidate(BackupIdsRequest request) => await _backups.BulkValidateAsync(request.Ids);

    /// <summary>Deletes every backup older than <c>daysToKeep</c> days (what a schedule's retention does after each run).</summary>
    [HttpPost("cleanup")]
    [RequirePermission(PermissionHelper.Permissions.Backup_Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status500InternalServerError, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "cleanupBackups")]
    public async Task<IActionResult> Cleanup(BackupCleanupRequest request)
    {
        try
        {
            await _backups.CleanupAsync(request.DaysToKeep);
            return NoContent();
        }
        catch (BackupOperationException ex)
        {
            return Failure(StatusCodes.Status500InternalServerError, ex.Code, ex.Message);
        }
    }

    /// <summary>Can the app open its database for a backup and for a restore ("Test connections").</summary>
    [HttpPost("test-connection")]
    [ProducesResponseType<BackupConnectionDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "testBackupConnection")]
    public async Task<BackupConnectionDto> TestConnection() => await _backups.TestConnectionAsync();
}
