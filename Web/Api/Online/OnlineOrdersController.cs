using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Sync;
using Web.Api.Infrastructure;
using Web.Attributes;

namespace Web.Api.Online;

/// <summary>What the till shows of the online orders: whether this PC can receive them, and the open ones for its branch.</summary>
public sealed record OnlineOrdersDto(bool Connected, bool Reachable, IReadOnlyList<TillOrder> Orders);

public sealed record OnlineOrderMoveRequest(string Status);

/// <summary>
/// Orders guests sent from the shop's online menu to this till (the owner chose "send to the POS" in the menu editor). The cashier sees
/// them on the POS screen, loads one into the sale, and marks it ready or done; the guest's phone follows. They live in the cloud, so
/// this asks it each time (<see cref="MenuOrderInbox"/>). Open to everyone at the till, like the POS screen itself.
/// </summary>
[Route("api/v1/online-orders")]
[AnySignedInUser]
public sealed class OnlineOrdersController : ApiControllerBase
{
    private static readonly string[] Moves = ["accepted", "ready", "done", "rejected"];
    private readonly MenuOrderInbox _inbox;

    public OnlineOrdersController(MenuOrderInbox inbox) => _inbox = inbox;

    /// <summary>The open orders, oldest first. Not connected to Online, or the cloud out of reach: an empty list that says so.</summary>
    [HttpGet]
    [ProducesResponseType<OnlineOrdersDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getOnlineOrders")]
    public async Task<OnlineOrdersDto> Get(CancellationToken ct)
    {
        if (!_inbox.Connected) return new OnlineOrdersDto(false, false, []);
        try { return new OnlineOrdersDto(true, true, await _inbox.OpenAsync(ct)); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return new OnlineOrdersDto(true, false, []); }
    }

    /// <summary>Moves an order on: accepted (being made), ready, done, or rejected (the guest is told it could not be taken).</summary>
    [HttpPut("{id:int}/status")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "moveOnlineOrder")]
    public async Task<IActionResult> Move(int id, OnlineOrderMoveRequest request, CancellationToken ct)
    {
        if (!Moves.Contains(request.Status)) return ValidationFailure(nameof(request.Status), "Accepted, ready, done or rejected.");
        try { return await _inbox.MoveAsync(id, request.Status, ct) ? NoContent() : NotFound(); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Failure(StatusCodes.Status503ServiceUnavailable, "cloud_unreachable", "The cloud cannot be reached. Check the internet and try again.");
        }
    }
}
