// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Daml.Runtime.Streams;

/// <summary>
/// One entry in an active-contract-set snapshot. The snapshot yields
/// <see cref="Created"/> rows and an <see cref="Unclassified"/> row for anything
/// the projector cannot classify, then ends with a single terminal
/// <see cref="Checkpoint"/> — emitted even when the snapshot is empty — or, when
/// the transport faults mid-snapshot, a terminal <see cref="StreamError"/> in
/// place of that <see cref="Checkpoint"/>.
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
    /// even when the snapshot is empty — carrying the snapshot's effective offset
    /// as a <see cref="StakeholderResume"/> ticket.
    /// </summary>
    /// <param name="Resume">The resume ticket for the snapshot's effective offset — pass it to
    /// <c>ILedgerStreamer.SubscribeAsync</c> for a gapless, duplicate-free handover; that
    /// subscription's lower bound is exclusive, so the event at this offset is not re-delivered.
    /// The raw offset is reachable via <see cref="StakeholderResume.Offset"/>.</param>
    public sealed record Checkpoint(StakeholderResume Resume) : AcsSnapshotEntry<T>;

    /// <summary>
    /// The transport stream failed mid-snapshot. Surfaced in-band rather than
    /// thrown so a caller draining the snapshot with <c>await foreach</c> can
    /// decide policy — retry from a fresh snapshot, log, or stop — with the same
    /// value-not-exception handling it uses for
    /// <see cref="ContractStreamEvent{T}.StreamError"/> on the live subscription.
    /// </summary>
    /// <remarks>
    /// Terminal, and mutually exclusive with <see cref="Checkpoint"/>: a faulted
    /// snapshot ends with this entry instead of the <see cref="Checkpoint"/> a
    /// successful snapshot ends with, so no snapshot offset is available to hand
    /// over to a live subscription and the caller must treat the snapshot as
    /// incomplete.
    /// </remarks>
    /// <param name="StatusCode">Transport status code from the failed call.
    /// For gRPC streams this is <c>(int)Grpc.Core.StatusCode</c>; consumers that
    /// want the typed enum cast back. Held as <c>int</c> so this type stays free
    /// of any transport-library dep.</param>
    /// <param name="Message">Status detail / message from the participant or transport.</param>
    public sealed record StreamError(
        int StatusCode,
        string Message) : AcsSnapshotEntry<T>;
}
