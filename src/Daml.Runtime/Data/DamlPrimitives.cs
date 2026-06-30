// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Daml.Runtime.Data;

/// <summary>
/// Represents a Daml Int value (64-bit signed integer).
/// </summary>
public sealed record DamlInt64(long Value) : DamlValue
{
    /// <summary>Unwraps the underlying 64-bit integer.</summary>
    public static implicit operator long(DamlInt64 value) => value.Value;

    /// <summary>Wraps a 64-bit integer as a Daml Int value.</summary>
    public static implicit operator DamlInt64(long value) => new(value);
}

/// <summary>
/// Represents a Daml Numeric value (fixed-point decimal).
/// </summary>
/// <remarks>
/// A Daml Numeric carries up to 38 significant digits on the ledger, but the
/// backing <see cref="decimal"/> holds at most 28-29. Ledger values that need
/// more digits cannot be represented losslessly by this type:
/// <see cref="Serialization.DamlJsonSerializer"/> rounds excess fractional
/// precision to the nearest representable <see cref="decimal"/> and throws
/// <see cref="System.Text.Json.JsonException"/> for magnitudes beyond
/// <see cref="decimal.MaxValue"/>.
/// <para>
/// Equality compares <see cref="Value"/> only: <see cref="Scale"/> is not part
/// of the wire format (<see cref="Serialization.DamlJsonSerializer"/> never
/// writes it and deserialization reconstructs the default of 10), so two
/// Numerics with the same value but different scales are equal. The
/// <see cref="Scale"/> property is retained as the hook for future
/// scale-padded reading.
/// </para>
/// </remarks>
/// <param name="Value">The decimal value.</param>
/// <param name="Scale">The scale (number of decimal places) of the numeric.</param>
public sealed record DamlNumeric(decimal Value, int Scale = 10) : DamlValue
{
    /// <summary>Unwraps the underlying decimal, discarding the Daml scale.</summary>
    public static implicit operator decimal(DamlNumeric value) => value.Value;

    /// <summary>Wraps a decimal as a Daml Numeric with the default scale of 10.</summary>
    public static implicit operator DamlNumeric(decimal value) => new(value);

    /// <summary>
    /// Compares by <see cref="Value"/> only; <see cref="Scale"/> never reaches the
    /// wire, so including it would break round-trip equality for any non-default scale.
    /// </summary>
    public bool Equals(DamlNumeric? other) => other is not null && Value == other.Value;

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode();
}

/// <summary>
/// Represents a Daml Text value (string).
/// </summary>
public sealed record DamlText(string Value) : DamlValue
{
    /// <summary>Unwraps the underlying string.</summary>
    public static implicit operator string(DamlText value) => value.Value;

    /// <summary>Wraps a string as a Daml Text value.</summary>
    public static implicit operator DamlText(string value) => new(value);
}

/// <summary>
/// Represents a Daml Bool value.
/// </summary>
public sealed record DamlBool(bool Value) : DamlValue
{
    /// <summary>Unwraps the underlying boolean.</summary>
    public static implicit operator bool(DamlBool value) => value.Value;

    /// <summary>Wraps a boolean as a Daml Bool value.</summary>
    public static implicit operator DamlBool(bool value) => new(value);
}

/// <summary>
/// Represents a Daml Unit value (empty tuple).
/// </summary>
public sealed record DamlUnit : DamlValue
{
    /// <summary>The single Unit value; Unit carries no data, so one shared instance suffices.</summary>
    public static readonly DamlUnit Instance = new();
    private DamlUnit() { }
}

/// <summary>
/// Represents a Daml Date value.
/// </summary>
public sealed record DamlDate(DateOnly Value) : DamlValue
{
    /// <summary>
    /// Creates a DamlDate from days since epoch (1970-01-01).
    /// </summary>
    public static DamlDate FromDaysSinceEpoch(int days) =>
        new(DateOnly.FromDayNumber(days + DateOnly.FromDateTime(DateTime.UnixEpoch).DayNumber));

    /// <summary>
    /// Gets the number of days since epoch.
    /// </summary>
    public int DaysSinceEpoch =>
        Value.DayNumber - DateOnly.FromDateTime(DateTime.UnixEpoch).DayNumber;

    /// <summary>Unwraps the underlying calendar date.</summary>
    public static implicit operator DateOnly(DamlDate value) => value.Value;

    /// <summary>Wraps a calendar date as a Daml Date value.</summary>
    public static implicit operator DamlDate(DateOnly value) => new(value);
}

/// <summary>
/// Represents a Daml Time value (timestamp with microsecond precision).
/// </summary>
public sealed record DamlTimestamp(DateTimeOffset Value) : DamlValue
{
    /// <summary>
    /// Creates a DamlTimestamp from microseconds since epoch.
    /// </summary>
    public static DamlTimestamp FromMicrosecondsSinceEpoch(long microseconds) =>
        new(DateTimeOffset.UnixEpoch.AddTicks(microseconds * 10));

