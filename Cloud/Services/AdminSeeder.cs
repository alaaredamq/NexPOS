using Cloud.Data;
using Microsoft.EntityFrameworkCore;

namespace Cloud.Services;

/// <summary>
/// Creates or updates the admin account from configuration (Cloud__AdminUser / Cloud__AdminPassword).
/// There is deliberately no default password and no sign-up page: with nothing configured the portal has no
/// way in, and the log says so.
/// </summary>
public static class AdminSeeder
{
    public const int MinPasswordLength = 12;

    public static async Task EnsureAsync(ControlDbContext db, CloudOptions options, TimeProvider time, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(options.AdminUser) || string.IsNullOrEmpty(options.AdminPassword))
        {
            if (!await db.AdminUsers.AnyAsync())
                logger.LogWarning("No admin account exists. Set Cloud__AdminUser and Cloud__AdminPassword (at least {Min} characters) and restart.", MinPasswordLength);
            return;
        }

        if (options.AdminPassword.Length < MinPasswordLength)
            throw new InvalidOperationException($"Cloud:AdminPassword must be at least {MinPasswordLength} characters.");

        var username = options.AdminUser.Trim();
        var admin = await db.AdminUsers.FirstOrDefaultAsync(a => a.Username == username);
        if (admin is null)
        {
            db.AdminUsers.Add(new AdminUser
            {
                Username = username,
                PasswordHash = PasswordHasher.Hash(options.AdminPassword),
                CreatedAt = time.GetUtcNow().UtcDateTime,
            });
            logger.LogInformation("Created admin account '{User}'.", username);
        }
        else if (!PasswordHasher.Verify(options.AdminPassword, admin.PasswordHash))
        {
            admin.PasswordHash = PasswordHasher.Hash(options.AdminPassword);
            logger.LogInformation("Updated the password of admin account '{User}' from configuration.", username);
        }
        await db.SaveChangesAsync();
    }
}
