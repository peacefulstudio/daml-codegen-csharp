// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Daml.Ledger.Abstractions.Extensions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Xunit;

namespace Daml.Ledger.Abstractions.Tests;

/// <summary>
/// Behavioural coverage for <see cref="StreamerSnapshot.SnapshotAsync{T}"/>: the typed
/// projection consumers would otherwise hand-roll, and the incomplete-snapshot shapes it
/// must refuse to hand back as a short list.
/// </summary>
public sealed class StreamerSnapshotTests
{
    private static readonly SubmitterInfo Alice = new(new Party("alice"));

    [Fact]
    public async Task SnapshotAsync_decodes_created_rows_into_typed_contracts()
    {
        var streamer = new FakeStreamer(
            Created("cid-1", "alice", 1),
            Created("cid-2", "bob", 2),
            new AcsSnapshotEntry<Probe>.Checkpoint(new StakeholderResume(LedgerOffset.At(2))));

        var contracts = await streamer.SnapshotAsync<Probe>(Alice, cancellationToken: TestContext.Current.CancellationToken);

        contracts.Should().HaveCount(2);
        contracts[0].Id.Value.Should().Be("cid-1");
        contracts[0].Data.Owner.Should().Be(new Party("alice"));
        contracts[1].Data.Owner.Should().Be(new Party("bob"));
    }

    [Fact]
    public async Task SnapshotAsync_surfaces_the_contract_key_the_snapshot_row_carried()
    {
        var streamer = new FakeStreamer(
            new AcsSnapshotEntry<Probe>.Created(
                new ContractId<Probe>("cid-1"),
                new Probe(new Party("alice"), ProbeGrade.Low),
                new ContractKey(DamlRecord.Create(new DamlField("owner", new DamlParty("alice")))),
                LedgerOffset.At(1),
                new SynchronizerId("sync"),
                [new Party("alice")]),
            new AcsSnapshotEntry<Probe>.Checkpoint(new StakeholderResume(LedgerOffset.At(1))));

        var contracts = await streamer.SnapshotAsync(Probe.Key, Alice, cancellationToken: TestContext.Current.CancellationToken);

        contracts.Should().ContainSingle().Which.Key.Value.Should().Be(
            new Party("alice"),
            "this is the entry point the key feature documents, so a key dropped anywhere along the "
            + "Created to Contract chain reaches the caller as a silently absent key");
    }

    [Fact]
    public async Task SnapshotAsync_infers_both_type_arguments_from_the_key_witness()
    {
        var streamer = new FakeStreamer(
            new AcsSnapshotEntry<Probe>.Created(
                new ContractId<Probe>("cid-1"),
                new Probe(new Party("alice"), ProbeGrade.Low),
                new ContractKey(DamlRecord.Create(new DamlField("owner", new DamlParty("alice")))),
                LedgerOffset.At(1),
                new SynchronizerId("sync"),
                [new Party("alice")]),
            new AcsSnapshotEntry<Probe>.Checkpoint(new StakeholderResume(LedgerOffset.At(1))));

        var contracts = await streamer.SnapshotAsync(Probe.Key, Alice, cancellationToken: TestContext.Current.CancellationToken);

        contracts.Should().BeAssignableTo<IReadOnlyList<Contract<Probe, Party>>>(
            "the witness argument exists so one argument fixes both type parameters; C# performs no "
            + "partial type-argument inference, so a call site spelling neither must still bind");
    }

    [Fact]
    public async Task SnapshotAsync_reports_a_keyed_row_that_carried_no_key()
    {
        var streamer = new FakeStreamer(
            new AcsSnapshotEntry<Probe>.Created(
                new ContractId<Probe>("cid-1"),
                new Probe(new Party("alice"), ProbeGrade.Low),
                null,
                LedgerOffset.At(1),
                new SynchronizerId("sync"),
                [new Party("alice")]),
            new AcsSnapshotEntry<Probe>.Checkpoint(new StakeholderResume(LedgerOffset.At(1))));

        var draining = async () => await streamer.SnapshotAsync(Probe.Key, Alice, cancellationToken: TestContext.Current.CancellationToken);

        await draining.Should().ThrowAsync<LedgerOperationException>(
            "a keyed snapshot whose row carried no key would otherwise return contracts the caller "
            + "cannot address by key, which is the short-list failure this convenience prevents");
    }

