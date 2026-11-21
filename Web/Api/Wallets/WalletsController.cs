using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.Infrastructure;
using Web.Api.Wallets.Contracts;
using Web.Attributes;
using Web.Helpers;

namespace Web.Api.Wallets;

/// <summary>
/// The "Whish Wallet" page: the shop's Whish Money account, what it holds and every movement. Reading needs <c>PaymentMethods.View</c>;
/// setting it up, recording movements and ticking rows off need <c>PaymentMethods.Manage</c> (owner and manager by default, not a
/// cashier: a cashier only takes Whish payments at checkout). In every plan, and not on the selling path, so not behind
/// <c>[RequireSalesLicense]</c>.
/// </summary>
[Route("api/v1/wallets")]
public sealed class WalletsController : ApiControllerBase
{
    public const string WalletNotFound = "wallet_not_found";
    public const string WalletExists = "wallet_exists";

    private readonly IWalletApiService _wallets;

    public WalletsController(IWalletApiService wallets) => _wallets = wallets;

    /// <summary>The shop's wallets with their balances. Empty until Whish is set up.</summary>
    [HttpGet]
    [RequirePermission(PermissionHelper.Permissions.PaymentMethods_View)]
    [ProducesResponseType<IReadOnlyList<WalletDto>>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getWallets")]
    public async Task<IReadOnlyList<WalletDto>> GetAll() => await _wallets.GetAllAsync();

    [HttpGet("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.PaymentMethods_View)]
    [ProducesResponseType<WalletDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "getWallet")]
    public async Task<IActionResult> Get(int id) =>
        await _wallets.GetAsync(id) is { } wallet ? Ok(wallet) : NotFoundWallet();

    /// <summary>
    /// Sets up the Whish wallet, and the "Whish" payment method with it (an existing method of that name is used), so checkout offers
    /// Whish at once. 409 <c>wallet_exists</c> when it is already set up.
    /// </summary>
    [HttpPost("whish")]
    [RequirePermission(PermissionHelper.Permissions.PaymentMethods_Manage)]
    [ProducesResponseType<WalletDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "createWhishWallet")]
    public async Task<IActionResult> CreateWhish(CreateWalletRequest request)
    {
        try
        {
            var wallet = await _wallets.CreateWhishAsync(request, CurrentUserId);
            return Created($"/api/v1/wallets/{wallet.Id}", wallet);
        }
        catch (InvalidOperationException ex)
        {
            return ConflictFailure(WalletExists, ex.Message);
        }
    }

    /// <summary>Renames it, changes the shop's Whish number, or turns it off (sales stop landing in it; the history stays).</summary>
    [HttpPut("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.PaymentMethods_Manage)]
    [ProducesResponseType<WalletDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "updateWallet")]
    public async Task<IActionResult> Update(int id, UpdateWalletRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return ValidationFailure(nameof(request.Name), "Give the wallet a name.");
        return await _wallets.UpdateAsync(id, request) is { } wallet ? Ok(wallet) : NotFoundWallet();
    }

    /// <summary>
    /// The wallet's movements, newest first. <c>from</c> / <c>to</c> are local dates and include both days; <c>type</c> is one movement
    /// type; <c>status</c> is <c>checked</c> or <c>unchecked</c>; <c>q</c> searches the reference, the notes and the receipt number.
    /// </summary>
    [HttpGet("{id:int}/transactions")]
    [RequirePermission(PermissionHelper.Permissions.PaymentMethods_View)]
    [ProducesResponseType<PagedResult<WalletTransactionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "getWalletTransactions")]
    public async Task<IActionResult> Transactions(int id, [FromQuery] PagedQuery paging, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] string? type, [FromQuery] string? status, [FromQuery] int? currencyId)
    {
        if (await _wallets.GetAsync(id) is null) return NotFoundWallet();
        var filter = new WalletTransactionQuery { From = from, To = to, Type = type, Status = status, CurrencyId = currencyId };
        return Ok(await _wallets.GetTransactionsAsync(id, filter, paging));
    }

    /// <summary>Money moved by hand: a deposit, a withdrawal, a supplier paid, a fee, or a correction. Sales come from checkout only.</summary>
    [HttpPost("{id:int}/transactions")]
    [RequirePermission(PermissionHelper.Permissions.PaymentMethods_Manage)]
    [ProducesResponseType<WalletTransactionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "addWalletMovement")]
    public async Task<IActionResult> AddMovement(int id, WalletMovementRequest request)
    {
        try
        {
            var row = await _wallets.AddMovementAsync(id, request, CurrentUserId);
            return Created($"/api/v1/wallets/{id}/transactions/{row.Id}", row);
        }
        catch (KeyNotFoundException)
        {
            return NotFoundWallet();
        }
        catch (ArgumentException ex)
        {
            var message = ex.Message;
            var at = message.IndexOf(" (Parameter", StringComparison.Ordinal);
            return ValidationFailure(ex.ParamName ?? "amount", at > 0 ? message[..at] : message);
        }
    }

    /// <summary>Ticks rows off against the Whish statement (<c>checked: false</c> puts them back). Answers how many changed.</summary>
    [HttpPost("{id:int}/transactions/check")]
    [RequirePermission(PermissionHelper.Permissions.PaymentMethods_Manage)]
    [ProducesResponseType<WalletCheckResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "checkWalletTransactions")]
    public async Task<IActionResult> Check(int id, WalletCheckRequest request)
    {
        if (await _wallets.GetAsync(id) is null) return NotFoundWallet();
        return Ok(new WalletCheckResultDto { Changed = await _wallets.SetCheckedAsync(id, request, CurrentUserId) });
    }

    private ObjectResult NotFoundWallet() => Failure(StatusCodes.Status404NotFound, WalletNotFound, "That wallet does not exist.");
}
