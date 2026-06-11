// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Daml.Runtime.Data;

/// <summary>
/// Identifier of a Canton synchronizer (formerly "domain").
/// </summary>
/// <remarks>
/// <para>
/// Treated as an opaque string per Canton's documented guidance — the wrapper
/// deliberately does not decompose the id into <c>name</c> / <c>fingerprint</c>
/// / <c>protocol-version</c> components. The wire format has already changed
/// once between Canton 3.4 (<c>name::fingerprint</c>) and 3.5
/// (<c>name::fingerprint::protocol-version</c>) and may evolve again; storing
/// the raw string keeps the wrapper safe across format transitions.
/// See <see href="https://forum.canton.network/t/format-of-synchronizer-id-will-change-in-canton-3-5-potential-breaking-change/8445">
/// Canton Network Forum — synchronizer_id format change</see> for upstream guidance.
/// </para>
/// <para>
/// <b>Caveat for cross-version comparison.</b> Post-Logical-Synchronizer-Upgrade,
/// a 3.4 id (<c>global_sync::abc</c>) and the corresponding 3.5 id
/// (<c>global_sync::abc::35-0</c>) refer to the <em>same</em> synchronizer but
/// will not compare equal as raw strings. Code that needs to bridge the
/// transition must handle the format-aware comparison itself.
/// </para>
/// </remarks>
[JsonConverter(typeof(SynchronizerIdJsonConverter))]
public readonly record struct SynchronizerId
{
    private readonly string? _id;

    /// <summary>The verbatim wire-format synchronizer id.</summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when accessed on a default-initialized value.
    /// </exception>
    public string Id =>
        _id ?? throw new InvalidOperationException("Cannot access Id of a default (uninitialized) SynchronizerId.");

    /// <summary>Constructs a <see cref="SynchronizerId"/> from a non-empty wire string.</summary>
    /// <param name="id">The verbatim wire-format synchronizer id; stored opaque (non-null, non-whitespace).</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="id"/> is null, empty, or whitespace.</exception>
    public SynchronizerId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id, nameof(id));
        _id = id;
    }

    /// <summary>Extracts the wire-format id; explicit so it is never silently used as arbitrary text.</summary>
    public static explicit operator string(SynchronizerId id) =>
        id._id ?? throw new InvalidOperationException("Cannot convert a default (uninitialized) SynchronizerId to string.");

    /// <summary>Parses a wire-format id; explicit so arbitrary strings never silently become synchronizer ids.</summary>
    public static explicit operator SynchronizerId(string id) => new(id);

    /// <remarks>
    /// Returns a sentinel — not a throw — for <c>default(SynchronizerId)</c>: logging
    /// frameworks may invoke <c>ToString</c> on a captured value during exception
    /// handling, and a throw here would mask the original exception.
    /// </remarks>
    public override string ToString() => _id ?? "<uninitialized SynchronizerId>";
}

/// <summary>
/// System.Text.Json converter for <see cref="SynchronizerId"/>. Serializes as
/// a plain JSON string so the type round-trips through JSON payloads from PQS
/// and the JSON Ledger API, which encode synchronizer ids as raw strings
/// (e.g. <c>"global_sync::1220abcd...::35-0"</c>).
/// </summary>
internal sealed class SynchronizerIdJsonConverter : JsonConverter<SynchronizerId>
{
    // HandleNull=true so a bare `null` on a non-nullable SynchronizerId field surfaces
    // as a JsonException here instead of silently producing a default(SynchronizerId)
    // that later throws InvalidOperationException on .Id access. SynchronizerId? is
    // unaffected — STJ short-circuits null for Nullable<T> before invoking the converter.
    public override bool HandleNull => true;

    public override SynchronizerId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected string token for SynchronizerId, got {reader.TokenType}.");
        }

        var id = reader.GetString();
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new JsonException(id is null
                ? "SynchronizerId id cannot be null."
                : $"SynchronizerId id cannot be whitespace; got '{id}'.");
        }

        // Translate any ArgumentException the constructor might grow in the future
        // into a JsonException, so callers catching serialization errors see the right type.
        try
        {
            return new SynchronizerId(id);
        }
        catch (ArgumentException ex)
        {
            throw new JsonException($"Invalid SynchronizerId '{id}'.", ex);
        }
    }

    public override void Write(Utf8JsonWriter writer, SynchronizerId value, JsonSerializerOptions options)
    {
        // Mirror Read: translate the InvalidOperationException that SynchronizerId.Id
        // throws for default(SynchronizerId) into a JsonException so callers can catch
        // both directions of the round-trip uniformly.
        string id;
        try
        {
            id = value.Id;
        }
        catch (InvalidOperationException ex)
        {
            throw new JsonException("Cannot serialize an uninitialized SynchronizerId.", ex);
        }

        writer.WriteStringValue(id);
    }
}
