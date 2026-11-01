using Application.IServices.Reports;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.Customers.Contracts;
using Web.Api.Infrastructure;
using Web.Attributes;
using Web.Helpers;

namespace Web.Api.Customers;

/// <summary>Customers and their debt ledger: list, details, create, edit, delete, CSV export, manual debt entries. Mirrors the Razor <c>CustomersController</c>.</summary>
[Route("api/v1/customers")]
public sealed class CustomersController : ApiControllerBase
{
    private readonly ICustomerApiService _customers;
    private readonly IExportService _export;

    public CustomersController(ICustomerApiService customers, IExportService export)
    {
        _customers = customers;
        _export = export;
    }

    /// <summary>
    /// One page of customers. <c>sort</c>: <c>name</c> (default), <c>email</c>, <c>phoneNumber</c>, <c>address</c>, <c>ordersCount</c>,
    /// <c>totalSpent</c>, <c>balance</c>, <c>isActive</c>; <c>q</c> matches the name, email and phone number.
    /// </summary>
    [HttpGet]
    [RequirePermission(PermissionHelper.Permissions.Customers_View)]
    [ProducesResponseType<PagedResult<CustomerDto>>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "listCustomers")]
    public async Task<PagedResult<CustomerDto>> List([FromQuery] PagedQuery query) => await _customers.ListAsync(query);

    /// <summary>Totals for the tiles above the list (all customers, not just the current page).</summary>
    [HttpGet("summary")]
    [RequirePermission(PermissionHelper.Permissions.Customers_View)]
    [ProducesResponseType<CustomerSummaryDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getCustomerSummary")]
    public async Task<CustomerSummaryDto> Summary() => await _customers.SummaryAsync();

    /// <summary>The customer, the figures of the details page and the 10 latest orders.</summary>
    [HttpGet("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Customers_View)]
    [ProducesResponseType<CustomerDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "getCustomer")]
    public async Task<ActionResult<CustomerDetailsDto>> Get(int id)
        => await _customers.GetDetailsAsync(id) is { } details ? details : NotFound();

    /// <summary>Creates a customer. Only the first and last name are required.</summary>
    [HttpPost]
    [RequirePermission(PermissionHelper.Permissions.Customers_Create)]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "createCustomer")]
    public async Task<ActionResult<CustomerDto>> Create(CustomerRequest request)
    {
        try
        {
            var created = await _customers.CreateAsync(request);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }
        catch (CustomerRuleException ex)
        {
            return ValidationFailure(ex.Field, ex.Message);
        }
    }

    /// <summary>Changes the contact details and the active flag. The balance is not editable here: it only moves through the debt ledger.</summary>
    [HttpPut("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Customers_Edit)]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "updateCustomer")]
    public async Task<ActionResult<CustomerDto>> Update(int id, CustomerRequest request)
    {
        try
        {
            return await _customers.UpdateAsync(id, request) is { } updated ? updated : NotFound();
        }
        catch (CustomerRuleException ex)
        {
            return ValidationFailure(ex.Field, ex.Message);
        }
    }

    /// <summary>Deletes (soft) a customer whose balance is zero. 409 <c>customer_has_balance</c> otherwise: settle the debt or deactivate the customer.</summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Customers_Delete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "deleteCustomer")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            return await _customers.DeleteAsync(id) ? NoContent() : NotFound();
        }
        catch (CustomerHasBalanceException ex)
        {
            return ConflictFailure("customer_has_balance", ex.Message);
        }
    }

    /// <summary>The customer's debt ledger, newest first. Same <c>page</c> / <c>pageSize</c> as the lists; <c>sort</c> and <c>q</c> are ignored.</summary>
    [HttpGet("{id:int}/debt-transactions")]
    [RequirePermission(PermissionHelper.Permissions.Customers_View)]
    [ProducesResponseType<PagedResult<CustomerDebtTransactionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "listCustomerDebtTransactions")]
    public async Task<ActionResult<PagedResult<CustomerDebtTransactionDto>>> DebtTransactions(int id, [FromQuery] PagedQuery query)
        => await _customers.ListDebtTransactionsAsync(id, query) is { } page ? page : NotFound();

    /// <summary>
    /// Records a manual debt entry (staff editing what a customer owes, or logging a payment) and returns the new balance.
    /// There is no cap: paying more than is owed leaves a negative balance (the business owes the customer), as on the Razor page.
    /// </summary>
    [HttpPost("{id:int}/debt-transactions")]
    [RequirePermission(PermissionHelper.Permissions.Customers_Edit)]
    [ProducesResponseType<CustomerDebtResultDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "addCustomerDebtTransaction")]
    public async Task<ActionResult<CustomerDebtResultDto>> AddDebtTransaction(int id, CustomerDebtRequest request)
    {
        var result = await _customers.AddDebtTransactionAsync(id, CurrentUserId, request);
        return result is null ? NotFound() : StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>All customers as a CSV file (the same export the Razor page offers).</summary>
    [HttpGet("export")]
    [RequirePermission(PermissionHelper.Permissions.Customers_View)]
    [Produces("text/csv")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, "text/csv")]
    [SwaggerOperation(OperationId = "exportCustomers")]
    public async Task<IActionResult> Export()
    {
        var csv = await _export.ExportCustomersToCsvAsync();
        return File(csv, "text/csv", $"customers_export_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
    }
}
