using Microsoft.AspNetCore.Mvc;
using Web.Api.Infrastructure;

namespace Web.Api.Money;

/// <summary>A field of the request breaks a rule the DTO attributes cannot judge (a taken code, a pair of the same currency, ...). 400 <c>validation_failed</c> on <see cref="Field"/>.</summary>
public sealed class MoneyRuleException : Exception
{
    public string Field { get; }

    public MoneyRuleException(string field, string message) : base(message) => Field = field;
}

/// <summary>The current state of the data says no (the default currency cannot be deleted, a method is in use, ...). 409 with a stable <see cref="Code"/>.</summary>
public sealed class MoneyConflictException : Exception
{
    public string Code { get; }

    public MoneyConflictException(string code, string message) : base(message) => Code = code;
}

/// <summary>Turns the two exceptions above into the API's problem responses so each controller action stays one line.</summary>
public abstract class MoneyControllerBase : ApiControllerBase
{
    /// <summary>The result of <paramref name="run"/>, or 404 when it yields null.</summary>
    protected async Task<ActionResult<T>> Guard<T>(Func<Task<T?>> run) where T : class
    {
        try
        {
            return await run() is { } result ? result : NotFound();
        }
        catch (MoneyRuleException ex)
        {
            return ValidationFailure(ex.Field, ex.Message);
        }
        catch (MoneyConflictException ex)
        {
            return ConflictFailure(ex.Code, ex.Message);
        }
    }

    /// <summary>201 with a link to the <c>Get</c> action of the same controller.</summary>
    protected async Task<ActionResult<T>> GuardCreated<T>(Func<Task<T>> create, Func<T, int> idOf)
    {
        try
        {
            var created = await create();
            return CreatedAtAction("Get", new { id = idOf(created) }, created);
        }
        catch (MoneyRuleException ex)
        {
            return ValidationFailure(ex.Field, ex.Message);
        }
        catch (MoneyConflictException ex)
        {
            return ConflictFailure(ex.Code, ex.Message);
        }
    }

    /// <summary>204 when <paramref name="run"/> says it did something, 404 when it says there was nothing.</summary>
    protected async Task<IActionResult> Done(Func<Task<bool>> run)
    {
        try
        {
            return await run() ? NoContent() : NotFound();
        }
        catch (MoneyRuleException ex)
        {
            return ValidationFailure(ex.Field, ex.Message);
        }
        catch (MoneyConflictException ex)
        {
            return ConflictFailure(ex.Code, ex.Message);
        }
    }

    /// <summary>An action that returns a DTO but may refuse for a rule (the calculator, the converter).</summary>
    protected async Task<ActionResult<T>> Rules<T>(Func<Task<T>> run) where T : class
    {
        try
        {
            return await run();
        }
        catch (MoneyRuleException ex)
        {
            return ValidationFailure(ex.Field, ex.Message);
        }
        catch (MoneyConflictException ex)
        {
            return ConflictFailure(ex.Code, ex.Message);
        }
    }
}

/// <summary>Sorting for the small in-memory lists of this module.</summary>
internal static class MoneySort
{
    public static IEnumerable<T> By<T, TKey>(IEnumerable<T> rows, Func<T, TKey> key, bool descending)
        => descending ? rows.OrderByDescending(key) : rows.OrderBy(key);

    public static IEnumerable<T> ByText<T>(IEnumerable<T> rows, Func<T, string> key, bool descending)
        => descending
            ? rows.OrderByDescending(key, StringComparer.CurrentCultureIgnoreCase)
            : rows.OrderBy(key, StringComparer.CurrentCultureIgnoreCase);

    public static bool Contains(string? text, string term) => text?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false;
}
