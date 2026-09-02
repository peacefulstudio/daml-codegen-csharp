// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Threading.Tasks;
using AwesomeAssertions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Xunit;

namespace Daml.Ledger.Abstractions.Testing.Conformance.Tests;

public sealed class CommandIdConformanceTests
{
    private static readonly SubmitterInfo Submitter = new(new Party("alice"));

    private const string VerbatimContractFragment =
        "the caller-supplied commandId must reach the participant unchanged";

    private const string MintContractFragment =
        "an omitted commandId obliges the implementation to mint one";

    [Fact]
    public async Task TryExerciseAsync_verbatim_check_fails_against_a_client_that_mints_over_the_supplied_id()
    {
        var kit = new CommandIdFixtureKit(new SuppliedCommandIdIgnoringFakeClient());

        var run = await Record.ExceptionAsync(
            () => kit.TryExerciseAsync_dispatches_the_caller_supplied_commandId_verbatim());

        ShouldBeTheContractFailure(run, VerbatimContractFragment, SuppliedCommandIdIgnoringFakeClient.MintedPrefix);
    }

    [Fact]
    public async Task TryCreateAsync_verbatim_check_fails_against_a_client_that_mints_over_the_supplied_id()
    {
        var kit = new CommandIdFixtureKit(new SuppliedCommandIdIgnoringFakeClient());

        var run = await Record.ExceptionAsync(
            () => kit.TryCreateAsync_dispatches_the_caller_supplied_commandId_verbatim());

        ShouldBeTheContractFailure(run, VerbatimContractFragment, SuppliedCommandIdIgnoringFakeClient.MintedPrefix);
    }

    [Fact]
    public async Task TryExerciseAsync_mint_check_fails_against_a_client_that_leaves_the_command_id_unset()
    {
        var kit = new CommandIdFixtureKit(new UnsetCommandIdFakeClient());

        var run = await Record.ExceptionAsync(
            () => kit.TryExerciseAsync_mints_a_command_id_when_the_caller_omits_one());

        ShouldBeTheContractFailure(run, MintContractFragment, "<null>");
    }

    [Fact]
    public async Task TryCreateAsync_mint_check_fails_against_a_client_that_leaves_the_command_id_unset()
    {
        var kit = new CommandIdFixtureKit(new UnsetCommandIdFakeClient());

        var run = await Record.ExceptionAsync(
            () => kit.TryCreateAsync_mints_a_command_id_when_the_caller_omits_one());

        ShouldBeTheContractFailure(run, MintContractFragment, "<null>");
    }

    [Fact]
    public async Task TryExerciseAsync_mint_check_passes_against_a_client_that_mints_over_the_supplied_id()
    {
        var kit = new CommandIdFixtureKit(new SuppliedCommandIdIgnoringFakeClient());

        var run = await Record.ExceptionAsync(
            () => kit.TryExerciseAsync_mints_a_command_id_when_the_caller_omits_one());

        run.Should().BeNull(
            "a client that always mints breaks only the verbatim direction; a mint check that also " +
            "rejected it would be failing every implementation rather than isolating one fault");
    }

    [Fact]
    public async Task TryExerciseAsync_verbatim_check_passes_against_a_client_that_leaves_the_command_id_unset()
    {
        var kit = new CommandIdFixtureKit(new UnsetCommandIdFakeClient());

        var run = await Record.ExceptionAsync(
            () => kit.TryExerciseAsync_dispatches_the_caller_supplied_commandId_verbatim());

        run.Should().BeNull(
            "a client that never mints breaks only the omission direction; a verbatim check that also " +
            "rejected it would be failing every implementation rather than isolating one fault");
    }

