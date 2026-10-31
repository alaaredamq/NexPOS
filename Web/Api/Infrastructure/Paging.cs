using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace Web.Api.Infrastructure;

/// <summary>
/// Query string of every list endpoint: <c>?page=1&amp;pageSize=25&amp;sort=name&amp;dir=asc&amp;q=milk</c>.
/// Bind it with <c>[FromQuery] PagedQuery query</c>. <c>sort</c> is a column name the endpoint defines (it must
/// whitelist it); <c>q</c> is free-text search.
/// </summary>
public sealed class PagedQuery
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 200;

    /// <summary>1-based page number.</summary>
    [Range(1, int.MaxValue)]
    [FromQuery(Name = "page")]
    public int Page { get; set; } = 1;

    [Range(1, MaxPageSize)]
    [FromQuery(Name = "pageSize")]
    public int PageSize { get; set; } = DefaultPageSize;

    [FromQuery(Name = "sort")]
    public string? Sort { get; set; }

    /// <summary><c>asc</c> (default) or <c>desc</c>.</summary>
    [RegularExpression("(?i)^(asc|desc)$")]
    [FromQuery(Name = "dir")]
    public string? Dir { get; set; }

    [MaxLength(200)]
    [FromQuery(Name = "q")]
    public string? Q { get; set; }

    public bool Descending => string.Equals(Dir, "desc", StringComparison.OrdinalIgnoreCase);

    public int Skip => (Page - 1) * PageSize;
}

/// <summary>What every list endpoint returns.</summary>
public sealed record PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>Rows matching the filter across all pages.</summary>
    public required int Total { get; init; }

    public required int Page { get; init; }

    public required int PageSize { get; init; }

    public static PagedResult<T> Of(IReadOnlyList<T> items, int total, PagedQuery query)
        => new() { Items = items, Total = total, Page = query.Page, PageSize = query.PageSize };

    /// <summary>Pages an in-memory sequence (small lists whose service has no paged query yet).</summary>
    public static PagedResult<T> From(IEnumerable<T> all, PagedQuery query)
    {
        var list = all as IReadOnlyList<T> ?? all.ToList();
        return Of(list.Skip(query.Skip).Take(query.PageSize).ToList(), list.Count, query);
    }
}
