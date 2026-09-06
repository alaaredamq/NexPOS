using Core.Entities.Configuration;
using Core.IRepositories.Base;

namespace Core.IRepositories.Configuration;

public interface ICodeListRepository : IBaseRepository<CodeList, int>
{
    Task<CodeList?> GetByKeyAsync(string key);
    Task<IEnumerable<CodeList>> GetActiveAsync();
    Task<IEnumerable<CodeListValue>> GetValuesByCodeListKeyAsync(string key);
    Task<IEnumerable<CodeListValue>> GetActiveValuesByCodeListKeyAsync(string key);
}
