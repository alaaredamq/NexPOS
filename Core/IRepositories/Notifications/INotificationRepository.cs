using Core.Entities.Base;
using Core.Entities.Notifications;
using Core.IRepositories.Base;

namespace Core.IRepositories.Notifications;

public interface INotificationRepository : IBaseRepository<Notification, int>
{
    Task<IEnumerable<Notification>> GetUnreadByUserIdAsync(int userId);
    Task<IEnumerable<Notification>> GetLast5ByUserIdAsync(int userId);
    Task MarkAsReadAsync(int id);
    Task MarkAllAsReadAsync(int userId);
    Task<int> GetUnreadCountAsync(int userId);
}
