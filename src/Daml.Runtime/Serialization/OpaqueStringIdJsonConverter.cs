// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Daml.Runtime.Serialization;

/// <summary>
/// Shared <see cref="System.Text.Json"/> behaviour for the Daml identity structs that travel
/// as a bare JSON string — an opaque, non-empty identifier the wrapper never decomposes.
/// One implementation so the family cannot drift apart on its null posture or on the
/// diagnostics it produces for a malformed id.
/// </summary>
/// <typeparam name="TId">The identity struct, constructed from and rendered as one string.</typeparam>
internal abstract class OpaqueStringIdJsonConverter<TId> : JsonConverter<TId>
    where TId : struct
{
    private static readonly string TypeName = typeof(TId).Name;

    /// <remarks>
    /// The family's posture on null: a JSON null is rejected wherever the declared type forbids
    /// one and accepted wherever it permits one. Handling null here is what turns a bare
    /// <c>null</c> on a non-nullable field into a <see cref="JsonException"/> naming the type,
    /// rather than a default value that only throws when something later reads it.
    /// <see cref="Nullable{T}"/> fields are unaffected: the serializer short-circuits null for
    /// them before the converter runs. The reference-type member of the family,
    /// <see cref="ContractIdJsonConverter{TContractId}"/>, cannot mirror this and reaches the
    /// same posture through <see cref="JsonSerializerOptions.RespectNullableAnnotations"/>.
    /// </remarks>
    public override bool HandleNull => true;

    /// <summary>Builds the identity value from its wire string, which is never empty or whitespace.</summary>
    /// <exception cref="ArgumentException">The string is not a well-formed identifier.</exception>
    protected abstract TId Parse(string id);

    /// <summary>Extracts the wire string from an identity value.</summary>
    /// <exception cref="InvalidOperationException">The value is uninitialized.</exception>
    protected abstract string Format(TId value);

    /// <inheritdoc/>
    public override TId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected string token for {TypeName}, got {reader.TokenType}.");
        }

        var id = reader.GetString()!;
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new JsonException($"{TypeName} id cannot be empty or whitespace; got '{id}'.");
        }

        try
        {
            return Parse(id);
        }
        catch (ArgumentException ex)
        {
            throw new JsonException($"Invalid {TypeName} '{id}'.", ex);
        }
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, TId value, JsonSerializerOptions options)
    {
        string id;
        try
        {
            id = Format(value);
        }
        catch (InvalidOperationException ex)
        {
            throw new JsonException($"Cannot serialize an uninitialized {TypeName}.", ex);
        }

        writer.WriteStringValue(id);
    }
}