    /// <summary>
    /// Gets the microseconds since epoch.
    /// </summary>
    public long MicrosecondsSinceEpoch =>
        (Value - DateTimeOffset.UnixEpoch).Ticks / 10;

    /// <summary>Unwraps the underlying timestamp.</summary>
    public static implicit operator DateTimeOffset(DamlTimestamp value) => value.Value;

    /// <summary>Wraps a timestamp as a Daml Time value. The conversion preserves the full 100ns tick precision; the ledger truncates to microseconds.</summary>
    public static implicit operator DamlTimestamp(DateTimeOffset value) => new(value);
}

/// <summary>
/// Represents a first-class Daml Party identifier.
/// Conversions to and from <see cref="string"/> are both explicit, so a party can
/// never be silently mistaken for an arbitrary string (or vice versa); use
/// <see cref="Id"/> or <see cref="ToString"/> for logging and interpolation.
/// </summary>
[JsonConverter(typeof(PartyJsonConverter))]
public readonly record struct Party
{
    private readonly string? _id;

    /// <summary>
    /// The full party identifier string (e.g. "Alice::1220abcd..."); throws
    /// <see cref="InvalidOperationException"/> for a default-constructed Party.
    /// </summary>
    public string Id =>
        _id ?? throw new InvalidOperationException("Cannot access Id of a default (uninitialized) Party.");

    /// <summary>
    /// Creates a Party from its identifier string; rejects null or whitespace ids.
    /// </summary>
    public Party(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id, nameof(id));
        _id = id;
    }

    /// <summary>Extracts the party identifier; explicit so a Party is never silently used as text.</summary>
    public static explicit operator string(Party party) =>
        party._id ?? throw new InvalidOperationException("Cannot convert a default (uninitialized) Party to string.");

    /// <summary>Parses a party identifier; explicit so arbitrary strings never silently become parties.</summary>
    public static explicit operator Party(string id) => new(id);

    /// <summary>Returns the party identifier, or a placeholder for a default-constructed Party.</summary>
    public override string ToString() => _id ?? "<uninitialized Party>";

    /// <summary>Converts this Party to its wire-level <see cref="DamlParty"/> carrier.</summary>
    public DamlParty ToDamlValue() =>
        new(_id ?? throw new InvalidOperationException("Cannot serialize a default (uninitialized) Party."));

    /// <summary>Builds a validated Party from a wire-level <see cref="DamlParty"/> carrier.</summary>
    public static Party FromDamlValue(DamlParty value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(value.Value ?? throw new InvalidOperationException("Cannot create Party from DamlParty with null Value."));
    }
}

/// <summary>
/// System.Text.Json converter for <see cref="Party"/>. Serializes as a plain JSON string
/// so Party round-trips through JSON payloads produced by PQS and the JSON Ledger API,
/// which encode parties as raw strings (e.g. "Alice::1220abcd...").
/// </summary>
internal sealed class PartyJsonConverter : JsonConverter<Party>
{
    // HandleNull=true so a bare `null` on a non-nullable Party field surfaces as a
    // JsonException here instead of silently producing a default(Party) that later
    // throws InvalidOperationException on .Id access. Party? is unaffected — STJ
    // short-circuits null for Nullable<T> before invoking the converter.
    public override bool HandleNull => true;

    public override Party Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected string token for Party, got {reader.TokenType}.");
        }

        var id = reader.GetString()!;
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new JsonException("Party id cannot be null or whitespace.");
        }

        // Translate any ArgumentException the constructor might grow in the future
        // into a JsonException, so callers catching serialization errors see the right type.
        try
        {
            return new Party(id);
        }
        catch (ArgumentException ex)
        {
            throw new JsonException($"Invalid Party id '{id}'.", ex);
        }
    }

    public override void Write(Utf8JsonWriter writer, Party value, JsonSerializerOptions options)
    {
        // Mirror Read: translate the InvalidOperationException that Party.Id throws
        // for default(Party) into a JsonException so callers can catch both directions
        // of the round-trip uniformly.
        string id;
        try
        {
            id = value.Id;
        }
        catch (InvalidOperationException ex)
        {
            throw new JsonException("Cannot serialize an uninitialized Party.", ex);
        }

        writer.WriteStringValue(id);
    }
}

/// <summary>
/// Represents a Daml Party identifier.
/// </summary>
public sealed record DamlParty(string Value) : DamlValue
{
    /// <summary>Unwraps the underlying party identifier string.</summary>
    public static implicit operator string(DamlParty value) => value.Value;

    /// <summary>Wraps a party identifier string as a wire-level Daml Party value.</summary>
    public static implicit operator DamlParty(string value) => new(value);

    /// <summary>Returns the party identifier string.</summary>
    public override string ToString() => Value;
}
