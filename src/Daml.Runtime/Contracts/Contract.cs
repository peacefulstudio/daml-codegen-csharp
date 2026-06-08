// Copyright (c) 2026 Peaceful Studio OÜ
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
/// Base record for generated contracts.
/// </summary>
/// <typeparam name="T">The template type.</typeparam>
public sealed record Contract<T>(ContractId<T> Id, T Data) : IContract<ContractId<T>, T>
    where T : ITemplate
{
    /// <summary>
    /// Creates a contract from a ledger event.
    /// </summary>
    public static Contract<T> FromCreatedEvent(CreatedEvent @event, Func<DamlRecord, T> decoder)
    {
        var id = new ContractId<T>(@event.ContractId);
        var data = decoder(@event.CreateArguments);
        return new Contract<T>(id, data);
    }
}
