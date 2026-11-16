using System.Security.Cryptography;

namespace Infrastructure.Security;

/// <summary>
/// The key this installation signs and checks its own JWTs with, when none is configured (<c>Jwt:SecretKey</c> in user-secrets or the
/// <c>Jwt__SecretKey</c> environment variable still wins).
/// <para>
/// A token is only ever checked by the PC that issued it, so every installation needs its own secret key and nobody else's: one is made
/// at random on the first start and kept beside the database, so it survives restarts and updates. A new laptop or a new shop simply
/// gets its own. The key used to be committed to the repository, which made every installation's key public; never go back to a shared
/// default.
/// </para>
/// </summary>
public static class JwtKeyStore
{
    public const int MinLength = 32;
    private const int KeyBytes = 48; // 64 characters of base64

    /// <summary>The key saved at <paramref name="path"/>; made and saved first when there is none (or it is unusable).</summary>
    public static string GetOrCreate(string path)
    {
        if (TryRead(path) is { } existing) return existing;

        var folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeyBytes));
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, key);
        try
        {
            // Never replace a key another process wrote a moment ago: its tokens are already out there.
            File.Move(temp, path, overwrite: TryRead(path) is null);
        }
        catch (IOException)
        {
            // Lost a race with a second instance starting at the same time: use the key it saved.
        }
        finally
        {
            try { File.Delete(temp); } catch (IOException) { /* already moved */ }
        }

        return TryRead(path) ?? throw new InvalidOperationException($"Could not save the JWT signing key at {path}.");
    }

    private static string? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var text = File.ReadAllText(path).Trim();
            return text.Length >= MinLength ? text : null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
