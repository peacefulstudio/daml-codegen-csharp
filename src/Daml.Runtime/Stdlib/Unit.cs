// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Daml.Runtime.Stdlib;

/// <summary>
/// The Daml unit type — a single inhabitant, no payload.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the role of <see cref="System.ValueTuple"/> but participates in the
/// codegen's typed return-value path. Codegen emits <c>ExerciseOutcome&lt;Unit&gt;</c>
/// for choices declared as <c>choice Foo : ()</c>. <see cref="Value"/> is the only
/// reachable inhabitant: the constructor is private, the type is a sealed class
/// (not a record, to prevent <c>with</c>-expression clones), and equality is
/// overridden so any two references that survive the type system compare equal.
/// </para>
/// <para>
/// This is distinct from <c>Daml.Runtime.Data.DamlUnit</c>, which is the
/// wire-level <c>DamlValue</c> representation of unit. The codegen's typed
/// wrappers carry <see cref="Unit"/> at the call site and convert via
/// <c>DamlUnit.Instance</c> at the wire boundary.
/// </para>
/// <para>
/// Through <see cref="System.Text.Json"/> it travels as an empty object, <c>{}</c>, and reads
/// back as <see cref="Value"/> — the only inhabitant there is to construct. Without
/// <see cref="UnitJsonConverter"/>, a read refuses the type outright, because the private
/// constructor leaves <see cref="System.Text.Json"/> nothing to call: the reflection-based
/// deserializer needs a public constructor or settable members to populate, and this type
/// offers neither by design. It names <see cref="UnitJsonConverter"/> in a
/// <see cref="JsonConverterAttribute"/>, so it converts on bare <see cref="JsonSerializerOptions"/>
/// with no registration — the shape an <c>ExerciseOutcome&lt;Unit&gt;.One</c> needs to round-trip
/// its <see cref="Value"/> at all.
/// </para>
/// <para>
/// A <see cref="JsonSerializerOptions.PropertyNamingPolicy"/> has nothing to rename here: the
/// written object carries no property. Deliberately unaffected, too, by
/// <see cref="System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow"/>:
/// <see cref="UnitJsonConverter.Read"/> walks and skips every member of whatever object it is
/// given rather than routing through <see cref="System.Text.Json"/>'s reflection contract, so a
/// strict caller's setting never reaches it and an object carrying any members — the canonical
/// <c>{}</c> or a forward-compatible producer's extra fields alike — still reads back as
/// <see cref="Value"/>. <see cref="Unit"/> declares no member to disallow, so there is nothing a
/// stricter reading would protect.
/// </para>
/// <para>
/// A <see cref="JsonSerializerOptions.ReferenceHandler"/> has nothing to preserve here either, and
/// <see cref="UnitJsonConverter"/> supports every setting of it: unlike the other converters this
/// namespace's <see cref="Daml.Runtime.Serialization.DiscriminatedUnionJson"/> backs,
/// <see cref="UnitJsonConverter.Write"/> and <see cref="UnitJsonConverter.Read"/> never make a nested
/// <see cref="JsonSerializer"/> call — <c>Write</c> emits the fixed <c>{}</c> directly to the ambient
/// <see cref="Utf8JsonWriter"/> and <c>Read</c> only skips tokens on the ambient
/// <see cref="Utf8JsonReader"/> — so there is no independent reference resolver to start, and every
/// read yields the same <see cref="Value"/> singleton regardless of which
/// <see cref="JsonSerializerOptions.ReferenceHandler"/> the caller has configured.
/// </para>
/// </remarks>
[JsonConverter(typeof(UnitJsonConverter))]
public sealed class Unit : IEquatable<Unit>
{
    private Unit() { }

    /// <summary>The single inhabitant of <see cref="Unit"/>.</summary>
    public static Unit Value { get; } = new();

    /// <inheritdoc/>
    public bool Equals(Unit? other) => other is not null;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Unit;

    /// <inheritdoc/>
    public override int GetHashCode() => 0;

    /// <summary>Two <see cref="Unit"/> references always compare equal.</summary>
    public static bool operator ==(Unit? left, Unit? right) => (left is null) == (right is null);

    /// <summary>Two <see cref="Unit"/> references always compare equal.</summary>
    public static bool operator !=(Unit? left, Unit? right) => !(left == right);
}

/// <summary>
/// Supplies the <see cref="System.Text.Json"/> converter for <see cref="Unit"/>. Without it, the
/// private constructor leaves the reflection-based serializer nothing to call, and a read of
/// <c>{}</c> throws <see cref="NotSupportedException"/> instead of yielding <see cref="Unit.Value"/>.
/// </summary>
internal sealed class UnitJsonConverter : JsonConverter<Unit>
{
    public override Unit Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected a JSON object for {nameof(Unit)}, got {reader.TokenType}.");
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            reader.Skip();
        }

        return Unit.Value;
    }

    public override void Write(Utf8JsonWriter writer, Unit value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteEndObject();
    }
}
