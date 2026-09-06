using System.Linq.Expressions;
using Core.Specifications;
using Core.Entities.Base;

namespace Core.IRepositories.Base;

public interface IBaseRepository<T, TId> where T : IEntityBase<TId> {
    #region Att

    IQueryable<T> Table { get; }

    #endregion

    #region Methods

    Task<T> GetByIdAsync(TId id);
    Task<T> SaveAsync(T entity);
    Task<T> DeleteAsync(T entity);
    Task<T> DeleteAsync(int id);
    Task<T> SoftDeleteAsync(T entity);
    Task<bool> DeleteRangeAsync(List<T> entities);
    Task<bool> SoftDeleteRangeAsync(List<T> entities);
    Task<bool> DeleteByIdAsync(TId id);
    Task<bool> DeleteRangeByIdsAsync(TId id);
    Task<List<T>> GetAllAsync(Expression<Func<T, bool>>? predicate = null);
    Task<int> CountAsync(ISpecification<T> spec);
    Task SaveChangesAsync();
    
    // Queryable
    IQueryable<T> GetAllQueryable(Expression<Func<T, bool>>? predicate = null);
    Task<T> FindAsync(TId id);

    #endregion
}