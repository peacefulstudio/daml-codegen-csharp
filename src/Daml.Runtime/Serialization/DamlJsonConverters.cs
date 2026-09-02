// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Daml.Runtime.Serialization;

/// <summary>
/// The <see cref="System.Text.Json"/> converters for the scalar identity types generated
/// records carry as fields — <see cref="Party"/>, <see cref="ContractId{T}"/> and
/// <see cref="SynchronizerId"/> — each of which travels as a bare JSON string in PQS rows
/// and JSON Ledger API payloads rather than as an object.
/// </summary>
/// <remarks>
/// Each of those types already carries its converter as a
/// <see cref="JsonConverterAttribute"/>, so plain
/// <c>JsonSerializer.Deserialize&lt;MyTemplate&gt;(json)</c> needs no registration at all.
/// <see cref="AddDamlConverters"/> exists for the hosts that build their own
/// <see cref="JsonSerializerOptions"/> — a ledger or PQS client's default options, say —
/// and want the Daml conversions listed explicitly rather than inherited from attributes.
/// <see cref="System.Text.Json"/> does not walk the base chain to find an inherited
/// attribute, so a type deriving from <see cref="ContractId{T}"/> needs one of its own.
/// The emitted <c>T.ContractId</c> is given that attribute by the codegen; a hand-written
/// derived contract id is not, and falls back to <c>{"Value":"..."}</c> unless registered
/// here.
/// </remarks>
public static class DamlJsonConverters
{
    /// <summary>
    /// The converters, in the order <see cref="AddDamlConverters"/> appends them.
    /// Each instance is stateless and safe to share across
    /// <see cref="JsonSerializerOptions"/>.
    /// </summary>
    public static IReadOnlyList<JsonConverter> All { get; } =
    [
        new PartyJsonConverter(),
        new ContractIdJsonConverterFactory(),
        new SynchronizerIdJsonConverter(),
    ];

    /// <summary>
    /// Appends every converter in <see cref="All"/> to <paramref name="options"/>, sets
    /// <see cref="JsonSerializerOptions.RespectNullableAnnotations"/>, and returns
    /// <paramref name="options"/> so the call chains off an options initializer.
    /// </summary>
    /// <remarks>
    /// The three identity types share one posture on null: a JSON null is rejected wherever the
    /// declared type forbids one, and read as absent wherever it permits one. <see cref="Party"/>
    /// and <see cref="SynchronizerId"/> are structs and their converter enforces that itself;
    /// <see cref="ContractId{T}"/> is a reference type, and <c>ContractId&lt;T&gt;?</c> — the C#
    /// rendering of <c>Optional (ContractId T)</c> — is indistinguishable from it at runtime, so
    /// only the nullability annotation separates a required contract id from an optional one.
    /// Enabling <see cref="JsonSerializerOptions.RespectNullableAnnotations"/> is therefore not a
    /// preference but the condition for the family agreeing at all; it applies to every type
    /// <paramref name="options"/> serializes, not only the Daml ones.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="options"/> is already read-only — that is, serialization has begun.
    /// </exception>
    public static JsonSerializerOptions AddDamlConverters(this JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        foreach (var converter in All)
        {
            options.Converters.Add(converter);
        }
        options.RespectNullableAnnotations = true;
        return options;
    }
}
