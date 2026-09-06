using System.Linq.Expressions;
using Core.Specifications;
using Infrastructure.Data;
using Core.Entities.Base;
using Core.ICache;
using Core.IRepositories.Base;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Repositories.Base;

public class BaseRepository <T, TId> : IBaseRepository<T, TId> where T : class, IEntityBase<TId> 
{
    private readonly DbContext _context;
    private readonly IServiceProvider _serviceProvider;
    protected ICacheService _CacheService => GetService<ICacheService>();

    public BaseRepository(ErpDbContext context, IServiceProvider serviceProvider) {
        _context = context;
        _serviceProvider = serviceProvider;
    }
    
    protected T GetService<T>() where T : notnull
        => _serviceProvider.GetRequiredService<T>();
    
    public DbSet<T> _Entities => _context.Set<T>();
    public IQueryable<T> Table => _Entities;
    
    public async Task<T> GetByIdAsync(TId id) {
        return await _Entities.FindAsync(id) ?? throw new ArgumentNullException(nameof(id) + " not found in database as " + typeof(T).Name);
    }

    public async Task<T> SaveAsync(T entity) {
        if (entity.Id.Equals(default(TId)))
        {
            await _Entities.AddAsync(entity);
        }
        else
        {
            _Entities.Update(entity);
        }

        await _context.SaveChangesAsync();

        return entity;
    }

    public async Task<T> DeleteAsync(T entity) {
        _Entities.Remove(entity);
        await _context.SaveChangesAsync();

        return entity;
    }

    public async Task<T> DeleteAsync(int id) {
        var entity = await _Entities.FindAsync(id);
        if(entity == null) throw new ArgumentNullException(nameof(id) + " not found in database as " + typeof(T).Name);
        _Entities.Remove(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<T> SoftDeleteAsync(T entity) {
        if(entity is ISSoftDeletable softDelete) {
            softDelete.IsDeleted = true;
            _Entities.Update(entity);
            await _context.SaveChangesAsync();
        }
        else {
            throw new Exception("This entity does not have soft delete property");
        }

        return entity;
    }

    public async Task<bool> DeleteRangeAsync(List<T> entities) {
        if(!entities.Any()) throw new ArgumentNullException(nameof(entities) + " is empty");
        _Entities.RemoveRange(entities);
        _context.SaveChanges();

        return true;
    }

    public async Task<bool> SoftDeleteRangeAsync(List<T> entities) {
        if(!entities.Any()) throw new ArgumentNullException(nameof(entities) + " is empty");
        foreach (var entity in entities) {
            if(entity is ISSoftDeletable softDelete) {
                softDelete.IsDeleted = true;
                _Entities.Update(entity);
            }
            else {
                throw new Exception("This entity does not have soft delete property");
            }
        }
        _context.SaveChanges();

        return true;
    }

    public async Task<bool> DeleteByIdAsync(TId id) {
        var entity = _Entities.Find(id);
        if(entity == null) throw new ArgumentNullException(nameof(id) + " not found in database as " + typeof(T).Name);
        _Entities.Remove(entity);
        _context.SaveChanges();

        return true;
    }

    public async Task<bool> DeleteRangeByIdsAsync(TId id) {
        var entities = _Entities.Where(e => e.Id.Equals(id)).ToList();
        if(!entities.Any()) throw new ArgumentNullException(nameof(id) + " not found in database as " + typeof(T).Name);
        _Entities.RemoveRange(entities);
        _context.SaveChanges();

        return true;
    }

    public async Task<List<T>> GetAllAsync(Expression<Func<T, bool>>? predicate = null) {
        if(predicate == null) return await _Entities.ToListAsync();
        return await _Entities.Where(predicate).ToListAsync();
    }

    public async Task<int> CountAsync(ISpecification<T> spec) {
        return await SpecificationEvaluator<T>.GetQuery(_Entities, spec).CountAsync();
    }
    
    public async Task SaveChangesAsync() {
        await _context.SaveChangesAsync();
    }
    
    public IQueryable<T> GetAllQueryable(Expression<Func<T, bool>>? predicate = null) {
        if(predicate == null) return _Entities;
        return _Entities.Where(predicate);
    }
    
    public async Task<T> FindAsync(TId id) {
        return await _Entities.FindAsync(id);
    }
}