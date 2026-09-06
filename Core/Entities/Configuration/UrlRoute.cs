using Core.Entities.Base;

namespace Core.Entities.Configuration;

public class UrlRoute : BaseEntity, IEntityBase<int>
{
    public string Key { get; set; } = string.Empty; // Unique identifier for the route
    public string Pattern { get; set; } = string.Empty; // URL pattern like "/{lang}/account/login"
    public string Controller { get; set; } = string.Empty; // Controller name
    public string Action { get; set; } = string.Empty; // Action name
    public string? Area { get; set; } // Optional area
    // public Dictionary<string, object>? DefaultValues { get; set; } // Default route values - Temporarily disabled
    public int DisplayOrder { get; set; } = 0; // Order for display
    public bool IsActive { get; set; } = true; // Whether route is active
    public string? Description { get; set; } // Description of the route
    public bool RequiresAuthentication { get; set; } = false; // Whether route requires auth
    public string? RequiredPermission { get; set; } // Required permission if any
    public string? RequiredRole { get; set; } // Required role if any

    // Present in the real database but missing here until A15's reconciliation - added to match.
    public bool IsDeleted { get; set; }
}
