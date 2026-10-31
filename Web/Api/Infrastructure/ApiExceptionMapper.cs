using Application.Exceptions;

namespace Web.Api.Infrastructure;

/// <summary>
/// Decides what a request that threw is told, for <c>api/v1</c>. Domain exceptions the services already throw get
/// their proper status and code; anything else is a 500 whose detail is hidden outside Development (the exception
/// itself is in the log and, through the shared trace id, in the audit trail).
/// </summary>
public static class ApiExceptionMapper
{
    public sealed record Mapped(int Status, string Code, string? Detail, bool Unexpected);

    public static Mapped Map(Exception exception, bool includeDetail) => exception switch
    {
        LicenseRequiredException => new(StatusCodes.Status403Forbidden, ApiCodes.LicenseSalesStopped, null, false),
        BadHttpRequestException bad => new(bad.StatusCode, ApiCodes.ForStatus(bad.StatusCode), includeDetail ? bad.Message : null, false),
        _ => new(StatusCodes.Status500InternalServerError, ApiCodes.InternalError, includeDetail ? exception.Message : null, true),
    };
}
