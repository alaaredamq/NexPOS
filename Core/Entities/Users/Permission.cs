using Core.Entities.Base;

namespace Core.Entities.Users;

public class Permission : ISSoftDeletable, IEntityBase<int>
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string Category { get; set; } = null!; // e.g., "Products", "Users", "Reports"
    public string Action { get; set; } = null!; // e.g., "Create", "Read", "Update", "Delete"
    public string? Resource { get; set; } // Optional specific resource

    #region Navigation Properties

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
    public ICollection<UserPermission> UserPermissions { get; set; } = new List<UserPermission>();

    #endregion

    #region Constructors

    public Permission(string name, string category, string action, string? description = null, string? resource = null)
    {
        Name = name;
        Category = category;
        Action = action;
        Description = description;
        Resource = resource;
    }

    protected Permission() { }

    #endregion

    #region Methods

    public string GetFullPermissionName()
    {
        return string.IsNullOrEmpty(Resource) 
            ? $"{Category}.{Action}" 
            : $"{Category}.{Action}.{Resource}";
    }

    #endregion
}
