using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.Infrastructure;
using Web.Api.Live.Contracts;

namespace Web.Api.Live;

/// <summary>What every open screen asks, a few seconds apart, to find out whether another PC changed anything.</summary>
[Route("api/v1/live")]
[SwaggerTag("Live")]
public sealed class LiveController : ApiControllerBase
{
    private readonly DataVersion _version;

    public LiveController(DataVersion version)
    {
        _version = version;
    }

    /// <summary>
    /// The current data version (see <see cref="DataVersion"/>). A different value than last time means a sale, a stock change or an
    /// edit happened somewhere (this PC included) and the screen should reload its numbers. Cheap on purpose: no database access.
    /// </summary>
    [HttpGet("version")]
    [AnySignedInUser]
    [ProducesResponseType<LiveVersionDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getLiveVersion")]
    public LiveVersionDto Version()
    {
        Response.Headers.CacheControl = "no-store";
        return new LiveVersionDto { Version = _version.Version };
    }
}
