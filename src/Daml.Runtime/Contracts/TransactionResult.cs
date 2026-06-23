// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;

namespace Daml.Runtime.Contracts;

/// <summary>
/// Result of a submitted transaction.
/// </summary>
/// <param name="UpdateId">Ledger-assigned update identifier.</param>
/// <param name="CompletionOffset">Offset at which the transaction was committed.</param>
/// <param name="CreatedContracts">Contracts created by the transaction. Project to
/// typed <see cref="ContractId{T}"/> values via <see cref="TransactionResultExtensions"/>.</param>
/// <param name="ArchivedContractIds">Raw contract IDs archived by the transaction.</param>
public sealed record TransactionResult(
    string UpdateId,
    long CompletionOffset,
    IReadOnlyList<CreatedContract> CreatedContracts,
    IReadOnlyList<string> ArchivedContractIds)
{
    /// <summary>
    /// Choice-exercise events observed in the transaction, in transaction order.
    /// Defaults to an empty list — populated by ledger-client transport
    /// implementations when the transaction was requested with
    /// ledger-effects shape. Codegen-emitted choice wrappers deserialize each
    /// <see cref="ExercisedEvent.ExerciseResult"/> through the appropriate typed
    /// projector to surface a typed <c>ExerciseOutcome&lt;TResult&gt;</c> for choices
    /// whose return type is not a contract id (e.g. <c>choice C : Decimal</c>).
    /// </summary>
    public IReadOnlyList<ExercisedEvent> ExercisedEvents { get; init; } = Array.Empty<ExercisedEvent>();
}

/// <summary>
/// Information about a contract created by a transaction.
/// </summary>
/// <param name="ContractId">The on-ledger contract ID.</param>
/// <param name="TemplateId">The template identifier (package + module + entity).</param>
/// <param name="Payload">The serialized payload of the contract.</param>
public sealed record CreatedContract(
    string ContractId,
    Identifier TemplateId,
    string Payload)
{
    /// <summary>
    /// Interface ids the participant computed for this created event
    /// (Canton gRPC <c>CreatedEvent.interface_views[].interface_id</c>).
    /// Defaults to an empty list — populated by ledger-client transport
    /// implementations for interface-only consumption, where a contract is
    /// known only as an interface and must be dispatched at runtime.
    /// </summary>
    public IReadOnlyList<Identifier> InterfaceIds { get; init; } = Array.Empty<Identifier>();
}
