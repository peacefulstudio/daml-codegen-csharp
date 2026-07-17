// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Streams;
using Xunit;

namespace Daml.Ledger.Abstractions.Testing.Conformance;

/// <summary>
/// The documented behavioral contract for an <see cref="ILedgerClient"/> implementation.
/// Adopters subclass with a concrete client factory and a probe Daml marker; the seeded
/// client must expose the canonical scenario (at least one active contract, one
/// unclassifiable row, a terminal checkpoint, at least one event on the subscription
/// stream, honored <c>(fromOffset, toOffset]</c> bounds, and cancellation-honoring
/// streams).
/// </summary>
/// <typeparam name="TProbe">The Daml marker the seeded snapshot/stream is filtered to.</typeparam>
public abstract class LedgerClientConformanceTests<TProbe>
    where TProbe : IDamlType
{
    /// <summary>Creates a client seeded with the canonical conformance scenario.</summary>
    protected abstract ILedgerClient CreateClient();

    /// <summary>The submitter whose visibility scopes the reads.</summary>
    protected abstract SubmitterInfo Reader { get; }

    /// <summary>
    /// The budget within which a stream the contract requires to terminate (an ACS
    /// snapshot, a bounded subscription) must complete. Adopters whose transport is
    /// slower to seed may widen it.
    /// </summary>
    protected virtual TimeSpan StreamTimeout => TimeSpan.FromSeconds(30);

    /// <summary>
    /// The offset at which the seeded client exposes an empty active-contract-set
    /// snapshot. Defaults to <see cref="LedgerOffset.Begin"/> (offset 0). Adopters whose
    /// transport rejects an active-contract-set query at offset 0 with
    /// <c>INVALID_ARGUMENT</c> override this to a known-empty offset their transport accepts.
    /// </summary>
    protected virtual LedgerOffset EmptySnapshotOffset => LedgerOffset.Begin;

    /// <summary>A cancelled live subscription surfaces cancellation, not an in-band error.</summary>
    [Fact]
    public async Task Cancelling_a_live_subscription_throws_OperationCanceledException()
    {
        await using var client = CreateClient();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => DrainWithinBudget(
            client.SubscribeAsync<TProbe>(Reader, cancellationToken: cts.Token),
            "a cancelled live subscription must throw OperationCanceledException, not ignore the token");

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>An unclassifiable snapshot row is surfaced, never silently dropped.</summary>
    [Fact]
    public async Task Active_snapshot_surfaces_unclassifiable_rows_as_Unclassified()
    {
        await using var client = CreateClient();

        var entries = await CollectSnapshot(client);

        entries.Should().Contain(e => e is AcsSnapshotEntry<TProbe>.Unclassified);
    }

    /// <summary>The snapshot's final entry is the terminal checkpoint.</summary>
    [Fact]
    public async Task Active_snapshot_ends_with_a_terminal_Checkpoint()
    {
        await using var client = CreateClient();

        var entries = await CollectSnapshot(client);

        entries.Should().NotBeEmpty();
        entries[^1].Should().BeOfType<AcsSnapshotEntry<TProbe>.Checkpoint>();
    }

    /// <summary>Seeded active contracts arrive before the checkpoint; the stream is not truncated.</summary>
    [Fact]
    public async Task Active_snapshot_yields_seeded_rows_before_the_checkpoint()
    {
        await using var client = CreateClient();

        var entries = await CollectSnapshot(client);

        entries.Count(e => e is AcsSnapshotEntry<TProbe>.Created).Should().BeGreaterThan(0);
        entries.SkipLast(1).Should().NotContain(e => e is AcsSnapshotEntry<TProbe>.Checkpoint);
    }

    /// <summary>An empty snapshot still terminates with the single terminal checkpoint.</summary>
    [Fact]
    public async Task Empty_active_snapshot_still_ends_with_a_terminal_Checkpoint()
    {
        await using var client = CreateClient();

        var entries = await CollectSnapshot(client, EmptySnapshotOffset);

        entries.Should().ContainSingle()
            .Which.Should().BeOfType<AcsSnapshotEntry<TProbe>.Checkpoint>();
    }

    /// <summary>fromOffset is exclusive: resuming from an offset does not re-deliver the event at it.</summary>
    [Fact]
    public async Task Subscribing_from_an_offset_excludes_the_event_at_that_offset()
    {
        await using var client = CreateClient();
        var end = await client.GetLedgerEndAsync();

        var all = await CollectBounded(client, LedgerOffset.Begin, end);
        all.Should().NotBeEmpty(
            "the conformance scenario must seed at least one event on the subscription stream");
        var resumeFrom = OffsetOf(all[0]);

        var resumed = await CollectBounded(client, resumeFrom, end);

        resumed.Should().NotContain(
            e => OffsetOf(e) == resumeFrom,
            "fromOffset is exclusive: resuming from an offset must not re-deliver the event at it");
    }

    /// <summary>toOffset is inclusive and terminal: the event at toOffset is delivered, then the stream completes.</summary>
    [Fact]
    public async Task Bounded_subscription_delivers_the_event_at_toOffset_then_completes()
    {
        await using var client = CreateClient();
        var end = await client.GetLedgerEndAsync();

        var all = await CollectBounded(client, LedgerOffset.Begin, end);
        all.Should().NotBeEmpty(
            "the conformance scenario must seed at least one event on the subscription stream");
        var boundary = OffsetOf(all[0]);

        var bounded = await CollectBounded(client, LedgerOffset.Begin, boundary);

        bounded.Should().Contain(
            e => OffsetOf(e) == boundary,
            "toOffset is inclusive: the event at toOffset must be delivered");
        bounded.Should().OnlyContain(
            e => OffsetOf(e).Value <= boundary.Value,
            "a bounded subscription must complete at toOffset and deliver nothing past it");
    }

    private Task<IReadOnlyList<AcsSnapshotEntry<TProbe>>> CollectSnapshot(
        ILedgerClient client, LedgerOffset? activeAtOffset = null) =>
        CollectWithinBudget(
            client.SubscribeActiveAsync<TProbe>(Reader, activeAtOffset),
            "SubscribeActiveAsync must terminate with a terminal Checkpoint");

    private Task<IReadOnlyList<ContractStreamEvent<TProbe>>> CollectBounded(
        ILedgerClient client, LedgerOffset? fromOffset, LedgerOffset toOffset) =>
        CollectWithinBudget(
            client.SubscribeAsync<TProbe>(Reader, fromOffset, toOffset),
            $"A bounded SubscribeAsync (toOffset {toOffset.Value}) must complete");

    private async Task DrainWithinBudget<TItem>(
        IAsyncEnumerable<TItem> stream, string cancellationContract)
    {
        var drain = DrainToCompletion(stream);
        if (await Task.WhenAny(drain, Task.Delay(StreamTimeout)) != drain)
        {
            throw new TimeoutException($"{cancellationContract}; nothing observed within {StreamTimeout}.");
        }

        await drain;
    }

    private static async Task DrainToCompletion<TItem>(IAsyncEnumerable<TItem> stream)
    {
        await foreach (var _ in stream)
        {
        }
    }

    private async Task<IReadOnlyList<TItem>> CollectWithinBudget<TItem>(
        IAsyncEnumerable<TItem> stream, string terminationContract)
    {
        using var cts = new CancellationTokenSource(StreamTimeout);
        var items = new List<TItem>();
        try
        {
            await foreach (var item in stream.WithCancellation(cts.Token))
            {
                items.Add(item);
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            throw new TimeoutException($"{terminationContract}; not observed within {StreamTimeout}.");
        }

        return items;
    }

    private static LedgerOffset OffsetOf(ContractStreamEvent<TProbe> e) => e switch
    {
        ContractStreamEvent<TProbe>.Created c => c.Offset,
        ContractStreamEvent<TProbe>.Archived a => a.Offset,
        ContractStreamEvent<TProbe>.Exercised x => x.Offset,
        ContractStreamEvent<TProbe>.Assigned a => a.Offset,
        ContractStreamEvent<TProbe>.Unassigned u => u.Offset,
        ContractStreamEvent<TProbe>.Checkpoint cp => cp.Offset,
        ContractStreamEvent<TProbe>.Unclassified u => u.Offset,
        ContractStreamEvent<TProbe>.StreamError => throw new InvalidOperationException(
            "a bounded conformance subscription must not surface a transport StreamError"),
        _ => throw new InvalidOperationException("unrecognized ContractStreamEvent variant"),
    };
}
