// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;

namespace Daml.Ledger.Abstractions.Extensions;

/// <summary>
/// Throwing convenience over <see cref="ILedgerStreamer.SubscribeActiveAsync{T}"/> for the
/// common case: a caller who wants the active contracts and nothing else.
/// </summary>
public static class StreamerSnapshot
{
    /// <summary>
    /// Drains an active-contract-set snapshot into a materialized list of typed
    /// contracts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A snapshot that faults, carries a row the projector could not classify, or ends
    /// without its terminal checkpoint throws <see cref="LedgerOperationException"/>
    /// rather than shortening the returned list: a terminal
    /// <see cref="AcsSnapshotEntry{T}.StreamError"/> means the snapshot is incomplete, an
    /// <see cref="AcsSnapshotEntry{T}.Unclassified"/> row means a row was not projected,
    /// and a stream that ends without the documented terminal
    /// <see cref="AcsSnapshotEntry{T}.Checkpoint"/> means the transport truncated it.
    /// <see cref="AcsSnapshotEntry{T}.Created"/> rows are collected, and the checkpoint
    /// terminates the drain.
    /// A short list that looks complete is the failure mode this convenience exists to
    /// prevent; use <see cref="ILedgerStreamer.SubscribeActiveAsync{T}"/> directly for
    /// value-shaped handling of those cases.
    /// </para>
    /// <para>
    /// A create row whose payload did not decode into <typeparamref name="T"/> never reaches
    /// here as a create row: the projector surfaces it as
    /// <see cref="AcsSnapshotEntry{T}.Unclassified"/> with
    /// <see cref="UnclassifiedKind.DecodeFailure"/>, which this method reports as
    /// <see cref="LedgerOperationException"/> along with every other unclassified row.
    /// </para>
    /// <para>
    /// Cancelling <paramref name="cancellationToken"/> surfaces as
    /// <see cref="OperationCanceledException"/> even when the transport reports the cancelled
    /// call in-band as a <see cref="AcsSnapshotEntry{T}.StreamError"/> or by ending the stream
    /// short, matching the write-path conveniences in this namespace.
    /// </para>
    /// <para>
    /// The terminal checkpoint's <see cref="StakeholderResume"/> ticket is consumed and
    /// discarded, so this method cannot feed the gapless snapshot-to-stream handover.
    /// Stay on <see cref="ILedgerStreamer.SubscribeActiveAsync{T}"/> when the resume
    /// offset matters.
    /// </para>
    /// <para>
    /// The returned contracts carry no contract key. A keyed template's snapshot goes through
    /// the <see cref="SnapshotAsync{T, TKey}(ILedgerStreamer, KeyDescriptor{T, TKey}, SubmitterInfo, LedgerOffset?, CancellationToken)"/>
    /// overload, which yields <see cref="Contract{T, TKey}"/> with the key decoded.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The Daml template the snapshot is filtered to.</typeparam>
    /// <param name="streamer">The streaming capability.</param>
    /// <param name="submitter">The submitter authorization whose combined parties scope visibility.</param>
    /// <param name="activeAtOffset">Snapshot offset; <c>null</c> means the current ledger end.</param>
    /// <param name="cancellationToken">Cancels the underlying stream cleanly.</param>
    /// <exception cref="ArgumentNullException"><paramref name="streamer"/> is <c>null</c>.</exception>
    /// <exception cref="LedgerOperationException">
    /// The snapshot faulted, carried an unclassified row, or ended without its terminal
    /// checkpoint.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled.
    /// </exception>
    public static async Task<IReadOnlyList<Contract<T>>> SnapshotAsync<T>(
        this ILedgerStreamer streamer,
        SubmitterInfo submitter,
        LedgerOffset? activeAtOffset = null,
        CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T>
    {
        ArgumentNullException.ThrowIfNull(streamer);

        var rows = await DrainAsync<T>(streamer, submitter, activeAtOffset, cancellationToken)
            .ConfigureAwait(false);

        var contracts = new List<Contract<T>>(rows.Count);
        foreach (var row in rows)
        {
            contracts.Add(row.ToContract());
        }

        return contracts;
    }

    /// <summary>
    /// Drains an active-contract-set snapshot of a keyed template into a materialized list of
    /// typed contracts, each carrying its contract key decoded into
    /// <typeparamref name="TKey"/>.
    /// </summary>
    /// <remarks>
    /// Behaves exactly as
    /// <see cref="SnapshotAsync{T}(ILedgerStreamer, SubmitterInfo, LedgerOffset?, CancellationToken)"/>
    /// for every fault, unclassified row and truncation case, and additionally throws
    /// <see cref="LedgerOperationException"/> when a create row of a keyed template arrives
    /// without the key the keyed shape requires.
    /// </remarks>
    /// <typeparam name="T">The keyed Daml template the snapshot is filtered to.</typeparam>
    /// <typeparam name="TKey">The template's contract key type.</typeparam>
    /// <param name="streamer">The streaming capability.</param>
    /// <param name="key">The template's key witness. It is the type argument carrier — passing
    /// <c>Account.Key</c> infers both <typeparamref name="T"/> and <typeparamref name="TKey"/>
    /// from one argument, which C# cannot do from a partial type-argument list. The decode is
    /// taken from the template's own <see cref="IHasKey{TSelf, TKey}.Key"/> witness, so this
    /// overload and <see cref="Contract{T, TKey}.FromCreatedEvent"/> decode identically.</param>
    /// <param name="submitter">The submitter authorization whose combined parties scope visibility.</param>
    /// <param name="activeAtOffset">Snapshot offset; <c>null</c> means the current ledger end.</param>
    /// <param name="cancellationToken">Cancels the underlying stream cleanly.</param>
    /// <exception cref="ArgumentNullException"><paramref name="streamer"/> or
    /// <paramref name="key"/> is <c>null</c>.</exception>
    /// <exception cref="LedgerOperationException">
    /// The snapshot faulted, carried an unclassified row, ended without its terminal
    /// checkpoint, or carried a create row with no contract key.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled.
    /// </exception>
    public static async Task<IReadOnlyList<Contract<T, TKey>>> SnapshotAsync<T, TKey>(
        this ILedgerStreamer streamer,
        KeyDescriptor<T, TKey> key,
        SubmitterInfo submitter,
        LedgerOffset? activeAtOffset = null,
        CancellationToken cancellationToken = default)
        where T : ITemplate, IDamlRecord<T>, IHasKey<T, TKey>
    {
        ArgumentNullException.ThrowIfNull(streamer);
        ArgumentNullException.ThrowIfNull(key);
        _ = key; // type-inference carrier only; decode uses T.Key per IHasKey

        var rows = await DrainAsync<T>(streamer, submitter, activeAtOffset, cancellationToken)
            .ConfigureAwait(false);

        var contracts = new List<Contract<T, TKey>>(rows.Count);
        foreach (var row in rows)
        {
            contracts.Add(ToKeyedContract<T, TKey>(row));
        }

        return contracts;
    }

    private static Contract<T, TKey> ToKeyedContract<T, TKey>(AcsSnapshotEntry<T>.Created created)
        where T : ITemplate, IDamlRecord<T>, IHasKey<T, TKey>
    {
        if (created.Key is null)
            throw new LedgerOperationException(
                $"The active-contract-set snapshot for keyed template {typeof(T).Name} carried a "
                + $"create row for contract '{created.ContractId.Value}' with no contract key, so "
                + "the keyed contracts would be incomplete. Use the keyless SnapshotAsync overload "
                + "when the transport does not supply keys.");
        return created.ToContract<T, TKey>();
    }

    private static async Task<List<AcsSnapshotEntry<T>.Created>> DrainAsync<T>(
        ILedgerStreamer streamer,
        SubmitterInfo submitter,
        LedgerOffset? activeAtOffset,
        CancellationToken cancellationToken)
        where T : ITemplate, IDamlRecord<T>
    {
        var rows = new List<AcsSnapshotEntry<T>.Created>();
        var reachedCheckpoint = false;

        await foreach (var entry in streamer
            .SubscribeActiveAsync<T>(submitter, activeAtOffset, cancellationToken)
            .ConfigureAwait(false))
        {
            switch (entry)
            {
                case AcsSnapshotEntry<T>.Created created:
                    rows.Add(created);
                    break;
                case AcsSnapshotEntry<T>.Checkpoint:
                    reachedCheckpoint = true;
                    goto done;
                case AcsSnapshotEntry<T>.StreamError error:
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new LedgerOperationException(
                        $"The active-contract-set snapshot for {typeof(T).Name} faulted after {rows.Count} "
                        + $"contract(s): {error.Message}. Use SubscribeActiveAsync for value-shaped fault handling.",
                        error.StatusCode,
                        error.Category,
                        error.SourceException);
                case AcsSnapshotEntry<T>.Unclassified unclassified:
                    throw new LedgerOperationException(
                        $"The active-contract-set snapshot for {typeof(T).Name} carried an unclassified row "
                        + $"({DescribeKind(unclassified)}) {DescribePosition(unclassified.Offset)}, so the returned "
                        + "contracts would be incomplete. Use SubscribeActiveAsync to handle it as a value.");
                default:
                    throw new LedgerOperationException(
                        $"Unexpected snapshot entry {entry.GetType().Name} for {typeof(T).Name}.");
            }
        }
        done:

        if (!reachedCheckpoint)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new LedgerOperationException(
                $"The active-contract-set snapshot for {typeof(T).Name} ended after {rows.Count} contract(s) "
                + "without its terminal checkpoint, so the returned contracts would be incomplete.");
        }

        return rows;
    }

    private static string DescribeKind<T>(AcsSnapshotEntry<T>.Unclassified unclassified)
        where T : ITemplate, IDamlRecord<T> =>
        unclassified.RawKind is null
            ? unclassified.Kind.ToString()
            : $"{unclassified.Kind}: '{unclassified.RawKind}'";

    private static string DescribePosition(LedgerOffset? offset) =>
        offset is { } at ? $"at offset {at}" : "with no ledger offset";
}
