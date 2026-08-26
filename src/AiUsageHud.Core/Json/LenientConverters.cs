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
/// Gate a wire percentage into 0..=100 (+1 point of slack for a benign
/// rounding overshoot like 100.4, which the caller saturates to 100).
/// Rejecting here — the parse boundary — is the only place a bad value can
/// still fail loudly, since converting to a snapshot has no error channel: a
/// value we cannot vouch for must not be silently clamped into a confident
/// "100%" or "0%" bar, including non-finite input (<c>(int)double.NaN</c>
/// would otherwise be silently 0).
/// </summary>
public sealed class StrictPercentConverter : JsonConverter<double>
{
    private const double Slack = 1.0;

    public override double Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
    {
        if (reader.TokenType != JsonTokenType.Number || !reader.TryGetDouble(out var v))
            throw new JsonException("expected a numeric percentage");
        if (!double.IsFinite(v))
            throw new JsonException($"percentage {v} is not finite");
        if (v < 0.0 || v > 100.0 + Slack)
            throw new JsonException($"percentage {v} outside 0..=100");
        return v;
    }

    public override void Write(Utf8JsonWriter w, double v, JsonSerializerOptions o) => w.WriteNumberValue(v);
}

/// <summary>
/// Accept a JSON int, float, or numeric string carrying the same value — the
/// API returns these counters (percentages, window durations) as any of the
/// three. Anything else (null, bool, array, object, an unparseable or
/// non-finite string) is schema drift and must fail the parse: the caller
/// validates before writing the cache, so a fabricated 0 here would be
/// persisted and rendered as a real 0% bar or a collapsed window duration.
/// Unlike <see cref="LenientLongConverter"/>, an explicit null is rejected
/// rather than defaulted — only a wholly *absent* key defaults, via the
/// property's own default value, since this converter is never invoked then.
/// Mirrors serde's <c>de_int_or_float_lenient</c> (post-audit strict variant).
/// </summary>
public sealed class StrictCounterConverter : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                if (reader.TryGetInt64(out var i)) return i;
                if (reader.TryGetDouble(out var d) && TryExactInt64(d, out var fi)) return fi;
                throw new JsonException("number out of range for a counter field");
            case JsonTokenType.String:
                var s = reader.GetString() ?? "";
                if (long.TryParse(s, out var si)) return si;
                if (double.TryParse(s, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var sd) && TryExactInt64(sd, out var sfi))
                    return sfi;
                throw new JsonException($"expected numeric string, got \"{s}\"");
            default:
                throw new JsonException($"expected number or numeric string, got {reader.TokenType}");
        }
    }

    /// <c>(long)d</c> saturates instead of failing for NaN/huge magnitudes, so
    /// only a magnitude the cast reproduces exactly is accepted.
    private static bool TryExactInt64(double d, out long value)
    {
        if (double.IsFinite(d) && d >= long.MinValue && d < 9_223_372_036_854_775_808.0 /* 2^63 */)
        {
            value = (long)d;
            return true;
        }
        value = 0;
        return false;
    }

    public override void Write(Utf8JsonWriter w, long v, JsonSerializerOptions o) => w.WriteNumberValue(v);
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
