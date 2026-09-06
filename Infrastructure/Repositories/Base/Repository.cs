using Infrastructure.Data;
using Core.Entities.Base;
using Core.ICache;
using Core.IRepositories.Base;
using Infrastructure.Repositories.Base;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Repositories.Classes.Base;

public class Repository <T> : BaseRepository<T, int>, IRepository<T> where T : class, IEntityBase<int> {
    protected readonly ErpDbContext _Context;
    public Repository(ErpDbContext dbContext, IServiceProvider serviceProvider) : base(dbContext, serviceProvider) {
        _Context = dbContext;
    }

}