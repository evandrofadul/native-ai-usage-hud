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
            JsonTokenType.String => long.TryParse(reader.GetString(), out var s) ? s : null,
            _ => null,
        };

    public override void Write(Utf8JsonWriter w, long? v, JsonSerializerOptions o)
    {
        if (v is null) w.WriteNullValue(); else w.WriteNumberValue(v.Value);
    }
}

/// <summary>
/// Accept a plausible minor-unit scale (0..=6 covers every ISO 4217 currency;
/// the largest real exponent is 4). Integral floats are tolerated — the
/// Anthropic endpoint emits them (e.g. <c>used_credits: 14157.0</c>) — but
/// anything outside 0..=6 is schema drift: a wire <c>decimal_places: 100</c>
/// would overflow the scale and mis-state every amount, so it fails the parse
/// loudly instead of silently guessing.
/// </summary>
public sealed class LenientDecimalPlacesConverter : JsonConverter<int?>
{
    public override int? Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType != JsonTokenType.Number)
            throw new JsonException("decimal_places must be an integer in 0..=6");

        long? n = reader.TryGetInt64(out var i) ? i
            : reader.TryGetDouble(out var d) && d == Math.Truncate(d) ? (long)d
            : null;
        if (n is >= 0 and <= 6) return (int)n;
        throw new JsonException("decimal_places must be an integer in 0..=6");
    }

    public override void Write(Utf8JsonWriter w, int? v, JsonSerializerOptions o)
    {
        if (v is null) w.WriteNullValue(); else w.WriteNumberValue(v.Value);
    }
}

/// <summary>
/// Gate a currency code to a plausible ISO 4217 alpha code (three ASCII
/// uppercase letters). The value is embedded verbatim in UI text, so an
/// arbitrary string is drift as well as a minor injection surface.
/// </summary>
public sealed class IsoCurrencyConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        var s = reader.GetString();
        if (s is { Length: 3 } && s[0] is >= 'A' and <= 'Z' && s[1] is >= 'A' and <= 'Z' && s[2] is >= 'A' and <= 'Z')
            return s;
        throw new JsonException($"currency \"{s}\" is not an ISO 4217 alpha code");
    }

    public override void Write(Utf8JsonWriter w, string? v, JsonSerializerOptions o)
    {
        if (v is null) w.WriteNullValue(); else w.WriteStringValue(v);
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
