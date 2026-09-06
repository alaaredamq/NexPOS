using Core.Entities.Base;
using Core.Entities.Notifications;
using Core.IRepositories.Base;

namespace Core.IRepositories.Notifications;

public interface IAlertRepository : IBaseRepository<Alert, int>
{
    Task<IEnumerable<Alert>> GetActiveAlertsAsync();
    Task<IEnumerable<Alert>> GetByTypeAsync(AlertType type);

    /// <summary>
    /// Returns the single active alert of this type whose Metadata matches exactly, or null.
    /// Lets the stock-alert path dedupe without pulling every alert of the type into memory.
    /// </summary>
    Task<Alert?> GetActiveByTypeAndMetadataAsync(AlertType type, string metadataJson);
    Task<IEnumerable<Alert>> GetByStatusAsync(AlertStatus status);
    Task AcknowledgeAsync(int id, int userId);
    Task ResolveAsync(int id, int userId);
}
