using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.Account.Contracts;
using Web.Api.Infrastructure;

namespace Web.Api.Account;

/// <summary>The signed-in user's own profile and PIN. Open to every signed-in user: nobody needs a permission to look after their own account.</summary>
[Route("api/v1/account")]
public sealed class AccountController : ApiControllerBase
{
    private readonly IAccountApiService _account;

    public AccountController(IAccountApiService account) => _account = account;

    /// <summary>The account page: who you are, your role and when you last signed in.</summary>
    [HttpGet("profile")]
    [AnySignedInUser]
    [ProducesResponseType<ProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "getProfile")]
    public async Task<ActionResult<ProfileDto>> Profile() => await _account.GetProfileAsync(CurrentUserId) is { } profile ? profile : NotFound();

    /// <summary>
    /// Changes your own PIN. The current PIN is asked for, except right after an admin set or reset it (<c>mustChangePin</c> in the session).
    /// A wrong current PIN, a PIN that does not change or a differing confirmation answers 400 <c>validation_failed</c> with the field in <c>errors</c>.
    /// </summary>
    [HttpPost("change-pin")]
    [AnySignedInUser]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "changeOwnPin")]
    public async Task<IActionResult> ChangePin(ChangePinRequest request)
    {
        try
        {
            return await _account.ChangePinAsync(CurrentUserId, request) ? NoContent() : NotFound();
        }
        catch (AccountRuleException ex)
        {
            return ValidationFailure(ex.Field, ex.Message);
        }
    }
}
