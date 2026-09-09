// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Daml.Runtime.Streams;

/// <summary>
/// Projects the payload-carrying arms of a contract stream into the shape the rest of the
/// runtime speaks. A stream row's create-arguments arrive already decoded into the template
/// type, so no decoding helper is needed here.
/// </summary>
public static class ContractStreamEventExtensions
{
    /// <summary>
    /// Projects a created stream row to the keyless <see cref="Contract{T}"/> shape — the same
    /// pairing <c>Contract&lt;T&gt;.FromCreatedEvent</c> produces on the write path. The row's
    /// wire-level key is not carried: a keyed template's row goes through
    /// <see cref="ToContract{T, TKey}(ContractStreamEvent{T}.Created)"/>, which decodes it.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="created"/> is <c>null</c>.</exception>
    public static Contract<T> ToContract<T>(this ContractStreamEvent<T>.Created created)
        where T : ITemplate, IDamlRecord<T>
    {
        ArgumentNullException.ThrowIfNull(created);
        return new Contract<T>(created.ContractId, created.Payload);
    }

    /// <summary>
    /// Projects an assigned stream row to the keyless <see cref="Contract{T}"/> shape. An
    /// assignment re-emits the whole created contract, so the pairing is the one
    /// <see cref="ToContract{T}(ContractStreamEvent{T}.Created)"/> produces. The row's
    /// wire-level key is not carried.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="assigned"/> is <c>null</c>.</exception>
    public static Contract<T> ToContract<T>(this ContractStreamEvent<T>.Assigned assigned)
        where T : ITemplate, IDamlRecord<T>
    {
        ArgumentNullException.ThrowIfNull(assigned);
        return new Contract<T>(assigned.ContractId, assigned.Payload);
    }

    /// <summary>
    /// Projects a created stream row to the keyed <see cref="Contract{T, TKey}"/> shape, decoding
    /// the row's wire-level key through the template's <see cref="IHasKey{TSelf, TKey}.Key"/>
    /// witness and carrying the ledger's hash of it.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="created"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">The row carried no contract key, so the keyed
    /// shape cannot be populated.</exception>
    public static Contract<T, TKey> ToContract<T, TKey>(this ContractStreamEvent<T>.Created created)
        where T : ITemplate, IDamlRecord<T>, IHasKey<T, TKey>
    {
        ArgumentNullException.ThrowIfNull(created);
        return new Contract<T, TKey>(
            created.ContractId,
            created.Payload,
            Contract<T, TKey>.DecodeKey(created.Key, created.ContractId.Value));
    }

    /// <summary>
    /// Projects an assigned stream row to the keyed <see cref="Contract{T, TKey}"/> shape, decoding
    /// the row's wire-level key through the template's <see cref="IHasKey{TSelf, TKey}.Key"/>
    /// witness and carrying the ledger's hash of it. This is the hop that keeps the typed key
    /// across a reassignment.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="assigned"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">The row carried no contract key, so the keyed
    /// shape cannot be populated.</exception>
    public static Contract<T, TKey> ToContract<T, TKey>(this ContractStreamEvent<T>.Assigned assigned)
        where T : ITemplate, IDamlRecord<T>, IHasKey<T, TKey>
    {
        ArgumentNullException.ThrowIfNull(assigned);
        return new Contract<T, TKey>(
            assigned.ContractId,
            assigned.Payload,
            Contract<T, TKey>.DecodeKey(assigned.Key, assigned.ContractId.Value));
    }
}
