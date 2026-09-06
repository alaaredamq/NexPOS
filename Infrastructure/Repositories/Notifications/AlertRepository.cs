using Core.Entities.Notifications;
using Core.IRepositories.Notifications;
using Infrastructure.Data;
using Infrastructure.Repositories.Base;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories.Notifications;

public class AlertRepository : BaseRepository<Alert, int>, IAlertRepository
{
    public AlertRepository(ErpDbContext db, IServiceProvider serviceProvider) : base(db, serviceProvider) { }

    public async Task<IEnumerable<Alert>> GetActiveAlertsAsync()
    {
        return await Table
            .Where(a => a.Status == AlertStatus.Active)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<Alert>> GetByTypeAsync(AlertType type)
    {
        return await Table
            .Where(a => a.Type == type)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();
    }

    public async Task<Alert?> GetActiveByTypeAndMetadataAsync(AlertType type, string metadataJson)
    {
        return await Table
            .Where(a => a.Type == type && a.Status == AlertStatus.Active && a.Metadata == metadataJson)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<IEnumerable<Alert>> GetByStatusAsync(AlertStatus status)
    {
        return await Table
            .Where(a => a.Status == status)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();
    }

    public async Task AcknowledgeAsync(int id, int userId)
    {
        var alert = await FindAsync(id);
        if (alert != null && alert.Status == AlertStatus.Active)
        {
            alert.Status = AlertStatus.Acknowledged;
            alert.AcknowledgedAt = DateTime.UtcNow;
            alert.AcknowledgedByUserId = userId;
            await SaveChangesAsync();
        }
    }

    public async Task ResolveAsync(int id, int userId)
    {
        var alert = await FindAsync(id);
        if (alert != null)
        {
            alert.Status = AlertStatus.Resolved;
            alert.ResolvedAt = DateTime.UtcNow;
            alert.ResolvedByUserId = userId;
            await SaveChangesAsync();
        }
    }
}
