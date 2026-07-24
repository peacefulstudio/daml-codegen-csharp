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

public sealed class SubmitterAuthorityConformanceTests
{
    private static readonly Party Authorized = new("alice");
    private static readonly Party Unauthorized = new("mallory");

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_check_fails_against_a_client_that_dispatches_pre_set_ActAs()
    {
        var kit = new WriteFixtureKit(new ActAsIgnoringSubmitterFakeClient(Authorized));

        var run = await Record.ExceptionAsync(
            () => kit.TrySubmitAndWaitForTransactionAsync_submitter_parameter_overrides_pre_set_ActAs());

        run.Should().NotBeNull(
            "a client that dispatches the pre-set ActAs instead of the submitter parameter must fail this check");
    }

    [Fact]
    public async Task SubmitAndWaitAsync_check_fails_against_a_client_that_dispatches_pre_set_ActAs()
    {
        var kit = new WriteFixtureKit(new ActAsIgnoringSubmitterFakeClient(Authorized));

        var run = await Record.ExceptionAsync(
            () => kit.SubmitAndWaitAsync_submitter_parameter_overrides_pre_set_ActAs());

        run.Should().NotBeNull(
            "a client that dispatches the pre-set ActAs instead of the submitter parameter must fail this check");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_merge_check_fails_against_a_client_that_unions_ActAs_with_the_submitter()
    {
        var kit = new WriteFixtureKit(new SubmitterMergingFakeClient(Authorized));

        var run = await Record.ExceptionAsync(
            () => kit.TrySubmitAndWaitForTransactionAsync_submitter_parameter_is_not_merged_with_pre_set_ActAs());

        run.Should().NotBeNull(
            "a client that unions the pre-set ActAs with the submitter instead of overwriting it must fail this check");
    }

    [Fact]
    public async Task SubmitAndWaitAsync_merge_check_fails_against_a_client_that_unions_ActAs_with_the_submitter()
    {
        var kit = new WriteFixtureKit(new SubmitterMergingFakeClient(Authorized));

        var run = await Record.ExceptionAsync(
            () => kit.SubmitAndWaitAsync_submitter_parameter_is_not_merged_with_pre_set_ActAs());

        run.Should().NotBeNull(
            "a client that unions the pre-set ActAs with the submitter instead of overwriting it must fail this check");
    }

    private sealed class WriteFixtureKit(ILedgerClient writeClient) : LedgerClientConformanceTests<ConformanceProbe>
    {
        protected override ILedgerClient CreateClient() =>
            throw new System.NotSupportedException("this kit only drives the write-path checks");

        protected override SubmitterInfo Reader { get; } = Authorized;

        protected override WriteConformanceFixture CreateWriteFixture() => new(
            writeClient,
            new CommandsSubmission([ExerciseCommand.For(
                new ContractId<ConformanceProbe>("c1"), new ChoiceName("Archive"), DamlRecord.Create())]),
            Authorized,
            Unauthorized);
    }
}
