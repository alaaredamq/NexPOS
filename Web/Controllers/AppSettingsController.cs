using Application.IServices.Configuration;
using Core.Entities.Configuration;
using Microsoft.AspNetCore.Mvc;
using Web.Attributes;
using Web.Helpers;

namespace Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequirePermission(PermissionHelper.Permissions.Settings_View)]
public class AppSettingsController : ControllerBase
{
    private readonly IAppSettingService _service;

    public AppSettingsController(IAppSettingService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? type)
    {
        var items = !string.IsNullOrEmpty(type)
            ? await _service.GetByTypeAsync(type)
            : await _service.GetAllAsync();
        return Ok(items);
    }

    [HttpGet("{name}")]
    public async Task<IActionResult> GetByName(string name)
    {
        var setting = await _service.GetByNameAsync(name);
        if (setting == null) return NotFound();
        return Ok(setting);
    }

    [HttpGet("{name}/value")]
    public async Task<IActionResult> GetTypedValue(string name, [FromQuery] string type = "string")
    {
        try
        {
            dynamic value = type.ToLower() switch
            {
                "int" => await _service.GetTypedValueAsync<int>(name),
                "bool" => await _service.GetTypedValueAsync<bool>(name),
                "float" => await _service.GetTypedValueAsync<float>(name),
                "double" => await _service.GetTypedValueAsync<double>(name),
                "datetime" => await _service.GetTypedValueAsync<DateTime>(name),
                _ => await _service.GetTypedValueAsync<string>(name)
            };
            return Ok(new { Name = name, Value = value, Type = type });
        }
        catch
        {
            return BadRequest("Invalid type conversion");
        }
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var entity = await _service.GetByIdAsync(id);
        return Ok(entity);
    }

    [HttpPost]
    [RequirePermission(PermissionHelper.Permissions.Settings_Manage)]
    public async Task<IActionResult> Create([FromBody] AppSetting appSetting)
    {
        var entity = await _service.CreateAsync(appSetting);
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, entity);
    }

    [HttpPut("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Settings_Manage)]
    public async Task<IActionResult> Update(int id, [FromBody] AppSetting appSetting)
    {
        await _service.UpdateAsync(id, appSetting);
        return NoContent();
    }

    [HttpPut("{name}/value")]
    [RequirePermission(PermissionHelper.Permissions.Settings_Manage)]
    public async Task<IActionResult> UpdateValue(string name, [FromBody] UpdateValueRequest request)
    {
        await _service.UpdateByNameAsync(name, request.Value);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Settings_Manage)]
    public async Task<IActionResult> Delete(int id)
    {
        await _service.DeleteAsync(id);
        return NoContent();
    }
}

public record UpdateValueRequest(string Value);
