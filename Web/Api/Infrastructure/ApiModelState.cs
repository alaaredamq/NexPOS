using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Web.Api.Infrastructure;

/// <summary>Turns MVC's <see cref="ModelStateDictionary"/> into the API's <c>errors{field:[message]}</c>.</summary>
public static class ApiModelState
{
    /// <summary>
    /// The answer every <c>[ApiController]</c> gives for a request that fails model binding or DTO validation (400,
    /// <c>code: validation_failed</c>). Keys are camelCase to match the JSON the UI sends, so a field error lands on the field.
    /// Only <c>api/v1</c> uses it - the legacy API controllers keep MVC's default answer.
    /// </summary>
    public static IActionResult InvalidModelStateResponse(ActionContext context, Func<ActionContext, IActionResult> legacy)
    {
        if (!ApiPaths.IsV1(context.HttpContext.Request)) return legacy(context);

        var errors = new Dictionary<string, string[]>();
        foreach (var (key, entry) in context.ModelState)
        {
            if (entry.Errors.Count == 0) continue;

            // A binding failure (malformed JSON, a string where a number belongs) comes with the parser's own text
            // ("... Path: $.quantity | LineNumber: 0 | BytePositionInLine: 2."), which names .NET types and is for developers.
            var messages = entry.Errors
                .Select(e => IsBindingFailure(e) ? ApiProblem.Localize(context.HttpContext, "Api_InvalidValue") : e.ErrorMessage)
                .Distinct()
                .ToArray();

            errors[CamelCase(key)] = messages;
        }

        return new ObjectResult(ApiProblem.Validation(context.HttpContext, errors))
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentTypes = { ApiProblem.ContentType },
        };
    }

    private static bool IsBindingFailure(ModelError error)
        => error.Exception is not null
           || string.IsNullOrWhiteSpace(error.ErrorMessage)
           || JsonParserText.IsMatch(error.ErrorMessage);

    // System.Text.Json's JsonException.Message always ends like this.
    private static readonly System.Text.RegularExpressions.Regex JsonParserText =
        new(@"Path: \$.*\| LineNumber: \d+ \| BytePositionInLine: \d+", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary><c>Items[0].UnitPrice</c> becomes <c>items[0].unitPrice</c>; a leading <c>$.</c> (JSON path) is dropped.</summary>
    public static string CamelCase(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        if (key.StartsWith("$.", StringComparison.Ordinal)) key = key[2..];
        if (key == "$") return string.Empty;

        return string.Join('.', key.Split('.').Select(part =>
            part.Length > 0 && char.IsUpper(part[0]) ? char.ToLowerInvariant(part[0]) + part[1..] : part));
    }
}
