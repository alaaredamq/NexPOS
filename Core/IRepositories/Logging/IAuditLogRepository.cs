using Core.Entities.Base;
using Core.Entities.Logging;
using Core.IRepositories.Base;

namespace Core.IRepositories.Logging;

public interface IAuditLogRepository : IBaseRepository<AuditLog, int>
{
    Task<IEnumerable<AuditLog>> GetByUserAsync(int userId, int page = 1, int pageSize = 50);
    Task<IEnumerable<AuditLog>> GetByEntityAsync(string entityType, int entityId, int page = 1, int pageSize = 50);
    Task<IEnumerable<AuditLog>> GetByActionAsync(string action, int page = 1, int pageSize = 50);
    Task<IEnumerable<AuditLog>> GetBySeverityAsync(AuditLogSeverity severity, int page = 1, int pageSize = 50);
    Task<IEnumerable<AuditLog>> SearchAsync(string? searchTerm = null, string? entityType = null, 
        string? action = null, AuditLogSeverity? severity = null, DateTime? fromDate = null, 
        DateTime? toDate = null, int page = 1, int pageSize = 50);
}
