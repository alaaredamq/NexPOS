namespace Application.IServices.Licensing;

/// <summary>
/// Keeps Basic to <see cref="Common.Licensing.PlanRules.BasicMaxActiveUsers"/> active users: the owner plus two cashiers.
/// It counts every active user, whatever the role, so making everyone a Manager does not get round it.
/// </summary>
public interface IUserLimitGuard
{
    /// <summary>
    /// Call before a user is created active or switched from inactive to active, never on an ordinary save (sign-in updates
    /// the user too). Throws <see cref="Application.Exceptions.PlanLimitException"/> when the plan has no room left.
    /// Users who were already active over the limit (after a downgrade) stay active: only adding one more is refused.
    /// </summary>
    /// <param name="exceptUserId">The user being switched on, who must not be counted twice.</param>
    Task EnsureRoomForActiveUserAsync(int? exceptUserId = null);
}
