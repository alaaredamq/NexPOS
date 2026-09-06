using Core.Entities.Users;
using Core.IRepositories.Base;

namespace Core.IRepositories.Users;

public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByUsernameAsync(string username);
    Task<User?> GetByEmailAsync(string email);
    Task<List<User>> GetByRoleIdAsync(int roleId);
    Task<bool> IsUsernameUniqueAsync(string username, int? excludeUserId = null);
    Task<bool> IsEmailUniqueAsync(string email, int? excludeUserId = null);
}
