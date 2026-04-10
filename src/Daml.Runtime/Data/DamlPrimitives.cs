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
/// Represents a Daml Party identifier.
/// </summary>
public sealed record DamlParty(string Value) : DamlValue
{
    public static implicit operator string(DamlParty value) => value.Value;
    public static implicit operator DamlParty(string value) => new(value);

    public override string ToString() => Value;
}
