using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Web.Api.Infrastructure;

/// <summary>
/// Base of every controller of the SPA's API. Declare the route yourself (<c>[Route("api/v1/orders")]</c>, kebab-case)
/// - a test fails for a controller that does not start with <c>api/v1/</c>.
/// <list type="bullet">
/// <item>Signed-in users only; mark the odd public action <c>[AllowAnonymous]</c>. Add <c>[RequirePermission]</c> per action.</item>
/// <item>Success returns the DTO itself (no <c>{success:true}</c> wrapper); failures are <see cref="ProblemDetails"/> with a <c>code</c>.</item>
/// <item>Controllers stay thin: call a service, never <c>DbContext</c> (<c>Web/Requirements/rules.md</c>).</item>
/// </list>
/// </summary>
[ApiController]
[Authorize]
[Produces("application/json")]
[ProducesErrorResponseType(typeof(ApiProblemDetails))]
[ProducesResponseType<ApiProblemDetails>(StatusCodes.Status401Unauthorized, ApiProblem.ContentType)]
[ProducesResponseType<ApiProblemDetails>(StatusCodes.Status403Forbidden, ApiProblem.ContentType)]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>The signed-in user's id, or 0 when the claim is missing.</summary>
    protected int CurrentUserId
        => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    /// <summary>"en" or "ar": the language this request resolved (from <c>Accept-Language</c> for the API).</summary>
    protected string CurrentLanguage => HttpContext.Items["CurrentLanguage"] as string ?? "en";

    /// <summary>A failure with a stable <paramref name="code"/> and, optionally, an already-localized message for the user.</summary>
    protected ObjectResult Failure(int status, string code, string? message = null)
        => new(ApiProblem.Create(HttpContext, status, code, message))
        {
            StatusCode = status,
            ContentTypes = { ApiProblem.ContentType },
        };

    /// <summary>409 with a code the UI can branch on (<c>drawer_not_open</c>, <c>insufficient_stock</c>, ...).</summary>
    protected ObjectResult ConflictFailure(string code, string? message = null)
        => Failure(StatusCodes.Status409Conflict, code, message);

    /// <summary>400 with per-field messages (<c>errors{field:[msg]}</c>) for rules a DTO's attributes cannot express.</summary>
    protected ObjectResult ValidationFailure(string field, string message)
    {
        var problem = ApiProblem.Validation(HttpContext,
            new Dictionary<string, string[]> { [ApiModelState.CamelCase(field)] = new[] { message } });
        return new ObjectResult(problem) { StatusCode = StatusCodes.Status400BadRequest, ContentTypes = { ApiProblem.ContentType } };
    }
}