    [Fact]
    public async Task SnapshotAsync_surfaces_the_key_hash_the_snapshot_row_carried()
    {
        const string ledgerKeyHash = "6CgQL9eNNqIjS5cB6/kK1IsqdxjcgXl/3kxSiUEkiBA=";
        var streamer = new FakeStreamer(
            new AcsSnapshotEntry<Probe>.Created(
                new ContractId<Probe>("cid-1"),
                new Probe(new Party("alice"), ProbeGrade.Low),
                new ContractKey(
                    DamlRecord.Create(new DamlField("owner", new DamlParty("alice"))),
                    Probe.TemplateId)
                {
                    KeyHash = ledgerKeyHash,
                },
                LedgerOffset.At(1),
                new SynchronizerId("sync"),
                [new Party("alice")]),
            new AcsSnapshotEntry<Probe>.Checkpoint(new StakeholderResume(LedgerOffset.At(1))));

        var contracts = await streamer.SnapshotAsync(Probe.Key, Alice, cancellationToken: TestContext.Current.CancellationToken);

        contracts.Should().ContainSingle().Which.Key.Hash.Should().Be(
            ledgerKeyHash,
            "this drains the whole Created to Contract chain, so a hash dropped at "
            + "any hop reaches the caller as a silently absent hash");
    }

    [Fact]
    public async Task SnapshotAsync_returns_an_empty_list_for_an_empty_snapshot()
    {
        var streamer = new FakeStreamer(
            new AcsSnapshotEntry<Probe>.Checkpoint(new StakeholderResume(LedgerOffset.At(7))));

        var contracts = await streamer.SnapshotAsync<Probe>(Alice, cancellationToken: TestContext.Current.CancellationToken);

        contracts.Should().BeEmpty();
    }

    [Fact]
    public async Task SnapshotAsync_throws_on_a_terminal_stream_error()
    {
        var streamer = new FakeStreamer(
            Created("cid-1", "alice", 1),
            new AcsSnapshotEntry<Probe>.StreamError(14, "unavailable"));

        var snapshot = async () => await streamer.SnapshotAsync<Probe>(Alice, cancellationToken: TestContext.Current.CancellationToken);

        var thrown = await snapshot.Should().ThrowAsync<LedgerOperationException>();
        thrown.Which.StatusCode.Should().Be(14);
        thrown.Which.Message.Should().Contain("unavailable");
    }

    [Fact]
    public async Task SnapshotAsync_carries_the_stream_errors_classification_onto_the_thrown_exception()
    {
        var streamer = new FakeStreamer(
            Created("cid-1", "alice", 1),
            new AcsSnapshotEntry<Probe>.StreamError(
                14, "unavailable", DamlErrorCategory.TransientServerFailure));

        var snapshot = async () => await streamer.SnapshotAsync<Probe>(Alice, cancellationToken: TestContext.Current.CancellationToken);

        var thrown = await snapshot.Should().ThrowAsync<LedgerOperationException>();
        thrown.Which.Category.Should().Be(
            DamlErrorCategory.TransientServerFailure,
            "a classification the transport determined is discarded unless this branch forwards it, "
            + "leaving a caller who catches the exception unable to tell a retryable transient fault "
            + "from a permanent one with only the status code to go on");
        thrown.Which.StatusCode.Should().Be(
            14,
            "the classification has to arrive alongside the status code, not in place of it");
    }

    [Fact]
    public async Task SnapshotAsync_carries_the_stream_errors_source_exception_onto_the_thrown_exception()
    {
        var transportFault = new InvalidOperationException("the channel went away");
        var streamer = new FakeStreamer(
            Created("cid-1", "alice", 1),
            new AcsSnapshotEntry<Probe>.StreamError(
                14, "unavailable", DamlErrorCategory.TransientServerFailure, transportFault));

        var snapshot = async () => await streamer.SnapshotAsync<Probe>(Alice, cancellationToken: TestContext.Current.CancellationToken);

        var thrown = await snapshot.Should().ThrowAsync<LedgerOperationException>();
        thrown.Which.InnerException.Should().BeSameAs(
            transportFault,
            "the exception the transport caught is lost unless this branch forwards it, leaving a caller "
            + "who catches the rethrow with none of the stack the fault actually came from");
    }

