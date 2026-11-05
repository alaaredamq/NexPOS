using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.Infrastructure;
using Web.Api.Orders.Contracts;
using Web.Api.Pos;
using Web.Attributes;
using Web.Helpers;

namespace Web.Api.Orders;

/// <summary>Orders: list, tiles, details, status change (cancelling reverses the sale) and CSV export.</summary>
/// <remarks>
/// Port of the old <c>OrdersController</c>: the whole controller needed Orders.View, changing a status Orders.Edit.
/// Reprinting an order's receipt and its PDF are <c>pos/sales/{orderId}/print</c> and <c>pos/sales/{orderId}/receipt.pdf</c> (P1): same service, same permission.
/// </remarks>
[Route("api/v1/orders")]
[SwaggerTag("Orders")]
public sealed class OrdersController : PosControllerBase
{
    private readonly IOrderQueryService _orders;

    public OrdersController(IOrderQueryService orders) => _orders = orders;

    /// <summary>One page of the orders list, newest first.</summary>
    /// <remarks>
    /// <c>q</c> matches the order number, the customer's name or the total. <c>sort</c> is one of <c>date</c> (default, descending),
    /// <c>number</c>, <c>customer</c>, <c>total</c>, <c>status</c>, <c>items</c>. <c>from</c> / <c>to</c> are local days (yyyy-MM-dd).
    /// </remarks>
    [HttpGet]
    [RequirePermission(PermissionHelper.Permissions.Orders_View)]
    [SwaggerOperation(OperationId = "listOrders")]
    [ProducesResponseType<PagedResult<OrderListItemDto>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<OrderListItemDto>> List([FromQuery] PagedQuery query, [FromQuery] OrderListQuery filter)
        => await _orders.ListAsync(query, filter);

    /// <summary>The tiles above the list: counts by status and revenue per currency, for the last 30 days.</summary>
    [HttpGet("summary")]
    [RequirePermission(PermissionHelper.Permissions.Orders_View)]
    [SwaggerOperation(OperationId = "getOrdersSummary")]
    [ProducesResponseType<OrderSummaryDto>(StatusCodes.Status200OK)]
    public async Task<OrderSummaryDto> Summary() => await _orders.SummaryAsync();

    /// <summary>The filtered list (every page, not just the visible one) as a CSV.</summary>
    [HttpGet("export")]
    [RequirePermission(PermissionHelper.Permissions.Orders_View)]
    [SwaggerOperation(OperationId = "exportOrders")]
    [Produces("text/csv", "application/json")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "text/csv")]
    public async Task<IActionResult> Export([FromQuery] PagedQuery query, [FromQuery] OrderListQuery filter)
        => File(await _orders.ExportAsync(query, filter), "text/csv", $"orders_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

    /// <summary>One order with its lines, totals and receipts.</summary>
    [HttpGet("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Orders_View)]
    [SwaggerOperation(OperationId = "getOrder")]
    [ProducesResponseType<OrderDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    public async Task<OrderDetailDto> Get(int id) => await _orders.GetAsync(id);

    /// <summary>Changes the order's status. <c>4</c> (Cancelled) reverses the whole sale.</summary>
    /// <remarks>
    /// Cancelling voids the receipt, puts the stock back, takes a debt sale off the customer's account and stops the open drawer expecting the money - all or nothing.
    /// Any other status only changes the label (Returned does <b>not</b> restore stock). 409 <c>order_closed</c>: already cancelled or returned, nothing to cancel;
    /// 409 <c>order_cancelled</c>: a cancelled order cannot be reopened.
    /// </remarks>
    [HttpPut("{id:int}/status")]
    [RequirePermission(PermissionHelper.Permissions.Orders_Edit)]
    [SwaggerOperation(OperationId = "updateOrderStatus")]
    [ProducesResponseType<OrderStatusResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, ApiProblem.ContentType)]
    public async Task<OrderStatusResultDto> UpdateStatus(int id, [FromBody] OrderStatusRequest request)
        => await _orders.UpdateStatusAsync(id, request.Status, CurrentUserId);

    /// <summary>Changes the order's customer (null = walk-in). The order's receipts follow it, and a sale on the customer's account moves its debt along.</summary>
    /// <remarks>
    /// Same operation as <c>PUT receipts/{id}/customer</c>. 409 <c>order_closed</c>: cancelled or returned orders keep their customer;
    /// 409 <c>customer_not_found</c>; 400 <c>debt_requires_customer</c>: a sale on a customer's account cannot go back to walk-in.
    /// </remarks>
    [HttpPut("{id:int}/customer")]
    [RequirePermission(PermissionHelper.Permissions.Sales_ChangeCustomer)]
    [SwaggerOperation(OperationId = "updateOrderCustomer")]
    [ProducesResponseType<SaleCustomerResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, ApiProblem.ContentType)]
    public async Task<SaleCustomerResultDto> UpdateCustomer(int id, [FromBody] SaleCustomerRequest request)
        => await _orders.ChangeCustomerAsync(id, request.CustomerId, CurrentUserId);
}
