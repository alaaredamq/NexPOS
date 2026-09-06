using Core.Entities.Configuration;
using Core.IRepositories.Base;

namespace Core.IRepositories.Configuration;

public interface ICodeListValueRepository : IBaseRepository<CodeListValue, int>
{
    Task<IEnumerable<CodeListValue>> GetByCodeListIdAsync(int codeListId);
    Task<IEnumerable<CodeListValue>> GetActiveByCodeListIdAsync(int codeListId);
    Task<IEnumerable<CodeListValue>> GetByCodeListKeyAsync(string key);
    Task<IEnumerable<CodeListValue>> GetActiveByCodeListKeyAsync(string key);
}