    [Fact]
    public async Task SnapshotAsync_throws_rather_than_dropping_an_unclassified_row()
    {
        var streamer = new FakeStreamer(
            Created("cid-1", "alice", 1),
            new AcsSnapshotEntry<Probe>.Unclassified(LedgerOffset.At(2), UnclassifiedKind.Unknown, "ACTIVE_CONTRACT"),
            new AcsSnapshotEntry<Probe>.Checkpoint(new StakeholderResume(LedgerOffset.At(2))));

        var snapshot = async () => await streamer.SnapshotAsync<Probe>(Alice, cancellationToken: TestContext.Current.CancellationToken);

        await snapshot.Should().ThrowAsync<LedgerOperationException>()
            .WithMessage("*ACTIVE_CONTRACT*");
    }

    [Fact]
    public async Task SnapshotAsync_reports_an_unclassified_row_that_carries_no_offset()
    {
        var streamer = new FakeStreamer(
            Created("cid-1", "alice", 1),
            new AcsSnapshotEntry<Probe>.Unclassified(null, UnclassifiedKind.DecodeFailure),
            new AcsSnapshotEntry<Probe>.Checkpoint(new StakeholderResume(LedgerOffset.At(2))));

        var snapshot = async () => await streamer.SnapshotAsync<Probe>(Alice, cancellationToken: TestContext.Current.CancellationToken);

        await snapshot.Should().ThrowAsync<LedgerOperationException>()
            .WithMessage(
                "*with no ledger offset*",
                "interpolating a null offset would render an empty position and read as 'at offset , so the returned' — the row's absent ledger position has to be said, not blanked");
    }

    [Fact]
    public async Task SnapshotAsync_throws_when_the_snapshot_ends_without_its_checkpoint()
    {
        var streamer = new FakeStreamer(Created("cid-1", "alice", 1));

        var snapshot = async () => await streamer.SnapshotAsync<Probe>(Alice, cancellationToken: TestContext.Current.CancellationToken);

        await snapshot.Should().ThrowAsync<LedgerOperationException>()
            .WithMessage("*without its terminal checkpoint*");
    }

    [Fact]
    public async Task SnapshotAsync_throws_on_a_row_the_projector_could_not_decode()
    {
        var streamer = new FakeStreamer(
            Created("cid-1", "alice", 1),
            new AcsSnapshotEntry<Probe>.Unclassified(LedgerOffset.At(2), UnclassifiedKind.DecodeFailure),
            new AcsSnapshotEntry<Probe>.Checkpoint(new StakeholderResume(LedgerOffset.At(2))));

        var snapshot = async () => await streamer.SnapshotAsync<Probe>(Alice, cancellationToken: TestContext.Current.CancellationToken);

        await snapshot.Should().ThrowAsync<LedgerOperationException>(
                "a payload the projector could not decode reaches this layer as a DecodeFailure row "
                + "rather than a create row, and leaves the snapshot as unusable as any other "
                + "unclassified row — which is what publisher-ahead-of-reader drift looks like")
            .WithMessage("*DecodeFailure*");
    }

    [Fact]
    public async Task SnapshotAsync_reports_caller_cancellation_rather_than_an_in_band_transport_fault()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var streamer = FakeStreamer.IgnoringCancellation(new AcsSnapshotEntry<Probe>.StreamError(1, "CANCELLED"));

        var snapshot = async () => await streamer.SnapshotAsync<Probe>(Alice, cancellationToken: cts.Token);

