// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Ledger.Abstractions.Extensions;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime;
using Daml.Runtime.Streams;
using AwesomeAssertions;
using Xunit;

namespace Daml.Ledger.Abstractions.Tests;

/// <summary>
/// Verifies <see cref="SingleCommandExtensions"/>: the shared single-command submission
/// path that generated exercisers and the hand-written write-path extensions both use.
/// </summary>
public class SingleCommandExtensionsTests
{
    private static readonly ExerciseCommand SampleCommand = new(
        new Identifier("pkg", "Module", "Template"),
        ContractId: new ContractId<SampleTemplate>("cid-1"),
        Choice: new ChoiceName("DoIt"),
        ChoiceArgument: new DamlRecord(null, []));

    private static readonly SubmitterInfo Alice = new Party("alice");

    private static readonly TransactionResult TransactionCreatingOneSampleTemplate = new(
        UpdateId: "update-id",
        CompletionOffset: LedgerOffset.Begin,
        CreatedContracts: [CreatedOf("cid-created")],
        ArchivedContractIds: [],
        CommandId: new CommandId("cmd-id"));

    private static CreatedContract CreatedOf(string contractId) =>
        new(
            EventId: $"evt-{contractId}",
            ContractId: contractId,
            TemplateId: SampleTemplate.TemplateId,
            Payload: DamlRecord.Create(),
            WitnessParties: [new Party("alice")],
            Signatories: [new Party("alice")],
            Observers: []);

    [Fact]
    public async Task TrySubmitSingleAsync_uses_the_supplied_command_id()
    {
        var writer = new CapturingWriter();

        await writer.TrySubmitSingleAsync(
            SampleCommand, Alice, commandId: new CommandId("caller-supplied"),
            cancellationToken: TestContext.Current.CancellationToken);

        writer.LastSubmission!.CommandId.Should().Be(new CommandId("caller-supplied"));
    }

    [Fact]
    public async Task TrySubmitSingleAsync_mints_a_command_id_when_none_is_supplied()
    {
        var writer = new CapturingWriter();

        await writer.TrySubmitSingleAsync(
            SampleCommand, Alice, cancellationToken: TestContext.Current.CancellationToken);

        writer.LastSubmission!.CommandId.Should().NotBeNull(
            "the submission must carry a client-assigned command id rather than leaving command_id unset");
    }

    [Fact]
    public async Task TrySubmitSingleAsync_mints_a_distinct_command_id_per_call()
    {
        var writer = new CapturingWriter();

        await writer.TrySubmitSingleAsync(SampleCommand, Alice, cancellationToken: TestContext.Current.CancellationToken);
        var first = writer.LastSubmission!.CommandId;
        await writer.TrySubmitSingleAsync(SampleCommand, Alice, cancellationToken: TestContext.Current.CancellationToken);

        writer.LastSubmission!.CommandId.Should().NotBe(first);
    }

    [Fact]
    public async Task TrySubmitSingleAsync_treats_an_empty_workflow_id_as_absent()
    {
        var writer = new CapturingWriter();

        await writer.TrySubmitSingleAsync(
            SampleCommand, Alice, workflowId: string.Empty,
            cancellationToken: TestContext.Current.CancellationToken);

        writer.LastSubmission!.WorkflowId.Should().BeNull(
            "workflow_id is a correlation key and an empty one correlates nothing");
    }

    [Fact]
    public async Task TrySubmitSingleAsync_treats_a_null_workflow_id_as_absent()
    {
        var writer = new CapturingWriter();

        await writer.TrySubmitSingleAsync(
            SampleCommand, Alice, workflowId: null,
            cancellationToken: TestContext.Current.CancellationToken);

        writer.LastSubmission!.WorkflowId.Should().BeNull();
    }

    [Fact]
    public async Task TrySubmitSingleAsync_carries_a_non_empty_workflow_id()
    {
        var writer = new CapturingWriter();

        await writer.TrySubmitSingleAsync(
            SampleCommand, Alice, workflowId: "wf-1",
            cancellationToken: TestContext.Current.CancellationToken);

        writer.LastSubmission!.WorkflowId.Should().Be(new WorkflowId("wf-1"));
    }

