// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

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
/// Verifies the default-interface-method (DIM) overloads of <see cref="ILedgerClient"/>
/// that accept <see cref="SubmitterInfo"/>: single-party submissions delegate to the
/// existing string <c>actAs</c> methods, multi-party submissions throw a clear
/// <see cref="NotSupportedException"/>, and an explicit override of the SubmitterInfo
/// overload bypasses the default delegation entirely.
/// </summary>
public class LedgerClientSubmitterInfoTests
{
    private static readonly ExerciseCommand SampleCommand = new(
        new Identifier("pkg", "Module", "Template"),
        ContractId: "cid-1",
        Choice: "DoIt",
        ChoiceArgument: new DamlRecord(null, []));

    private static readonly SubmitterInfo SingleParty = new(new Party("alice"));

    private static readonly SubmitterInfo MultiParty = new(
        actAs: new HashSet<Party> { new("alice"), new("bob") });

    private static readonly SubmitterInfo SinglePartyWithReadAs = new(
        actAs: new HashSet<Party> { new("alice") },
        readAs: new HashSet<Party> { new("observer") });

    [Fact]
    public async Task ExerciseAsync_with_result_single_party_should_delegate_to_string_actAs()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.ExerciseAsync<int>(SampleCommand, SingleParty);

        fake.LastExerciseWithResultActAs.Should().Be("alice");
    }

    [Fact]
    public async Task ExerciseAsync_void_single_party_should_delegate_to_string_actAs()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.ExerciseAsync(SampleCommand, SingleParty);

        fake.LastExerciseVoidActAs.Should().Be("alice");
    }

    [Fact]
    public async Task TryCreateAsync_single_party_should_delegate_to_string_actAs()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.TryCreateAsync(new FakeTemplate(), SingleParty);

        fake.LastCreateActAs.Should().Be("alice");
    }

    [Fact]
    public async Task TryExerciseForCreatedAsync_single_party_should_delegate_to_string_actAs()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.TryExerciseForCreatedAsync<FakeTemplate>(SampleCommand, SingleParty);

        fake.LastExerciseForCreatedActAs.Should().Be("alice");
    }

    [Fact]
    public async Task SubscribeAsync_single_party_should_delegate_to_string_actAs()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await foreach (var _ in client.SubscribeAsync<FakeTemplate>(SingleParty))
        {
            // Drain — the fake yields nothing.
        }

        fake.LastSubscribeActAs.Should().Be("alice");
    }

    [Fact]
    public async Task SubscribeActiveAsync_single_party_should_delegate_to_string_actAs()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await foreach (var _ in client.SubscribeActiveAsync<FakeTemplate>(SingleParty))
        {
            // Drain — the fake yields nothing.
        }

        fake.LastSubscribeActiveActAs.Should().Be("alice");
    }

    [Fact]
    public async Task ExerciseAsync_with_result_multi_party_should_throw_NotSupportedException()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        Func<Task> act = () => client.ExerciseAsync<int>(SampleCommand, MultiParty);

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*ExerciseAsync*single ActAs party with no ReadAs parties*");
    }

    [Fact]
    public async Task ExerciseAsync_void_multi_party_should_throw_NotSupportedException()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        Func<Task> act = () => client.ExerciseAsync(SampleCommand, MultiParty);

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*ExerciseAsync*single ActAs party with no ReadAs parties*");
    }

    [Fact]
    public async Task TryCreateAsync_multi_party_should_throw_NotSupportedException()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        Func<Task> act = () => client.TryCreateAsync(new FakeTemplate(), MultiParty);

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*TryCreateAsync*single ActAs party with no ReadAs parties*");
    }

    [Fact]
    public async Task TryExerciseForCreatedAsync_multi_party_should_throw_NotSupportedException()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        Func<Task> act = () => client.TryExerciseForCreatedAsync<FakeTemplate>(SampleCommand, MultiParty);

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*TryExerciseForCreatedAsync*single ActAs party with no ReadAs parties*");
    }

    [Fact]
    public async Task SubscribeAsync_multi_party_should_throw_NotSupportedException()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        Func<Task> act = async () =>
        {
            await foreach (var _ in client.SubscribeAsync<FakeTemplate>(MultiParty))
            {
                // Drain.
            }
        };

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*SubscribeAsync*single ActAs party with no ReadAs parties*");
    }

    [Fact]
    public async Task SubscribeActiveAsync_multi_party_should_throw_NotSupportedException()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        Func<Task> act = async () =>
        {
            await foreach (var _ in client.SubscribeActiveAsync<FakeTemplate>(MultiParty))
            {
                // Drain.
            }
        };

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*SubscribeActiveAsync*single ActAs party with no ReadAs parties*");
    }

    [Fact]
    public async Task SubmitterInfo_with_readAs_should_be_treated_as_multi_party()
    {
        // Single ActAs party but with a ReadAs party — must NOT be flattened to the
        // single-party path because the readAs visibility would be lost.
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        Func<Task> act = () => client.ExerciseAsync<int>(SampleCommand, SinglePartyWithReadAs);

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task Custom_override_of_SubmitterInfo_overload_should_be_invoked_for_multi_party()
    {
        var fake = new MultiPartyAwareLedgerClient();
        ILedgerClient client = fake;

        await client.ExerciseAsync<int>(SampleCommand, MultiParty);

        // The override recorded the call directly; the string-actAs base method must NOT have run.
        fake.OverrideHits.Should().Be(1);
        fake.LastExerciseWithResultActAs.Should().BeNull();
    }

    [Fact]
    public async Task Custom_override_of_SubmitterInfo_overload_should_win_over_default_for_single_party()
    {
        // Even single-party submissions go through the override when the implementation
        // chooses to override — the default delegation is opt-out, not opt-in.
        var fake = new MultiPartyAwareLedgerClient();
        ILedgerClient client = fake;

        await client.ExerciseAsync<int>(SampleCommand, SingleParty);

        fake.OverrideHits.Should().Be(1);
        fake.LastExerciseWithResultActAs.Should().BeNull();
    }

    /// <summary>
    /// Records the string-<c>actAs</c> argument each method receives so tests can
    /// assert that the default-interface-method delegation hit the legacy overload
    /// with the right party.
    /// </summary>
    private class RecordingLedgerClient : ILedgerClient
    {
        public string? LastExerciseWithResultActAs { get; private set; }
        public string? LastExerciseVoidActAs { get; private set; }
        public string? LastCreateActAs { get; private set; }
        public string? LastExerciseForCreatedActAs { get; private set; }
        public string? LastSubscribeActAs { get; private set; }
        public string? LastSubscribeActiveActAs { get; private set; }

        public Task<TResult> ExerciseAsync<TResult>(
            ExerciseCommand command,
            string actAs,
            string? workflowId = null,
            CancellationToken cancellationToken = default)
        {
            LastExerciseWithResultActAs = actAs;
            return Task.FromResult(default(TResult)!);
        }

        public Task ExerciseAsync(
            ExerciseCommand command,
            string actAs,
            string? workflowId = null,
            CancellationToken cancellationToken = default)
        {
            LastExerciseVoidActAs = actAs;
            return Task.CompletedTask;
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
            string actAs,
            string? workflowId = null,
            CancellationToken cancellationToken = default)
            where TTemplate : ITemplate
        {
            LastCreateActAs = actAs;
            return Task.FromResult<ExerciseOutcome<ContractId<TTemplate>>>(
                new ExerciseOutcome<ContractId<TTemplate>>.None());
        }

        public Task<ExerciseOutcome<ContractId<TTemplate>>> TryExerciseForCreatedAsync<TTemplate>(
            ExerciseCommand command,
            string actAs,
            string? workflowId = null,
            CancellationToken cancellationToken = default)
            where TTemplate : ITemplate
        {
            LastExerciseForCreatedActAs = actAs;
            return Task.FromResult<ExerciseOutcome<ContractId<TTemplate>>>(
                new ExerciseOutcome<ContractId<TTemplate>>.None());
        }

        public IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
            string actAs,
            long? fromOffset = null,
            CancellationToken cancellationToken = default)
            where T : ITemplate
        {
            LastSubscribeActAs = actAs;
            return EmptyAsync<ContractStreamEvent<T>>(cancellationToken);
        }

        public IAsyncEnumerable<ContractStreamEvent<T>.Created> SubscribeActiveAsync<T>(
            string actAs,
            CancellationToken cancellationToken = default)
            where T : ITemplate
        {
            LastSubscribeActiveActAs = actAs;
            return EmptyAsync<ContractStreamEvent<T>.Created>(cancellationToken);
        }

        public Task<long> GetLedgerEndAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(0L);

        public void Dispose()
        {
            // No resources to release in the test fake.
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
    /// Overrides the SubmitterInfo overload of <c>ExerciseAsync&lt;TResult&gt;</c> via
    /// explicit interface implementation so it bypasses the default-interface-method
    /// delegation entirely. Tests that the override wins over the default DIM
    /// implementation, including for multi-party calls that the default would otherwise refuse.
    /// </summary>
    private sealed class MultiPartyAwareLedgerClient : RecordingLedgerClient, ILedgerClient
    {
        public int OverrideHits { get; private set; }

        Task<TResult> ILedgerClient.ExerciseAsync<TResult>(
            ExerciseCommand command,
            SubmitterInfo submitter,
            string? workflowId,
            CancellationToken cancellationToken)
        {
            OverrideHits++;
            return Task.FromResult(default(TResult)!);
        }
    }

    /// <summary>
    /// Minimal <see cref="ITemplate"/> for routing tests. Carries no payload —
    /// the assertions here are about whether the SubmitterInfo overload reaches
    /// the right legacy method, not about template encoding.
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
