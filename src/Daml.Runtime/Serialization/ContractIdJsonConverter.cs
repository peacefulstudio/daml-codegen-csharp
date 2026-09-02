// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Daml.Runtime.Contracts;

namespace Daml.Runtime.Serialization;

/// <summary>
/// Supplies the <see cref="System.Text.Json"/> converter for any closed
/// <see cref="ContractId{T}"/> and for the per-template contract-id types codegen
/// derives from it. PQS rows and the JSON Ledger API encode a contract id as a raw
/// JSON string; without this factory the default object contract writes
/// <c>{"Value":"..."}</c> and cannot read a bare string back, so every consumer ends up
/// hand-rolling the same reflective factory.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ContractId{T}"/> carries this factory as a
/// <see cref="JsonConverterAttribute"/>, so a property declared as the closed generic —
/// which is how generated template payloads declare their contract-id fields — converts
/// with no registration. <see cref="System.Text.Json"/> reads
/// <see cref="JsonConverterAttribute"/> off the declared type and does not walk its base
/// chain, so the emitted <c>T.ContractId</c> (including <c>T.Contract.Id</c>) carries the
/// attribute of its own, written by the codegen onto the generated record — it converts
/// unregistered too. A <em>hand-written</em> type deriving from <see cref="ContractId{T}"/>
/// carries no such attribute and does need registration: directly, or through
/// <see cref="DamlJsonConverters.AddDamlConverters"/>, which is also what makes the
/// <see cref="JsonSerializerOptions"/> converter list self-describing.
/// </para>
/// <para>
/// Codegen emits a <c>T.ContractId</c> record deriving from <see cref="ContractId{T}"/>
/// for every template, and that derived type — not the open generic — is what a
/// consumer-authored DTO usually declares. Matching only the exact closed generic would
/// leave those properties on the default object contract, so one value would take two
/// wire shapes depending on the type it was declared as. The match therefore walks the
/// base chain, and <see cref="ContractIdJsonConverter{TContractId}"/> reads back the
/// concrete derived type rather than its base.
/// </para>
/// <para>
/// <b>AOT / trimming incompatibility:</b> <see cref="CreateConverter"/> uses
/// <see cref="Activator.CreateInstance(Type)"/> and <see cref="Type.MakeGenericType"/> to
/// instantiate the closed converter at runtime. These reflection-based calls are not
/// compatible with Native AOT compilation or aggressive IL trimming and will produce
/// <see cref="System.NotSupportedException"/> in those environments. Use a source-generated
/// <see cref="System.Text.Json.Serialization.JsonSerializerContext"/> instead when targeting AOT.
/// </para>
/// </remarks>
[RequiresUnreferencedCode("ContractIdJsonConverterFactory uses MakeGenericType and Activator.CreateInstance, which are not trimming-safe.")]
[RequiresDynamicCode("ContractIdJsonConverterFactory uses MakeGenericType at runtime, which requires dynamic code generation.")]
public sealed class ContractIdJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert) =>
        !IsAbstract(typeToConvert) && ClosedContractIdBaseOf(typeToConvert) is not null;

    /// <inheritdoc/>
    /// <exception cref="ArgumentException">
    /// <paramref name="typeToConvert"/> is neither a closed <see cref="ContractId{T}"/>
    /// nor a constructible type derived from one.
    /// </exception>
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        if (!CanConvert(typeToConvert))
        {
            throw new ArgumentException(
                $"'{typeToConvert}' is neither a closed {typeof(ContractId<>).Name} nor a constructible type derived from one.",
                nameof(typeToConvert));
        }

        return (JsonConverter)Activator.CreateInstance(
            typeof(ContractIdJsonConverter<>).MakeGenericType(typeToConvert))!;
    }

    private static bool IsAbstract(Type typeToConvert) => typeToConvert is { IsAbstract: true };

    private static Type? ClosedContractIdBaseOf(Type typeToConvert)
    {
        for (var candidate = typeToConvert; candidate is not null; candidate = candidate.BaseType)
        {
            if (candidate is { IsGenericType: true }
                && candidate.GetGenericTypeDefinition() == typeof(ContractId<>))
            {
                return candidate;
            }
        }

        return null;
    }
}

internal sealed class ContractIdJsonConverter<TContractId> : JsonConverter<TContractId>
    where TContractId : ContractId
{
    private static readonly ConstructorInfo FromContractIdString =
        typeof(TContractId).GetConstructor([typeof(string)])
        ?? throw new InvalidOperationException(
            $"'{typeof(TContractId)}' has no public constructor taking a single contract-id string.");

    private static readonly string TypeName = DescribeContractIdType(typeof(TContractId));

    /// <remarks>
    /// The reference-type member of the identity-converter family cannot police null the way
    /// <see cref="OpaqueStringIdJsonConverter{TId}"/> does. <c>ContractId&lt;T&gt;</c> and
    /// <c>ContractId&lt;T&gt;?</c> — the C# rendering of <c>Optional (ContractId T)</c> — are the
    /// same runtime type, so a converter handed the null token has no way to tell a required
    /// field from an optional one and would have to reject both. The family reaches its shared
    /// posture instead through <see cref="JsonSerializerOptions.RespectNullableAnnotations"/>,
    /// which <see cref="DamlJsonConverters.AddDamlConverters"/> enables.
    /// </remarks>
    public override bool HandleNull => false;

    public override TContractId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected string token for {TypeName}, got {reader.TokenType}.");
        }

        var value = reader.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new JsonException(
                $"Invalid contract id for {TypeName}: contract ids must be non-null and non-whitespace.");
        }

        try
        {
            return (TContractId)FromContractIdString.Invoke([value]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException inner)
        {
            throw new JsonException($"Invalid contract id for {TypeName}: {inner.Message}", inner);
        }
    }

    public override void Write(Utf8JsonWriter writer, TContractId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }

    private static string DescribeContractIdType(Type type) => type switch
    {
        { IsGenericType: true } => $"ContractId<{type.GetGenericArguments()[0].Name}>",
        { DeclaringType: not null } => $"{type.DeclaringType.Name}.{type.Name}",
        _ => type.Name,
    };
}
