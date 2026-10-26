using Core.Entities.Users;
using Core.IRepositories.Users;
using Infrastructure.Data;
using Infrastructure.Repositories.Classes.Base;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories.Users;

public class UserRepository : Repository<User>, IUserRepository
{
    public UserRepository(ErpDbContext dbContext, IServiceProvider serviceProvider)
        : base(dbContext, serviceProvider)
    {
    }

    public async Task<User?> GetByUsernameAsync(string username)
    {
        return await GetAllQueryable()
            .Include(u => u.Role)
            .ThenInclude(r => r.RolePermissions)
            .ThenInclude(rp => rp.Permission)
            .Include(u => u.UserPermissions)
            .ThenInclude(up => up.Permission)
            .AsSplitQuery() // two sibling collections: one joined query would return role-permissions x user-permissions rows
            .FirstOrDefaultAsync(u => u.Username == username && u.IsActive);
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        return await GetAllQueryable()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Email == email && u.IsActive);
    }

    public async Task<List<User>> GetByRoleIdAsync(int roleId)
    {
        return await GetAllQueryable()
            .Include(u => u.Role)
            .Where(u => u.RoleId == roleId && u.IsActive)
            .ToListAsync();
    }

    public async Task<bool> IsUsernameUniqueAsync(string username, int? excludeUserId = null)
    {
        var query = GetAllQueryable().Where(u => u.Username == username);
        if (excludeUserId.HasValue)
            query = query.Where(u => u.Id != excludeUserId.Value);
        
        return !await query.AnyAsync();
    }

    public async Task<bool> IsEmailUniqueAsync(string email, int? excludeUserId = null)
    {
        var query = GetAllQueryable().Where(u => u.Email == email);
        if (excludeUserId.HasValue)
            query = query.Where(u => u.Id != excludeUserId.Value);
        
        return !await query.AnyAsync();
    }
}
