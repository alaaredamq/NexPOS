using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Core.Auditing;

/// <summary>
/// Decides what may be written to the audit trail in clear text. Anything that looks like a secret
/// (PINs, passwords, hashes, tokens, ...) is replaced with <see cref="Redacted"/> - whether it comes
/// from an entity property, a query string, a form field or a JSON body.
/// </summary>
public static class AuditRedactor
{
    public const string Redacted = "[REDACTED]";

    /// <summary>Longest string value kept as-is; longer ones are cut with a marker.</summary>
    public const int MaxValueLength = 500;

    /// <summary>Longest JSON document stored in one column.</summary>
    public const int MaxJsonLength = 16_000;

    private static readonly Regex SecretName = new(
        "password|passwd|passcode|passphrase|secret|token|api[_-]?key|private[_-]?key|hash|salt|credential|authorization|cookie|cvv|card[_-]?number",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // "Pin", "NewPin", "ConfirmPin", "user_pin" - case-sensitive on purpose, so "Shipping"/"Spinning" don't match.
    private static readonly Regex PinName = new(
        @"(^[Pp]in(?:[Cc]ode)?$)|([a-z0-9]Pin(?:Code)?$)|(_pin$)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // MustChangePin, HasPassword, IsTokenValid ... say *whether* something is set, not what it is.
    // Support wants to see those, and they leak nothing.
    private static readonly Regex FlagName = new(
        "^(Must|Has|Is|Require|Requires|Can|Allow|Allows)[A-Z_]",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly JsonSerializerOptions DetailsJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        Converters = { new JsonStringEnumConverter() },
        MaxDepth = 16
    };

    public static bool IsSensitiveName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (FlagName.IsMatch(name)) return false;
        return SecretName.IsMatch(name) || PinName.IsMatch(name);
    }

    public static string? Truncate(string? value, int max = MaxValueLength)
    {
        if (value is null || value.Length <= max) return value;
        return value[..max] + $"…[+{value.Length - max} chars]";
    }

    /// <summary>A single name/value pair from a query string or form: the value is hidden if the name is a secret.</summary>
    public static string? RedactValue(string name, string? value)
        => IsSensitiveName(name) ? Redacted : Truncate(value);

    /// <summary>Redacted copy of a set of request parameters (query string or form fields).</summary>
    public static Dictionary<string, string?> RedactPairs(IEnumerable<KeyValuePair<string, string?>> pairs)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in pairs)
        {
            result[key] = RedactValue(key, value);
        }
        return result;
    }

    /// <summary>Serialises <paramref name="details"/> to compact, redacted, size-capped JSON (null for null).</summary>
    public static string? SerializeDetails(object? details)
    {
        if (details is null) return null;

        try
        {
            var node = details is JsonNode already
                ? already.DeepClone()
                : JsonSerializer.SerializeToNode(details, DetailsJson);
            RedactNode(node);
            return Cap(node?.ToJsonString());
        }
        catch (Exception)
        {
            // Details are a courtesy; an unserialisable object must never break the event itself.
            return JsonSerializer.Serialize(new { error = "details could not be serialised", type = details.GetType().Name });
        }
    }

    /// <summary>Redacts and size-caps a JSON document (a request body). Null if it is not valid JSON.</summary>
    public static string? RedactJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            var node = JsonNode.Parse(json);
            RedactNode(node);
            return Cap(node?.ToJsonString());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Keeps a JSON document inside <see cref="MaxJsonLength"/>, still valid JSON when it has to be cut.</summary>
    public static string? Cap(string? json)
    {
        if (json is null || json.Length <= MaxJsonLength) return json;
        return JsonSerializer.Serialize(new
        {
            truncated = true,
            length = json.Length,
            preview = json[..2000]
        });
    }

    // A JsonValue can't replace itself - its parent holds the slot - so each container fixes up its own children.
    private static void RedactNode(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (IsSensitiveName(key))
                    {
                        obj[key] = Redacted;
                        continue;
                    }

                    var child = obj[key];
                    if (TryTruncate(child, out var cut)) obj[key] = cut;
                    else RedactNode(child);
                }
                break;

            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (TryTruncate(array[i], out var cut)) array[i] = cut;
                    else RedactNode(array[i]);
                }
                break;
        }
    }

    private static bool TryTruncate(JsonNode? node, out string? truncated)
    {
        truncated = null;
        if (node is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > MaxValueLength)
        {
            truncated = Truncate(text);
            return true;
        }
        return false;
    }
}
