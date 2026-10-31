using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.Infrastructure;
using Web.Api.Session.Contracts;

namespace Web.Api.Session;

/// <summary>The shell's bootstrap: who is signed in and what they may see.</summary>
[Route("api/v1/session")]
public sealed class SessionController : ApiControllerBase
{
    private readonly ISessionBootstrapService _bootstrap;

    public SessionController(ISessionBootstrapService bootstrap) => _bootstrap = bootstrap;

    /// <summary>The whole shell in one round trip.</summary>
    /// <remarks>
    /// User, role, permissions, permission-filtered navigation (with the SPA path of each page), licence state, branding,
    /// language, the user's drawer and the unread-notification count. A 401 means the cookie is missing, expired, or the account was disabled - go to the login page.
    /// </remarks>
    [HttpGet]
    [AnySignedInUser]
    [ProducesResponseType<SessionDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getSession")]
    public async Task<ActionResult<SessionDto>> Get() => await _bootstrap.BuildAsync(User, CurrentLanguage);
}
