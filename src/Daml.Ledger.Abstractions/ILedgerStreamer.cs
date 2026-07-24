// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.Threading;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Streams;

namespace Daml.Ledger.Abstractions;

/// <summary>The streaming capability: subscribe to contract events and ACS snapshots.</summary>
public interface ILedgerStreamer
{
    /// <summary>
    /// Streams active-contract-set delta events for <typeparamref name="T"/> over the
    /// half-open offset window <c>(fromOffset, toOffset]</c> — lower bound exclusive,
    /// upper bound inclusive.
    /// </summary>
    /// <remarks>
    /// Uses ACS-delta transaction shape with stakeholder-based visibility — the same
    /// visibility basis as the <see cref="SubscribeActiveAsync{T}"/> snapshot, so the
    /// live stream and the snapshot reconstruct the same contract set: a snapshot
    /// followed by a resume from its offset rebuilds exactly the contracts the
    /// snapshot held, plus every subsequent delta, with no divergence. The stream
    /// emits <see cref="ContractStreamEvent{T}.Created"/> and
    /// <see cref="ContractStreamEvent{T}.Archived"/> events — an archival is a
    /// first-class <see cref="ContractStreamEvent{T}.Archived"/> event, not a
    /// consuming exercise — plus
    /// <see cref="ContractStreamEvent{T}.Assigned"/>/<see cref="ContractStreamEvent{T}.Unassigned"/>
    /// for cross-synchronizer reassignments,
    /// <see cref="ContractStreamEvent{T}.Checkpoint"/>,
    /// <see cref="ContractStreamEvent{T}.StreamError"/>, and
    /// <see cref="ContractStreamEvent{T}.Unclassified"/>. It never emits
    /// <see cref="ContractStreamEvent{T}.Exercised"/>; that variant appears only on
    /// the ledger-effects shape exposed by <see cref="SubscribeLedgerEffectsAsync{T}"/>.
    /// A consumer maintaining a contract cache evicts on
    /// <see cref="ContractStreamEvent{T}.Archived"/> and checkpoints on the last
    /// observed offset — the documented cache/checkpoint pattern is sound on this
    /// shape. For an event boundary, <c>break</c> out of the enumeration; for an
    /// offset boundary, pass <paramref name="toOffset"/>.
    /// </remarks>
    /// <typeparam name="T">A Daml template or interface marker.</typeparam>
    /// <param name="submitter">The submitter authorization whose combined parties scope visibility.</param>
    /// <param name="fromOffset">
    /// Exclusive lower bound: the stream resumes strictly after this offset, so the
    /// event at <paramref name="fromOffset"/> is never delivered. <c>null</c> means
    /// <see cref="LedgerOffset.Begin"/>. Because the bound is exclusive, resuming
    /// from a previously returned offset — a persisted
    /// <see cref="ContractStreamEvent{T}.Checkpoint"/> offset or a completion
    /// offset — never re-delivers the event already seen at that offset.
    /// See <see href="https://docs.canton.network/reference/json-api-reference/post-v2updates">Canton
    /// Ledger API — update stream offsets (begin exclusive, end inclusive)</see>.
    /// </param>
    /// <param name="toOffset">
    /// Inclusive, terminal upper bound: the event at <paramref name="toOffset"/> is
    /// delivered and then the bounded stream completes. <c>null</c> follows the live
    /// stream, which does not complete on its own.
    /// </param>
    /// <param name="cancellationToken">Cancels the underlying stream cleanly.</param>
    IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
        SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default)
        where T : IDamlType;

    /// <summary>
    /// Resumes the stakeholder-based live stream from a <see cref="SubscribeActiveAsync{T}"/>
    /// snapshot's terminal checkpoint — the typed counterpart of
    /// <see cref="SubscribeAsync{T}(SubmitterInfo, LedgerOffset?, LedgerOffset?, CancellationToken)"/>
    /// that guards the snapshot↔stream pairing at compile time.
    /// </summary>
    /// <remarks>
    /// Default implementation forwards to
    /// <see cref="SubscribeAsync{T}(SubmitterInfo, LedgerOffset?, LedgerOffset?, CancellationToken)"/>
    /// via <see cref="StakeholderResume.Offset"/>; implementations may override for a more
    /// direct path.
    /// </remarks>
    /// <typeparam name="T">A Daml template or interface marker.</typeparam>
    /// <param name="submitter">The submitter authorization whose combined parties scope visibility.</param>
    /// <param name="resumeFrom">The resume ticket from a <see cref="SubscribeActiveAsync{T}"/>
    /// snapshot's terminal <see cref="AcsSnapshotEntry{T}.Checkpoint"/>.</param>
    /// <param name="toOffset">
    /// Inclusive, terminal upper bound: the event at <paramref name="toOffset"/> is
    /// delivered and then the bounded stream completes. <c>null</c> follows the live
    /// stream, which does not complete on its own.
    /// </param>
    /// <param name="cancellationToken">Cancels the underlying stream cleanly.</param>
    IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
        SubmitterInfo submitter,
        StakeholderResume resumeFrom,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default)
        where T : IDamlType =>
        SubscribeAsync<T>(submitter, resumeFrom.Offset, toOffset, cancellationToken);

    /// <summary>Streams the active-contract-set snapshot for <typeparamref name="T"/>.</summary>
    /// <remarks>
    /// A successful snapshot stream terminates with a single terminal
    /// <see cref="AcsSnapshotEntry{T}.Checkpoint"/> carrying the snapshot's
    /// effective offset as a <see cref="StakeholderResume"/> ticket — emitted even
    /// when the snapshot is empty. Resume a live
    /// <see cref="SubscribeAsync{T}(SubmitterInfo, StakeholderResume, LedgerOffset?, CancellationToken)"/>
    /// from that ticket for a gapless, duplicate-free handover: because that overload
    /// treats its lower bound as exclusive, the event at the snapshot offset is not
    /// delivered twice across the handover. A mid-snapshot transport fault surfaces
    /// in-band as a terminal <see cref="AcsSnapshotEntry{T}.StreamError"/> in place of
    /// that <see cref="AcsSnapshotEntry{T}.Checkpoint"/>, so a caller draining the
    /// snapshot handles faults as values rather than exceptions — the same fault
    /// contract as <see cref="SubscribeAsync{T}(SubmitterInfo, LedgerOffset?, LedgerOffset?, CancellationToken)"/>.
    /// See <see href="https://docs.canton.network/reference/json-api-asyncapi-reference/operations/v2-state-active-contracts/details">Canton
    /// Ledger API — active contracts snapshot and its active-at offset</see> for the snapshot→stream
    /// handover this mirrors.
    /// </remarks>
    /// <typeparam name="T">A Daml template or interface marker.</typeparam>
    /// <param name="submitter">The submitter authorization whose combined parties scope visibility.</param>
    /// <param name="activeAtOffset">Snapshot offset; <c>null</c> means the current ledger end.</param>
    /// <param name="cancellationToken">Cancels the underlying stream cleanly.</param>
    IAsyncEnumerable<AcsSnapshotEntry<T>> SubscribeActiveAsync<T>(
        SubmitterInfo submitter,
        LedgerOffset? activeAtOffset = null,
        CancellationToken cancellationToken = default)
        where T : IDamlType;

    /// <summary>
    /// Streams ledger-effects events for <typeparamref name="T"/> over the half-open
    /// offset window <c>(fromOffset, toOffset]</c> — lower bound exclusive, upper
    /// bound inclusive.
    /// </summary>
    /// <remarks>
    /// Uses ledger-effects transaction shape with witness-based visibility. The
    /// stream emits <see cref="ContractStreamEvent{T}.Created"/> and
    /// <see cref="ContractStreamEvent{T}.Exercised"/> events — a consuming exercise
    /// (<see cref="ContractStreamEvent{T}.Exercised.Consuming"/> is <c>true</c>) is a
    /// contract's archival on this shape, so there is no separate
    /// <see cref="ContractStreamEvent{T}.Archived"/> event — plus
    /// <see cref="ContractStreamEvent{T}.Assigned"/>/<see cref="ContractStreamEvent{T}.Unassigned"/>
    /// for cross-synchronizer reassignments,
    /// <see cref="ContractStreamEvent{T}.Checkpoint"/>,
    /// <see cref="ContractStreamEvent{T}.StreamError"/>, and
    /// <see cref="ContractStreamEvent{T}.Unclassified"/>. It never emits
    /// <see cref="ContractStreamEvent{T}.Archived"/>; that variant appears only on
    /// the ACS-delta shape exposed by
    /// <see cref="SubscribeAsync{T}(SubmitterInfo, LedgerOffset?, LedgerOffset?, CancellationToken)"/>.
    /// A consumer maintaining a contract cache must therefore evict on the consuming
    /// <see cref="ContractStreamEvent{T}.Exercised"/>, not on
    /// <see cref="ContractStreamEvent{T}.Archived"/> — waiting on the latter here
    /// leaves archived contracts cached forever.
    /// <para>
    /// This shape matches on witnesses, whereas <see cref="SubscribeActiveAsync{T}"/>
    /// matches on stakeholders, so a snapshot's <see cref="StakeholderResume"/> ticket
    /// only resumes <see cref="SubscribeAsync{T}(SubmitterInfo, StakeholderResume, LedgerOffset?, CancellationToken)"/>
    /// — there is no overload of this method accepting it; use
    /// <see cref="StakeholderResume.Offset"/> if a cross-basis resume is deliberate.
    /// </para>
    /// For an event boundary (e.g. the target contract's consuming
    /// <see cref="ContractStreamEvent{T}.Exercised"/> event), <c>break</c> out of the
    /// enumeration; for an offset boundary, pass <paramref name="toOffset"/>.
    /// </remarks>
    /// <typeparam name="T">A Daml template or interface marker.</typeparam>
    /// <param name="submitter">The submitter authorization whose combined parties scope visibility.</param>
    /// <param name="fromOffset">
    /// Exclusive lower bound: the stream resumes strictly after this offset, so the
    /// event at <paramref name="fromOffset"/> is never delivered. <c>null</c> means
    /// <see cref="LedgerOffset.Begin"/>. Because the bound is exclusive, resuming
    /// from a previously returned offset — a persisted
    /// <see cref="ContractStreamEvent{T}.Checkpoint"/> offset or a completion
    /// offset — never re-delivers the event already seen at that offset.
    /// </param>
    /// <param name="toOffset">
    /// Inclusive, terminal upper bound: the event at <paramref name="toOffset"/> is
    /// delivered and then the bounded stream completes. <c>null</c> follows the live
    /// stream, which does not complete on its own.
    /// </param>
    /// <param name="cancellationToken">Cancels the underlying stream cleanly.</param>
    IAsyncEnumerable<ContractStreamEvent<T>> SubscribeLedgerEffectsAsync<T>(
        SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default)
        where T : IDamlType;
}
