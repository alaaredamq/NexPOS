using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.Infrastructure;
using Web.Api.Notifications.Contracts;
using Web.Attributes;
using Web.Helpers;

namespace Web.Api.Notifications;

/// <summary>
/// The bell feed, the notifications list and the system alerts. Port of the Razor <c>NotificationsController</c>.
/// <para>
/// Permissions: the old controller only required a signed-in user, so notifications stay open to every signed-in user.
/// Alerts need <c>Alerts.Read</c> (the old bell already hid them from users without it) and, new on this page, <c>Alerts.Update</c> to
/// dismiss and <c>Alerts.Delete</c> to delete - the same permissions the legacy <c>api/Alerts</c> controller asks for.
/// </para>
/// </summary>
[Route("api/v1/notifications")]
[SwaggerTag("Notifications")]
public sealed class NotificationsController : ApiControllerBase
{
    private const int MaxFeedItems = 50;

    private readonly INotificationApiService _notifications;

    public NotificationsController(INotificationApiService notifications)
    {
        _notifications = notifications;
    }

    private bool CanSeeAlerts => PermissionHelper.HasPermission(User, PermissionHelper.Permissions.Alerts_View);

    // ---- bell ------------------------------------------------------------------------------------------

    /// <summary>What the top-bar bell polls: the newest <c>take</c> items (1-50, default 5) and the unread total. Alerts are left out for a caller without <c>Alerts.Read</c>.</summary>
    [HttpGet("feed")]
    [AnySignedInUser]
    [ProducesResponseType<NotificationFeedDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getNotificationFeed")]
    public async Task<NotificationFeedDto> Feed([FromQuery] int take = 5)
        => await _notifications.FeedAsync(Math.Clamp(take, 1, MaxFeedItems), CanSeeAlerts);

    /// <summary>
    /// Marks one bell item read: a notification is marked read, an alert is acknowledged as the signed-in user.
    /// <c>source</c> is <c>alert</c> (needs <c>Alerts.Read</c>) or <c>notification</c>.
    /// </summary>
    [HttpPost("feed/{source}/{id:int}/read")]
    [AnySignedInUser]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "markFeedItemRead")]
    public async Task<IActionResult> MarkFeedItemRead(string source, int id)
    {
        if (source is not (NotificationApiService.SourceAlert or NotificationApiService.SourceNotification)) return NotFound();
        if (source == NotificationApiService.SourceAlert && !CanSeeAlerts) return Failure(StatusCodes.Status403Forbidden, ApiCodes.Forbidden);

        return await _notifications.MarkFeedItemReadAsync(source, id, CurrentUserId) ? NoContent() : NotFound();
    }

    /// <summary>Marks every notification read and, for a caller with <c>Alerts.Read</c>, acknowledges every active alert.</summary>
    [HttpPost("read-all")]
    [AnySignedInUser]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [SwaggerOperation(OperationId = "markAllNotificationsRead")]
    public async Task<IActionResult> MarkAllRead()
    {
        await _notifications.MarkAllReadAsync(CurrentUserId, CanSeeAlerts);
        return NoContent();
    }

    // ---- page -------------------------------------------------------------------------------------------

    /// <summary>The tiles of the notifications page. The alert figures are zero (and <c>alertsVisible</c> false) without <c>Alerts.Read</c>.</summary>
    [HttpGet("summary")]
    [AnySignedInUser]
    [ProducesResponseType<NotificationSummaryDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getNotificationSummary")]
    public async Task<NotificationSummaryDto> Summary() => await _notifications.SummaryAsync(CanSeeAlerts);

    /// <summary>
    /// One page of notifications, newest first (<c>dir=asc</c> for oldest first; <c>createdAt</c> is the only sort).
    /// <c>q</c> matches the title or message, <c>kind</c> is <c>info</c>/<c>warning</c>/<c>error</c>/<c>success</c>, <c>unread=true</c> hides the read ones.
    /// </summary>
    [HttpGet]
    [AnySignedInUser]
    [ProducesResponseType<PagedResult<NotificationRowDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "listNotifications")]
    public async Task<ActionResult<PagedResult<NotificationRowDto>>> List([FromQuery] PagedQuery query, [FromQuery] string? kind = null, [FromQuery] bool unread = false)
    {
        try
        {
            return await _notifications.ListNotificationsAsync(query, kind, unread);
        }
        catch (NotificationRuleException ex)
        {
            return ValidationFailure(ex.Field, ex.Message);
        }
    }

    /// <summary>Marks one notification read.</summary>
    [HttpPost("{id:int}/read")]
    [AnySignedInUser]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "markNotificationRead")]
    public async Task<IActionResult> MarkRead(int id) => await _notifications.MarkNotificationReadAsync(id) ? NoContent() : NotFound();

    /// <summary>Deletes one notification.</summary>
    [HttpDelete("{id:int}")]
    [AnySignedInUser]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "deleteNotification")]
    public async Task<IActionResult> Delete(int id) => await _notifications.DeleteNotificationAsync(id) ? NoContent() : NotFound();

    /// <summary>Deletes every notification (read or not). Alerts are not touched.</summary>
    [HttpDelete]
    [AnySignedInUser]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [SwaggerOperation(OperationId = "clearNotifications")]
    public async Task<IActionResult> Clear()
    {
        await _notifications.ClearNotificationsAsync();
        return NoContent();
    }

    /// <summary>Housekeeping: deletes notifications older than 30 days and alerts that were dismissed more than 7 days ago.</summary>
    [HttpPost("cleanup")]
    [AnySignedInUser]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [SwaggerOperation(OperationId = "cleanupNotifications")]
    public async Task<IActionResult> Cleanup()
    {
        await _notifications.CleanupAsync();
        return NoContent();
    }

    // ---- alerts -----------------------------------------------------------------------------------------

    /// <summary>
    /// One page of alerts, newest first. <c>scope</c>: <c>open</c> (default: active or acknowledged), <c>closed</c> (resolved or dismissed) or <c>all</c>;
    /// <c>kind</c> is an alert kind (<c>lowStock</c>, <c>outOfStock</c>, ...); <c>q</c> matches the title or message.
    /// </summary>
    [HttpGet("alerts")]
    [RequirePermission(PermissionHelper.Permissions.Alerts_View)]
    [ProducesResponseType<PagedResult<AlertRowDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "listAlerts")]
    public async Task<ActionResult<PagedResult<AlertRowDto>>> ListAlerts([FromQuery] PagedQuery query, [FromQuery] string? kind = null, [FromQuery] string? scope = null)
    {
        try
        {
            return await _notifications.ListAlertsAsync(query, kind, scope);
        }
        catch (NotificationRuleException ex)
        {
            return ValidationFailure(ex.Field, ex.Message);
        }
    }

    /// <summary>Dismisses one alert: it leaves the bell and the default list.</summary>
    [HttpPost("alerts/{id:int}/dismiss")]
    [RequirePermission(PermissionHelper.Permissions.Alerts_Update)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "dismissAlert")]
    public async Task<IActionResult> DismissAlert(int id) => await _notifications.DismissAlertAsync(id) ? NoContent() : NotFound();

    /// <summary>Dismisses every open alert.</summary>
    [HttpPost("alerts/dismiss-all")]
    [RequirePermission(PermissionHelper.Permissions.Alerts_Update)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [SwaggerOperation(OperationId = "dismissAllAlerts")]
    public async Task<IActionResult> DismissAllAlerts()
    {
        await _notifications.DismissAllAlertsAsync();
        return NoContent();
    }

    /// <summary>Deletes one alert for good.</summary>
    [HttpDelete("alerts/{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Alerts_Delete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "deleteAlert")]
    public async Task<IActionResult> DeleteAlert(int id) => await _notifications.DeleteAlertAsync(id) ? NoContent() : NotFound();
}
