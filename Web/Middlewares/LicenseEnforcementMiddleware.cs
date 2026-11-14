using Application.DTOs.Licensing;
using Application.IServices.Licensing;
using Core.Auditing;
using Core.Entities.Logging;
using Microsoft.Extensions.Localization;
using Web.Api.Infrastructure;

namespace Web.Middlewares;

/// <summary>
/// When the license does not allow selling (expired past the grace week, not activated, wrong
/// computer, ...), keeps the POS screen closed and refuses requests that start or change a sale.
/// Everything else (products, orders, reports, backups, settings, the License page) keeps
/// working, so a shop's data is never locked away.
///
/// Matching is by path so it works for both URL shapes, /POS and /{lang}/POS, and sits before
/// routing and authentication. <c>POSCheckoutService</c> and <c>OrderService</c> repeat the check
/// as a backstop.
///
/// This middleware is for the legacy URLs. The versioned API (<c>api/v1</c>) is deliberately not matched here: its
/// sale endpoints carry <c>[RequireSalesLicense]</c> (Web/Api/Infrastructure), which answers 403 <c>license_sales_stopped</c>
/// as problem JSON, and the licence screens stay reachable because nothing blocks reads.
/// </summary>
public class LicenseEnforcementMiddleware
{
    private enum Blocked { None, Page, PosApi, CartApi }

    private readonly RequestDelegate _next;

    public LicenseEnforcementMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context, ILicenseService license, IStringLocalizer<SharedResource> localizer, IAuditTrail auditTrail)
    {
        var blocked = Classify(context.Request);
        if (blocked == Blocked.None)
        {
            await _next(context);
            return;
        }

        if (license.GetState().AllowsSales)
        {
            // Licensed - but a PC other than the store server must be a paired till to sell (see TillGate).
            var access = await TillGate.CheckAsync(context);
            if (access == TillAccess.Allowed)
            {
                await _next(context);
                return;
            }

            await RefuseDevice(context, blocked, access, localizer, auditTrail);
            return;
        }

        // A refused sale answers HTTP 200 {success:false}, which the request log reads as a success - so say so explicitly.
        // (Only for actual sale calls; being bounced away from the POS *page* is just a redirect.)
        if (blocked != Blocked.Page)
        {
            auditTrail.LogBusiness(AuditActions.SaleFailed,
                $"Sale blocked by the license ({license.GetState().Status}): {context.Request.Method} {context.Request.Path}",
                "License", AuditOutcome.Denied, AuditLogSeverity.Warning,
                details: new { license = license.GetState().Status.ToString(), area = blocked.ToString() });
        }

        if (blocked == Blocked.Page)
        {
            var lang = context.Items["CurrentLanguage"] as string ?? "en";
            context.Response.Redirect($"/{lang}/License?from=pos");
            return;
        }

        var body = new
        {
            success = false,
            message = localizer["License_SalesStopped"].Value,
            errorCode = "license_required"
        };

        // The POS script reads {success:false, message} from a normal 200 response (the convention
        // every controller in this app follows). The cart API is plain REST, so it gets a real 403.
        context.Response.StatusCode = blocked == Blocked.CartApi ? StatusCodes.Status403Forbidden : StatusCodes.Status200OK;
        await context.Response.WriteAsJsonAsync(body);
    }

    private static async Task RefuseDevice(
        HttpContext context, Blocked blocked, TillAccess access, IStringLocalizer<SharedResource> localizer, IAuditTrail auditTrail)
    {
        if (blocked != Blocked.Page)
        {
            auditTrail.LogBusiness(AuditActions.SaleFailed,
                $"Sale blocked: this PC is not a registered till ({access}): {context.Request.Method} {context.Request.Path}",
                "License", AuditOutcome.Denied, AuditLogSeverity.Warning,
                details: new { device = access.ToString(), area = blocked.ToString() });
        }

        if (blocked == Blocked.Page)
        {
            // The till's own pairing screen (public, so a PC with no seat can still reach it).
            context.Response.Redirect("/app/pair-till");
            return;
        }

        var code = TillGate.CodeFor(access);
        var body = new
        {
            success = false,
            message = localizer[access == TillAccess.SeatsExceeded ? "License_SeatsExceeded" : "License_DeviceNotRegistered"].Value,
            errorCode = code
        };
        context.Response.StatusCode = blocked == Blocked.CartApi ? StatusCodes.Status403Forbidden : StatusCodes.Status200OK;
        await context.Response.WriteAsJsonAsync(body);
    }

    private static Blocked Classify(HttpRequest request)
    {
        var segments = (request.Path.Value ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
        var offset = segments.Length > 0 && (Is(segments[0], "en") || Is(segments[0], "ar")) ? 1 : 0;

        var controller = segments.Length > offset ? segments[offset] : null;
        var action = segments.Length > offset + 1 ? segments[offset + 1] : null;
        var isRead = HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method);

        if (controller is not null && Is(controller, "POS"))
        {
            if (isRead) return action is null || Is(action, "Index") ? Blocked.Page : Blocked.None;
            return action is not null && (Is(action, "Checkout") || Is(action, "HoldCart") || Is(action, "ResumeHeldCart"))
                ? Blocked.PosApi
                : Blocked.None;
        }

        if (controller is not null && Is(controller, "api") && action is not null && Is(action, "carts")
            && !isRead && !HttpMethods.IsOptions(request.Method))
        {
            return Blocked.CartApi;
        }

        return Blocked.None;
    }

    private static bool Is(string segment, string name) => segment.Equals(name, StringComparison.OrdinalIgnoreCase);
}
