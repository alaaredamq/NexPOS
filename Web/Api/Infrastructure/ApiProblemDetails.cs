namespace Web.Api.Infrastructure;

/// <summary>
/// The shape of every error the API returns (<c>application/problem+json</c>). A documentation type: the server writes
/// a <see cref="Microsoft.AspNetCore.Mvc.ProblemDetails"/> with the same fields (<c>code</c> and <c>traceId</c> travel as its extensions),
/// and this is what the generated TypeScript client types errors with. Show <see cref="Detail"/> when present, else
/// <see cref="Title"/>; branch on <see cref="Code"/>; put <see cref="Errors"/> on the fields they name.
/// </summary>
public sealed class ApiProblemDetails
{
    public string? Type { get; init; }

    /// <summary>A generic, localized message for the code (for example "You do not have permission to do that.").</summary>
    public required string Title { get; init; }

    public required int Status { get; init; }

    /// <summary>A specific, localized message for this occurrence, when there is one.</summary>
    public string? Detail { get; init; }

    public string? Instance { get; init; }

    /// <summary>Stable machine string: <c>unauthenticated</c>, <c>forbidden</c>, <c>validation_failed</c>, <c>license_sales_stopped</c>, <c>drawer_not_open</c> ...</summary>
    public required string Code { get; init; }

    /// <summary>Quote this to support: it is the <c>X-Request-Id</c> header and the id on the audit-trail rows of the request.</summary>
    public required string TraceId { get; init; }

    /// <summary>Only for <c>validation_failed</c>: field name (camelCase, as in the request body) to its messages.</summary>
    public IReadOnlyDictionary<string, string[]>? Errors { get; init; }
}
