using Application.IServices.Configuration;
using Core.Entities.Configuration;
using Core.IRepositories.Configuration;

namespace Application.Services.Configuration;

public class CodeListService : ICodeListService
{
    private readonly ICodeListRepository _repository;

    public CodeListService(ICodeListRepository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<CodeList>> GetAllAsync()
    {
        return await _repository.GetAllAsync();
    }

    public async Task<CodeList?> GetByIdAsync(int id)
    {
        return await _repository.GetByIdAsync(id);
    }

    public async Task<CodeList> CreateAsync(CodeList codeList)
    {
        return await _repository.SaveAsync(codeList);
    }

    public async Task<CodeList> UpdateAsync(CodeList codeList)
    {
        return await _repository.SaveAsync(codeList);
    }

    public async Task DeleteAsync(int id)
    {
        var codeList = await _repository.GetByIdAsync(id);
        if (codeList != null)
        {
            await _repository.DeleteAsync(codeList);
        }
    }

    public async Task<CodeList?> GetByKeyAsync(string key)
    {
        return await _repository.GetByKeyAsync(key);
    }

    public async Task<IEnumerable<CodeList>> GetActiveAsync()
    {
        return await _repository.GetActiveAsync();
    }

    public async Task<IEnumerable<CodeListValue>> GetValuesByCodeListKeyAsync(string key)
    {
        return await _repository.GetValuesByCodeListKeyAsync(key);
    }

    public async Task<IEnumerable<CodeListValue>> GetActiveValuesByCodeListKeyAsync(string key)
    {
        return await _repository.GetActiveValuesByCodeListKeyAsync(key);
    }
}
