using Application.IServices.Base;
using Application.IServices.Logging;
using Core.Entities.Logging;
using Core.IRepositories.Logging;
using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace Application.Services.Logging;

public class AuditLogService : IAuditLogService
{
    private readonly IAuditLogRepository _repository;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuditLogService(IAuditLogRepository repository, IHttpContextAccessor httpContextAccessor)
    {
        _repository = repository;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<IEnumerable<AuditLog>> GetAllAsync()
    {
        return await _repository.GetAllAsync();
    }

    public async Task<AuditLog?> GetByIdAsync(int id)
    {
        return await _repository.GetByIdAsync(id);
    }

    public async Task<AuditLog> CreateAsync(AuditLog auditLog)
    {
        return await _repository.SaveAsync(auditLog);
    }

    public async Task<AuditLog> UpdateAsync(AuditLog auditLog)
    {
        return await _repository.SaveAsync(auditLog);
    }

    public async Task DeleteAsync(int id)
    {
        var auditLog = await _repository.GetByIdAsync(id);
        if (auditLog != null)
        {
            await _repository.DeleteAsync(auditLog);
        }
    }

    public async Task LogActionAsync(string action, string entityType, int? entityId = null, int? userId = null, 
        string? oldValuesJson = null, string? newValuesJson = null, string? additionalInfoJson = null,
        AuditLogSeverity severity = AuditLogSeverity.Info)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var ipAddress = httpContext?.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        var userAgent = httpContext?.Request.Headers["User-Agent"].ToString() ?? "Unknown";

        // Try to get user ID from context if not provided
        if (userId == null && httpContext?.Items.ContainsKey("CurrentUser") == true)
        {
            var user = httpContext.Items["CurrentUser"];
            if (user != null)
            {
                // Assuming user has an Id property - adjust based on your User entity
                userId = (int)user.GetType().GetProperty("Id")?.GetValue(user);
            }
        }

        var auditLog = new AuditLog(userId, action, entityType, entityId, ipAddress, userAgent, 
            oldValuesJson, newValuesJson, additionalInfoJson, severity);

        await _repository.SaveAsync(auditLog);
    }

    public async Task<IEnumerable<AuditLog>> GetByUserAsync(int userId, int page = 1, int pageSize = 50)
    {
        return await _repository.GetByUserAsync(userId, page, pageSize);
    }

    public async Task<IEnumerable<AuditLog>> GetByEntityAsync(string entityType, int entityId, int page = 1, int pageSize = 50)
    {
        return await _repository.GetByEntityAsync(entityType, entityId, page, pageSize);
    }

    public async Task<IEnumerable<AuditLog>> GetByActionAsync(string action, int page = 1, int pageSize = 50)
    {
        return await _repository.GetByActionAsync(action, page, pageSize);
    }

    public async Task<IEnumerable<AuditLog>> GetBySeverityAsync(AuditLogSeverity severity, int page = 1, int pageSize = 50)
    {
        return await _repository.GetBySeverityAsync(severity, page, pageSize);
    }

    public async Task<IEnumerable<AuditLog>> SearchAsync(string? searchTerm = null, string? entityType = null, 
        string? action = null, AuditLogSeverity? severity = null, DateTime? fromDate = null, 
        DateTime? toDate = null, int page = 1, int pageSize = 50)
    {
        return await _repository.SearchAsync(searchTerm, entityType, action, severity, fromDate, toDate, page, pageSize);
    }
}
