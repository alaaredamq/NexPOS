using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Time;

/// <summary>
/// Timestamps come back from the database as <see cref="DateTimeKind.Unspecified"/> even though they were
/// stored as UTC, and System.Text.Json writes those with no zone ("2026-10-26T09:39:00"). The browser
/// then reads them as LOCAL time, so every `new Date(x).toLocaleString()` in the UI showed the UTC clock.
/// These converters write every Unspecified value as UTC ("...Z") so the browser converts it to local.
///
/// Values already marked Local or Utc are written exactly as before. A value that is a local DATE or
/// wall-clock time rather than a stored UTC timestamp must therefore be marked <see cref="DateTimeKind.Local"/>
/// where it is produced (see the sales report DTO). There is deliberately no "midnight means date" shortcut:
/// a backup scheduled for 02:00 in UTC+2 is stored as exactly 00:00Z and must still get its "Z".
/// </summary>
public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTime();

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value);
}

public sealed class UtcNullableDateTimeJsonConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null ? null : reader.GetDateTime();

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.Value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
            : value.Value);
    }
}
