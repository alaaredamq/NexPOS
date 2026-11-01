using System.ComponentModel.DataAnnotations;

namespace Web.Api.Roles.Contracts;

/// <summary>A role as the roles table lists it.</summary>
public sealed record RoleDto
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Shipped with the app: cannot be deleted.</summary>
    public required bool IsSystemRole { get; init; }

    /// <summary>The one role that implicitly holds every permission: it can be neither edited nor deleted.</summary>
    public required bool IsSuperAdmin { get; init; }

    /// <summary>Users holding the role; above zero it cannot be deleted.</summary>
    public required int UserCount { get; init; }

    /// <summary>How many permissions are granted (the whole catalogue for SuperAdmin).</summary>
    public required int PermissionCount { get; init; }

    /// <summary>UTC.</summary>
    public required DateTime CreatedAt { get; init; }
}

/// <summary>A role with its grants, for the permission matrix.</summary>
public sealed record RoleDetailsDto
{
    public required RoleDto Role { get; init; }

    /// <summary>Granted permission keys (<c>Products.View</c>), in catalogue order. The catalogue is generated into <c>permissions.ts</c>.</summary>
    public required IReadOnlyList<string> Permissions { get; init; }
}

/// <summary>Body of create and update. The editor always sends the complete tick state: the stored grants are made to match it.</summary>
public sealed record RoleRequest
{
    /// <summary>Trimmed; unique without regard to case.</summary>
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public required string Name { get; init; }

    [StringLength(500)]
    public string? Description { get; init; }

    /// <summary>Permission keys to grant. Unknown keys are dropped.</summary>
    public IReadOnlyList<string>? Permissions { get; init; }
}
