using Core.Entities.Notifications;
using Core.IRepositories.Notifications;
using Infrastructure.Data;
using Infrastructure.Repositories.Base;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories.Notifications;

public class NotificationRepository : BaseRepository<Notification, int>, INotificationRepository
{
    public NotificationRepository(ErpDbContext db, IServiceProvider serviceProvider) : base(db, serviceProvider) { }

    public async Task<IEnumerable<Notification>> GetUnreadByUserIdAsync(int userId)
    {
        return await Table
            .Where(n => n.UserId == userId && !n.IsRead)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<Notification>> GetLast5ByUserIdAsync(int userId)
    {
        return await Table
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(5)
            .ToListAsync();
    }

    public async Task MarkAsReadAsync(int id)
    {
        var notification = await FindAsync(id);
        if (notification != null)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            await SaveChangesAsync();
        }
    }

    public async Task MarkAllAsReadAsync(int userId)
    {
        var notifications = await Table
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync();

        foreach (var notification in notifications)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
        }

        await SaveChangesAsync();
    }

    public async Task<int> GetUnreadCountAsync(int userId)
    {
        return await Table
            .CountAsync(n => n.UserId == userId && !n.IsRead);
    }
}
