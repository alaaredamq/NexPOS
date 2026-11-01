using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.Infrastructure;
using Web.Api.Users.Contracts;
using Web.Attributes;
using Web.Helpers;

namespace Web.Api.Users;

/// <summary>
/// Users: list, create, edit, enable / disable, reset PIN, delete. Mirrors the Razor <c>UsersController</c>, whose class-level
/// <c>Users.View</c> applies to every action in addition to the one named on the action.
/// </summary>
[Route("api/v1/users")]
[RequirePermission(PermissionHelper.Permissions.Users_View)]
public sealed class UsersController : ApiControllerBase
{
    private readonly IUserApiService _users;

    public UsersController(IUserApiService users) => _users = users;

    /// <summary>
    /// One page of users. <c>sort</c>: <c>name</c> (default), <c>username</c>, <c>email</c>, <c>role</c>, <c>isActive</c>, <c>lastLoginAt</c>, <c>createdAt</c>;
    /// <c>q</c> matches the username, email, name and role name.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<UserDto>>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "listUsers")]
    public async Task<PagedResult<UserDto>> List([FromQuery] PagedQuery query) => await _users.ListAsync(query);

    /// <summary>Totals for the tiles above the list (all users and roles, not just the current page).</summary>
    [HttpGet("summary")]
    [ProducesResponseType<UserSummaryDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getUserSummary")]
    public async Task<UserSummaryDto> Summary() => await _users.SummaryAsync();

    /// <summary>One user.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "getUser")]
    public async Task<ActionResult<UserDto>> Get(int id) => await _users.GetAsync(id) is { } user ? user : NotFound();

    /// <summary>
    /// Creates a user with the PIN an admin chose; the user must change it at the first sign-in. A taken username or email,
    /// or an unknown role, answers 400 <c>validation_failed</c> with the field in <c>errors</c>.
    /// </summary>
    [HttpPost]
    [RequirePermission(PermissionHelper.Permissions.Users_Create)]
    [ProducesResponseType<UserDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "createUser")]
    public async Task<ActionResult<UserDto>> Create(UserCreateRequest request)
    {
        try
        {
            var created = await _users.CreateAsync(request);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }
        catch (UserRuleException ex)
        {
            return ValidationFailure(ex.Field, ex.Message);
        }
    }

    /// <summary>Changes the name, email, role and active flag. The role and the flag reach the user's open session on its next request.</summary>
    [HttpPut("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Users_Edit)]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "updateUser")]
    public async Task<ActionResult<UserDto>> Update(int id, UserUpdateRequest request)
    {
        try
        {
            return await _users.UpdateAsync(id, request) is { } updated ? updated : NotFound();
        }
        catch (UserRuleException ex)
        {
            return ValidationFailure(ex.Field, ex.Message);
        }
    }

    /// <summary>Enables or disables a user (the "toggle" button of the Razor page, without the guessing: you say which).</summary>
    [HttpPut("{id:int}/active")]
    [RequirePermission(PermissionHelper.Permissions.Users_Edit)]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "setUserActive")]
    public async Task<ActionResult<UserDto>> SetActive(int id, UserActiveRequest request)
        => await _users.SetActiveAsync(id, request.IsActive) is { } updated ? updated : NotFound();

    /// <summary>Sets a new PIN for the user, who must replace it at the next sign-in.</summary>
    [HttpPost("{id:int}/reset-pin")]
    [RequirePermission(PermissionHelper.Permissions.Users_Edit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "resetUserPin")]
    public async Task<IActionResult> ResetPin(int id, UserResetPinRequest request)
        => await _users.ResetPinAsync(id, request.NewPin) ? NoContent() : NotFound();

    /// <summary>Deletes a user. 409 <c>user_in_use</c> for someone with sales, receipts or sessions on record: deactivate them instead.</summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Users_Delete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "deleteUser")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            return await _users.DeleteAsync(id) ? NoContent() : NotFound();
        }
        catch (UserInUseException ex)
        {
            return ConflictFailure("user_in_use", ex.Message);
        }
    }
}
