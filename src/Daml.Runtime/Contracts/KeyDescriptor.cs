// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;

namespace Daml.Runtime.Contracts;

/// <summary>
/// Type witness pairing a keyed template with its key type and carrying the codec between the
/// wire value and that key. Generated keyed templates expose a singleton through the
/// <see cref="IHasKey{TSelf, TKey}.Key"/> static abstract member, so a call site passing it to a
/// generic method lets the compiler infer both type parameters from one argument — C# performs no
/// partial type-argument inference, so the pair must travel together. Because the codec rides on
/// the descriptor, <typeparamref name="TKey"/> needs no record constraint and a bare
/// <see cref="Party"/> key is admitted on the same footing as a record key. A mismatched pair is
/// unconstructible: the constraints tie <typeparamref name="TKey"/> to
/// <typeparamref name="TTemplate"/> through <see cref="IHasKey{TSelf, TKey}"/>.
/// </summary>
/// <typeparam name="TTemplate">The keyed template type.</typeparam>
/// <typeparam name="TKey">The template's contract key type.</typeparam>
public sealed class KeyDescriptor<TTemplate, TKey> : IKeyDescriptor
    where TTemplate : ITemplate, IHasKey<TTemplate, TKey>
{
    /// <summary>
    /// Gets the function encoding a <typeparamref name="TKey"/> into the ledger's key value, the
    /// form <see cref="Commands.ExerciseByKeyCommand"/> carries.
    /// </summary>
    public required Func<TKey, DamlValue> KeyEncoder { get; init; }

    /// <summary>
    /// Gets the function decoding the ledger's key value into <typeparamref name="TKey"/>.
    /// </summary>
    public required Func<DamlValue, TKey> KeyDecoder { get; init; }

    /// <summary>
    /// Gets the function reading the key's Daml-LF JSON into its wire value.
    /// </summary>
    public required Func<JsonElement, DamlLfJsonDecodeContext, DamlValue> KeyJsonReader { get; init; }

    /// <summary>
    /// Gets the function decoding the key from Daml-LF JSON.
    /// </summary>
    public Func<JsonElement, DamlLfJsonDecodeContext, TKey> KeyJsonDecoder =>
        (json, context) => KeyDecoder(KeyJsonReader(json, context));

    Type IKeyDescriptor.TemplateType => typeof(TTemplate);

    Type IKeyDescriptor.KeyType => typeof(TKey);

    DamlValue IKeyDescriptor.ReadKeyJson(JsonElement json, DamlLfJsonDecodeContext context) => KeyJsonReader(json, context);

    object? IKeyDescriptor.DecodeKey(DamlValue value) => KeyDecoder(value);
}

/// <summary>
/// Erased facet of <see cref="KeyDescriptor{TTemplate, TKey}"/>, letting a call site that knows
/// neither type parameter read and decode a contract key.
/// </summary>
public interface IKeyDescriptor
{
    /// <summary>Gets the CLR type of the keyed template.</summary>
    Type TemplateType { get; }

    /// <summary>Gets the CLR type of the contract key.</summary>
    Type KeyType { get; }

    /// <summary>Reads the key's Daml-LF JSON into its wire value.</summary>
    DamlValue ReadKeyJson(JsonElement json, DamlLfJsonDecodeContext context);

    /// <summary>Decodes a wire key value into a boxed key.</summary>
    object? DecodeKey(DamlValue value);
}
