using Application.Exceptions;
using Application.IServices.Licensing;
using Common.Licensing;
using Core.IRepositories.Users;

namespace Application.Services.Licensing;

public sealed class UserLimitGuard : IUserLimitGuard
{
    private readonly ILicenseService _license;
    private readonly IUserRepository _users;

    public UserLimitGuard(ILicenseService license, IUserRepository users)
    {
        _license = license;
        _users = users;
    }

    public async Task EnsureRoomForActiveUserAsync(int? exceptUserId = null)
    {
        if (_license.GetState().MaxActiveUsers is not { } max) return;

        var active = (await _users.GetAllAsync(u => u.IsActive)).Count(u => u.Id != exceptUserId);
        if (active >= max) throw new PlanLimitException(ProFeature.MoreUsers, max);
    }
}
