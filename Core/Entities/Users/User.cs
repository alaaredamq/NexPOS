using Core.Entities.Base;

namespace Core.Entities.Users;

public class User : ISSoftDeletable, IEntityBase<int>
{
    public string Username { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    /// <summary>
    /// BCrypt hash of the user's numeric PIN. Null until an admin or the user themselves sets one -
    /// PIN login always fails for a user with no PIN set.
    /// </summary>
    public string? PinHash { get; set; }
    /// <summary>
    /// Forces a PIN change immediately after the next successful PIN login (set when an admin
    /// resets a PIN, or seeded true for the default admin account).
    /// </summary>
    public bool MustChangePin { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public bool IsActive { get; set; }
    public bool MustChangePassword { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime? PasswordChangedAt { get; set; }
    public int RoleId { get; set; }

    #region Navigation Properties

    public Role Role { get; set; } = null!;
    public ICollection<UserSession> Sessions { get; set; } = new List<UserSession>();
    public ICollection<UserPermission> UserPermissions { get; set; } = new List<UserPermission>();

    #endregion

    #region Constructors

    public User(string username, string email, string passwordHash, int roleId, string? firstName = null, string? lastName = null)
    {
        Username = username;
        Email = email;
        PasswordHash = passwordHash;
        RoleId = roleId;
        FirstName = firstName;
        LastName = lastName;
        IsActive = true;
        MustChangePassword = false;
    }

    protected User() { }

    #endregion

    #region Methods

    public string GetFullName()
    {
        if (!string.IsNullOrEmpty(FirstName) && !string.IsNullOrEmpty(LastName))
            return $"{FirstName} {LastName}";
        return Username;
    }

    public void UpdateLastLogin()
    {
        LastLoginAt = DateTime.UtcNow;
    }

    public void ChangePassword(string newPasswordHash)
    {
        PasswordHash = newPasswordHash;
        PasswordChangedAt = DateTime.UtcNow;
        MustChangePassword = false;
    }

    #endregion
}
