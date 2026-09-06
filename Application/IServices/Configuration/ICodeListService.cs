using Application.IServices.Base;
using Core.Entities.Configuration;

namespace Application.IServices.Configuration;

public interface ICodeListService : IBaseService
{
    Task<IEnumerable<CodeList>> GetAllAsync();
    Task<CodeList?> GetByIdAsync(int id);
    Task<CodeList> CreateAsync(CodeList codeList);
    Task<CodeList> UpdateAsync(CodeList codeList);
    Task DeleteAsync(int id);
    
    Task<CodeList?> GetByKeyAsync(string key);
    Task<IEnumerable<CodeList>> GetActiveAsync();
    Task<IEnumerable<CodeListValue>> GetValuesByCodeListKeyAsync(string key);
    Task<IEnumerable<CodeListValue>> GetActiveValuesByCodeListKeyAsync(string key);
}
