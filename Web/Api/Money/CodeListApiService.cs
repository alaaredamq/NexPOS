using Application.IServices.Configuration;
using Core.Entities.Configuration;
using Microsoft.Extensions.Localization;
using Web.Api.Infrastructure;
using Web.Api.Money.Contracts;

namespace Web.Api.Money;

public interface ICodeListApiService
{
    Task<PagedResult<CodeListDto>> ListAsync(PagedQuery query);

    /// <summary>Null when the list does not exist.</summary>
    Task<CodeListDetailsDto?> GetAsync(int id);

    /// <summary>Throws <see cref="MoneyRuleException"/> for a taken key.</summary>
    Task<CodeListDto> CreateAsync(CodeListRequest request);

    /// <summary>Null when the list does not exist. Throws <see cref="MoneyRuleException"/> for a taken key.</summary>
    Task<CodeListDto?> UpdateAsync(int id, CodeListRequest request);

    /// <summary>Deletes the list and its values. False when it does not exist.</summary>
    Task<bool> DeleteAsync(int id);

    /// <summary>Null when the list does not exist. Throws <see cref="MoneyRuleException"/> for a value the list already has.</summary>
    Task<CodeListValueDto?> AddValueAsync(int listId, CodeListValueRequest request);

    /// <summary>Null when the list or the value does not exist. Throws <see cref="MoneyRuleException"/> for a value the list already has.</summary>
    Task<CodeListValueDto?> UpdateValueAsync(int listId, int valueId, CodeListValueRequest request);

    /// <summary>False when the list or the value does not exist.</summary>
    Task<bool> DeleteValueAsync(int listId, int valueId);
}

/// <summary>
/// Code lists and their values on top of <see cref="ICodeListService"/> / <see cref="ICodeListValueService"/>. The legacy API could create, edit and read lists but had no way to
/// add a value (its value service was injected and never called), and nothing else in the app reads a code list yet.
/// </summary>
public sealed class CodeListApiService : ICodeListApiService
{
    private static readonly string[] SortColumns = { "key", "name", "displayOrder", "isActive", "valueCount", "createdAt" };

    private readonly ICodeListService _lists;
    private readonly ICodeListValueService _values;
    private readonly IStringLocalizer<SharedResource> _text;

    public CodeListApiService(ICodeListService lists, ICodeListValueService values, IStringLocalizer<SharedResource> text)
    {
        _lists = lists;
        _values = values;
        _text = text;
    }

    public async Task<PagedResult<CodeListDto>> ListAsync(PagedQuery query)
    {
        var counts = await CountsAsync();
        IEnumerable<CodeListDto> rows = (await _lists.GetAllAsync()).Select(l => ToDto(l, counts));

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            rows = rows.Where(l => MoneySort.Contains(l.Key, term) || MoneySort.Contains(l.Name, term) || MoneySort.Contains(l.Description, term));
        }

