using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiUsageHud.Core.Json;

/// <summary>
/// Accept a JSON int OR float (truncating floats) OR null (→ 0) for a
/// <see cref="long"/>. Mirrors serde's <c>de_int_or_float</c>.
/// </summary>
public sealed class LenientLongConverter : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o) =>
        reader.TokenType switch
        {
            JsonTokenType.Null => 0,
            JsonTokenType.Number => reader.TryGetInt64(out var i) ? i : (long)reader.GetDouble(),
            JsonTokenType.String => long.TryParse(reader.GetString(), out var s) ? s : 0,
            _ => 0,
        };

    public override void Write(Utf8JsonWriter w, long v, JsonSerializerOptions o) => w.WriteNumberValue(v);
}

/// <summary>Nullable variant: int/float → long?, null/other → null.</summary>
public sealed class LenientNullableLongConverter : JsonConverter<long?>
{
    public override long? Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o) =>
        reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.Number => reader.TryGetInt64(out var i) ? i : (long)reader.GetDouble(),
            _ => null,
        };

    public override void Write(Utf8JsonWriter w, long? v, JsonSerializerOptions o)
    {
        if (v is null) w.WriteNullValue(); else w.WriteNumberValue(v.Value);
    }
}

/// <summary>
/// Accept a money string ("$0.00") OR a number (0.0 → "$0.00"). Mirrors serde's
/// <c>de_money_string</c>.
/// </summary>
public sealed class MoneyStringConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString() ?? "$0.00",
            JsonTokenType.Number => "$" + reader.GetDouble().ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            _ => "$0.00",
        };

    public override void Write(Utf8JsonWriter w, string v, JsonSerializerOptions o) => w.WriteStringValue(v);
}
