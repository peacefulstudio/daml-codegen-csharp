// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using Daml.Ledger.Abstractions.Extensions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using AwesomeAssertions;
using Xunit;

namespace Daml.Ledger.Abstractions.Tests;

/// <summary>
/// Verifies the authorization routing of <see cref="ILedgerClient"/>: the
/// <see cref="SubmitterInfo"/> overloads are the primitives implementations override,
/// the <c>Party</c> <c>actAs</c> convenience overloads forward to them with a single
/// act-as party and no read-as parties, and multi-party / read-as submissions carry
/// every party through to the primitive instead of throwing.
/// </summary>
public class LedgerClientSubmitterInfoTests
{
    private static readonly ExerciseCommand SampleCommand = new(
        new Identifier("pkg", "Module", "Template"),
        ContractId: new ContractId<SampleTemplate>("cid-1"),
        Choice: new ChoiceName("DoIt"),
        ChoiceArgument: new DamlRecord(null, []));

    private static readonly SubmitterInfo MultiParty = new(
        actAs: new HashSet<Party> { new("alice"), new("bob") });

    private static readonly SubmitterInfo SinglePartyWithReadAs = new(
        actAs: new HashSet<Party> { new("alice") },
        readAs: new HashSet<Party> { new("observer") });

    [Fact]
    public async Task ExerciseAsync_with_result_Party_actAs_forwards_to_SubmitterInfo_primitive_with_single_actAs_no_readAs()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.ExerciseAsync<int>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        fake.LastExerciseSubmitter!.Value.ActAs.Select(p => p.Id).Should().Equal("alice");
        fake.LastExerciseSubmitter!.Value.ReadAs.Should().BeEmpty();
    }

    [Fact]
    public async Task ExerciseAsync_void_Party_actAs_forwards_to_transaction_primitive_with_single_actAs_no_readAs()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.ExerciseAsync(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        fake.LastTransactionSubmitter!.Value.ActAs.Select(p => p.Id).Should().Equal("alice");
        fake.LastTransactionSubmitter!.Value.ReadAs.Should().BeEmpty();
    }

    [Fact]
    public async Task TryCreateAsync_Party_actAs_forwards_to_SubmitterInfo_primitive_with_single_actAs_no_readAs()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.TryCreateAsync(new FakeTemplate(), new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        fake.LastCreateSubmitter!.Value.ActAs.Select(p => p.Id).Should().Equal("alice");
        fake.LastCreateSubmitter!.Value.ReadAs.Should().BeEmpty();
    }

    [Fact]
    public async Task TryCreateOneByExerciseAsync_Party_actAs_forwards_to_transaction_primitive_with_single_actAs_no_readAs()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.TryCreateOneByExerciseAsync<FakeTemplate>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        fake.LastTransactionSubmitter!.Value.ActAs.Select(p => p.Id).Should().Equal("alice");
        fake.LastTransactionSubmitter!.Value.ReadAs.Should().BeEmpty();
    }

    [Fact]
    public async Task SubscribeAsync_Party_actAs_forwards_to_SubmitterInfo_primitive_with_single_actAs_no_readAs()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await foreach (var _ in client.SubscribeAsync<FakeTemplate>(new Party("alice"), cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        fake.LastSubscribeSubmitter!.Value.ActAs.Select(p => p.Id).Should().Equal("alice");
        fake.LastSubscribeSubmitter!.Value.ReadAs.Should().BeEmpty();
    }

    [Fact]
    public async Task SubscribeActiveAsync_Party_actAs_forwards_to_SubmitterInfo_primitive_with_single_actAs_no_readAs()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await foreach (var _ in client.SubscribeActiveAsync<FakeTemplate>(new Party("alice"), cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        fake.LastSubscribeActiveSubmitter!.Value.ActAs.Select(p => p.Id).Should().Equal("alice");
        fake.LastSubscribeActiveSubmitter!.Value.ReadAs.Should().BeEmpty();
    }

    [Fact]
    public async Task ExerciseAsync_with_single_actAs_and_readAs_carries_readAs_through_without_throwing()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.ExerciseAsync<int>(SampleCommand, SinglePartyWithReadAs, cancellationToken: TestContext.Current.CancellationToken);

        fake.LastExerciseSubmitter!.Value.ActAs.Select(p => p.Id).Should().Equal("alice");
        fake.LastExerciseSubmitter!.Value.ReadAs.Select(p => p.Id).Should().Equal("observer");
    }

    [Fact]
    public async Task TryCreateAsync_with_single_actAs_and_readAs_carries_readAs_through_without_throwing()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.TryCreateAsync(new FakeTemplate(), SinglePartyWithReadAs, cancellationToken: TestContext.Current.CancellationToken);

        fake.LastCreateSubmitter!.Value.ActAs.Select(p => p.Id).Should().Equal("alice");
        fake.LastCreateSubmitter!.Value.ReadAs.Select(p => p.Id).Should().Equal("observer");
    }

    [Fact]
    public async Task SubscribeAsync_with_single_actAs_and_readAs_carries_readAs_through_without_throwing()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await foreach (var _ in client.SubscribeAsync<FakeTemplate>(SinglePartyWithReadAs, cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        fake.LastSubscribeSubmitter!.Value.ActAs.Select(p => p.Id).Should().Equal("alice");
        fake.LastSubscribeSubmitter!.Value.ReadAs.Select(p => p.Id).Should().Equal("observer");
    }

    [Fact]
    public async Task ExerciseAsync_with_multi_party_actAs_carries_all_parties_through_without_throwing()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.ExerciseAsync<int>(SampleCommand, MultiParty, cancellationToken: TestContext.Current.CancellationToken);

        fake.LastExerciseSubmitter!.Value.ActAs.Select(p => p.Id).Should().BeEquivalentTo("alice", "bob");
        fake.LastExerciseSubmitter!.Value.ReadAs.Should().BeEmpty();
    }

    [Fact]
    public async Task TryCreateOneByExerciseAsync_with_multi_party_actAs_carries_all_parties_through_without_throwing()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.TryCreateOneByExerciseAsync<FakeTemplate>(SampleCommand, MultiParty, cancellationToken: TestContext.Current.CancellationToken);

        fake.LastTransactionSubmitter!.Value.ActAs.Select(p => p.Id).Should().BeEquivalentTo("alice", "bob");
    }

    [Fact]
    public async Task SubscribeActiveAsync_with_multi_party_actAs_carries_all_parties_through_without_throwing()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await foreach (var _ in client.SubscribeActiveAsync<FakeTemplate>(MultiParty, cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        fake.LastSubscribeActiveSubmitter!.Value.ActAs.Select(p => p.Id).Should().BeEquivalentTo("alice", "bob");
    }

    [Fact]
    public async Task TryCreateOneByExerciseAsync_with_single_actAs_and_readAs_carries_readAs_through_without_throwing()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.TryCreateOneByExerciseAsync<FakeTemplate>(SampleCommand, SinglePartyWithReadAs, cancellationToken: TestContext.Current.CancellationToken);

        fake.LastTransactionSubmitter!.Value.ActAs.Select(p => p.Id).Should().Equal("alice");
        fake.LastTransactionSubmitter!.Value.ReadAs.Select(p => p.Id).Should().Equal("observer");
    }

    [Fact]
    public async Task SubscribeActiveAsync_with_single_actAs_and_readAs_carries_readAs_through_without_throwing()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await foreach (var _ in client.SubscribeActiveAsync<FakeTemplate>(SinglePartyWithReadAs, cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        fake.LastSubscribeActiveSubmitter!.Value.ActAs.Select(p => p.Id).Should().Equal("alice");
        fake.LastSubscribeActiveSubmitter!.Value.ReadAs.Select(p => p.Id).Should().Equal("observer");
    }

    [Fact]
    public async Task TryCreateAsync_with_multi_party_actAs_carries_all_parties_through_without_throwing()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.TryCreateAsync(new FakeTemplate(), MultiParty, cancellationToken: TestContext.Current.CancellationToken);

        fake.LastCreateSubmitter!.Value.ActAs.Select(p => p.Id).Should().BeEquivalentTo("alice", "bob");
        fake.LastCreateSubmitter!.Value.ReadAs.Should().BeEmpty();
    }

    [Fact]
    public async Task SubscribeAsync_with_multi_party_actAs_carries_all_parties_through_without_throwing()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await foreach (var _ in client.SubscribeAsync<FakeTemplate>(MultiParty, cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        fake.LastSubscribeSubmitter!.Value.ActAs.Select(p => p.Id).Should().BeEquivalentTo("alice", "bob");
        fake.LastSubscribeSubmitter!.Value.ReadAs.Should().BeEmpty();
    }

    [Fact]
    public async Task TryExerciseAsync_Party_actAs_forwards_timeout_to_the_SubmitterInfo_primitive()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.TryExerciseAsync<int>(SampleCommand, new Party("alice"), timeout: TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);

        fake.LastExerciseTimeout.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task TryExerciseAsync_Party_actAs_defaults_timeout_to_null_at_the_primitive()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.TryExerciseAsync<int>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        fake.LastExerciseSubmitter.Should().NotBeNull();
        fake.LastExerciseTimeout.Should().BeNull();
    }

    [Fact]
    public async Task TryCreateAsync_Party_actAs_forwards_timeout_to_the_SubmitterInfo_primitive()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.TryCreateAsync(new FakeTemplate(), new Party("alice"), timeout: TimeSpan.FromSeconds(7), cancellationToken: TestContext.Current.CancellationToken);

        fake.LastCreateTimeout.Should().Be(TimeSpan.FromSeconds(7));
    }

    [Fact]
    public async Task TryCreateOneByExerciseAsync_Party_actAs_forwards_timeout_to_the_transaction_primitive()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.TryCreateOneByExerciseAsync<FakeTemplate>(SampleCommand, new Party("alice"), timeout: TimeSpan.FromSeconds(9), cancellationToken: TestContext.Current.CancellationToken);

        fake.LastTransactionTimeout.Should().Be(TimeSpan.FromSeconds(9));
    }

    [Fact]
    public async Task TryCreateManyByExerciseAsync_Party_actAs_forwards_to_transaction_primitive_with_single_actAs_no_readAs()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.TryCreateManyByExerciseAsync<FakeTemplate>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        fake.LastTransactionSubmitter!.Value.ActAs.Select(p => p.Id).Should().Equal("alice");
        fake.LastTransactionSubmitter!.Value.ReadAs.Should().BeEmpty();
    }

    [Fact]
    public async Task TryCreateManyByExerciseAsync_Party_actAs_forwards_timeout_to_the_transaction_primitive()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.TryCreateManyByExerciseAsync<FakeTemplate>(SampleCommand, new Party("alice"), timeout: TimeSpan.FromSeconds(9), cancellationToken: TestContext.Current.CancellationToken);

        fake.LastTransactionTimeout.Should().Be(TimeSpan.FromSeconds(9));
    }

    [Fact]
    public async Task ExerciseAsync_extension_reaches_the_primitive_with_null_timeout()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.ExerciseAsync<int>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        fake.LastExerciseSubmitter.Should().NotBeNull();
        fake.LastExerciseTimeout.Should().BeNull();
    }

    [Fact]
    public async Task SubscribeAsync_Party_actAs_forwards_fromOffset_and_toOffset_to_the_SubmitterInfo_primitive()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await foreach (var _ in client.SubscribeAsync<FakeTemplate>(new Party("alice"), fromOffset: LedgerOffset.At(10), toOffset: LedgerOffset.At(42), cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        fake.LastSubscribeFromOffset.Should().Be(LedgerOffset.At(10));
        fake.LastSubscribeToOffset.Should().Be(LedgerOffset.At(42));
    }

    [Fact]
    public async Task SubscribeAsync_Party_actAs_defaults_toOffset_to_null_at_the_primitive()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await foreach (var _ in client.SubscribeAsync<FakeTemplate>(new Party("alice"), cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        fake.LastSubscribeSubmitter.Should().NotBeNull();
        fake.LastSubscribeToOffset.Should().BeNull();
    }

    [Fact]
    public async Task SubscribeAsync_accepts_a_StakeholderResume_and_forwards_its_offset_to_the_SubmitterInfo_primitive()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;
        var resume = new StakeholderResume(LedgerOffset.At(10));

        await foreach (var _ in client.SubscribeAsync<FakeTemplate>(
            new SubmitterInfo(new Party("alice")), resume, cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        fake.LastSubscribeFromOffset.Should().Be(resume.Offset);
    }

    [Fact]
    public async Task SubscribeAsync_Party_actAs_StakeholderResume_overload_forwards_the_ticket_offset_to_the_SubmitterInfo_primitive()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;
        var resume = new StakeholderResume(LedgerOffset.At(21));

        await foreach (var _ in client.SubscribeAsync<FakeTemplate>(new Party("alice"), resume, cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        fake.LastSubscribeSubmitter!.Value.ActAs.Select(p => p.Id).Should().Equal("alice");
        fake.LastSubscribeFromOffset.Should().Be(resume.Offset);
    }

    [Fact]
    public async Task SubscribeActiveAsync_Party_actAs_forwards_activeAtOffset_to_the_SubmitterInfo_primitive()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await foreach (var _ in client.SubscribeActiveAsync<FakeTemplate>(new Party("alice"), activeAtOffset: LedgerOffset.At(33), cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        fake.LastSubscribeActiveAtOffset.Should().Be(LedgerOffset.At(33));
    }

    [Fact]
    public async Task SubscribeActiveAsync_Party_actAs_defaults_activeAtOffset_to_null_at_the_primitive()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await foreach (var _ in client.SubscribeActiveAsync<FakeTemplate>(new Party("alice"), cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        fake.LastSubscribeActiveSubmitter.Should().NotBeNull();
        fake.LastSubscribeActiveAtOffset.Should().BeNull();
    }

    [Fact]
    public async Task SubscribeLedgerEffectsAsync_Party_actAs_forwards_to_SubmitterInfo_primitive_with_single_actAs_no_readAs()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await foreach (var _ in client.SubscribeLedgerEffectsAsync<FakeTemplate>(new Party("alice"), cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        fake.LastSubscribeLedgerEffectsSubmitter!.Value.ActAs.Select(p => p.Id).Should().Equal("alice");
        fake.LastSubscribeLedgerEffectsSubmitter!.Value.ReadAs.Should().BeEmpty();
    }

    [Fact]
    public async Task SubscribeLedgerEffectsAsync_with_single_actAs_and_readAs_carries_readAs_through_without_throwing()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await foreach (var _ in client.SubscribeLedgerEffectsAsync<FakeTemplate>(SinglePartyWithReadAs, cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        fake.LastSubscribeLedgerEffectsSubmitter!.Value.ActAs.Select(p => p.Id).Should().Equal("alice");
        fake.LastSubscribeLedgerEffectsSubmitter!.Value.ReadAs.Select(p => p.Id).Should().Equal("observer");
    }

    [Fact]
    public async Task SubscribeLedgerEffectsAsync_with_multi_party_actAs_carries_all_parties_through_without_throwing()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await foreach (var _ in client.SubscribeLedgerEffectsAsync<FakeTemplate>(MultiParty, cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        fake.LastSubscribeLedgerEffectsSubmitter!.Value.ActAs.Select(p => p.Id).Should().BeEquivalentTo("alice", "bob");
        fake.LastSubscribeLedgerEffectsSubmitter!.Value.ReadAs.Should().BeEmpty();
    }

    [Fact]
    public async Task SubscribeLedgerEffectsAsync_Party_actAs_forwards_fromOffset_and_toOffset_to_the_SubmitterInfo_primitive()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await foreach (var _ in client.SubscribeLedgerEffectsAsync<FakeTemplate>(new Party("alice"), fromOffset: LedgerOffset.At(10), toOffset: LedgerOffset.At(42), cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        fake.LastSubscribeLedgerEffectsFromOffset.Should().Be(LedgerOffset.At(10));
        fake.LastSubscribeLedgerEffectsToOffset.Should().Be(LedgerOffset.At(42));
    }

    [Fact]
    public async Task SubscribeLedgerEffectsAsync_Party_actAs_defaults_toOffset_to_null_at_the_primitive()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await foreach (var _ in client.SubscribeLedgerEffectsAsync<FakeTemplate>(new Party("alice"), cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        fake.LastSubscribeLedgerEffectsSubmitter.Should().NotBeNull();
        fake.LastSubscribeLedgerEffectsToOffset.Should().BeNull();
    }

    /// <summary>
    /// Records the <see cref="SubmitterInfo"/>, <c>timeout</c>, and offset bounds each
    /// primitive receives so tests can assert that the <c>Party</c>-<c>actAs</c>
    /// convenience overloads forward every parameter to the primitive — the right
    /// single party, read-as / multi-party submissions carrying every party through,
    /// and the per-call <c>timeout</c> / <c>fromOffset</c> / <c>toOffset</c> /
    /// <c>activeAtOffset</c> values (or their <c>null</c> defaults).
    /// </summary>
    private class RecordingLedgerClient : ILedgerClient
    {
        public SubmitterInfo? LastExerciseSubmitter { get; private set; }
        public SubmitterInfo? LastCreateSubmitter { get; private set; }
        public SubmitterInfo? LastTransactionSubmitter { get; private set; }
        public SubmitterInfo? LastSubscribeSubmitter { get; private set; }
        public SubmitterInfo? LastSubscribeActiveSubmitter { get; private set; }
        public SubmitterInfo? LastSubscribeLedgerEffectsSubmitter { get; private set; }
        public TimeSpan? LastExerciseTimeout { get; private set; }
        public TimeSpan? LastCreateTimeout { get; private set; }
        public TimeSpan? LastTransactionTimeout { get; private set; }
        public LedgerOffset? LastSubscribeFromOffset { get; private set; }
        public LedgerOffset? LastSubscribeToOffset { get; private set; }
        public LedgerOffset? LastSubscribeActiveAtOffset { get; private set; }
        public LedgerOffset? LastSubscribeLedgerEffectsFromOffset { get; private set; }
        public LedgerOffset? LastSubscribeLedgerEffectsToOffset { get; private set; }

        public Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(
            ExerciseCommand command,
            SubmitterInfo submitter,
            string? workflowId = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            LastExerciseSubmitter = submitter;
            LastExerciseTimeout = timeout;
            return Task.FromResult<ExerciseOutcome<TResult>>(new ExerciseOutcome<TResult>.One(default(TResult)!));
        }

        public Task<SubmitAndWaitResult> SubmitAndWaitAsync(
            CommandsSubmission submission,
            SubmitterInfo submitter,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new SubmitAndWaitResult(new CommandId("cmd-id"), "update-id", LedgerOffset.Begin));

        public Task<ExerciseOutcome<TransactionResult>> TrySubmitAndWaitForTransactionAsync(
            CommandsSubmission submission,
            SubmitterInfo submitter,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            var effective = submission.WithSubmitter(submitter);
            var actAs = effective.ActAs ?? [];
            var readAs = effective.ReadAs ?? [];
            LastTransactionSubmitter = new SubmitterInfo(
                new HashSet<Party>(actAs), readAs.Count > 0 ? new HashSet<Party>(readAs) : null);
            LastTransactionTimeout = timeout;
            return Task.FromResult<ExerciseOutcome<TransactionResult>>(
                new ExerciseOutcome<TransactionResult>.None());
        }

        public Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
            TTemplate payload,
            SubmitterInfo submitter,
            string? workflowId = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
            where TTemplate : ITemplate
        {
            LastCreateSubmitter = submitter;
            LastCreateTimeout = timeout;
            return Task.FromResult<ExerciseOutcome<ContractId<TTemplate>>>(
                new ExerciseOutcome<ContractId<TTemplate>>.None());
        }

        public IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
            SubmitterInfo submitter,
            LedgerOffset? fromOffset = null,
            LedgerOffset? toOffset = null,
            CancellationToken cancellationToken = default)
            where T : IDamlType
        {
            LastSubscribeSubmitter = submitter;
            LastSubscribeFromOffset = fromOffset;
            LastSubscribeToOffset = toOffset;
            return EmptyAsync<ContractStreamEvent<T>>(cancellationToken);
        }

        public IAsyncEnumerable<ContractStreamEvent<T>> SubscribeLedgerEffectsAsync<T>(
            SubmitterInfo submitter,
            LedgerOffset? fromOffset = null,
            LedgerOffset? toOffset = null,
            CancellationToken cancellationToken = default)
            where T : IDamlType
        {
            LastSubscribeLedgerEffectsSubmitter = submitter;
            LastSubscribeLedgerEffectsFromOffset = fromOffset;
            LastSubscribeLedgerEffectsToOffset = toOffset;
            return EmptyAsync<ContractStreamEvent<T>>(cancellationToken);
        }

        public IAsyncEnumerable<AcsSnapshotEntry<T>> SubscribeActiveAsync<T>(
            SubmitterInfo submitter,
            LedgerOffset? activeAtOffset = null,
            CancellationToken cancellationToken = default)
            where T : IDamlType
        {
            LastSubscribeActiveSubmitter = submitter;
            LastSubscribeActiveAtOffset = activeAtOffset;
            return EmptyAsync<AcsSnapshotEntry<T>>(cancellationToken);
        }

        public Task<LedgerOffset> GetLedgerEndAsync(
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(LedgerOffset.Begin);

        public void Dispose()
        {
        }

        private static async IAsyncEnumerable<TItem> EmptyAsync<TItem>(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            yield break;
        }
    }

    /// <summary>
    /// Minimal <see cref="ITemplate"/> for routing tests. Carries no payload —
    /// the assertions here are about which submitter reaches the primitive,
    /// not about template encoding.
    /// </summary>
    private sealed record FakeTemplate : ITemplate
    {
        public static Identifier TemplateId { get; } =
            new("pkg", "Module", "FakeTemplate");

        public static string PackageId => "pkg";
        public static string PackageName => "fake";
        public static Version PackageVersion { get; } = new(1, 0, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => new(TemplateId, []);
    }
}
