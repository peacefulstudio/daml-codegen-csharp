// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.Richtypes;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

public class HoldingInterfaceChoiceTests
{
    private static readonly ContractId<IHolding> Target = new("holding-cid");

    private static TransactionResult EmptyTransaction() =>
        new(
            UpdateId: "upd-1",
            CompletionOffset: LedgerOffset.At(1),
            CreatedContracts: Array.Empty<CreatedContract>(),
            ArchivedContractIds: Array.Empty<string>(),
            CommandId: default);

    [Fact]
    public void Describe_argument_round_trips_through_its_record()
    {
        var argument = new Describe("balance: ");

        var restored = Describe.FromRecord(argument.ToRecord());

        restored.Should().Be(argument);
    }

    [Fact]
    public void DescribeCommand_carries_the_interface_id_and_the_choice_argument()
    {
        var command = Target.DescribeCommand(new Describe("balance: "));

        command.TemplateId.Should().Be(IHolding.InterfaceId);
        command.Choice.Should().Be(new ChoiceName("Describe"));
        command.ContractId.Value.Should().Be("holding-cid");
        command.ChoiceArgument.As<DamlRecord>()
            .GetRequiredField("prefix").As<DamlText>().Value.Should().Be("balance: ");
    }

    [Fact]
    public void ReissueCommand_carries_the_interface_id_and_the_choice_argument()
    {
        var command = Target.ReissueCommand(new Reissue(12.5m));

        command.TemplateId.Should().Be(IHolding.InterfaceId);
        command.Choice.Should().Be(new ChoiceName("Reissue"));
        command.ChoiceArgument.As<DamlRecord>()
            .GetRequiredField("newAmount").As<DamlNumeric>().Value.Should().Be(12.5m);
    }

    [Fact]
    public async Task DescribeAsync_submits_the_interface_typed_exercise_command()
    {
        using var client = new FakeLedgerClient(_ => new ExerciseOutcome<TransactionResult>.One(EmptyTransaction()));

        var outcome = await Target.DescribeAsync(client, new Describe("balance: "), new Party("alice"),
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.One>();
        client.LastSubmission!.CommandId.Should().NotBeNull(
            "a generated interface exerciser must route through the shared submission helper, which assigns a command id");
        var command = client.LastSubmission!.Commands.Should().ContainSingle().Which
            .Should().BeOfType<ExerciseCommand>().Subject;
        command.TemplateId.Should().Be(IHolding.InterfaceId);
        command.Choice.Should().Be(new ChoiceName("Describe"));
    }

    [Fact]
    public void HoldingView_round_trips_through_its_record()
    {
        var view = new HoldingView(42m);

        HoldingView.FromRecord(view.ToRecord()).Should().Be(view);
    }
}
