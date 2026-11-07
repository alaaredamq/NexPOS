namespace Cloud.Data;

/// <summary>
/// A shop owner (or a person the owner trusts) who signs in to the phone app. Belongs to exactly one client and can only
/// ever reach that client's shop. Separate from <see cref="AdminUser"/>, which is you.
/// </summary>
public class OwnerUser
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;
    /// <summary>Lower-case; the sign-in name. Unique across all clients.</summary>
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSignInAt { get; set; }
}

/// <summary>One signed-in phone. Only the token's hash is stored, so a leaked control database cannot be used to sign in.</summary>
public class OwnerSession
{
    public int Id { get; set; }
    public int OwnerUserId { get; set; }
    public OwnerUser OwnerUser { get; set; } = null!;
    public string TokenHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

public enum CommandStatus
{
    /// <summary>Waiting for the shop PC to ask.</summary>
    Queued = 0,
    /// <summary>Handed to the shop PC; no answer yet.</summary>
    Delivered = 1,
    Applied = 2,
    /// <summary>The shop PC refused it (see the result code). Final.</summary>
    Rejected = 3,
    /// <summary>The owner withdrew it before the shop PC took it.</summary>
    Discarded = 4,
}

/// <summary>
/// One change the owner asked for from the phone, waiting for (or finished with) the shop PC. The cloud never edits the
/// shop's data itself: the shop PC is the single source of truth, so there is nothing to reconcile.
/// </summary>
public class SiteCommand
{
    public Guid Id { get; set; }
    public Guid SiteId { get; set; }
    public Site Site { get; set; } = null!;
    /// <summary>For example <c>customer.create</c>.</summary>
    public string Type { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    /// <summary>Chosen by the phone. Sending the same key twice for one shop returns the first command, so a retry on bad mobile data cannot add something twice.</summary>
    public string IdempotencyKey { get; set; } = "";
    public int OwnerUserId { get; set; }
    public string RequestedBy { get; set; } = "";
    public CommandStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ResultCode { get; set; }
    public string? ResultMessage { get; set; }
    public string? ResultDataJson { get; set; }
}
