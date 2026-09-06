using Application.IServices.Base;
using Core.Entities.Logging;

namespace Application.IServices.Logging;

public interface IAuditLogService : IBaseService
{
    Task<IEnumerable<AuditLog>> GetAllAsync();
    Task<AuditLog?> GetByIdAsync(int id);
    Task<AuditLog> CreateAsync(AuditLog auditLog);
    Task<AuditLog> UpdateAsync(AuditLog auditLog);
    Task DeleteAsync(int id);
    
    Task LogActionAsync(string action, string entityType, int? entityId = null, int? userId = null, 
        string? oldValuesJson = null, string? newValuesJson = null, string? additionalInfoJson = null,
        AuditLogSeverity severity = AuditLogSeverity.Info);
    
    Task<IEnumerable<AuditLog>> GetByUserAsync(int userId, int page = 1, int pageSize = 50);
    Task<IEnumerable<AuditLog>> GetByEntityAsync(string entityType, int entityId, int page = 1, int pageSize = 50);
    Task<IEnumerable<AuditLog>> GetByActionAsync(string action, int page = 1, int pageSize = 50);
    Task<IEnumerable<AuditLog>> GetBySeverityAsync(AuditLogSeverity severity, int page = 1, int pageSize = 50);
    Task<IEnumerable<AuditLog>> SearchAsync(string? searchTerm = null, string? entityType = null, 
        string? action = null, AuditLogSeverity? severity = null, DateTime? fromDate = null, 
        DateTime? toDate = null, int page = 1, int pageSize = 50);
}
