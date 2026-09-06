using Application.IServices.Configuration;
using Core.Entities.Configuration;
using Core.IRepositories.Configuration;

namespace Application.Services.Configuration;

public class CodeListValueService : ICodeListValueService
{
    private readonly ICodeListValueRepository _repository;

    public CodeListValueService(ICodeListValueRepository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<CodeListValue>> GetAllAsync()
    {
        return await _repository.GetAllAsync();
    }

    public async Task<CodeListValue?> GetByIdAsync(int id)
    {
        return await _repository.GetByIdAsync(id);
    }

    public async Task<CodeListValue> CreateAsync(CodeListValue codeListValue)
    {
        return await _repository.SaveAsync(codeListValue);
    }

    public async Task<CodeListValue> UpdateAsync(CodeListValue codeListValue)
    {
        return await _repository.SaveAsync(codeListValue);
    }

    public async Task DeleteAsync(int id)
    {
        var codeListValue = await _repository.GetByIdAsync(id);
        if (codeListValue != null)
        {
            await _repository.DeleteAsync(codeListValue);
        }
    }

    public async Task<IEnumerable<CodeListValue>> GetByCodeListIdAsync(int codeListId)
    {
        return await _repository.GetByCodeListIdAsync(codeListId);
    }

    public async Task<IEnumerable<CodeListValue>> GetActiveByCodeListIdAsync(int codeListId)
    {
        return await _repository.GetActiveByCodeListIdAsync(codeListId);
    }

    public async Task<IEnumerable<CodeListValue>> GetByCodeListKeyAsync(string key)
    {
        return await _repository.GetByCodeListKeyAsync(key);
    }

    public async Task<IEnumerable<CodeListValue>> GetActiveByCodeListKeyAsync(string key)
    {
        return await _repository.GetActiveByCodeListKeyAsync(key);
    }
}
