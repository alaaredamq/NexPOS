using System.Globalization;
using Application.IServices.Reports;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.Infrastructure;
using Web.Api.Suppliers.Contracts;
using Web.Attributes;
using Web.Helpers;

namespace Web.Api.Suppliers;

/// <summary>Suppliers: list, details, create, edit, delete, the debt ledger and CSV export. Mirrors the Razor <c>SuppliersController</c>.</summary>
[Route("api/v1/suppliers")]
[SwaggerTag("Suppliers")]
public sealed class SuppliersController : ApiControllerBase
{
    private readonly ISupplierApiService _suppliers;
    private readonly IExportService _export;
    private readonly IStringLocalizer<SharedResource> _text;

    public SuppliersController(ISupplierApiService suppliers, IExportService export, IStringLocalizer<SharedResource> text)
    {
        _suppliers = suppliers;
        _export = export;
        _text = text;
    }

    /// <summary>One page of suppliers. <c>sort</c>: <c>name</c> (default), <c>email</c>, <c>phone</c>, <c>balance</c>, <c>isActive</c>, <c>discount</c>, <c>products</c>; <c>q</c> matches name, email and phone.</summary>
    [HttpGet]
    [RequirePermission(PermissionHelper.Permissions.Suppliers_View)]
    [ProducesResponseType<PagedResult<SupplierDto>>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "listSuppliers")]
    public async Task<PagedResult<SupplierDto>> List([FromQuery] PagedQuery query) => await _suppliers.ListAsync(query);

    /// <summary>Totals for the tiles above the list (all suppliers, not just the current page).</summary>
    [HttpGet("summary")]
    [RequirePermission(PermissionHelper.Permissions.Suppliers_View)]
    [ProducesResponseType<SupplierSummaryDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getSupplierSummary")]
    public async Task<SupplierSummaryDto> Summary() => await _suppliers.SummaryAsync();

    /// <summary>The currencies the supplier form offers, and the one a new supplier starts with.</summary>
    [HttpGet("options")]
    [RequirePermission(PermissionHelper.Permissions.Suppliers_View)]
    [ProducesResponseType<SupplierOptionsDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getSupplierOptions")]
    public async Task<SupplierOptionsDto> Options() => await _suppliers.OptionsAsync();

    /// <summary>One supplier with its balance.</summary>
    [HttpGet("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Suppliers_View)]
    [ProducesResponseType<SupplierDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "getSupplier")]
    public async Task<ActionResult<SupplierDto>> Get(int id) => await _suppliers.GetAsync(id) is { } supplier ? supplier : NotFound();

    /// <summary>Creates a supplier with a zero balance.</summary>
    [HttpPost]
    [RequirePermission(PermissionHelper.Permissions.Suppliers_Create)]
    [ProducesResponseType<SupplierDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "createSupplier")]
    public async Task<ActionResult<SupplierDto>> Create(SupplierRequest request)
    {
        try
        {
            var created = await _suppliers.CreateAsync(request);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }
        catch (SupplierCurrencyException)
        {
            return ValidationFailure(nameof(SupplierRequest.CurrencyId), _text["Sup_CurrencyInvalid"].Value);
        }
    }

    /// <summary>Edits a supplier. The balance is not editable here: use the debt ledger.</summary>
    [HttpPut("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Suppliers_Edit)]
    [ProducesResponseType<SupplierDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "updateSupplier")]
    public async Task<ActionResult<SupplierDto>> Update(int id, SupplierRequest request)
    {
        try
        {
            return await _suppliers.UpdateAsync(id, request) is { } updated ? updated : NotFound();
        }
        catch (SupplierCurrencyException)
        {
            return ValidationFailure(nameof(SupplierRequest.CurrencyId), _text["Sup_CurrencyInvalid"].Value);
        }
    }

    /// <summary>Deletes a supplier. 409 <c>supplier_has_balance</c> while money is owed either way; 409 <c>supplier_in_use</c> while products (even deleted ones) or ledger rows point at it.</summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Suppliers_Delete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "deleteSupplier")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            return await _suppliers.DeleteAsync(id) ? NoContent() : NotFound();
        }
        catch (SupplierHasBalanceException ex)
        {
            var amount = $"{ex.Notation}{ex.Balance.ToString("N2", CultureInfo.InvariantCulture)}";
            return ConflictFailure("supplier_has_balance", _text["Sup_HasBalance", amount].Value);
        }
        catch (SupplierInUseException)
        {
            return ConflictFailure("supplier_in_use", _text["Sup_InUse"].Value);
        }
    }

    /// <summary>The supplier's debt ledger, newest first.</summary>
    [HttpGet("{id:int}/debt-transactions")]
    [RequirePermission(PermissionHelper.Permissions.Suppliers_View)]
    [ProducesResponseType<PagedResult<SupplierDebtEntryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "listSupplierDebtTransactions")]
    public async Task<ActionResult<PagedResult<SupplierDebtEntryDto>>> Ledger(int id, [FromQuery] PagedQuery query)
        => await _suppliers.LedgerAsync(id, query) is { } page ? page : NotFound();

    /// <summary>Records a manual debt entry: the business owes more, or pays something off.</summary>
    [HttpPost("{id:int}/debt-transactions")]
    [RequirePermission(PermissionHelper.Permissions.Suppliers_Edit)]
    [ProducesResponseType<SupplierDebtResultDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "addSupplierDebtTransaction")]
    public async Task<ActionResult<SupplierDebtResultDto>> AddDebt(int id, SupplierDebtRequest request)
    {
        if (CurrentUserId == 0) return Unauthorized();
        if (await _suppliers.AddDebtAsync(id, CurrentUserId, request) is not { } result) return NotFound();
        return Created($"/api/v1/suppliers/{id}/debt-transactions", result);
    }

    /// <summary>All suppliers as a CSV file (the same export the Razor page offers).</summary>
    [HttpGet("export")]
    [RequirePermission(PermissionHelper.Permissions.Suppliers_View)]
    [Produces("text/csv")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, "text/csv")]
    [SwaggerOperation(OperationId = "exportSuppliers")]
    public async Task<IActionResult> Export()
    {
        var csv = await _export.ExportSuppliersToCsvAsync();
        return File(csv, "text/csv", $"suppliers_export_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
    }
}
