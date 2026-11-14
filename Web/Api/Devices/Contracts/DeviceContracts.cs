using System.ComponentModel.DataAnnotations;

namespace Web.Api.Devices.Contracts;

/// <summary>One till in the list on the License page.</summary>
public sealed record TillDeviceDto
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    /// <summary><c>Pending</c>: waiting for the till to enter its pairing code (holds a seat until the code expires). <c>Active</c>: paired.</summary>
    public required string State { get; init; }

    public required DateTime CreatedAt { get; init; }

    public DateTime? PairedAt { get; init; }

    /// <summary>Last time the till sold or asked to (updated every few minutes at most).</summary>
    public DateTime? LastSeenAt { get; init; }

    /// <summary>Pending only: when the pairing code stops working.</summary>
    public DateTime? PairingExpiresAt { get; init; }

    /// <summary>Active, but the licence now covers fewer PCs than are registered: this till is refused until a seat is freed or a bigger key is installed.</summary>
    public required bool Overflow { get; init; }
}

/// <summary>The package: how many PCs the licence covers and which tills use them. The store server itself is always the first seat.</summary>
public sealed record TillSeatsDto
{
    /// <summary>PCs the licence covers, the store server included.</summary>
    public required int Seats { get; init; }

    /// <summary>Seats for tills (<see cref="Seats"/> minus the server). Null in a build that does not enforce licences (no limit).</summary>
    public int? TillSeats { get; init; }

    /// <summary>Tills that hold a seat: paired ones plus pairing codes still open.</summary>
    public required int Used { get; init; }

    /// <summary>False in a Debug build.</summary>
    public required bool Enforced { get; init; }

    public required IReadOnlyList<TillDeviceDto> Devices { get; init; }
}

public sealed record PairingCodeRequest
{
    /// <summary>What to call the till ("Till 2", "Back counter").</summary>
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public required string Name { get; init; }
}

public sealed record PairingCodeDto
{
    public required int DeviceId { get; init; }

    /// <summary>Shown once, as <c>XXXX-XXXX</c>. Type it on the till; valid for 15 minutes, once.</summary>
    public required string Code { get; init; }

    public required DateTime ExpiresAt { get; init; }
}

public sealed record PairRequest
{
    [Required]
    [StringLength(32, MinimumLength = 1)]
    public required string Code { get; init; }
}

public sealed record PairedDto
{
    /// <summary>The name the administrator gave this till.</summary>
    public required string Name { get; init; }
}

/// <summary>What the PC asking is, so the shell can show the pairing screen instead of a dead POS.</summary>
public sealed record DeviceStatusDto
{
    /// <summary>True for the store server's own PC (it never needs pairing).</summary>
    public required bool IsServer { get; init; }

    /// <summary><c>Allowed</c>, <c>NotRegistered</c> or <c>SeatsExceeded</c>.</summary>
    public required string Access { get; init; }
}
