// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using FluentAssertions;
using Xunit;

namespace Daml.Ledger.Abstractions.Tests;

/// <summary>
/// Verifies the authorization routing of <see cref="ILedgerClient"/>: the
/// <see cref="SubmitterInfo"/> overloads are the primitives implementations override,
/// the <c>Party</c> <c>actAs</c> default-interface-methods forward to them with a single
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
    public async Task ExerciseAsync_void_Party_actAs_forwards_to_SubmitterInfo_primitive_with_single_actAs_no_readAs()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.ExerciseAsync(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        fake.LastExerciseSubmitter!.Value.ActAs.Select(p => p.Id).Should().Equal("alice");
        fake.LastExerciseSubmitter!.Value.ReadAs.Should().BeEmpty();
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
    public async Task TryExerciseForCreatedAsync_Party_actAs_forwards_to_SubmitterInfo_primitive_with_single_actAs_no_readAs()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.TryExerciseForCreatedAsync<FakeTemplate>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        fake.LastExerciseForCreatedSubmitter!.Value.ActAs.Select(p => p.Id).Should().Equal("alice");
        fake.LastExerciseForCreatedSubmitter!.Value.ReadAs.Should().BeEmpty();
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
    public async Task TryExerciseForCreatedAsync_with_multi_party_actAs_carries_all_parties_through_without_throwing()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.TryExerciseForCreatedAsync<FakeTemplate>(SampleCommand, MultiParty, cancellationToken: TestContext.Current.CancellationToken);

        fake.LastExerciseForCreatedSubmitter!.Value.ActAs.Select(p => p.Id).Should().BeEquivalentTo("alice", "bob");
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
    public async Task TryExerciseForCreatedAsync_with_single_actAs_and_readAs_carries_readAs_through_without_throwing()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.TryExerciseForCreatedAsync<FakeTemplate>(SampleCommand, SinglePartyWithReadAs, cancellationToken: TestContext.Current.CancellationToken);

        fake.LastExerciseForCreatedSubmitter!.Value.ActAs.Select(p => p.Id).Should().Equal("alice");
        fake.LastExerciseForCreatedSubmitter!.Value.ReadAs.Select(p => p.Id).Should().Equal("observer");
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

    /// <summary>
    /// Records the <see cref="SubmitterInfo"/> each primitive receives so tests can
    /// assert that the <c>Party</c>-<c>actAs</c> default-interface-method forwards with
    /// the right single party and that read-as / multi-party submissions carry every
    /// party through to the primitive.
    /// </summary>
    private class RecordingLedgerClient : ILedgerClient
    {
        public SubmitterInfo? LastExerciseSubmitter { get; private set; }
        public SubmitterInfo? LastCreateSubmitter { get; private set; }
        public SubmitterInfo? LastExerciseForCreatedSubmitter { get; private set; }
        public SubmitterInfo? LastSubscribeSubmitter { get; private set; }
        public SubmitterInfo? LastSubscribeActiveSubmitter { get; private set; }

        public Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(
            ExerciseCommand command,
            SubmitterInfo submitter,
            string? workflowId = null,
            CancellationToken cancellationToken = default)
        {
            LastExerciseSubmitter = submitter;
            return Task.FromResult<ExerciseOutcome<TResult>>(new ExerciseOutcome<TResult>.One(default(TResult)!));
        }

        public Task<string> SubmitAsync(
            CommandsSubmission submission,
            CancellationToken cancellationToken = default)
            => Task.FromResult("update-id");

        public Task<ExerciseOutcome<TransactionResult>> TrySubmitAndWaitForTransactionAsync(
            CommandsSubmission submission,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
            TTemplate payload,
            SubmitterInfo submitter,
            string? workflowId = null,
            CancellationToken cancellationToken = default)
            where TTemplate : ITemplate
        {
            LastCreateSubmitter = submitter;
            return Task.FromResult<ExerciseOutcome<ContractId<TTemplate>>>(
                new ExerciseOutcome<ContractId<TTemplate>>.None());
        }

        public Task<ExerciseOutcome<ContractId<TTemplate>>> TryExerciseForCreatedAsync<TTemplate>(
            ExerciseCommand command,
            SubmitterInfo submitter,
            string? workflowId = null,
            CancellationToken cancellationToken = default)
            where TTemplate : ITemplate
        {
            LastExerciseForCreatedSubmitter = submitter;
            return Task.FromResult<ExerciseOutcome<ContractId<TTemplate>>>(
                new ExerciseOutcome<ContractId<TTemplate>>.None());
        }

        public IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
            SubmitterInfo submitter,
            long? fromOffset = null,
            CancellationToken cancellationToken = default)
            where T : ITemplate
        {
            LastSubscribeSubmitter = submitter;
            return EmptyAsync<ContractStreamEvent<T>>(cancellationToken);
        }

        public IAsyncEnumerable<ContractStreamEvent<T>.Created> SubscribeActiveAsync<T>(
            SubmitterInfo submitter,
            CancellationToken cancellationToken = default)
            where T : ITemplate
        {
            LastSubscribeActiveSubmitter = submitter;
            return EmptyAsync<ContractStreamEvent<T>.Created>(cancellationToken);
        }

        public Task<long> GetLedgerEndAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(0L);

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

        public DamlRecord ToRecord() => new(TemplateId, []);
    }
}
