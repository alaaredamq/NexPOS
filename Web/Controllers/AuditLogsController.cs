using Application.IServices.Logging;
using Core.Entities.Logging;
using Microsoft.AspNetCore.Mvc;
using Web.Attributes;
using Web.Helpers;

namespace Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequirePermission(PermissionHelper.Permissions.AuditLogs_View)]
public class AuditLogsController : ControllerBase
{
    private readonly IAuditLogService _service;

    public AuditLogsController(IAuditLogService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var logs = await _service.GetAllAsync();
        return Ok(logs);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var log = await _service.GetByIdAsync(id);
        if (log == null) return NotFound();
        return Ok(log);
    }

    [HttpGet("user/{userId:int}")]
    public async Task<IActionResult> GetByUser(int userId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var logs = await _service.GetByUserAsync(userId, page, pageSize);
        return Ok(logs);
    }

    [HttpGet("entity/{entityType}/{entityId:int}")]
    public async Task<IActionResult> GetByEntity(string entityType, int entityId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var logs = await _service.GetByEntityAsync(entityType, entityId, page, pageSize);
        return Ok(logs);
    }

    [HttpGet("action/{action}")]
    public async Task<IActionResult> GetByAction(string action, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var logs = await _service.GetByActionAsync(action, page, pageSize);
        return Ok(logs);
    }

    [HttpGet("severity/{severity}")]
    public async Task<IActionResult> GetBySeverity(AuditLogSeverity severity, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var logs = await _service.GetBySeverityAsync(severity, page, pageSize);
        return Ok(logs);
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string? searchTerm = null,
        [FromQuery] string? entityType = null,
        [FromQuery] string? action = null,
        [FromQuery] AuditLogSeverity? severity = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var logs = await _service.SearchAsync(searchTerm, entityType, action, severity, fromDate, toDate, page, pageSize);
        return Ok(logs);
    }

    [HttpPost("log")]
    [RequirePermission("AuditLogs.Create")]
    public async Task<IActionResult> LogAction([FromBody] LogActionRequest request)
    {
        await _service.LogActionAsync(
            request.Action,
            request.EntityType,
            request.EntityId,
            request.UserId,
            request.OldValuesJson,
            request.NewValuesJson,
            request.AdditionalInfoJson,
            request.Severity
        );
        return Ok(new { message = "Action logged successfully" });
    }
}

public record LogActionRequest(
    string Action,
    string EntityType,
    int? EntityId = null,
    int? UserId = null,
    string? OldValuesJson = null,
    string? NewValuesJson = null,
    string? AdditionalInfoJson = null,
    AuditLogSeverity Severity = AuditLogSeverity.Info
);
