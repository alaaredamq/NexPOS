using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Web.Api.Infrastructure;

namespace Web.Api.CashDrawer;

/// <summary>
/// The drawer said no (already open, session closed, ...). Carries the status, the stable <c>code</c> the UI branches on and the
/// <c>SharedResource</c> key of the message; <see cref="DrawerRuleExceptionFilterAttribute"/> answers it as problem JSON in the request's language.
/// </summary>
public sealed class DrawerRuleException : Exception
{
    public int Status { get; }

    public string Code { get; }

    public string ResourceKey { get; }

    public DrawerRuleException(int status, string code, string resourceKey) : base(resourceKey)
    {
        Status = status;
        Code = code;
        ResourceKey = resourceKey;
    }
}

public sealed class DrawerRuleExceptionFilterAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is not DrawerRuleException rule) return;

        var http = context.HttpContext;
        var problem = ApiProblem.Create(http, rule.Status, rule.Code, ApiProblem.Localize(http, rule.ResourceKey));
        context.Result = new ObjectResult(problem) { StatusCode = rule.Status, ContentTypes = { ApiProblem.ContentType } };
        context.ExceptionHandled = true;
    }
}

public static class DrawerCodes
{
    public const string AlreadyOpen = "drawer_already_open";
    public const string SessionNotFound = "session_not_found";
    public const string SessionClosed = "session_closed";
    public const string SessionStillOpen = "session_still_open";
    public const string ZReportUnavailable = "z_report_unavailable";
}
