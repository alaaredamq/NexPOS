using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.Infrastructure;
using Web.Api.Roles.Contracts;
using Web.Attributes;
using Web.Helpers;
using Web.Services;

namespace Web.Api.Roles;

/// <summary>
/// Roles and their permission grants. Mirrors the role actions of the Razor <c>UsersController</c>: the class-level <c>Users.View</c>
/// plus <c>Users.Manage</c> for everything that shows or changes a role's grants.
/// </summary>
[Route("api/v1/roles")]
[RequirePermission(PermissionHelper.Permissions.Users_View)]
public sealed class RolesController : ApiControllerBase
{
    private readonly IRoleApiService _roles;

    public RolesController(IRoleApiService roles) => _roles = roles;

    /// <summary>
    /// One page of roles (also what the user forms offer to pick from; ask for <c>pageSize=200</c>). <c>sort</c>: <c>name</c> (default),
    /// <c>userCount</c>, <c>permissionCount</c>, <c>createdAt</c>; <c>q</c> matches the name and description.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<RoleDto>>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "listRoles")]
    public async Task<PagedResult<RoleDto>> List([FromQuery] PagedQuery query) => await _roles.ListAsync(query);

    /// <summary>A role with the permission keys it grants (the whole catalogue for SuperAdmin).</summary>
    [HttpGet("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Users_Manage)]
    [ProducesResponseType<RoleDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "getRole")]
    public async Task<ActionResult<RoleDetailsDto>> Get(int id) => await _roles.GetAsync(id) is { } role ? role : NotFound();

    /// <summary>Creates a role holding exactly the permissions sent.</summary>
    [HttpPost]
    [RequirePermission(PermissionHelper.Permissions.Users_Manage)]
    [ProducesResponseType<RoleDetailsDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "createRole")]
    public async Task<ActionResult<RoleDetailsDto>> Create(RoleRequest request)
    {
        try
        {
            var created = await _roles.CreateAsync(request);
            return CreatedAtAction(nameof(Get), new { id = created.Role.Id }, created);
        }
        catch (RoleRuleException ex)
        {
            return Refuse(ex);
        }
    }

    /// <summary>
    /// Replaces the name, description and grants of a role. People who hold the role are affected on their next request.
    /// 409 <c>role_super_admin_locked</c> for SuperAdmin, which always holds everything.
    /// </summary>
    [HttpPut("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Users_Manage)]
    [ProducesResponseType<RoleDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "updateRole")]
    public async Task<ActionResult<RoleDetailsDto>> Update(int id, RoleRequest request)
    {
        try
        {
            return await _roles.UpdateAsync(id, request);
        }
        catch (RoleRuleException ex)
        {
            return Refuse(ex);
        }
    }

    /// <summary>Deletes a role. 409 <c>role_system</c> for a built-in role and <c>role_in_use</c> while users still hold it.</summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Users_Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "deleteRole")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            await _roles.DeleteAsync(id);
            return NoContent();
        }
        catch (RoleRuleException ex)
        {
            return Refuse(ex);
        }
    }

    // A bad name is a validation error on the field; a missing role is a 404; anything else is a rule about the role's current state.
    private ObjectResult Refuse(RoleRuleException ex) => ex.Code switch
    {
        RoleErrors.NameRequired or RoleErrors.NameTooLong or RoleErrors.NameTaken => ValidationFailure("name", ex.Message),
        RoleErrors.NotFound => Failure(StatusCodes.Status404NotFound, ApiCodes.NotFound, ex.Message),
        _ => ConflictFailure(ex.Code, ex.Message),
    };
}
