// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using FluentAssertions;
using Richtypes;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

public class RelabelAsyncTests
{
    private static readonly ContractId<RichRecord> Target = new("rich-cid");
    private static readonly RichRecord.Relabel Argument = new("renamed");

    private static TransactionResult TransactionCreating(string newRichRecordId) =>
        new(
            UpdateId: "upd-1",
            CompletionOffset: 1,
            CreatedContracts: new[] { new CreatedContract(newRichRecordId, RichRecord.TemplateId, "{}") },
            ArchivedContractIds: Array.Empty<string>());

    [Fact]
    public async Task projects_the_created_rich_record_on_success()
    {
        using var client = new FakeLedgerClient(
            _ => new ExerciseOutcome<TransactionResult>.One(TransactionCreating("new-rich-cid")));

        var outcome = await Target.RelabelAsync(client, Argument, new Party("alice"),
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<RelabelResult>.One>();
        ((ExerciseOutcome<RelabelResult>.One)outcome).Result.RichRecord.Value.Should().Be("new-rich-cid");
    }

    [Fact]
    public async Task builds_an_exercise_command_for_the_relabel_choice_on_the_target()
    {
        using var client = new FakeLedgerClient(
            _ => new ExerciseOutcome<TransactionResult>.One(TransactionCreating("new-rich-cid")));

        await Target.RelabelAsync(client, Argument, new Party("alice"), workflowId: "wf-7",
            cancellationToken: TestContext.Current.CancellationToken);

        client.LastSubmission.Should().NotBeNull();
        client.LastSubmission!.WorkflowId.Should().Be("wf-7");
        var command = client.LastSubmission.Commands.Should().ContainSingle().Which
            .Should().BeOfType<ExerciseCommand>().Subject;
        command.ContractId.Should().Be("rich-cid");
        command.Choice.Should().Be("Relabel");
    }

    [Fact]
    public async Task maps_a_daml_error_outcome_through_to_the_typed_result()
    {
        using var client = new FakeLedgerClient(
            _ => new ExerciseOutcome<TransactionResult>.DamlError(
                DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
                "CONTRACT_NOT_FOUND",
                "gone",
                new Dictionary<string, string>()));

        var outcome = await Target.RelabelAsync(client, Argument, new Party("alice"),
            cancellationToken: TestContext.Current.CancellationToken);

        var error = outcome.Should().BeOfType<ExerciseOutcome<RelabelResult>.DamlError>().Subject;
        error.ErrorId.Should().Be("CONTRACT_NOT_FOUND");
        error.Message.Should().Be("gone");
    }

    [Fact]
    public async Task maps_an_infra_error_outcome_through_to_the_typed_result()
    {
        using var client = new FakeLedgerClient(
            _ => new ExerciseOutcome<TransactionResult>.InfraError(14, "unavailable"));

        var outcome = await Target.RelabelAsync(client, Argument, new Party("alice"),
            cancellationToken: TestContext.Current.CancellationToken);

        var error = outcome.Should().BeOfType<ExerciseOutcome<RelabelResult>.InfraError>().Subject;
        error.StatusCode.Should().Be(14);
        error.Message.Should().Be("unavailable");
    }

    [Fact]
    public async Task throws_on_null_contract_id()
    {
        using var client = new FakeLedgerClient();

        var act = async () => await ((ContractId<RichRecord>)null!).RelabelAsync(client, Argument, new Party("alice"),
            cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task throws_on_null_argument()
    {
        using var client = new FakeLedgerClient();

        var act = async () => await Target.RelabelAsync(client, null!, new Party("alice"),
            cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
