// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;

namespace Daml.Runtime.Contracts;

/// <summary>
/// Represents an active contract on the ledger.
/// </summary>
/// <typeparam name="TId">The contract ID type.</typeparam>
/// <typeparam name="TData">The template data type.</typeparam>
public interface IContract<TId, TData>
    where TData : ITemplate
{
    /// <summary>
    /// Gets the contract ID.
    /// </summary>
    TId Id { get; }

    /// <summary>
    /// Gets the contract payload data.
    /// </summary>
    TData Data { get; }
}

/// <summary>
/// A contract key decoded into the template's key type, paired with the ledger's hash of it.
/// </summary>
/// <typeparam name="TKey">The template's contract key type.</typeparam>
/// <param name="Value">The key, decoded through the template's
/// <see cref="KeyDescriptor{TTemplate, TKey}.KeyDecoder"/>.</param>
/// <param name="Hash">The ledger's hash of the key — the value Canton indexes keyed contracts
/// by, as the base64 text the JSON encoding uses. It is Canton-computed over the key and the
/// template id, so it cannot be reconstructed here; a <c>null</c> is a stated absence, meaning
/// the created event carried no hash.</param>
public sealed record ContractKey<TKey>(TKey Value, string? Hash);

/// <summary>
/// Base record for generated contracts of a template that declares no contract key. A keyed
/// template's contracts use <see cref="Contract{T, TKey}"/>, which carries the decoded key.
/// </summary>
/// <typeparam name="T">The template type.</typeparam>
/// <param name="Id">The on-ledger contract ID.</param>
/// <param name="Data">The contract payload data.</param>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1000:Do not declare static members on generic types",
    Justification = "The static factory is the wire-decoding entry point for this Daml stdlib shape; generated code calls it as Contract<...>.FromCreatedEvent, mirroring the Daml constructor it decodes.")]
public sealed record Contract<T>(ContractId<T> Id, T Data) : IContract<ContractId<T>, T>
    where T : ITemplate
{
    /// <summary>
    /// Creates a contract from a ledger event.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="event"/> or
    /// <paramref name="decoder"/> is <c>null</c>.</exception>
    public static Contract<T> FromCreatedEvent(CreatedEvent @event, Func<DamlRecord, T> decoder)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(decoder);

        var id = new ContractId<T>(@event.ContractId);
        var data = decoder(@event.CreateArguments);
        return new Contract<T>(id, data);
    }
}

/// <summary>
/// Base record for generated contracts of a template that declares a contract key. The key is
/// non-nullable: this type argument pair exists only for a template whose
/// <see cref="IHasKey{TSelf, TKey}"/> facet declares the link, so every contract of that
/// template carries one.
/// </summary>
/// <typeparam name="T">The keyed template type.</typeparam>
/// <typeparam name="TKey">The template's contract key type.</typeparam>
/// <param name="Id">The on-ledger contract ID.</param>
/// <param name="Data">The contract payload data.</param>
/// <param name="Key">The contract key, decoded into <typeparamref name="TKey"/> and carrying
/// the ledger's hash of it.</param>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1000:Do not declare static members on generic types",
    Justification = "The static factory is the wire-decoding entry point for this Daml stdlib shape; generated code calls it as Contract<...>.FromCreatedEvent, mirroring the Daml constructor it decodes.")]
public sealed record Contract<T, TKey>(ContractId<T> Id, T Data, ContractKey<TKey> Key)
    : IContract<ContractId<T>, T>
    where T : ITemplate, IHasKey<T, TKey>
{
    /// <summary>
    /// Creates a keyed contract from a ledger event, decoding the key through the template's
    /// <see cref="IHasKey{TSelf, TKey}.Key"/> witness.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="event"/> or
    /// <paramref name="decoder"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">The event carried no contract key, so the
    /// keyed shape cannot be populated.</exception>
    public static Contract<T, TKey> FromCreatedEvent(CreatedEvent @event, Func<DamlRecord, T> decoder)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(decoder);

        var id = new ContractId<T>(@event.ContractId);
        var data = decoder(@event.CreateArguments);
        return new Contract<T, TKey>(id, data, DecodeKey(@event.ContractKey, @event.ContractId));
    }

    internal static ContractKey<TKey> DecodeKey(ContractKey? key, string contractId) =>
        key is null
            ? throw new InvalidOperationException(
                $"The created event for contract '{contractId}' of keyed template "
                + $"'{typeof(T).FullName}' carried no contract key, so the keyed contract shape "
                + "cannot be populated. Read this contract through the keyless overload when the "
                + "transport does not supply the key.")
            : new ContractKey<TKey>(T.Key.KeyDecoder(key.Value), key.KeyHash);
}
