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
using Daml.Runtime.Contracts;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Xunit;

namespace Daml.Ledger.Abstractions.Testing.Conformance;

/// <summary>
/// The documented behavioral contract for an <see cref="ILedgerClient"/> implementation.
/// Adopters subclass with a concrete client factory and a probe Daml marker; the seeded
/// client must expose the canonical scenario: at least one active contract, one
/// unclassifiable row, a terminal checkpoint, at least one event on both the ACS-delta
/// subscription
/// (<see cref="ILedgerStreamer.SubscribeAsync{T}(SubmitterInfo, LedgerOffset?, LedgerOffset?, CancellationToken)"/>,
/// which surfaces archival as a first-class <see cref="ContractStreamEvent{T}.Archived"/> and never an
/// <see cref="ContractStreamEvent{T}.Exercised"/>) and the ledger-effects subscription
/// (<see cref="ILedgerStreamer.SubscribeLedgerEffectsAsync{T}"/>, which signals archival
/// with a consuming <see cref="ContractStreamEvent{T}.Exercised"/> and never an
/// <see cref="ContractStreamEvent{T}.Archived"/>), honored <c>(fromOffset, toOffset]</c>
/// bounds, and cancellation-honoring streams.
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

    /// <summary>
    /// A client whose active-contract-set snapshot faults mid-stream, or <c>null</c> if
    /// the adopter's transport cannot induce a mid-snapshot transport fault
    /// deterministically. When non-null, the returned client's
    /// <see cref="ILedgerStreamer.SubscribeActiveAsync{T}"/> must terminate with a single
    /// <see cref="AcsSnapshotEntry{T}.StreamError"/> and yield no terminal
    /// <see cref="AcsSnapshotEntry{T}.Checkpoint"/>. Defaults to <c>null</c>, which skips
    /// the fault-path conformance check.
    /// </summary>
    protected virtual ILedgerClient? CreateFaultingSnapshotClient() => null;

    /// <summary>
    /// A write-capable client and submission proving the submitter-authority contract of
    /// <see cref="ILedgerWriter.SubmitAndWaitAsync"/> / <see cref="ILedgerWriter.TrySubmitAndWaitForTransactionAsync"/>,
    /// or <c>null</c> if the adopter cannot seed a deterministic authorization boundary
    /// (e.g. a fake with no notion of authorized/unauthorized parties). Defaults to
    /// <c>null</c>, which skips the write-path checks below.
    /// </summary>
    protected virtual WriteConformanceFixture? CreateWriteFixture() => null;

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

    /// <summary>
    /// A mid-snapshot transport fault surfaces in-band as a terminal
    /// <see cref="AcsSnapshotEntry{T}.StreamError"/>, never thrown, and in place of the
    /// terminal <see cref="AcsSnapshotEntry{T}.Checkpoint"/> a successful snapshot ends with.
    /// Opt-in: skipped unless the adopter overrides <see cref="CreateFaultingSnapshotClient"/>.
    /// </summary>
    [Fact]
    public async Task Active_snapshot_surfaces_a_mid_snapshot_fault_as_StreamError()
    {
        var faultingClient = CreateFaultingSnapshotClient();
        Assert.SkipWhen(
            faultingClient is null,
            "adopter opted out of the fault-path check: its transport cannot induce a deterministic mid-snapshot fault");
        await using var client = faultingClient!;

        var entries = await CollectSnapshot(client);

        entries.Should().NotBeEmpty();
        entries[^1].Should().BeOfType<AcsSnapshotEntry<TProbe>.StreamError>(
            "a mid-snapshot transport fault must surface in-band as a terminal StreamError, not be thrown");
        entries.Should().NotContain(
            e => e is AcsSnapshotEntry<TProbe>.Checkpoint,
            "a faulted snapshot yields no terminal Checkpoint — there is no valid snapshot offset to hand over to a live subscription");
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

    /// <summary>
    /// The ledger-effects subscription signals archival with a consuming
    /// <see cref="ContractStreamEvent{T}.Exercised"/>, never an
    /// <see cref="ContractStreamEvent{T}.Archived"/> variant — the shape's defining contract.
    /// </summary>
    [Fact]
    public async Task Ledger_effects_subscription_never_yields_Archived()
    {
        await using var client = CreateClient();
        var end = await client.GetLedgerEndAsync();

        var events = await CollectWithinBudget(
            client.SubscribeLedgerEffectsAsync<TProbe>(Reader, LedgerOffset.Begin, end),
            $"A bounded SubscribeLedgerEffectsAsync (toOffset {end.Value}) must complete");

        events.Should().NotBeEmpty(
            "the conformance scenario must seed at least one event on the ledger-effects stream");
        events.Should().NotContain(
            e => e is ContractStreamEvent<TProbe>.Archived,
            "the ledger-effects shape signals archival via a consuming Exercised, never an Archived variant");
    }

    /// <summary>
    /// The ACS-delta subscription surfaces archival as a first-class
    /// <see cref="ContractStreamEvent{T}.Archived"/> event, never an
    /// <see cref="ContractStreamEvent{T}.Exercised"/> variant — the shape's defining contract.
    /// </summary>
    [Fact]
    public async Task Acs_delta_subscription_never_yields_Exercised()
    {
        await using var client = CreateClient();
        var end = await client.GetLedgerEndAsync();

        var events = await CollectBounded(client, LedgerOffset.Begin, end);

        events.Should().NotBeEmpty(
            "the conformance scenario must seed at least one event on the subscription stream");
        events.Should().NotContain(
            e => e is ContractStreamEvent<TProbe>.Exercised,
            "the ACS-delta shape surfaces archival as a first-class Archived event, never an Exercised variant");
    }

    /// <summary>
    /// <see cref="ILedgerWriter.TrySubmitAndWaitForTransactionAsync"/> must apply the
    /// <c>submitter</c> parameter authoritatively via <c>CommandsSubmission.WithSubmitter</c>,
    /// overwriting any <see cref="CommandsSubmission.ActAs"/> already set on the submission —
    /// not dispatch whatever the caller pre-set. Opt-in: skipped unless the adopter overrides
    /// <see cref="CreateWriteFixture"/>.
    /// </summary>
    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_submitter_parameter_overrides_pre_set_ActAs()
    {
        var maybeFixture = CreateWriteFixture();
        Assert.SkipWhen(maybeFixture is null, WriteFixtureSkipReason);
        await using var fixture = maybeFixture!;

        var submissionWithWrongActAs = fixture.Submission.WithActAs(fixture.Unauthorized);

        var outcome = await fixture.Client.TrySubmitAndWaitForTransactionAsync(submissionWithWrongActAs, fixture.Authorized);

        outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.One>(
            "the submitter parameter must win over the pre-set (unauthorized) ActAs; an implementation " +
            "that dispatches the pre-set ActAs instead of applying the submitter authoritatively would be " +
            "rejected by the seeded client as unauthorized");
    }

    /// <summary>
    /// <see cref="ILedgerWriter.SubmitAndWaitAsync"/> must apply the <c>submitter</c> parameter
    /// authoritatively via <c>CommandsSubmission.WithSubmitter</c>, overwriting any
    /// <see cref="CommandsSubmission.ActAs"/> already set on the submission — not dispatch
    /// whatever the caller pre-set. Opt-in: skipped unless the adopter overrides
    /// <see cref="CreateWriteFixture"/>.
    /// </summary>
    [Fact]
    public async Task SubmitAndWaitAsync_submitter_parameter_overrides_pre_set_ActAs()
    {
        var maybeFixture = CreateWriteFixture();
        Assert.SkipWhen(maybeFixture is null, WriteFixtureSkipReason);
        await using var fixture = maybeFixture!;

        var submissionWithWrongActAs = fixture.Submission.WithActAs(fixture.Unauthorized);

        var act = () => fixture.Client.SubmitAndWaitAsync(submissionWithWrongActAs, fixture.Authorized);

        await act.Should().NotThrowAsync(
            "the submitter parameter must win over the pre-set (unauthorized) ActAs; an implementation " +
            "that dispatches the pre-set ActAs instead of applying the submitter authoritatively would be " +
            "rejected by the seeded client as unauthorized");
    }

    /// <summary>
    /// <see cref="ILedgerWriter.TrySubmitAndWaitForTransactionAsync"/> must not merge the
    /// <c>submitter</c> parameter with <see cref="CommandsSubmission.ActAs"/> already set on
    /// the submission — an authorized pre-set <c>ActAs</c> must not leak through and rescue an
    /// unauthorized <c>submitter</c>. Opt-in: skipped unless the adopter overrides
    /// <see cref="CreateWriteFixture"/>. Uses its own fresh <see cref="CreateWriteFixture"/>
    /// call (a distinct seeded client/contract from the sibling override check) so a stateful
    /// adopter's already-consumed contract from that check cannot masquerade as this one's
    /// authorization failure.
    /// </summary>
    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_submitter_parameter_is_not_merged_with_pre_set_ActAs()
    {
        var maybeFixture = CreateWriteFixture();
        Assert.SkipWhen(maybeFixture is null, WriteFixtureSkipReason);
        await using var fixture = maybeFixture!;

        var submissionWithAuthorizedActAs = fixture.Submission.WithActAs(fixture.Authorized);

        var outcome = await fixture.Client.TrySubmitAndWaitForTransactionAsync(submissionWithAuthorizedActAs, fixture.Unauthorized);

        outcome.Should().NotBeOfType<ExerciseOutcome<TransactionResult>.One>(
            "the submitter parameter must replace, not merge with, the pre-set ActAs; an implementation " +
            "that unions the submitter into the existing ActAs instead of overwriting it would let the " +
            "authorized pre-set ActAs rescue an unauthorized submitter");
    }

    /// <summary>
    /// <see cref="ILedgerWriter.SubmitAndWaitAsync"/> must not merge the <c>submitter</c>
    /// parameter with <see cref="CommandsSubmission.ActAs"/> already set on the submission — an
    /// authorized pre-set <c>ActAs</c> must not leak through and rescue an unauthorized
    /// <c>submitter</c>. Opt-in: skipped unless the adopter overrides <see cref="CreateWriteFixture"/>.
    /// Uses its own fresh <see cref="CreateWriteFixture"/> call (a distinct seeded client/contract
    /// from the sibling override check) so a stateful adopter's already-consumed contract from
    /// that check cannot masquerade as this one's authorization failure.
    /// </summary>
    [Fact]
    public async Task SubmitAndWaitAsync_submitter_parameter_is_not_merged_with_pre_set_ActAs()
    {
        var maybeFixture = CreateWriteFixture();
        Assert.SkipWhen(maybeFixture is null, WriteFixtureSkipReason);
        await using var fixture = maybeFixture!;

        var submissionWithAuthorizedActAs = fixture.Submission.WithActAs(fixture.Authorized);

        var act = () => fixture.Client.SubmitAndWaitAsync(submissionWithAuthorizedActAs, fixture.Unauthorized);

        await act.Should().ThrowAsync<LedgerOperationException>(
            "the submitter parameter must replace, not merge with, the pre-set ActAs; an implementation " +
            "that unions the submitter into the existing ActAs instead of overwriting it would let the " +
            "authorized pre-set ActAs rescue an unauthorized submitter");
    }

    private const string WriteFixtureSkipReason =
        "adopter opted out of the submitter-authority check: CreateWriteFixture() returned null";

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
