using Core.Entities.Base;

namespace Core.Entities.Users;

public class UserSession : ISSoftDeletable, IEntityBase<int>
{
    public int UserId { get; set; }
    public string SessionToken { get; set; } = null!;
    public string? RefreshToken { get; set; }
    public new DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? LastAccessedAt { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public bool IsActive { get; set; }

    #region Navigation Properties

    public User User { get; set; } = null!;

    #endregion

    #region Constructors

    public UserSession(int userId, string sessionToken, DateTime expiresAt, string? ipAddress = null, string? userAgent = null, string? refreshToken = null)
    {
        UserId = userId;
        SessionToken = sessionToken;
        RefreshToken = refreshToken;
        CreatedAt = DateTime.UtcNow;
        ExpiresAt = expiresAt;
        LastAccessedAt = DateTime.UtcNow;
        IpAddress = ipAddress;
        UserAgent = userAgent;
        IsActive = true;
    }

    protected UserSession() { }

    #endregion

    #region Methods

    public bool IsExpired()
    {
        return DateTime.UtcNow > ExpiresAt;
    }

    public void UpdateLastAccess()
    {
        LastAccessedAt = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    #endregion
}
