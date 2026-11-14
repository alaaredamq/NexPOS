namespace Web.Api.Infrastructure;

/// <summary>Where the JSON API lives and how it is told apart from the MVC pages and the legacy <c>api/*</c> controllers.</summary>
public static class ApiPaths
{
    /// <summary>Route prefix of every controller of the SPA's API. Modules write <c>[Route("api/v1/orders")]</c>.</summary>
    public const string V1Prefix = "api/v1";

    /// <summary>Custom request header a browser must add to every mutating <c>api/v1</c> call (cross-site forms cannot send it).</summary>
    public const string ClientHeader = "X-NexPOS-Client";

    public const string ClientHeaderValue = "web";

    /// <summary>
    /// Any <c>/api/*</c> request, including the legacy controllers the Razor pages still call.
    /// Used where the right answer is "JSON, not a redirect to the login page".
    /// </summary>
    public static bool IsApi(HttpRequest request) => request.Path.StartsWithSegments("/api");

    /// <summary>Only the new, versioned API. Legacy <c>/api/*</c> keeps its old <c>{success,message}</c> shapes until cutover.</summary>
    public static bool IsV1(HttpRequest request) => request.Path.StartsWithSegments("/api/v1");
}

/// <summary>
/// The stable, machine-readable <c>code</c> carried by every error. The UI branches on these, never on messages.
/// Modules add their own (<c>drawer_not_open</c>, ...) next to the ones that belong to the whole API.
/// </summary>
public static class ApiCodes
{
    public const string BadRequest = "bad_request";
    public const string ValidationFailed = "validation_failed";
    public const string Unauthenticated = "unauthenticated";
    public const string Forbidden = "forbidden";
    public const string ClientHeaderRequired = "client_header_required";
    public const string NotFound = "not_found";
    public const string MethodNotAllowed = "method_not_allowed";
    public const string Conflict = "conflict";
    public const string PayloadTooLarge = "payload_too_large";
    public const string RateLimited = "rate_limited";
    public const string InternalError = "internal_error";

    /// <summary>The licence does not allow selling (expired past the grace week, not activated, wrong PC, ...).</summary>
    public const string LicenseSalesStopped = "license_sales_stopped";

    /// <summary>A PC other than the store server tried to sell without having been paired as a till.</summary>
    public const string DeviceNotRegistered = "device_not_registered";

    /// <summary>A paired till, but the licence now covers fewer PCs than are registered and this one is over the limit.</summary>
    public const string SeatsExceeded = "seats_exceeded";

    /// <summary>Every PC of the package is already in use.</summary>
    public const string SeatsFull = "seats_full";

    public const string InvalidPairingCode = "invalid_pairing_code";

    public const string InvalidCredentials = "invalid_credentials";
    public const string AccountDisabled = "account_disabled";

    /// <summary>The generic code for an HTTP status nobody gave a more specific code.</summary>
    public static string ForStatus(int status) => status switch
    {
        StatusCodes.Status400BadRequest => BadRequest,
        StatusCodes.Status401Unauthorized => Unauthenticated,
        StatusCodes.Status403Forbidden => Forbidden,
        StatusCodes.Status404NotFound => NotFound,
        StatusCodes.Status405MethodNotAllowed => MethodNotAllowed,
        StatusCodes.Status409Conflict => Conflict,
        StatusCodes.Status413PayloadTooLarge => PayloadTooLarge,
        StatusCodes.Status422UnprocessableEntity => ValidationFailed,
        StatusCodes.Status429TooManyRequests => RateLimited,
        >= 500 => InternalError,
        _ => BadRequest,
    };

    /// <summary>
    /// Resource key (in <c>SharedResource</c>) of the generic message for a problem's title. A code the API
    /// does not know (a module's own, like <c>drawer_not_open</c>) gets the generic message of its HTTP status.
    /// </summary>
    public static string MessageKey(string code, int status) => code switch
    {
        Unauthenticated => "Api_Unauthenticated",
        Forbidden => "Api_Forbidden",
        ClientHeaderRequired => "Api_ClientHeaderRequired",
        NotFound => "Api_NotFound",
        MethodNotAllowed => "Api_MethodNotAllowed",
        ValidationFailed => "Api_ValidationFailed",
        Conflict => "Api_Conflict",
        PayloadTooLarge => "Api_PayloadTooLarge",
        RateLimited => "Api_RateLimited",
        InternalError => "Api_InternalError",
        LicenseSalesStopped => "License_SalesStopped",
        DeviceNotRegistered => "License_DeviceNotRegistered",
        SeatsExceeded => "License_SeatsExceeded",
        SeatsFull => "License_SeatsFull",
        InvalidPairingCode => "License_InvalidPairingCode",
        InvalidCredentials => "Invalid username or PIN",
        AccountDisabled => "Account is disabled",
        BadRequest => "Api_BadRequest",
        _ => ForStatus(status) is var generic && generic != code ? MessageKey(generic, status) : "Api_BadRequest",
    };
}
