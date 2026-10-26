using Application.Services.Users;
using Core.Auditing;
using Core.Entities.Users;
using Core.IRepositories.Users;
using Moq;
using Xunit;

namespace Tests.Services;

/// <summary>
/// Sign-in is PIN-only, so an account created by an admin has exactly one credential - the PIN they
/// chose - and the user must replace it at first sign-in. No usable password is ever created.
/// </summary>
public class UserServiceTests
{
    private static (UserService sut, Mock<IUserRepository> repo) Create()
    {
        var repo = new Mock<IUserRepository>();
        repo.Setup(r => r.SaveAsync(It.IsAny<User>())).ReturnsAsync((User u) => u);
        var sut = new UserService(repo.Object, Mock.Of<IRoleRepository>(), Mock.Of<IUserSessionRepository>(), Mock.Of<IAuditTrail>());
        return (sut, repo);
    }

    [Fact]
    public async Task CreateWithPinAsync_StoresHashedPin_AndRequiresChangeAtFirstSignIn()
    {
        var (sut, _) = Create();
        var user = new User("newcashier", "new@example.test", "temp_hash", roleId: 2, "New", "Cashier");

        var created = await sut.CreateWithPinAsync(user, "4321");

        Assert.NotNull(created.PinHash);
        Assert.NotEqual("4321", created.PinHash);
        Assert.True(await sut.ValidatePinAsync(created, "4321"));
        Assert.False(await sut.ValidatePinAsync(created, "1234"));
        Assert.True(created.MustChangePin);
    }

    [Fact]
    public async Task CreateWithPinAsync_GivesNoUsablePassword()
    {
        var (sut, _) = Create();
        var user = new User("nopassword", "np@example.test", "temp_hash", roleId: 2);

        var created = await sut.CreateWithPinAsync(user, "4321");

        Assert.NotEqual("temp_hash", created.PasswordHash);
        // Not the PIN, and not empty/guessable values someone might try on the legacy password endpoint.
        foreach (var guess in new[] { "4321", "", "password", "temp_hash", "admin" })
            Assert.False(await sut.ValidatePasswordAsync(created, guess), $"'{guess}' must not unlock the password");
    }

    [Fact]
    public async Task CreateWithPinAsync_TwoAccounts_GetDifferentRandomPasswords()
    {
        var (sut, _) = Create();
        var a = await sut.CreateWithPinAsync(new User("a", "a@example.test", "x", 2), "1111");
        var b = await sut.CreateWithPinAsync(new User("b", "b@example.test", "x", 2), "1111");

        Assert.NotEqual(a.PasswordHash, b.PasswordHash);
    }
}
