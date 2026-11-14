using System.Net;
using Application.DTOs.Licensing;
using Application.IServices.Licensing;

namespace Web.Api.Infrastructure;

/// <summary>
/// Decides whether the PC behind a request may sell. The store server's own PC is always the first seat of the licence:
/// it reaches the app over loopback and needs nothing. Any other PC is a till and must present the token it received when
/// it was paired (cookie <see cref="CookieName"/>, or header <see cref="HeaderName"/> for tools).
/// <para>
/// Today the app listens on this PC only (<c>http://localhost:5000</c>), so every request is the server's and nothing here can refuse
/// anyone; the check starts to matter when the server is opened to the shop network. It deliberately trusts only the socket
/// address, never <c>X-Forwarded-For</c>, which any till could forge. (A reverse proxy on this PC would make every request look
/// local: do not put one in front of a multi-seat install without moving this check to the proxy.)
/// </para>
/// </summary>
public static class TillGate
{
    public const string CookieName = "nexpos_till";
    public const string HeaderName = "X-Till-Token";

    /// <summary>
    /// True for requests from this PC. A missing address (in-process test host, pipe transports) cannot come from the network,
    /// so it counts as local too.
    /// </summary>
    public static bool IsServerPc(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress;
        return ip is null || IPAddress.IsLoopback(ip);
    }

    public static string? TokenOf(HttpRequest request)
    {
        var header = request.Headers[HeaderName].ToString();
        if (!string.IsNullOrWhiteSpace(header)) return header;
        return request.Cookies.TryGetValue(CookieName, out var cookie) ? cookie : null;
    }

    public static async Task<TillAccess> CheckAsync(HttpContext context)
    {
        if (IsServerPc(context)) return TillAccess.Allowed;

        var tills = context.RequestServices.GetRequiredService<ITillDeviceService>();
        return await tills.CheckAccessAsync(TokenOf(context.Request), context.RequestAborted);
    }

    /// <summary>The problem code that tells a till why it was refused.</summary>
    public static string CodeFor(TillAccess access) =>
        access == TillAccess.SeatsExceeded ? ApiCodes.SeatsExceeded : ApiCodes.DeviceNotRegistered;
}
