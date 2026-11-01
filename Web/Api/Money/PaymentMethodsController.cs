using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.Infrastructure;
using Web.Api.Money.Contracts;
using Web.Attributes;
using Web.Helpers;

namespace Web.Api.Money;

/// <summary>Payment methods of the till. Permissions as the legacy <c>PaymentMethodsController</c>: <c>PaymentMethods.View</c> to read, <c>PaymentMethods.Manage</c> to change.</summary>
[Route("api/v1/payment-methods")]
[RequirePermission(PermissionHelper.Permissions.PaymentMethods_View)]
public sealed class PaymentMethodsController : MoneyControllerBase
{
    private readonly IPaymentMethodApiService _methods;

    public PaymentMethodsController(IPaymentMethodApiService methods) => _methods = methods;

    /// <summary>One page of payment methods. <c>sort</c>: <c>name</c> (default), <c>isDefault</c>, <c>createdAt</c>; <c>q</c> matches the name. Includes the system "Customer Debt" method (<c>isSystem</c>).</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<PaymentMethodItemDto>>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "listPaymentMethods")]
    public async Task<PagedResult<PaymentMethodItemDto>> List([FromQuery] PagedQuery query) => await _methods.ListAsync(query);

    /// <summary>One payment method.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<PaymentMethodItemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "getPaymentMethod")]
    public async Task<ActionResult<PaymentMethodItemDto>> Get(int id) => await _methods.GetAsync(id) is { } found ? found : NotFound();

    /// <summary>Creates a payment method. A taken name (or "Customer Debt") answers 400 <c>validation_failed</c> on <c>name</c>; <c>isDefault</c> takes the default away from the previous one.</summary>
    [HttpPost]
    [RequirePermission(PermissionHelper.Permissions.PaymentMethods_Manage)]
    [ProducesResponseType<PaymentMethodItemDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "createPaymentMethod")]
    public Task<ActionResult<PaymentMethodItemDto>> Create(PaymentMethodRequest request) => GuardCreated(() => _methods.CreateAsync(request), m => m.Id);

    /// <summary>Changes a payment method. 409 <c>payment_method_system</c> for "Customer Debt".</summary>
    [HttpPut("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.PaymentMethods_Manage)]
    [ProducesResponseType<PaymentMethodItemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "updatePaymentMethod")]
    public Task<ActionResult<PaymentMethodItemDto>> Update(int id, PaymentMethodRequest request) => Guard(() => _methods.UpdateAsync(id, request));

    /// <summary>Deletes a payment method. 409 <c>payment_method_system</c> for "Customer Debt"; 409 <c>payment_method_in_use</c> once receipts were paid with it.</summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.PaymentMethods_Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "deletePaymentMethod")]
    public Task<IActionResult> Delete(int id) => Done(() => _methods.DeleteAsync(id));
}
