using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.Infrastructure;
using Web.Api.Money.Contracts;
using Web.Attributes;
using Web.Helpers;

namespace Web.Api.Money;

/// <summary>
/// Code lists and their values. <c>Settings.View</c> to read and <c>Settings.Manage</c> to change, as the legacy <c>CodeListsController</c>; unlike it, <c>Settings.Manage</c> alone may also read
/// (the built-in Admin role holds Manage but not View, so it could not open its own lists).
/// </summary>
[Route("api/v1/code-lists")]
[RequireAnyPermission(PermissionHelper.Permissions.Settings_View, PermissionHelper.Permissions.Settings_Manage)]
public sealed class CodeListsController : MoneyControllerBase
{
    private readonly ICodeListApiService _lists;

    public CodeListsController(ICodeListApiService lists) => _lists = lists;

    /// <summary>One page of code lists. <c>sort</c>: <c>displayOrder</c> (default), <c>key</c>, <c>name</c>, <c>isActive</c>, <c>valueCount</c>, <c>createdAt</c>; <c>q</c> matches key, name and description.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<CodeListDto>>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "listCodeLists")]
    public async Task<PagedResult<CodeListDto>> List([FromQuery] PagedQuery query) => await _lists.ListAsync(query);

    /// <summary>One code list with all its values (active or not), in display order.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<CodeListDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "getCodeList")]
    public async Task<ActionResult<CodeListDetailsDto>> Get(int id) => await _lists.GetAsync(id) is { } found ? found : NotFound();

    /// <summary>Creates a code list. A taken key answers 400 <c>validation_failed</c> on <c>key</c>.</summary>
    [HttpPost]
    [RequirePermission(PermissionHelper.Permissions.Settings_Manage)]
    [ProducesResponseType<CodeListDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "createCodeList")]
    public Task<ActionResult<CodeListDto>> Create(CodeListRequest request) => GuardCreated(() => _lists.CreateAsync(request), l => l.Id);

    /// <summary>Changes a code list.</summary>
    [HttpPut("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Settings_Manage)]
    [ProducesResponseType<CodeListDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "updateCodeList")]
    public Task<ActionResult<CodeListDto>> Update(int id, CodeListRequest request) => Guard(() => _lists.UpdateAsync(id, request));

    /// <summary>Deletes a code list together with its values.</summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Settings_Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "deleteCodeList")]
    public Task<IActionResult> Delete(int id) => Done(() => _lists.DeleteAsync(id));

    /// <summary>Adds a value to a list. A value the list already has (ignoring case) answers 400 <c>validation_failed</c> on <c>value</c>.</summary>
    [HttpPost("{id:int}/values")]
    [RequirePermission(PermissionHelper.Permissions.Settings_Manage)]
    [ProducesResponseType<CodeListValueDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "addCodeListValue")]
    public async Task<ActionResult<CodeListValueDto>> AddValue(int id, CodeListValueRequest request)
    {
        var result = await Guard(() => _lists.AddValueAsync(id, request));
        if (result.Value is { } created) return Created($"/api/v1/code-lists/{id}", created);
        return result;
    }

    /// <summary>Changes a value of a list.</summary>
    [HttpPut("{id:int}/values/{valueId:int}")]
    [RequirePermission(PermissionHelper.Permissions.Settings_Manage)]
    [ProducesResponseType<CodeListValueDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "updateCodeListValue")]
    public Task<ActionResult<CodeListValueDto>> UpdateValue(int id, int valueId, CodeListValueRequest request)
        => Guard(() => _lists.UpdateValueAsync(id, valueId, request));

    /// <summary>Removes a value from a list.</summary>
    [HttpDelete("{id:int}/values/{valueId:int}")]
    [RequirePermission(PermissionHelper.Permissions.Settings_Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "deleteCodeListValue")]
    public Task<IActionResult> DeleteValue(int id, int valueId) => Done(() => _lists.DeleteValueAsync(id, valueId));
}
