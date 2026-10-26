using Application.IServices.Notifications;
using Core.Entities.Notifications;
using Microsoft.AspNetCore.Mvc;
using Web.Attributes;
using Web.Helpers;

namespace Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequirePermission(PermissionHelper.Permissions.Alerts_View)]
public class AlertsController : ControllerBase
{
    private readonly IAlertService _service;

    public AlertsController(IAlertService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var alerts = await _service.GetAllAsync();
        return Ok(alerts);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var alert = await _service.GetByIdAsync(id);
        if (alert == null) return NotFound();
        return Ok(alert);
    }

    [HttpGet("active")]
    public async Task<IActionResult> GetActive()
    {
        var alerts = await _service.GetActiveAlertsAsync();
        return Ok(alerts);
    }

    [HttpGet("type/{type}")]
    public async Task<IActionResult> GetByType(AlertType type)
    {
        var alerts = await _service.GetByTypeAsync(type);
        return Ok(alerts);
    }

    [HttpGet("status/{status}")]
    public async Task<IActionResult> GetByStatus(AlertStatus status)
    {
        var alerts = await _service.GetByStatusAsync(status);
        return Ok(alerts);
    }

    [HttpPost]
    [RequirePermission(PermissionHelper.Permissions.Alerts_Create)]
    public async Task<IActionResult> Create([FromBody] CreateAlertRequest request)
    {
        var alert = await _service.CreateAlertAsync(request.Title, request.Message, request.Type, request.Metadata);
        
        return CreatedAtAction(nameof(GetById), new { id = alert.Id }, alert);
    }

    [HttpPut("{id:int}/acknowledge")]
    [RequirePermission(PermissionHelper.Permissions.Alerts_Update)]
    public async Task<IActionResult> Acknowledge(int id, [FromBody] AcknowledgeAlertRequest? request = null)
    {
        // Always the signed-in user; a client-supplied UserId would let anyone act as someone else.
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();
        await _service.AcknowledgeAsync(id, userId);
        return Ok(new { message = "Alert acknowledged" });
    }

    [HttpPut("{id:int}/resolve")]
    [RequirePermission(PermissionHelper.Permissions.Alerts_Update)]
    public async Task<IActionResult> Resolve(int id, [FromBody] ResolveAlertRequest? request = null)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();
        await _service.ResolveAsync(id, userId);
        return Ok(new { message = "Alert resolved" });
    }

    private bool TryGetCurrentUserId(out int userId) =>
        int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out userId);

    [HttpPut("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Alerts_Update)]
    public async Task<IActionResult> Update(int id, [FromBody] Alert alert)
    {
        alert.Id = id;
        await _service.UpdateAsync(alert);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Alerts_Delete)]
    public async Task<IActionResult> Delete(int id)
    {
        await _service.DeleteAsync(id);
        return NoContent();
    }
}

public record CreateAlertRequest(string Title, string Message, AlertType Type, string? Metadata = null);
public record AcknowledgeAlertRequest(int UserId);
public record ResolveAlertRequest(int UserId);
