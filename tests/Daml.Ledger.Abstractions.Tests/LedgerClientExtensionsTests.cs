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
/// Verifies <see cref="ThrowingExercise"/>: the throwing convenience wrappers around
/// <see cref="ILedgerWriter.TryExerciseAsync{TResult}(ExerciseCommand, SubmitterInfo, string?, TimeSpan?, CancellationToken)"/>.
/// </summary>
public class LedgerClientExtensionsTests
{
    private static readonly ExerciseCommand SampleCommand = new(
        new Identifier("pkg", "Module", "Template"),
        ContractId: new ContractId<SampleTemplate>("cid-1"),
        Choice: new ChoiceName("DoIt"),
        ChoiceArgument: new DamlRecord(null, []));

    private static readonly TransactionResult SampleTransaction = new(
        "update-id", LedgerOffset.Begin, [], [], new CommandId("cmd-id"));

    private static TransactionResult TransactionCreating(params string[] contractIds) =>
        new("update-id", LedgerOffset.Begin,
            contractIds.Select(id => new CreatedContract(id, SampleTemplate.TemplateId, "{}")).ToArray(),
            [],
            new CommandId("cmd-id"));

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
    public async Task ExerciseAsync_throws_OperationCanceledException_not_LedgerOperationException_when_TryExerciseAsync_returns_InfraError_and_caller_token_is_cancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<int>.InfraError(1, "Cancelled"));

        Func<Task> act = () => client.ExerciseAsync<int>(SampleCommand, new Party("alice"), cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExerciseAsync_void_throws_OperationCanceledException_not_LedgerOperationException_when_TrySubmitAndWaitForTransactionAsync_returns_InfraError_and_caller_token_is_cancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<TransactionResult>.InfraError(1, "Cancelled"));

        Func<Task> act = () => client.ExerciseAsync(SampleCommand, new Party("alice"), cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
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
    public async Task ExerciseAsync_void_does_not_throw_when_TrySubmitAndWaitForTransactionAsync_returns_One()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<TransactionResult>.One(SampleTransaction));

        Func<Task> act = () => client.ExerciseAsync(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExerciseAsync_void_does_not_throw_when_TrySubmitAndWaitForTransactionAsync_returns_None()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<TransactionResult>.None());

        Func<Task> act = () => client.ExerciseAsync(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExerciseAsync_void_does_not_throw_when_TrySubmitAndWaitForTransactionAsync_returns_Many()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<TransactionResult>.Many(2, ["cid-1", "cid-2"]));

        Func<Task> act = () => client.ExerciseAsync(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExerciseAsync_void_throws_InvalidOperationException_when_TrySubmitAndWaitForTransactionAsync_returns_DamlError()
    {
        var outcome = new ExerciseOutcome<TransactionResult>.DamlError(
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
    public async Task ExerciseAsync_void_throws_InvalidOperationException_when_TrySubmitAndWaitForTransactionAsync_returns_InfraError()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<TransactionResult>.InfraError(14, "Connection reset"));

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
    public async Task ExerciseAsync_with_SubmitterInfo_throws_OperationCanceledException_not_LedgerOperationException_when_TryExerciseAsync_returns_InfraError_and_caller_token_is_cancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<int>.InfraError(1, "Cancelled"));
        var submitter = new SubmitterInfo(new Party("alice"));

        Func<Task> act = () => client.ExerciseAsync<int>(SampleCommand, submitter, cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
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
    public async Task ExerciseAsync_void_with_SubmitterInfo_does_not_throw_when_TrySubmitAndWaitForTransactionAsync_returns_One()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<TransactionResult>.One(SampleTransaction));
        var submitter = new SubmitterInfo(new Party("alice"));

        Func<Task> act = () => client.ExerciseAsync(SampleCommand, submitter, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExerciseAsync_void_with_SubmitterInfo_does_not_throw_when_TrySubmitAndWaitForTransactionAsync_returns_None()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<TransactionResult>.None());
        var submitter = new SubmitterInfo(new Party("alice"));

        Func<Task> act = () => client.ExerciseAsync(SampleCommand, submitter, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExerciseAsync_void_with_SubmitterInfo_does_not_throw_when_TrySubmitAndWaitForTransactionAsync_returns_Many()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<TransactionResult>.Many(2, ["cid-1", "cid-2"]));
        var submitter = new SubmitterInfo(new Party("alice"));

        Func<Task> act = () => client.ExerciseAsync(SampleCommand, submitter, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExerciseAsync_void_with_SubmitterInfo_throws_InvalidOperationException_when_TrySubmitAndWaitForTransactionAsync_returns_DamlError()
    {
        var outcome = new ExerciseOutcome<TransactionResult>.DamlError(
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
    public async Task ExerciseAsync_void_with_SubmitterInfo_throws_InvalidOperationException_when_TrySubmitAndWaitForTransactionAsync_returns_InfraError()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<TransactionResult>.InfraError(14, "Connection reset"));
        var submitter = new SubmitterInfo(new Party("alice"));

        Func<Task> act = () => client.ExerciseAsync(SampleCommand, submitter, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Connection reset*");
    }

    [Fact]
    public async Task ExerciseAsync_void_with_SubmitterInfo_throws_OperationCanceledException_not_LedgerOperationException_when_TrySubmitAndWaitForTransactionAsync_returns_InfraError_and_caller_token_is_cancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<TransactionResult>.InfraError(1, "Cancelled"));
        var submitter = new SubmitterInfo(new Party("alice"));

        Func<Task> act = () => client.ExerciseAsync(SampleCommand, submitter, cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExerciseAsync_throws_LedgerOperationException_carrying_the_DamlError_outcome()
    {
        var outcome = new ExerciseOutcome<int>.DamlError(
            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
            "CONTRACT_NOT_FOUND",
            "Contract not found",
            new Dictionary<string, string> { ["cid"] = "00abc" });
        ILedgerClient client = new StubLedgerClient(outcome);

        Func<Task> act = () => client.ExerciseAsync<int>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        exception.Category.Should().Be(DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing);
        exception.ErrorId.Should().Be("CONTRACT_NOT_FOUND");
        exception.Metadata.Should().ContainKey("cid").WhoseValue.Should().Be("00abc");
        exception.StatusCode.Should().BeNull();
    }

    [Fact]
    public async Task ExerciseAsync_throws_LedgerOperationException_carrying_the_InfraError_outcome()
    {
        var sourceException = new InvalidOperationException("transport failed");
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<int>.InfraError(14, "Connection reset", sourceException));

        Func<Task> act = () => client.ExerciseAsync<int>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        exception.StatusCode.Should().Be(14);
        exception.InnerException.Should().BeSameAs(sourceException);
        exception.Category.Should().BeNull();
        exception.ErrorId.Should().BeNull();
        exception.Metadata.Should().BeNull();
    }

    [Fact]
    public async Task ExerciseAsync_throws_LedgerOperationException_with_null_InnerException_when_InfraError_has_no_SourceException()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<int>.InfraError(14, "Connection reset"));

        Func<Task> act = () => client.ExerciseAsync<int>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        exception.StatusCode.Should().Be(14);
        exception.InnerException.Should().BeNull();
    }

    [Fact]
    public async Task ExerciseAsync_throws_LedgerOperationException_without_error_detail_for_None_and_Many()
    {
        ILedgerClient noneClient = new StubLedgerClient(new ExerciseOutcome<int>.None());
        ILedgerClient manyClient = new StubLedgerClient(new ExerciseOutcome<int>.Many(2, ["cid-1", "cid-2"]));

        Func<Task> noneAct = () => noneClient.ExerciseAsync<int>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);
        Func<Task> manyAct = () => manyClient.ExerciseAsync<int>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        var noneException = (await noneAct.Should().ThrowAsync<LedgerOperationException>()).Which;
        var manyException = (await manyAct.Should().ThrowAsync<LedgerOperationException>()).Which;
        noneException.Category.Should().BeNull();
        noneException.StatusCode.Should().BeNull();
        manyException.Category.Should().BeNull();
        manyException.StatusCode.Should().BeNull();
    }

    [Fact]
    public async Task ExerciseAsync_void_throws_LedgerOperationException_carrying_the_DamlError_outcome()
    {
        var outcome = new ExerciseOutcome<TransactionResult>.DamlError(
            DamlErrorCategory.ContentionOnSharedResources,
            "LOCAL_VERDICT_LOCKED_CONTRACTS",
            "Contract is locked",
            new Dictionary<string, string>());
        ILedgerClient client = new StubLedgerClient(outcome);

        Func<Task> act = () => client.ExerciseAsync(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        exception.Category.Should().Be(DamlErrorCategory.ContentionOnSharedResources);
        exception.ErrorId.Should().Be("LOCAL_VERDICT_LOCKED_CONTRACTS");
    }

    [Fact]
    public async Task ExerciseAsync_void_throws_LedgerOperationException_carrying_the_InfraError_outcome()
    {
        var sourceException = new TimeoutException("deadline transport failure");
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<TransactionResult>.InfraError(4, "Deadline exceeded", sourceException));

        Func<Task> act = () => client.ExerciseAsync(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        exception.StatusCode.Should().Be(4);
        exception.InnerException.Should().BeSameAs(sourceException);
    }

    [Fact]
    public async Task TryCreateOneByExerciseAsync_returns_One_when_transaction_created_exactly_one_TTemplate()
    {
        ILedgerClient client = new StubLedgerClient(
            new ExerciseOutcome<TransactionResult>.One(TransactionCreating("cid-created")));

        var outcome = await client.TryCreateOneByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ContractId<SampleTemplate>>.One>()
            .Which.Result.Value.Should().Be("cid-created");
    }

    [Fact]
    public async Task TryCreateOneByExerciseAsync_returns_None_when_transaction_created_no_TTemplate()
    {
        ILedgerClient client = new StubLedgerClient(
            new ExerciseOutcome<TransactionResult>.One(TransactionCreating()));

        var outcome = await client.TryCreateOneByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ContractId<SampleTemplate>>.None>();
    }

    [Fact]
    public async Task TryCreateOneByExerciseAsync_returns_Many_when_transaction_created_more_than_one_TTemplate()
    {
        ILedgerClient client = new StubLedgerClient(
            new ExerciseOutcome<TransactionResult>.One(TransactionCreating("cid-1", "cid-2")));

        var outcome = await client.TryCreateOneByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        var many = outcome.Should().BeOfType<ExerciseOutcome<ContractId<SampleTemplate>>.Many>().Which;
        many.Count.Should().Be(2);
        many.ContractIds.Should().Equal("cid-1", "cid-2");
    }

    [Fact]
    public async Task TryCreateOneByExerciseAsync_propagates_writer_level_Many_without_collapsing_to_None()
    {
        ILedgerClient client = new StubLedgerClient(
            new ExerciseOutcome<TransactionResult>.Many(3, ["cid-1", "cid-2", "cid-3"]));

        var outcome = await client.TryCreateOneByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        var many = outcome.Should().BeOfType<ExerciseOutcome<ContractId<SampleTemplate>>.Many>().Which;
        many.Count.Should().Be(3);
        many.ContractIds.Should().Equal("cid-1", "cid-2", "cid-3");
    }

    [Fact]
    public async Task TryCreateOneByExerciseAsync_propagates_DamlError()
    {
        var outcome = new ExerciseOutcome<TransactionResult>.DamlError(
            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
            "CONTRACT_NOT_FOUND",
            "Contract not found",
            new Dictionary<string, string>());
        ILedgerClient client = new StubLedgerClient(outcome);

        var result = await client.TryCreateOneByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        result.Should().BeOfType<ExerciseOutcome<ContractId<SampleTemplate>>.DamlError>()
            .Which.ErrorId.Should().Be("CONTRACT_NOT_FOUND");
    }

    [Fact]
    public async Task TryCreateOneByExerciseAsync_propagates_InfraError()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<TransactionResult>.InfraError(14, "Connection reset"));

        var result = await client.TryCreateOneByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        result.Should().BeOfType<ExerciseOutcome<ContractId<SampleTemplate>>.InfraError>()
            .Which.StatusCode.Should().Be(14);
    }

    [Fact]
    public async Task TryCreateManyByExerciseAsync_returns_One_with_empty_list_when_transaction_created_no_TTemplate()
    {
        ILedgerClient client = new StubLedgerClient(
            new ExerciseOutcome<TransactionResult>.One(TransactionCreating()));

        var outcome = await client.TryCreateManyByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<IReadOnlyList<ContractId<SampleTemplate>>>.One>()
            .Which.Result.Should().BeEmpty();
    }

    [Fact]
    public async Task TryCreateManyByExerciseAsync_returns_One_with_single_item_when_transaction_created_one_TTemplate()
    {
        ILedgerClient client = new StubLedgerClient(
            new ExerciseOutcome<TransactionResult>.One(TransactionCreating("cid-1")));

        var outcome = await client.TryCreateManyByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<IReadOnlyList<ContractId<SampleTemplate>>>.One>()
            .Which.Result.Select(id => id.Value).Should().Equal("cid-1");
    }

    [Fact]
    public async Task TryCreateManyByExerciseAsync_returns_One_with_all_items_when_transaction_created_many_TTemplate()
    {
        ILedgerClient client = new StubLedgerClient(
            new ExerciseOutcome<TransactionResult>.One(TransactionCreating("cid-1", "cid-2", "cid-3")));

        var outcome = await client.TryCreateManyByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<IReadOnlyList<ContractId<SampleTemplate>>>.One>()
            .Which.Result.Select(id => id.Value).Should().Equal("cid-1", "cid-2", "cid-3");
    }

    [Fact]
    public async Task CreateOneByExerciseAsync_returns_id_when_transaction_created_exactly_one_TTemplate()
    {
        ILedgerClient client = new StubLedgerClient(
            new ExerciseOutcome<TransactionResult>.One(TransactionCreating("cid-1")));

        var id = await client.CreateOneByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        id.Value.Should().Be("cid-1");
    }

    [Fact]
    public async Task CreateOneByExerciseAsync_throws_LedgerOperationException_when_transaction_created_no_TTemplate()
    {
        ILedgerClient client = new StubLedgerClient(
            new ExerciseOutcome<TransactionResult>.One(TransactionCreating()));

        Func<Task> act = () => client.CreateOneByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<LedgerOperationException>()
            .WithMessage("*expected exactly one*");
    }

    [Fact]
    public async Task CreateOneByExerciseAsync_throws_LedgerOperationException_mentioning_CreateManyByExerciseAsync_when_transaction_created_many_TTemplate()
    {
        ILedgerClient client = new StubLedgerClient(
            new ExerciseOutcome<TransactionResult>.One(TransactionCreating("cid-1", "cid-2")));

        Func<Task> act = () => client.CreateOneByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<LedgerOperationException>()
            .WithMessage("*CreateManyByExerciseAsync*");
    }

    [Fact]
    public async Task CreateOneByExerciseAsync_throws_LedgerOperationException_on_DamlError()
    {
        var outcome = new ExerciseOutcome<TransactionResult>.DamlError(
            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
            "CONTRACT_NOT_FOUND",
            "Contract not found",
            new Dictionary<string, string>());
        ILedgerClient client = new StubLedgerClient(outcome);

        Func<Task> act = () => client.CreateOneByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<LedgerOperationException>()
            .WithMessage("*CONTRACT_NOT_FOUND*");
    }

    [Fact]
    public async Task CreateOneByExerciseAsync_throws_OperationCanceledException_when_InfraError_and_caller_token_is_cancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<TransactionResult>.InfraError(1, "Cancelled"));

        Func<Task> act = () => client.CreateOneByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CreateManyByExerciseAsync_returns_single_item_list_when_transaction_created_one_TTemplate()
    {
        ILedgerClient client = new StubLedgerClient(
            new ExerciseOutcome<TransactionResult>.One(TransactionCreating("cid-1")));

        var ids = await client.CreateManyByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        ids.Select(id => id.Value).Should().Equal("cid-1");
    }

    [Fact]
    public async Task CreateManyByExerciseAsync_returns_empty_list_when_transaction_created_no_TTemplate()
    {
        ILedgerClient client = new StubLedgerClient(
            new ExerciseOutcome<TransactionResult>.One(TransactionCreating()));

        var ids = await client.CreateManyByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        ids.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateManyByExerciseAsync_throws_LedgerOperationException_on_DamlError()
    {
        var outcome = new ExerciseOutcome<TransactionResult>.DamlError(
            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
            "CONTRACT_NOT_FOUND",
            "Contract not found",
            new Dictionary<string, string>());
        ILedgerClient client = new StubLedgerClient(outcome);

        Func<Task> act = () => client.CreateManyByExerciseAsync<SampleTemplate>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<LedgerOperationException>()
            .WithMessage("*CONTRACT_NOT_FOUND*");
    }

    [Fact]
    public async Task DisposeAsync_default_bridge_delegates_to_Dispose()
    {
        var client = new StubLedgerClient(new ExerciseOutcome<int>.None());

        await ((IAsyncDisposable)(ILedgerClient)client).DisposeAsync();

        client.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Read_paths_accept_an_interface_marker_as_type_argument()
    {
        ILedgerClient client = new StubLedgerClient(new ExerciseOutcome<TransactionResult>.None());

        var exercised = await client.TryCreateOneByExerciseAsync<SampleInterface>(
            SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);
        var subscription = client.SubscribeAsync<SampleInterface>(
            new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);
        var events = new List<ContractStreamEvent<SampleInterface>>();
        await foreach (var evt in subscription)
        {
            events.Add(evt);
        }

        exercised.Should().BeOfType<ExerciseOutcome<ContractId<SampleInterface>>.None>();
        events.Should().BeEmpty();
    }

    /// <summary>
    /// Minimal <see cref="ILedgerClient"/> stub that returns a pre-configured
    /// <see cref="ExerciseOutcome{T}"/> from both the <c>TryExerciseAsync&lt;TResult&gt;</c>
    /// and <c>TrySubmitAndWaitForTransactionAsync</c> primitives. The outcome is stored
    /// as <c>object</c> and cast on retrieval so a single non-generic stub can satisfy
    /// the generic contract for any <c>TResult</c>. The throwing extension wrappers and
    /// the <c>Party</c>-<c>actAs</c> overloads both route here.
    /// </summary>
    private sealed class StubLedgerClient : ILedgerClient
    {
        private readonly object _outcome;

        public StubLedgerClient(object outcome) => _outcome = outcome;

        public Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(
            ExerciseCommand command,
            SubmitterInfo submitter,
            string? workflowId = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult((ExerciseOutcome<TResult>)_outcome);

        public Task<SubmitAndWaitResult> SubmitAndWaitAsync(
            CommandsSubmission submission,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new SubmitAndWaitResult(new CommandId("cmd-id"), "update-id", LedgerOffset.Begin));

        public Task<ExerciseOutcome<TransactionResult>> TrySubmitAndWaitForTransactionAsync(
            CommandsSubmission submission,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult((ExerciseOutcome<TransactionResult>)_outcome);

        public Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
            TTemplate payload,
            SubmitterInfo submitter,
            string? workflowId = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
            where TTemplate : ITemplate
            => Task.FromResult<ExerciseOutcome<ContractId<TTemplate>>>(new ExerciseOutcome<ContractId<TTemplate>>.None());

        public IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
            SubmitterInfo submitter,
            LedgerOffset? fromOffset = null,
            LedgerOffset? toOffset = null,
            CancellationToken cancellationToken = default)
            where T : IDamlType
            => EmptyAsync<ContractStreamEvent<T>>(cancellationToken);

        public IAsyncEnumerable<ContractStreamEvent<T>> SubscribeLedgerEffectsAsync<T>(
            SubmitterInfo submitter,
            LedgerOffset? fromOffset = null,
            LedgerOffset? toOffset = null,
            CancellationToken cancellationToken = default)
            where T : IDamlType
            => EmptyAsync<ContractStreamEvent<T>>(cancellationToken);

        public IAsyncEnumerable<AcsSnapshotEntry<T>> SubscribeActiveAsync<T>(
            SubmitterInfo submitter,
            LedgerOffset? activeAtOffset = null,
            CancellationToken cancellationToken = default)
            where T : IDamlType
            => EmptyAsync<AcsSnapshotEntry<T>>(cancellationToken);

        public Task<LedgerOffset> GetLedgerEndAsync(
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(LedgerOffset.Begin);

        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;

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
    public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

    public DamlRecord ToRecord() => DamlRecord.Create();
    public static SampleTemplate FromRecord(DamlRecord record) => new();
}

internal sealed record SampleInterface : IDamlInterface
{
    public static Identifier InterfaceId => new("iface-pkg", "Module", "ISample");
    public static string PackageId => "iface-pkg";
    public static string PackageName => "iface-name";
    public static Version PackageVersion => new(1, 0, 0);
    public static DamlTypeDescriptor DamlTypeId { get; } = new(InterfaceId, DamlTypeKind.Interface, PackageName);

    public DamlRecord ToRecord() => DamlRecord.Create();
}
