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
/// Verifies <see cref="LedgerClientExtensions"/>: the throwing convenience
/// wrappers around <see cref="ILedgerClient.TryExerciseAsync{TResult}"/>.
/// </summary>
public class LedgerClientExtensionsTests
{
    private static readonly ExerciseCommand SampleCommand = new(
        new Identifier("pkg", "Module", "Template"),
        ContractId: new ContractId<SampleTemplate>("cid-1"),
        Choice: new ChoiceName("DoIt"),
        ChoiceArgument: new DamlRecord(null, []));

    [Fact]
    public async Task ExerciseAsync_returns_value_when_TryExerciseAsync_returns_One()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<int>.One(42));

        var result = await client.ExerciseAsync<int>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        result.Should().Be(42);
    }

    [Fact]
    public async Task ExerciseAsync_throws_InvalidOperationException_when_TryExerciseAsync_returns_DamlError()
    {
        var outcome = new ExerciseOutcome<int>.DamlError(
            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
            "CONTRACT_NOT_FOUND",
            "Contract not found",
            new Dictionary<string, string>());
        ILedgerClient client = new StubLedgerClient(outcome);

        Func<Task> act = () => client.ExerciseAsync<int>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*CONTRACT_NOT_FOUND*");
    }

    [Fact]
    public async Task ExerciseAsync_throws_InvalidOperationException_when_TryExerciseAsync_returns_InfraError()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<int>.InfraError(14, "Connection reset"));

        Func<Task> act = () => client.ExerciseAsync<int>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Connection reset*");
    }

    [Fact]
    public async Task ExerciseAsync_throws_InvalidOperationException_when_TryExerciseAsync_returns_None()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<int>.None());

        Func<Task> act = () => client.ExerciseAsync<int>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*None*");
    }

    [Fact]
    public async Task ExerciseAsync_throws_InvalidOperationException_when_TryExerciseAsync_returns_Many()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<int>.Many(3, ["cid-1", "cid-2", "cid-3"]));

        Func<Task> act = () => client.ExerciseAsync<int>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Many*3*");
    }

    [Fact]
    public async Task ExerciseAsync_void_does_not_throw_when_TryExerciseAsync_returns_One()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<object>.One(new object()));

        Func<Task> act = () => client.ExerciseAsync(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExerciseAsync_void_does_not_throw_when_TryExerciseAsync_returns_None()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<object>.None());

        Func<Task> act = () => client.ExerciseAsync(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExerciseAsync_void_does_not_throw_when_TryExerciseAsync_returns_Many()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<object>.Many(2, ["cid-1", "cid-2"]));

        Func<Task> act = () => client.ExerciseAsync(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExerciseAsync_void_throws_InvalidOperationException_when_TryExerciseAsync_returns_DamlError()
    {
        var outcome = new ExerciseOutcome<object>.DamlError(
            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
            "CONTRACT_NOT_FOUND",
            "Contract not found",
            new Dictionary<string, string>());
        ILedgerClient client = new StubLedgerClient(outcome);

        Func<Task> act = () => client.ExerciseAsync(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*CONTRACT_NOT_FOUND*");
    }

    [Fact]
    public async Task ExerciseAsync_void_throws_InvalidOperationException_when_TryExerciseAsync_returns_InfraError()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<object>.InfraError(14, "Connection reset"));

        Func<Task> act = () => client.ExerciseAsync(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Connection reset*");
    }

    [Fact]
    public async Task ExerciseAsync_with_SubmitterInfo_returns_value_when_TryExerciseAsync_returns_One()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<int>.One(99));
        var submitter = new SubmitterInfo(new Party("alice"));

        var result = await client.ExerciseAsync<int>(SampleCommand, submitter, cancellationToken: TestContext.Current.CancellationToken);

        result.Should().Be(99);
    }

    [Fact]
    public async Task ExerciseAsync_with_SubmitterInfo_throws_InvalidOperationException_when_TryExerciseAsync_returns_DamlError()
    {
        var outcome = new ExerciseOutcome<int>.DamlError(
            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
            "CONTRACT_NOT_FOUND",
            "Contract not found",
            new Dictionary<string, string>());
        ILedgerClient client = new StubLedgerClient(outcome);
        var submitter = new SubmitterInfo(new Party("alice"));

        Func<Task> act = () => client.ExerciseAsync<int>(SampleCommand, submitter, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*CONTRACT_NOT_FOUND*");
    }

    [Fact]
    public async Task ExerciseAsync_with_SubmitterInfo_throws_InvalidOperationException_when_TryExerciseAsync_returns_InfraError()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<int>.InfraError(14, "Connection reset"));
        var submitter = new SubmitterInfo(new Party("alice"));

        Func<Task> act = () => client.ExerciseAsync<int>(SampleCommand, submitter, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Connection reset*");
    }

    [Fact]
    public async Task ExerciseAsync_with_SubmitterInfo_throws_InvalidOperationException_when_TryExerciseAsync_returns_None()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<int>.None());
        var submitter = new SubmitterInfo(new Party("alice"));

        Func<Task> act = () => client.ExerciseAsync<int>(SampleCommand, submitter, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*None*");
    }

    [Fact]
    public async Task ExerciseAsync_with_SubmitterInfo_throws_InvalidOperationException_when_TryExerciseAsync_returns_Many()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<int>.Many(3, ["cid-1", "cid-2", "cid-3"]));
        var submitter = new SubmitterInfo(new Party("alice"));

        Func<Task> act = () => client.ExerciseAsync<int>(SampleCommand, submitter, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Many*3*");
    }

    [Fact]
    public async Task ExerciseAsync_void_with_SubmitterInfo_does_not_throw_when_TryExerciseAsync_returns_One()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<object>.One(new object()));
        var submitter = new SubmitterInfo(new Party("alice"));

        Func<Task> act = () => client.ExerciseAsync(SampleCommand, submitter, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExerciseAsync_void_with_SubmitterInfo_does_not_throw_when_TryExerciseAsync_returns_None()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<object>.None());
        var submitter = new SubmitterInfo(new Party("alice"));

        Func<Task> act = () => client.ExerciseAsync(SampleCommand, submitter, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExerciseAsync_void_with_SubmitterInfo_does_not_throw_when_TryExerciseAsync_returns_Many()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<object>.Many(2, ["cid-1", "cid-2"]));
        var submitter = new SubmitterInfo(new Party("alice"));

        Func<Task> act = () => client.ExerciseAsync(SampleCommand, submitter, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExerciseAsync_void_with_SubmitterInfo_throws_InvalidOperationException_when_TryExerciseAsync_returns_DamlError()
    {
        var outcome = new ExerciseOutcome<object>.DamlError(
            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
            "CONTRACT_NOT_FOUND",
            "Contract not found",
            new Dictionary<string, string>());
        ILedgerClient client = new StubLedgerClient(outcome);
        var submitter = new SubmitterInfo(new Party("alice"));

        Func<Task> act = () => client.ExerciseAsync(SampleCommand, submitter, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*CONTRACT_NOT_FOUND*");
    }

    [Fact]
    public async Task ExerciseAsync_void_with_SubmitterInfo_throws_InvalidOperationException_when_TryExerciseAsync_returns_InfraError()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<object>.InfraError(14, "Connection reset"));
        var submitter = new SubmitterInfo(new Party("alice"));

        Func<Task> act = () => client.ExerciseAsync(SampleCommand, submitter, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Connection reset*");
    }

    /// <summary>
    /// Minimal <see cref="ILedgerClient"/> stub that returns a pre-configured
    /// <see cref="ExerciseOutcome{T}"/> from the <see cref="SubmitterInfo"/>
    /// <c>TryExerciseAsync&lt;TResult&gt;</c> primitive. The outcome is stored as
    /// <c>object</c> and cast on retrieval so a single non-generic stub can satisfy
    /// the generic contract for any <c>TResult</c>. The throwing extension wrappers
    /// and the <c>Party</c>-<c>actAs</c> default-interface-method both route here.
    /// </summary>
    private sealed class StubLedgerClient : ILedgerClient
    {
        private readonly object _outcome;

        public StubLedgerClient(object outcome) => _outcome = outcome;

        public Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(
            ExerciseCommand command,
            SubmitterInfo submitter,
            string? workflowId = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult((ExerciseOutcome<TResult>)_outcome);

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
            => Task.FromResult<ExerciseOutcome<ContractId<TTemplate>>>(new ExerciseOutcome<ContractId<TTemplate>>.None());

        public Task<ExerciseOutcome<ContractId<TTemplate>>> TryExerciseForCreatedAsync<TTemplate>(
            ExerciseCommand command,
            SubmitterInfo submitter,
            string? workflowId = null,
            CancellationToken cancellationToken = default)
            where TTemplate : ITemplate
            => Task.FromResult<ExerciseOutcome<ContractId<TTemplate>>>(new ExerciseOutcome<ContractId<TTemplate>>.None());

        public IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
            SubmitterInfo submitter,
            long? fromOffset = null,
            CancellationToken cancellationToken = default)
            where T : ITemplate
            => EmptyAsync<ContractStreamEvent<T>>(cancellationToken);

        public IAsyncEnumerable<ContractStreamEvent<T>.Created> SubscribeActiveAsync<T>(
            SubmitterInfo submitter,
            CancellationToken cancellationToken = default)
            where T : ITemplate
            => EmptyAsync<ContractStreamEvent<T>.Created>(cancellationToken);

        public Task<long> GetLedgerEndAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(0L);

        public void Dispose() { }

        private static async IAsyncEnumerable<TItem> EmptyAsync<TItem>(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            yield break;
        }
    }
}

internal sealed record SampleTemplate : ITemplate
{
    public static Identifier TemplateId => new("pkg", "Module", "Template");
    public static string PackageId => "pkg";
    public static string PackageName => "pkg-name";
    public static Version PackageVersion => new(1, 0, 0);

    public DamlRecord ToRecord() => DamlRecord.Create();
    public static SampleTemplate FromRecord(DamlRecord record) => new();
}