        await snapshot.Should().ThrowAsync<OperationCanceledException>(
            "a transport that maps the cancelled call in-band would otherwise make the caller's "
            + "own cancellation read as an infrastructure failure");
    }

    [Fact]
    public async Task SnapshotAsync_reports_caller_cancellation_rather_than_a_truncated_snapshot()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var streamer = FakeStreamer.IgnoringCancellation(Created("cid-1", "alice", 1));

        var snapshot = async () => await streamer.SnapshotAsync<Probe>(Alice, cancellationToken: cts.Token);

        await snapshot.Should().ThrowAsync<OperationCanceledException>(
            "a stream cut short by the caller's own token is not a transport truncation");
    }

    private static AcsSnapshotEntry<Probe>.Created Created(string contractId, string owner, long offset) =>
        new(
            new ContractId<Probe>(contractId),
            new Probe(new Party(owner), ProbeGrade.Low),
            null,
            LedgerOffset.At(offset),
            new SynchronizerId("sync"),
            [new Party(owner)]);

    private enum ProbeGrade
    {
        Low,
        High,
    }

    private sealed record Probe(Party Owner, ProbeGrade Grade)
        : ITemplate, IDamlRecord<Probe>, IHasKey<Probe, Party>
    {
        public static Identifier TemplateId { get; } = new("pkg", "Module", "Probe");
        public static string PackageId => "pkg";
        public static string PackageName => "probe";
        public static Version PackageVersion { get; } = new(1, 0, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public static KeyDescriptor<Probe, Party> Key { get; } = new()
        {
            KeyEncoder = key => DamlRecord.Create(new DamlField("owner", key.ToDamlValue())),
            KeyDecoder = value =>
                Party.FromDamlValue(value.As<DamlRecord>().GetRequiredField("owner").As<DamlParty>()),
        };

        public DamlRecord ToRecord() => DamlRecord.Create(
            new DamlField("owner", Owner.ToDamlValue()),
            new DamlField("grade", new DamlEnum(null, Grade.ToString())));

        public static Probe FromRecord(DamlRecord record) =>
            new(
                Party.FromDamlValue(record.GetRequiredField("owner").As<DamlParty>()),
                record.GetRequiredField("grade").As<DamlEnum>().Constructor switch
                {
                    "Low" => ProbeGrade.Low,
                    "High" => ProbeGrade.High,
                    var unknown => throw new ArgumentOutOfRangeException(nameof(record), unknown, null),
                });
    }

    private sealed class FakeStreamer(params AcsSnapshotEntry<Probe>[] entries) : ILedgerStreamer
    {
        private bool ObservesCancellation { get; init; } = true;

        public static FakeStreamer IgnoringCancellation(params AcsSnapshotEntry<Probe>[] entries) =>
            new(entries) { ObservesCancellation = false };

        public IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
            SubmitterInfo submitter,
            LedgerOffset? fromOffset = null,
            LedgerOffset? toOffset = null,
            CancellationToken cancellationToken = default)
            where T : ITemplate, IDamlRecord<T> => throw new NotSupportedException();

        public IAsyncEnumerable<ContractStreamEvent<T>> SubscribeLedgerEffectsAsync<T>(
            SubmitterInfo submitter,
            LedgerOffset? fromOffset = null,
            LedgerOffset? toOffset = null,
            CancellationToken cancellationToken = default)
            where T : ITemplate, IDamlRecord<T> => throw new NotSupportedException();

        public IAsyncEnumerable<AcsSnapshotEntry<T>> SubscribeActiveAsync<T>(
            SubmitterInfo submitter,
            LedgerOffset? activeAtOffset = null,
            CancellationToken cancellationToken = default)
            where T : ITemplate, IDamlRecord<T> => (IAsyncEnumerable<AcsSnapshotEntry<T>>)(object)Replay(cancellationToken);

        public IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeAsync<TInterface, TView>(
            ViewDescriptor<TInterface, TView> view,
            SubmitterInfo submitter,
            LedgerOffset? fromOffset = null,
            LedgerOffset? toOffset = null,
            CancellationToken cancellationToken = default)
            where TInterface : IDamlInterface, IHasView<TView>
            where TView : IDamlRecord<TView> => throw new NotSupportedException();

        public IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeLedgerEffectsAsync<TInterface, TView>(
            ViewDescriptor<TInterface, TView> view,
            SubmitterInfo submitter,
            LedgerOffset? fromOffset = null,
            LedgerOffset? toOffset = null,
            CancellationToken cancellationToken = default)
            where TInterface : IDamlInterface, IHasView<TView>
            where TView : IDamlRecord<TView> => throw new NotSupportedException();

        public IAsyncEnumerable<InterfaceAcsSnapshotEntry<TInterface, TView>> SubscribeActiveAsync<TInterface, TView>(
            ViewDescriptor<TInterface, TView> view,
            SubmitterInfo submitter,
            LedgerOffset? activeAtOffset = null,
            CancellationToken cancellationToken = default)
            where TInterface : IDamlInterface, IHasView<TView>
            where TView : IDamlRecord<TView> => throw new NotSupportedException();

        private async IAsyncEnumerable<AcsSnapshotEntry<Probe>> Replay(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var entry in entries)
            {
                if (ObservesCancellation)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                yield return entry;
                await Task.Yield();
            }
        }
    }
}
