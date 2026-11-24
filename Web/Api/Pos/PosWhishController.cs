using System.ComponentModel.DataAnnotations;
using Application.IServices.Sales;
using Common.Licensing;
using Core.Entities.Sales;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.Infrastructure;
using Web.Attributes;

namespace Web.Api.Pos;

/// <summary>
/// Online Whish payments at the till (Pro): send the customer a payment request, wait for Whish to confirm it, then check out with
/// <c>walletPaymentRequestId</c>. The till asks Whish itself (keys stay on the shop PC); poll <c>GET .../{id}</c> every few seconds while
/// waiting. Any signed-in cashier may use it, like checkout.
/// </summary>
[Route("api/v1/pos/whish/requests")]
[RequirePlan(ProFeature.OnlinePayments)]
public sealed class PosWhishController : PosControllerBase
{
    private readonly IWalletPaymentService _payments;

    public PosWhishController(IWalletPaymentService payments) => _payments = payments;

    /// <summary>
    /// Asks Whish for a payment link the customer opens to pay. Retry with the <b>same</b> <c>idempotencyKey</c> after a lost answer: no second
    /// request is sent. Refusals: 409 <c>whish_not_set_up</c>, 400 <c>whish_currency_not_accepted</c> (only USD and LBP), 502 <c>whish_error</c>
    /// (Whish said no or could not be reached; <c>detail</c> says why), 409 <c>idempotency_key_reused</c>.
    /// </summary>
    [HttpPost]
    [AnySignedInUser]
    [RequireSalesLicense]
    [ProducesResponseType<WalletPaymentRequestDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status502BadGateway, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "createWhishPaymentRequest")]
    public async Task<IActionResult> Create(CreateWalletPaymentRequest request)
    {
        try
        {
            var created = await _payments.RequestAsync(request.WalletId, request.Amount, request.CurrencyId, request.IdempotencyKey, CurrentUserId);
            return Created($"/api/v1/pos/whish/requests/{created.Id}", ToDto(created));
        }
        catch (WalletPaymentRefusedException ex)
        {
            var status = ex.Code switch
            {
                WalletPaymentCodes.WhishError => StatusCodes.Status502BadGateway,
                WalletPaymentCodes.CurrencyNotAccepted or WalletPaymentCodes.Mismatch => StatusCodes.Status400BadRequest,
                _ => StatusCodes.Status409Conflict,
            };
            return Failure(status, ex.Code, ex.Message);
        }
    }

    /// <summary>The request, after asking Whish for news while it is still waiting. Poll this every few seconds.</summary>
    [HttpGet("{id:int}")]
    [AnySignedInUser]
    [ProducesResponseType<WalletPaymentRequestDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "getWhishPaymentRequest")]
    public async Task<IActionResult> Get(int id) =>
        await _payments.RefreshAsync(id) is { } request ? Ok(ToDto(request)) : NotFoundRequest();

    /// <summary>
    /// The payment link as a QR code (SVG) for the customer to scan with their phone. Drawn on the till, so it works without any outside
    /// service. 404 while the request has no link (Whish refused it). Not cached: each request has its own link.
    /// </summary>
    [HttpGet("{id:int}/qr.svg")]
    [AnySignedInUser]
    [Produces("image/svg+xml")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "image/svg+xml")]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "getWhishPaymentRequestQr")]
    public async Task<IActionResult> Qr(int id)
    {
        // Read only: no need to ask Whish for news to draw the link.
        var request = await _payments.GetAsync(id);
        if (request?.CollectUrl is not { Length: > 0 } url) return NotFoundRequest();

        using var generator = new QRCoder.QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCoder.QRCodeGenerator.ECCLevel.M);
        var svg = new QRCoder.SvgQRCode(data).GetGraphic(8, "#000000", "#ffffff", drawQuietZones: true);
        Response.Headers.CacheControl = "no-store";
        return File(System.Text.Encoding.UTF8.GetBytes(svg), "image/svg+xml");
    }

    /// <summary>The cashier stops waiting. If the customer pays afterwards, the wallet page lists it as paid with no sale.</summary>
    [HttpPost("{id:int}/cancel")]
    [AnySignedInUser]
    [ProducesResponseType<WalletPaymentRequestDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "cancelWhishPaymentRequest")]
    public async Task<IActionResult> Cancel(int id) =>
        await _payments.CancelAsync(id) is { } request ? Ok(ToDto(request)) : NotFoundRequest();

    private ObjectResult NotFoundRequest() => Failure(StatusCodes.Status404NotFound, WalletPaymentCodes.NotFound, "That Whish payment request does not exist.");

    internal static WalletPaymentRequestDto ToDto(WalletPaymentRequest r) => new()
    {
        Id = r.Id,
        ExternalId = r.ExternalId.ToString(),
        Status = r.Status.ToString(),
        Amount = r.Amount,
        CurrencyId = r.CurrencyId,
        CurrencyCode = r.CurrencyCode,
        CurrencyNotation = r.Currency?.Notation ?? string.Empty,
        CollectUrl = r.CollectUrl,
        PayerPhone = r.PayerPhone,
        FailureMessage = r.FailureMessage,
        OrderId = r.OrderId,
        CreatedAt = r.CreatedAt,
        CompletedAt = r.CompletedAt,
    };
}

public sealed class CreateWalletPaymentRequest
{
    /// <summary>The wallet (<c>walletId</c> of the payment method).</summary>
    public int WalletId { get; set; }

    /// <summary>What the customer pays, in <see cref="CurrencyId"/>: the sale's total.</summary>
    [Range(0.01, 1_000_000_000)]
    public decimal Amount { get; set; }

    public int CurrencyId { get; set; }

    /// <summary>A fresh value per attempt; resend the same one to retry.</summary>
    [Required, StringLength(64, MinimumLength = 8)]
    public string IdempotencyKey { get; set; } = string.Empty;
}

/// <summary>A payment request sent to Whish. <see cref="Status"/>: <c>Pending</c>, <c>Paid</c>, <c>Failed</c> or <c>Cancelled</c>.</summary>
public sealed record WalletPaymentRequestDto
{
    public required int Id { get; init; }

    /// <summary>Our number for it at Whish (a string: it is too long for some JSON readers). Printed on the receipt.</summary>
    public required string ExternalId { get; init; }

    public required string Status { get; init; }

    public required decimal Amount { get; init; }

    public required int CurrencyId { get; init; }

    public required string CurrencyCode { get; init; }

    public required string CurrencyNotation { get; init; }

    /// <summary>The page the customer opens to pay (shown as a QR code). Null if Whish refused.</summary>
    public string? CollectUrl { get; init; }

    public string? PayerPhone { get; init; }

    public string? FailureMessage { get; init; }

    /// <summary>The sale it paid for, once used.</summary>
    public int? OrderId { get; init; }

    public required DateTime CreatedAt { get; init; }

    public DateTime? CompletedAt { get; init; }
}
