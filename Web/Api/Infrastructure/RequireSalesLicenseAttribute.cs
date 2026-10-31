using Application.IServices.Licensing;
using Core.Auditing;
using Core.Entities.Logging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Web.Api.Infrastructure;

/// <summary>
/// Put on an action that starts or changes a sale (checkout, hold/resume a cart, ...). While the licence does not
/// allow selling it answers 403 <c>license_sales_stopped</c> and the action never runs; everything else - reads,
/// reports, backups, the licence screens themselves - keeps working, so a shop's data is never locked away.
/// <para>
/// The legacy <c>LicenseEnforcementMiddleware</c> decides by URL, which suits the fixed Razor/jQuery URLs. The versioned
/// API marks the actions instead, so a module cannot rename an endpoint and silently lose the gate. The services
/// (<c>POSCheckoutService</c>, <c>OrderService</c>) still repeat the check as a backstop.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireSalesLicenseAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var services = context.HttpContext.RequestServices;
        var state = services.GetRequiredService<ILicenseService>().GetState();
        if (state.AllowsSales) return;

        // A refused sale is just a 403 in the request log, so say in the audit trail why (same entry the legacy middleware writes).
        services.GetService<IAuditTrail>()?.LogBusiness(AuditActions.SaleFailed,
            $"Sale blocked by the license ({state.Status}): {context.HttpContext.Request.Method} {context.HttpContext.Request.Path}",
            "License", AuditOutcome.Denied, AuditLogSeverity.Warning,
            details: new { license = state.Status.ToString(), area = "api/v1" });

        context.Result = new ObjectResult(ApiProblem.Create(context.HttpContext, StatusCodes.Status403Forbidden, ApiCodes.LicenseSalesStopped))
        {
            StatusCode = StatusCodes.Status403Forbidden,
            ContentTypes = { ApiProblem.ContentType },
        };
    }
}
