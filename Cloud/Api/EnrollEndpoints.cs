using Cloud.Services;
using Sync;

namespace Cloud.Api;

/// <summary>
/// The one anonymous write endpoint: a till trades a one-time code for its device token. The code carries 60 bits
/// and works once, and failures from one address are throttled on top, so guessing is not a practical route.
/// </summary>
public static class EnrollEndpoints
{
    public static void MapEnrollApi(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/enroll", RedeemAsync).AllowAnonymous();
    }

    private static async Task<IResult> RedeemAsync(
        EnrollRequest request, HttpContext http, EnrollmentService enrollment, LoginThrottle throttle)
    {
        var key = "enroll:" + (http.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        if (throttle.IsBlocked(key))
            return Error(StatusCodes.Status429TooManyRequests, SyncErrorCodes.RateLimited, "Too many attempts. Try again in a few minutes.");

        var result = await enrollment.RedeemAsync(request?.Code, request?.MachineId, request?.DeviceName);
        switch (result.Outcome)
        {
            case RedeemOutcome.Success:
                throttle.Reset(key);
                return Results.Json(new EnrollResponse(result.Token!, result.SiteName!, result.ClientName!), SyncWire.Json);
            // Used and expired answers mean the code WAS real, so they are not guesses: they must not count toward the
            // throttle, or an owner retrying an old link would lock out everyone behind the same address.
            case RedeemOutcome.Used:
                return Error(StatusCodes.Status409Conflict, SyncErrorCodes.EnrollCodeUsed,
                    "This link has already been used. Ask for a new one.");
            case RedeemOutcome.Expired:
                return Error(StatusCodes.Status410Gone, SyncErrorCodes.EnrollCodeExpired,
                    "This link has expired. Ask for a new one.");
            case RedeemOutcome.Disabled:
                return Error(StatusCodes.Status403Forbidden, SyncErrorCodes.SiteDisabled,
                    "This installation is disabled. Contact your provider.");
            default:
                throttle.RecordFailure(key);
                return Error(StatusCodes.Status404NotFound, SyncErrorCodes.EnrollCodeInvalid,
                    "This link is not valid. Check that you copied all of it.");
        }
    }

    private static IResult Error(int status, string code, string message)
        => Results.Json(new SyncError(code, message), SyncWire.Json, statusCode: status);
}
