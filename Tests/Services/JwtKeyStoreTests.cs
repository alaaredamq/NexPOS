using Infrastructure.Security;
using Xunit;

namespace Tests.Services;

/// <summary>Each installation makes its own JWT signing key once and keeps it; nothing is shared between installations.</summary>
public class JwtKeyStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "nexpos-jwtkey-" + Guid.NewGuid().ToString("N"));
    private string KeyFile => Path.Combine(_folder, "jwt.key");

    [Fact]
    public void The_first_start_makes_a_long_random_key_and_later_starts_reuse_it()
    {
        var first = JwtKeyStore.GetOrCreate(KeyFile);

        Assert.True(first.Length >= JwtKeyStore.MinLength);
        Assert.True(File.Exists(KeyFile));
        Assert.Equal(first, JwtKeyStore.GetOrCreate(KeyFile));
        Assert.Single(Directory.GetFiles(_folder)); // no temp files left behind
    }

    [Fact]
    public void Two_installations_never_get_the_same_key()
    {
        var other = Path.Combine(_folder, "other", "jwt.key");
        Assert.NotEqual(JwtKeyStore.GetOrCreate(KeyFile), JwtKeyStore.GetOrCreate(other));
    }

    [Fact]
    public void An_unusable_saved_key_is_replaced()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(KeyFile, "too-short");

        var key = JwtKeyStore.GetOrCreate(KeyFile);

        Assert.True(key.Length >= JwtKeyStore.MinLength);
        Assert.Equal(key, File.ReadAllText(KeyFile));
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { /* temp folder */ }
    }
}