    [Fact]
    public async Task TrySubmitSingleAsync_forwards_the_timeout()
    {
        var writer = new CapturingWriter();

        await writer.TrySubmitSingleAsync(
            SampleCommand, Alice, timeout: TimeSpan.FromSeconds(3),
            cancellationToken: TestContext.Current.CancellationToken);

        writer.LastTimeout.Should().Be(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task TrySubmitSingleAsync_carries_the_single_command()
    {
        var writer = new CapturingWriter();

        await writer.TrySubmitSingleAsync(
            SampleCommand, Alice, cancellationToken: TestContext.Current.CancellationToken);

        writer.LastSubmission!.Commands.Should().ContainSingle().Which.Should().Be(SampleCommand);
    }

    [Fact]
    public async Task TrySubmitSingleAsync_throws_when_the_writer_is_null()
    {
        ILedgerWriter writer = null!;

        Func<Task> act = () => writer.TrySubmitSingleAsync(
            SampleCommand, Alice, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task TrySubmitSingleAsync_throws_when_the_command_is_null()
    {
        var writer = new CapturingWriter();

        Func<Task> act = () => writer.TrySubmitSingleAsync(
            null!, Alice, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task TrySubmitSingleAsync_forwards_the_cancellation_token()
    {
        var writer = new CapturingWriter();
        using var cts = new CancellationTokenSource();

        await writer.TrySubmitSingleAsync(SampleCommand, Alice, cancellationToken: cts.Token);

        writer.LastCancellationToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task TrySubmitSingleAsync_rejects_a_default_command_id_rather_than_submitting_it()
    {
        var writer = new CapturingWriter();

        Func<Task> act = () => writer.TrySubmitSingleAsync(
            SampleCommand, Alice, commandId: default(CommandId), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
        writer.LastSubmission.Should().BeNull();
    }

    [Fact]
    public async Task TryCreateOneByExerciseAsync_mints_a_command_id_so_a_retry_can_deduplicate()
    {
        var writer = new CapturingWriter();

        await writer.TryCreateOneByExerciseAsync<SampleTemplate>(
            SampleCommand, Alice, cancellationToken: TestContext.Current.CancellationToken);

        writer.LastSubmission!.CommandId.Should().NotBeNull();
    }

    [Fact]
    public async Task TryCreateOneByExerciseAsync_uses_the_supplied_command_id()
    {
        var writer = new CapturingWriter();

        await writer.TryCreateOneByExerciseAsync<SampleTemplate>(
            SampleCommand, Alice, commandId: new CommandId("caller-supplied"),
            cancellationToken: TestContext.Current.CancellationToken);

        writer.LastSubmission!.CommandId.Should().Be(new CommandId("caller-supplied"));
    }

    [Theory]
    [InlineData("TryCreateManyByExerciseAsync", "Party")]
    [InlineData("TryCreateManyByExerciseAsync", "SubmitterInfo")]
    [InlineData("CreateOneByExerciseAsync", "Party")]
    [InlineData("CreateOneByExerciseAsync", "SubmitterInfo")]
    [InlineData("CreateManyByExerciseAsync", "Party")]
    [InlineData("CreateManyByExerciseAsync", "SubmitterInfo")]
    public async Task CreateByExercise_uses_the_supplied_command_id(string method, string submitterShape)
    {
        var writer = new CapturingWriter(
            new ExerciseOutcome<TransactionResult>.One(TransactionCreatingOneSampleTemplate));

        await InvokeCreateByExercise(
            method, submitterShape, writer, new CommandId("caller-supplied"), TestContext.Current.CancellationToken);

        writer.LastSubmission.Should().NotBeNull("the CreateByExercise path must reach TrySubmitAndWaitForTransactionAsync for the command id to be checkable");
        writer.LastSubmission!.CommandId.Should().Be(
            new CommandId("caller-supplied"),
            "an overload that drops the caller's command id mints a fresh one instead, so a retry of a lost-but-accepted submission re-executes rather than deduplicating");
    }

    private static Task InvokeCreateByExercise(
        string method,
        string submitterShape,
        ILedgerWriter writer,
        CommandId commandId,
        CancellationToken cancellationToken)
    {
        var actAs = new Party("alice");
        SubmitterInfo submitter = actAs;

        return (method, submitterShape) switch
        {
            ("TryCreateManyByExerciseAsync", "Party") => writer.TryCreateManyByExerciseAsync<SampleTemplate>(
                SampleCommand, actAs, commandId: commandId, cancellationToken: cancellationToken),
            ("TryCreateManyByExerciseAsync", "SubmitterInfo") => writer.TryCreateManyByExerciseAsync<SampleTemplate>(
                SampleCommand, submitter, commandId: commandId, cancellationToken: cancellationToken),
            ("CreateOneByExerciseAsync", "Party") => writer.CreateOneByExerciseAsync<SampleTemplate>(
                SampleCommand, actAs, commandId: commandId, cancellationToken: cancellationToken),
            ("CreateOneByExerciseAsync", "SubmitterInfo") => writer.CreateOneByExerciseAsync<SampleTemplate>(
                SampleCommand, submitter, commandId: commandId, cancellationToken: cancellationToken),
            ("CreateManyByExerciseAsync", "Party") => writer.CreateManyByExerciseAsync<SampleTemplate>(
                SampleCommand, actAs, commandId: commandId, cancellationToken: cancellationToken),
            ("CreateManyByExerciseAsync", "SubmitterInfo") => writer.CreateManyByExerciseAsync<SampleTemplate>(
                SampleCommand, submitter, commandId: commandId, cancellationToken: cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(method), method + " / " + submitterShape),
        };
    }

    [Fact]
    public async Task ExerciseAsync_forwards_the_command_id_from_an_implicitly_converted_Party_for_a_void_choice()
    {
        var writer = new CapturingWriter();

        await writer.ExerciseAsync(
            SampleCommand, new Party("alice"), commandId: new CommandId("caller-supplied"),
            cancellationToken: TestContext.Current.CancellationToken);

        writer.LastSubmission!.CommandId.Should().Be(new CommandId("caller-supplied"));
    }

    [Fact]
    public async Task ExerciseAsync_mints_a_command_id_for_a_void_choice()
    {
        var writer = new CapturingWriter();

        await writer.ExerciseAsync(
            SampleCommand, Alice, cancellationToken: TestContext.Current.CancellationToken);

        writer.LastSubmission!.CommandId.Should().NotBeNull();
    }

    private sealed class CapturingWriter : ILedgerWriter
    {
        private static readonly TransactionResult SampleTransaction = new(
            "update-id", LedgerOffset.Begin, [], [], new CommandId("cmd-id"));

        private readonly ExerciseOutcome<TransactionResult> _outcome;

        public CapturingWriter()
            : this(new ExerciseOutcome<TransactionResult>.One(SampleTransaction))
        {
        }

        public CapturingWriter(ExerciseOutcome<TransactionResult> outcome) => _outcome = outcome;

        public CommandsSubmission? LastSubmission { get; private set; }

        public TimeSpan? LastTimeout { get; private set; }

        public CancellationToken LastCancellationToken { get; private set; }

        public Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(
            ExerciseCommand command,
            SubmitterInfo submitter,
            string? workflowId = null,
            CommandId? commandId = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<ExerciseOutcome<TResult>>(new ExerciseOutcome<TResult>.One(default(TResult)!));

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
            LastSubmission = submission;
            LastTimeout = timeout;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(_outcome);
        }

        public Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
            TTemplate payload,
            SubmitterInfo submitter,
            string? workflowId = null,
            CommandId? commandId = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
            where TTemplate : ITemplate
            => Task.FromResult<ExerciseOutcome<ContractId<TTemplate>>>(
                new ExerciseOutcome<ContractId<TTemplate>>.None());
    }
}
