using Core.Entities.Base;

namespace Core.Entities.Users;

public class UserPermission : IEntityBase<int>
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int PermissionId { get; set; }
    public bool IsGranted { get; set; } = true; // Allow for explicit denies
    public DateTime? ExpiresAt { get; set; } // Optional expiration

    // Present in the real database but missing here until A15's reconciliation - added to match.
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    #region Navigation Properties

    public User User { get; set; } = null!;
    public Permission Permission { get; set; } = null!;

    #endregion

    #region Constructors

    public UserPermission(int userId, int permissionId, bool isGranted = true, DateTime? expiresAt = null)
    {
        UserId = userId;
        PermissionId = permissionId;
        IsGranted = isGranted;
        ExpiresAt = expiresAt;
    }

    protected UserPermission() { }

    #endregion

    #region Methods

    public bool IsExpired()
    {
        return ExpiresAt.HasValue && DateTime.UtcNow > ExpiresAt.Value;
    }

    #endregion
}
