using Core.Entities.Logging;
using Core.IRepositories.Logging;
using Infrastructure.Data;
using Infrastructure.Repositories.Base;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories.Logging;

public class AuditLogRepository : BaseRepository<AuditLog, int>, IAuditLogRepository
{
    public AuditLogRepository(ErpDbContext db, IServiceProvider serviceProvider) : base(db, serviceProvider) { }

    public async Task<IEnumerable<AuditLog>> GetByUserAsync(int userId, int page = 1, int pageSize = 50)
    {
        return await Table
            .Include(al => al.User)
            .Where(al => al.UserId == userId)
            .OrderByDescending(al => al.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<IEnumerable<AuditLog>> GetByEntityAsync(string entityType, int entityId, int page = 1, int pageSize = 50)
    {
        return await Table
            .Include(al => al.User)
            .Where(al => al.EntityType == entityType && al.EntityId == entityId)
            .OrderByDescending(al => al.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<IEnumerable<AuditLog>> GetByActionAsync(string action, int page = 1, int pageSize = 50)
    {
        return await Table
            .Include(al => al.User)
            .Where(al => al.Action == action)
            .OrderByDescending(al => al.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<IEnumerable<AuditLog>> GetBySeverityAsync(AuditLogSeverity severity, int page = 1, int pageSize = 50)
    {
        return await Table
            .Include(al => al.User)
            .Where(al => al.Severity == severity)
            .OrderByDescending(al => al.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<IEnumerable<AuditLog>> SearchAsync(string? searchTerm = null, string? entityType = null, 
        string? action = null, AuditLogSeverity? severity = null, DateTime? fromDate = null, 
        DateTime? toDate = null, int page = 1, int pageSize = 50)
    {
        var query = Table
            .Include(al => al.User)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.ToLower();
            query = query.Where(al => 
                al.Action.ToLower().Contains(term) ||
                al.EntityType.ToLower().Contains(term) ||
                al.IpAddress.ToLower().Contains(term) ||
                (al.User != null && al.User.Username.ToLower().Contains(term))
            );
        }

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            query = query.Where(al => al.EntityType == entityType);
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(al => al.Action == action);
        }

        if (severity.HasValue)
        {
            query = query.Where(al => al.Severity == severity.Value);
        }

        if (fromDate.HasValue)
        {
            query = query.Where(al => al.CreatedAt >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(al => al.CreatedAt <= toDate.Value);
        }

        return await query
            .OrderByDescending(al => al.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }
}
