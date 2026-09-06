using Application.IServices.Base;
using Core.Entities.Notifications;

namespace Application.IServices.Notifications;

public interface IAlertService : IBaseService
{
    Task<IEnumerable<Alert>> GetAllAsync();
    Task<Alert?> GetByIdAsync(int id);
    Task<Alert> CreateAsync(Alert alert);
    Task<Alert> UpdateAsync(Alert alert);
    Task DeleteAsync(int id);
    
    Task<IEnumerable<Alert>> GetActiveAlertsAsync();
    Task<IEnumerable<Alert>> GetByTypeAsync(AlertType type);
    Task<IEnumerable<Alert>> GetByStatusAsync(AlertStatus status);
    Task AcknowledgeAsync(int id, int userId);
    Task ResolveAsync(int id, int userId);
    Task<Alert> CreateAlertAsync(string title, string message, AlertType type, string? metadata = null);
}
