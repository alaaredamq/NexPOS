using Core.Entities.Base;

namespace Core.Entities.Users;

public class RolePermission : IEntityBase<int>
{
    public int Id { get; set; }
    public int RoleId { get; set; }
    public int PermissionId { get; set; }
    public bool IsGranted { get; set; } = true; // Allow for explicit denies

    // Present in the real database but missing here until A15's reconciliation - added to match.
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    #region Navigation Properties

    public Role Role { get; set; } = null!;
    public Permission Permission { get; set; } = null!;

    #endregion

    #region Constructors

    public RolePermission(int roleId, int permissionId, bool isGranted = true)
    {
        RoleId = roleId;
        PermissionId = permissionId;
        IsGranted = isGranted;
    }

    protected RolePermission() { }

    #endregion
}