    [Fact]
    public async Task TryCreateAsync_mint_check_passes_against_a_client_that_mints_over_the_supplied_id()
    {
        var kit = new CommandIdFixtureKit(new SuppliedCommandIdIgnoringFakeClient());

        var run = await Record.ExceptionAsync(
            () => kit.TryCreateAsync_mints_a_command_id_when_the_caller_omits_one());

        run.Should().BeNull(
            "a client that always mints breaks only the verbatim direction; a mint check that also " +
            "rejected it would be failing every implementation rather than isolating one fault");
    }

    [Fact]
    public async Task TryCreateAsync_verbatim_check_passes_against_a_client_that_leaves_the_command_id_unset()
    {
        var kit = new CommandIdFixtureKit(new UnsetCommandIdFakeClient());

        var run = await Record.ExceptionAsync(
            () => kit.TryCreateAsync_dispatches_the_caller_supplied_commandId_verbatim());

        run.Should().BeNull(
            "a client that never mints breaks only the omission direction; a verbatim check that also " +
            "rejected it would be failing every implementation rather than isolating one fault");
    }

    [Fact]
    public async Task TryExerciseAsync_verbatim_check_passes_against_a_client_that_honors_the_contract()
    {
        var kit = new CommandIdFixtureKit(new CommandIdHonoringFakeClient());

        var run = await Record.ExceptionAsync(
            () => kit.TryExerciseAsync_dispatches_the_caller_supplied_commandId_verbatim());

        run.Should().BeNull();
    }

    [Fact]
    public async Task TryExerciseAsync_mint_check_passes_against_a_client_that_honors_the_contract()
    {
        var kit = new CommandIdFixtureKit(new CommandIdHonoringFakeClient());

        var run = await Record.ExceptionAsync(
            () => kit.TryExerciseAsync_mints_a_command_id_when_the_caller_omits_one());

        run.Should().BeNull();
    }

    [Fact]
    public async Task TryCreateAsync_verbatim_check_passes_against_a_client_that_honors_the_contract()
    {
        var kit = new CommandIdFixtureKit(new CommandIdHonoringFakeClient());

        var run = await Record.ExceptionAsync(
            () => kit.TryCreateAsync_dispatches_the_caller_supplied_commandId_verbatim());

        run.Should().BeNull();
    }

    [Fact]
    public async Task TryCreateAsync_mint_check_passes_against_a_client_that_honors_the_contract()
    {
        var kit = new CommandIdFixtureKit(new CommandIdHonoringFakeClient());

        var run = await Record.ExceptionAsync(
            () => kit.TryCreateAsync_mints_a_command_id_when_the_caller_omits_one());

        run.Should().BeNull();
    }

    private static void ShouldBeTheContractFailure(
        System.Exception? run, string contractFragment, string offendingValueFragment)
    {
        run.Should().NotBeNull("the buggy client must fail this check");
        run!.Message.Should().Contain(
            contractFragment,
            "the failure must come from the command-id assertion itself, not from an unrelated throw " +
            "or from the opt-out skip that a null fixture would raise");
        run.Message.Should().Contain(
            offendingValueFragment,
            "the failure message must quote the command id the client actually recorded, so an " +
            "implementer can see what their client did instead of only that something went wrong");
    }

    private sealed class CommandIdFixtureKit(CommandIdFakeClientBase fake)
        : LedgerClientConformanceTests<ConformanceProbe>
    {
        protected override ILedgerClient CreateClient() =>
            throw new System.NotSupportedException("this kit only drives the command-id checks");

        protected override SubmitterInfo Reader { get; } = Submitter;

        protected override CommandIdConformanceFixture CreateCommandIdFixture() => new(
            fake,
            (writer, commandId) => writer.TryExerciseAsync<DamlUnit>(
                ExerciseCommand.For(
                    new ContractId<ConformanceProbe>("c1"), new ChoiceName("Archive"), DamlRecord.Create()),
                Submitter,
                commandId: commandId),
            (writer, commandId) => writer.TryCreateAsync(
                new ConformanceProbe("alice"), Submitter, commandId: commandId),
            () => ValueTask.FromResult<string?>(fake.RecordedCommandId));
    }
}
