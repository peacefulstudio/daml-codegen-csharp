// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Daml.Runtime.Streams;

/// <summary>
/// One entry in an active-contract-set snapshot. The snapshot only ever yields
/// <see cref="Created"/> rows, an <see cref="Unclassified"/> row for anything the
/// projector cannot classify, and always ends with a single terminal
/// <see cref="Checkpoint"/> — emitted even when the snapshot is empty.
/// </summary>
/// <typeparam name="T">
/// The Daml marker the snapshot is filtered to: a template (matched by
/// <c>TemplateId</c>) or a Daml interface marker (matched by interface id).
/// </typeparam>
public abstract record AcsSnapshotEntry<T>
    where T : IDamlType
{
    /// <summary>Sealed; new variants live alongside the existing ones.</summary>
    private protected AcsSnapshotEntry() { }

    /// <summary>
    /// An active contract in the snapshot.
    /// </summary>
    /// <param name="ContractId">The on-ledger contract ID.</param>
    /// <param name="Payload">The deserialized create-arguments record.</param>
    /// <param name="Offset">The ledger offset at which the contract was created.</param>
    /// <param name="SynchronizerId">The synchronizer the contract is active on.</param>
    /// <param name="WitnessParties">Parties that witnessed the create event.</param>
    public sealed record Created(
        ContractId<T> ContractId,
        DamlRecord Payload,
        LedgerOffset Offset,
        SynchronizerId SynchronizerId,
        IReadOnlyList<Party> WitnessParties) : AcsSnapshotEntry<T>;

    /// <summary>
    /// A snapshot row the projector could not classify; surfaced, never dropped.
    /// </summary>
    /// <param name="Offset">The ledger offset at which the unrecognized row occurred.</param>
    /// <param name="Kind">A short description of the unrecognized row, for logging/diagnostics.</param>
    public sealed record Unclassified(
        LedgerOffset Offset,
        string Kind) : AcsSnapshotEntry<T>;

    /// <summary>
    /// The single terminal marker that always ends the snapshot stream — emitted
    /// even when the snapshot is empty — carrying the snapshot's effective offset.
    /// </summary>
    /// <param name="Offset">The offset the snapshot is valid at — resume a live
    /// <c>ILedgerStreamer.SubscribeAsync</c> from this offset for a gapless,
    /// duplicate-free handover; that subscription's lower bound is exclusive, so
    /// the event at this offset is not re-delivered.</param>
    public sealed record Checkpoint(LedgerOffset Offset) : AcsSnapshotEntry<T>;
}
