// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Daml.Runtime.Data;

/// <summary>
/// Represents a Daml Int value (64-bit signed integer).
/// </summary>
public sealed record DamlInt64(long Value) : DamlValue
{
    public static implicit operator long(DamlInt64 value) => value.Value;
    public static implicit operator DamlInt64(long value) => new(value);
}

/// <summary>
/// Represents a Daml Numeric value (arbitrary precision decimal).
/// </summary>
/// <param name="Value">The decimal value.</param>
/// <param name="Scale">The scale (number of decimal places) of the numeric.</param>
public sealed record DamlNumeric(decimal Value, int Scale = 10) : DamlValue
{
    public static implicit operator decimal(DamlNumeric value) => value.Value;
    public static implicit operator DamlNumeric(decimal value) => new(value);
}

/// <summary>
/// Represents a Daml Text value (string).
/// </summary>
public sealed record DamlText(string Value) : DamlValue
{
    public static implicit operator string(DamlText value) => value.Value;
    public static implicit operator DamlText(string value) => new(value);
}

/// <summary>
/// Represents a Daml Bool value.
/// </summary>
public sealed record DamlBool(bool Value) : DamlValue
{
    public static implicit operator bool(DamlBool value) => value.Value;
    public static implicit operator DamlBool(bool value) => new(value);
}

/// <summary>
/// Represents a Daml Unit value (empty tuple).
/// </summary>
public sealed record DamlUnit : DamlValue
{
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

    public static implicit operator DateOnly(DamlDate value) => value.Value;
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

    public static implicit operator DateTimeOffset(DamlTimestamp value) => value.Value;
    public static implicit operator DamlTimestamp(DateTimeOffset value) => new(value);
}

/// <summary>
/// Represents a first-class Daml Party identifier.
/// Implicit conversion to string (for logging/interpolation), explicit from string.
/// </summary>
[JsonConverter(typeof(PartyJsonConverter))]
public readonly record struct Party
{
    private readonly string? _id;

    public string Id =>
        _id ?? throw new InvalidOperationException("Cannot access Id of a default (uninitialized) Party.");

    public Party(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id, nameof(id));
        _id = id;
    }

    public static implicit operator string(Party party) =>
        party._id ?? throw new InvalidOperationException("Cannot convert a default (uninitialized) Party to string.");

    public static explicit operator Party(string id) => new(id);

    public override string ToString() => _id ?? "<uninitialized Party>";

    public DamlParty ToDamlValue() =>
        new(_id ?? throw new InvalidOperationException("Cannot serialize a default (uninitialized) Party."));

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
    public static implicit operator string(DamlParty value) => value.Value;
    public static implicit operator DamlParty(string value) => new(value);

    public override string ToString() => Value;
}
