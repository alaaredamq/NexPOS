using Application.IServices.Base;
using Core.Entities.Configuration;

namespace Application.IServices.Configuration;

public interface ICodeListValueService : IBaseService
{
    Task<IEnumerable<CodeListValue>> GetAllAsync();
    Task<CodeListValue?> GetByIdAsync(int id);
    Task<CodeListValue> CreateAsync(CodeListValue codeListValue);
    Task<CodeListValue> UpdateAsync(CodeListValue codeListValue);
    Task DeleteAsync(int id);
    
    Task<IEnumerable<CodeListValue>> GetByCodeListIdAsync(int codeListId);
    Task<IEnumerable<CodeListValue>> GetActiveByCodeListIdAsync(int codeListId);
    Task<IEnumerable<CodeListValue>> GetByCodeListKeyAsync(string key);
    Task<IEnumerable<CodeListValue>> GetActiveByCodeListKeyAsync(string key);
}
