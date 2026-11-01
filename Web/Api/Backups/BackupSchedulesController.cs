using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.Backups.Contracts;
using Web.Api.Infrastructure;
using Web.Attributes;
using Web.Helpers;

namespace Web.Api.Backups;

/// <summary>
/// Backup schedules: list, create, change, pause / resume, delete, run now. The scheduler itself is a hosted service
/// (<c>BackupSchedulingService</c>) that checks every minute; this only edits what it reads.
/// </summary>
[Route("api/v1/backup-schedules")]
[RequirePermission(PermissionHelper.Permissions.Backup_View)]
public sealed class BackupSchedulesController : ApiControllerBase
{
    private readonly IBackupScheduleApiService _schedules;

    public BackupSchedulesController(IBackupScheduleApiService schedules) => _schedules = schedules;

    /// <summary>All schedules (active or not), by name. There are a handful, so the list is not paged.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<BackupScheduleDto>>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "listBackupSchedules")]
    public async Task<IReadOnlyList<BackupScheduleDto>> List() => await _schedules.ListAsync();

    /// <summary>Creates a schedule. A weekly one needs <c>dayOfWeek</c>, a monthly one <c>dayOfMonth</c> (400 <c>validation_failed</c> with the field).</summary>
    [HttpPost]
    [RequirePermission(PermissionHelper.Permissions.Backup_Manage)]
    [ProducesResponseType<BackupScheduleDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "createBackupSchedule")]
    public async Task<ActionResult<BackupScheduleDto>> Create(BackupScheduleRequest request)
    {
        try
        {
            var created = await _schedules.CreateAsync(request);
            return CreatedAtAction(nameof(List), null, created);
        }
        catch (BackupScheduleRuleException ex)
        {
            return ValidationFailure(ex.Field, ex.Message);
        }
    }

    /// <summary>Replaces a schedule's settings and works out its next run again.</summary>
    [HttpPut("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Backup_Manage)]
    [ProducesResponseType<BackupScheduleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "updateBackupSchedule")]
    public async Task<ActionResult<BackupScheduleDto>> Update(int id, BackupScheduleRequest request)
    {
        try
        {
            return await _schedules.UpdateAsync(id, request) is { } updated ? updated : NotFound();
        }
        catch (BackupScheduleRuleException ex)
        {
            return ValidationFailure(ex.Field, ex.Message);
        }
    }

    /// <summary>Pauses or resumes a schedule (a paused one has no next run).</summary>
    [HttpPut("{id:int}/active")]
    [RequirePermission(PermissionHelper.Permissions.Backup_Manage)]
    [ProducesResponseType<BackupScheduleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "setBackupScheduleActive")]
    public async Task<ActionResult<BackupScheduleDto>> SetActive(int id, BackupScheduleActiveRequest request)
        => await _schedules.SetActiveAsync(id, request.IsActive) is { } updated ? updated : NotFound();

    /// <summary>Deletes a schedule. Backups it already made stay.</summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Backup_Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "deleteBackupSchedule")]
    public async Task<IActionResult> Delete(int id) => await _schedules.DeleteAsync(id) ? NoContent() : NotFound();

    /// <summary>Takes a backup now, as this schedule would, and records the run. Needs <c>Backup.Create</c>.</summary>
    [HttpPost("{id:int}/run")]
    [RequirePermission(PermissionHelper.Permissions.Backup_Create)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status500InternalServerError, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "runBackupSchedule")]
    public async Task<IActionResult> Run(int id)
    {
        try
        {
            return await _schedules.RunNowAsync(id) ? NoContent() : NotFound();
        }
        catch (BackupOperationException ex)
        {
            return Failure(StatusCodes.Status500InternalServerError, ex.Code, ex.Message);
        }
    }
}
