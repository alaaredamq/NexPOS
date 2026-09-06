using Application.IServices.Configuration;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using System.Threading.Tasks;
using Web.Attributes;
using Web.Helpers;

namespace Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequirePermission(PermissionHelper.Permissions.Settings_View)]
public class CodeListsController : ControllerBase
{
    private readonly ICodeListService _codeListService;
    private readonly ICodeListValueService _codeListValueService;

    public CodeListsController(ICodeListService codeListService, ICodeListValueService codeListValueService)
    {
        _codeListService = codeListService;
        _codeListValueService = codeListValueService;
    }

    [HttpGet]
    [SwaggerOperation(Summary = "Get all code lists", Description = "Retrieves all code lists from the system.")]
    [SwaggerResponse(200, "Code lists retrieved successfully")]
    public async Task<IActionResult> GetAll()
    {
        var codeLists = await _codeListService.GetAllAsync();
        return Ok(codeLists);
    }

    [HttpGet("active")]
    [SwaggerOperation(Summary = "Get active code lists", Description = "Retrieves only active code lists from the system.")]
    [SwaggerResponse(200, "Active code lists retrieved successfully")]
    public async Task<IActionResult> GetActive()
    {
        var codeLists = await _codeListService.GetActiveAsync();
        return Ok(codeLists);
    }

    [HttpGet("{id:int}")]
    [SwaggerOperation(Summary = "Get code list by ID", Description = "Retrieves a specific code list by its ID.")]
    [SwaggerResponse(200, "Code list retrieved successfully")]
    [SwaggerResponse(404, "Code list not found")]
    public async Task<IActionResult> GetById(int id)
    {
        var codeList = await _codeListService.GetByIdAsync(id);
        if (codeList == null)
            return NotFound();
        return Ok(codeList);
    }

    [HttpGet("key/{key}")]
    [SwaggerOperation(Summary = "Get code list by key", Description = "Retrieves a specific code list by its key.")]
    [SwaggerResponse(200, "Code list retrieved successfully")]
    [SwaggerResponse(404, "Code list not found")]
    public async Task<IActionResult> GetByKey(string key)
    {
        var codeList = await _codeListService.GetByKeyAsync(key);
        if (codeList == null)
            return NotFound();
        return Ok(codeList);
    }

    [HttpGet("{key}/values")]
    [SwaggerOperation(Summary = "Get code list values by key", Description = "Retrieves all values for a specific code list by its key.")]
    [SwaggerResponse(200, "Code list values retrieved successfully")]
    public async Task<IActionResult> GetValuesByKey(string key)
    {
        var values = await _codeListService.GetValuesByCodeListKeyAsync(key);
        return Ok(values);
    }

    [HttpGet("{key}/values/active")]
    [SwaggerOperation(Summary = "Get active code list values by key", Description = "Retrieves only active values for a specific code list by its key.")]
    [SwaggerResponse(200, "Active code list values retrieved successfully")]
    public async Task<IActionResult> GetActiveValuesByKey(string key)
    {
        var values = await _codeListService.GetActiveValuesByCodeListKeyAsync(key);
        return Ok(values);
    }

    [HttpPost]
    [RequirePermission(PermissionHelper.Permissions.Settings_Manage)]
    [SwaggerOperation(Summary = "Create code list", Description = "Creates a new code list.")]
    [SwaggerResponse(201, "Code list created successfully")]
    [SwaggerResponse(400, "Invalid input")]
    public async Task<IActionResult> Create([FromBody] Core.Entities.Configuration.CodeList codeList)
    {
        var created = await _codeListService.CreateAsync(codeList);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Settings_Manage)]
    [SwaggerOperation(Summary = "Update code list", Description = "Updates an existing code list.")]
    [SwaggerResponse(200, "Code list updated successfully")]
    [SwaggerResponse(404, "Code list not found")]
    [SwaggerResponse(400, "Invalid input")]
    public async Task<IActionResult> Update(int id, [FromBody] Core.Entities.Configuration.CodeList codeList)
    {
        codeList.Id = id;
        var updated = await _codeListService.UpdateAsync(codeList);
        return Ok(updated);
    }

    [HttpDelete("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Settings_Manage)]
    [SwaggerOperation(Summary = "Delete code list", Description = "Deletes a code list.")]
    [SwaggerResponse(204, "Code list deleted successfully")]
    [SwaggerResponse(404, "Code list not found")]
    public async Task<IActionResult> Delete(int id)
    {
        await _codeListService.DeleteAsync(id);
        return NoContent();
    }
}
