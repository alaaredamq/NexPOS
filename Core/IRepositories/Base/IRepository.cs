using Core.Entities.Base;

namespace Core.IRepositories.Base;

public interface IRepository<T> : IBaseRepository<T, int> where T : IEntityBase<int> {
    
}