        return PagedResult<CodeListDto>.From(Sort(rows, query), query);
    }

    public async Task<CodeListDetailsDto?> GetAsync(int id)
    {
        var list = await FindAsync(id);
        if (list is null) return null;

        var values = (await _values.GetByCodeListIdAsync(id)).OrderBy(v => v.DisplayOrder).ThenBy(v => v.Value, StringComparer.CurrentCultureIgnoreCase).ToList();
        return new CodeListDetailsDto
        {
            List = ToDto(list, new Dictionary<int, int> { [id] = values.Count }),
            Values = values.Select(ToDto).ToList(),
        };
    }

    public async Task<CodeListDto> CreateAsync(CodeListRequest request)
    {
        var key = request.Key.Trim();
        await CheckKeyAsync(key, id: null);

        var created = await _lists.CreateAsync(new CodeList
        {
            Key = key,
            Name = request.Name.Trim(),
            Description = Clean(request.Description),
            IsActive = request.IsActive,
            AllowMultipleSelection = request.AllowMultipleSelection,
            DisplayOrder = request.DisplayOrder,
        });
        return ToDto(created, new Dictionary<int, int>());
    }

    public async Task<CodeListDto?> UpdateAsync(int id, CodeListRequest request)
    {
        var list = await FindAsync(id);
        if (list is null) return null;

        var key = request.Key.Trim();
        await CheckKeyAsync(key, id);

        list.Key = key;
        list.Name = request.Name.Trim();
        list.Description = Clean(request.Description);
        list.IsActive = request.IsActive;
        list.AllowMultipleSelection = request.AllowMultipleSelection;
        list.DisplayOrder = request.DisplayOrder;
        list.UpdatedAt = DateTime.UtcNow;

        return ToDto(await _lists.UpdateAsync(list), await CountsAsync());
    }

    public async Task<bool> DeleteAsync(int id)
    {
        if (await FindAsync(id) is null) return false;

        await _lists.DeleteAsync(id);
        return true;
    }

    public async Task<CodeListValueDto?> AddValueAsync(int listId, CodeListValueRequest request)
    {
        if (await FindAsync(listId) is null) return null;

        var value = request.Value.Trim();
        CheckValue(value, await _values.GetByCodeListIdAsync(listId), valueId: null);

        var created = await _values.CreateAsync(new CodeListValue
        {
            CodeListId = listId,
            Value = value,
            DisplayText = Clean(request.DisplayText),
            Description = Clean(request.Description),
            IsActive = request.IsActive,
            DisplayOrder = request.DisplayOrder,
            Metadata = Clean(request.Metadata),
        });
        return ToDto(created);
    }

    public async Task<CodeListValueDto?> UpdateValueAsync(int listId, int valueId, CodeListValueRequest request)
    {
        var siblings = (await _values.GetByCodeListIdAsync(listId)).ToList();
        var existing = siblings.FirstOrDefault(v => v.Id == valueId);
        if (existing is null) return null;

        var value = request.Value.Trim();
        CheckValue(value, siblings, valueId);

        existing.Value = value;
        existing.DisplayText = Clean(request.DisplayText);
        existing.Description = Clean(request.Description);
        existing.IsActive = request.IsActive;
        existing.DisplayOrder = request.DisplayOrder;
        existing.Metadata = Clean(request.Metadata);
        existing.UpdatedAt = DateTime.UtcNow;

        return ToDto(await _values.UpdateAsync(existing));
    }

    public async Task<bool> DeleteValueAsync(int listId, int valueId)
    {
        if ((await _values.GetByCodeListIdAsync(listId)).All(v => v.Id != valueId)) return false;

        await _values.DeleteAsync(valueId);
        return true;
    }

    private async Task<CodeList?> FindAsync(int id) => (await _lists.GetAllAsync()).FirstOrDefault(l => l.Id == id);

    private async Task<Dictionary<int, int>> CountsAsync()
        => (await _values.GetAllAsync()).GroupBy(v => v.CodeListId).ToDictionary(g => g.Key, g => g.Count());

    private async Task CheckKeyAsync(string key, int? id)
    {
        if ((await _lists.GetAllAsync()).Any(l => l.Id != id && string.Equals(l.Key, key, StringComparison.OrdinalIgnoreCase)))
            throw new MoneyRuleException(nameof(CodeListRequest.Key), _text["Mny_CodeListKeyTaken"].Value);
    }

    private void CheckValue(string value, IEnumerable<CodeListValue> siblings, int? valueId)
    {
        if (siblings.Any(v => v.Id != valueId && string.Equals(v.Value, value, StringComparison.OrdinalIgnoreCase)))
            throw new MoneyRuleException(nameof(CodeListValueRequest.Value), _text["Mny_CodeListValueTaken"].Value);
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static IEnumerable<CodeListDto> Sort(IEnumerable<CodeListDto> rows, PagedQuery query)
    {
        var key = SortColumns.FirstOrDefault(c => string.Equals(c, query.Sort, StringComparison.OrdinalIgnoreCase)) ?? "displayOrder";
        var desc = query.Descending;
        return key switch
        {
            "key" => MoneySort.ByText(rows, l => l.Key, desc),
            "name" => MoneySort.ByText(rows, l => l.Name, desc),
            "isActive" => MoneySort.By(rows, l => l.IsActive, desc),
            "valueCount" => MoneySort.By(rows, l => l.ValueCount, desc),
            "createdAt" => MoneySort.By(rows, l => l.CreatedAt, desc),
            _ => MoneySort.By(rows, l => l.DisplayOrder, desc),
        };
    }

    private static CodeListDto ToDto(CodeList l, Dictionary<int, int> counts) => new()
    {
        Id = l.Id,
        Key = l.Key,
        Name = l.Name,
        Description = l.Description,
        IsActive = l.IsActive,
        AllowMultipleSelection = l.AllowMultipleSelection,
        DisplayOrder = l.DisplayOrder,
        ValueCount = counts.GetValueOrDefault(l.Id),
        CreatedAt = l.CreatedAt,
    };

    private static CodeListValueDto ToDto(CodeListValue v) => new()
    {
        Id = v.Id,
        CodeListId = v.CodeListId,
        Value = v.Value,
        DisplayText = v.DisplayText,
        Description = v.Description,
        IsActive = v.IsActive,
        DisplayOrder = v.DisplayOrder,
        Metadata = v.Metadata,
    };
}
