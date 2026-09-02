// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;

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
public sealed class KeyDescriptor<TTemplate, TKey>
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
}
