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
    /// Streams contract events for <typeparamref name="T"/> over the half-open
    /// offset window <c>(fromOffset, toOffset]</c> — lower bound exclusive, upper
    /// bound inclusive.
    /// </summary>
    /// <remarks>
    /// Uses ledger-effects transaction shape. The stream emits
    /// <see cref="ContractStreamEvent{T}.Created"/> and
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
    /// ACS-delta-shaped streams. A consumer maintaining a contract cache must
    /// therefore evict on the consuming
    /// <see cref="ContractStreamEvent{T}.Exercised"/>, not on
    /// <see cref="ContractStreamEvent{T}.Archived"/> — waiting on the latter here
    /// leaves archived contracts cached forever. For an event boundary (e.g. the
    /// target contract's consuming <see cref="ContractStreamEvent{T}.Exercised"/>
    /// event), <c>break</c> out of the enumeration; for an offset boundary, pass
    /// <paramref name="toOffset"/>.
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
    IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
        SubmitterInfo submitter,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default)
        where T : IDamlType;

    /// <summary>Streams the active-contract-set snapshot for <typeparamref name="T"/>.</summary>
    /// <remarks>
    /// The snapshot stream always terminates with a single terminal
    /// <see cref="AcsSnapshotEntry{T}.Checkpoint"/> carrying the snapshot's
    /// effective offset — emitted even when the snapshot is empty. Resume a live
    /// <see cref="SubscribeAsync{T}"/> from that offset for a gapless,
    /// duplicate-free handover: because <see cref="SubscribeAsync{T}"/> treats its
    /// lower bound as exclusive, the event at the snapshot offset is not delivered
    /// twice across the handover.
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
}
