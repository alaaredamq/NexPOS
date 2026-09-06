using Core.Entities.Base;

namespace Core.Entities.Users;

public class Role : ISSoftDeletable, IEntityBase<int>
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public bool IsSystemRole { get; set; } // Cannot be deleted if true

    #region Navigation Properties

    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();

    #endregion

    #region Constructors

    public Role(string name, string? description = null, bool isSystemRole = false)
    {
        Name = name;
        Description = description;
        IsActive = true;
        IsSystemRole = isSystemRole;
    }

    protected Role() { }

    #endregion
}
