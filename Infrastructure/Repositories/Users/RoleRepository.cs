using Core.Entities.Users;
using Core.IRepositories.Users;
using Infrastructure.Data;
using Infrastructure.Repositories.Classes.Base;

namespace Infrastructure.Repositories.Users;

public class RoleRepository : Repository<Role>, IRoleRepository
{
    public RoleRepository(ErpDbContext dbContext, IServiceProvider serviceProvider)
        : base(dbContext, serviceProvider)
    {
    }
}
