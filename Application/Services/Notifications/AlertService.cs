using Application.IServices.Base;
using Application.IServices.Notifications;
using Core.Entities.Notifications;
using Core.IRepositories.Notifications;

namespace Application.Services.Notifications;

public class AlertService : IAlertService
{
    private readonly IAlertRepository _repository;

    public AlertService(IAlertRepository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<Alert>> GetAllAsync()
    {
        return await _repository.GetAllAsync();
    }

    public async Task<Alert?> GetByIdAsync(int id)
    {
        return await _repository.GetByIdAsync(id);
    }

    public async Task<Alert> CreateAsync(Alert alert)
    {
        return await _repository.SaveAsync(alert);
    }

    public async Task<Alert> UpdateAsync(Alert alert)
    {
        return await _repository.SaveAsync(alert);
    }

    public async Task DeleteAsync(int id)
    {
        var alert = await _repository.GetByIdAsync(id);
        if (alert != null)
        {
            await _repository.DeleteAsync(alert);
        }
    }

    public async Task<IEnumerable<Alert>> GetActiveAlertsAsync()
    {
        return await _repository.GetActiveAlertsAsync();
    }

    public async Task<IEnumerable<Alert>> GetByTypeAsync(AlertType type)
    {
        return await _repository.GetByTypeAsync(type);
    }

    public async Task<IEnumerable<Alert>> GetByStatusAsync(AlertStatus status)
    {
        return await _repository.GetByStatusAsync(status);
    }

    public async Task AcknowledgeAsync(int id, int userId)
    {
        await _repository.AcknowledgeAsync(id, userId);
    }

    public async Task ResolveAsync(int id, int userId)
    {
        await _repository.ResolveAsync(id, userId);
    }

    public async Task<Alert> CreateAlertAsync(string title, string message, AlertType type, string? metadata = null)
    {
        var alert = new Alert
        {
            Title = title,
            Message = message,
            Type = type,
            Status = AlertStatus.Active,
            CreatedAt = DateTime.UtcNow,
            Metadata = metadata
        };

        return await _repository.SaveAsync(alert);
    }
}
