// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Daml.Ledger.Abstractions.Testing.Conformance.Tests;

public sealed class FakeConformanceTests : LedgerClientConformanceTests<ConformanceProbe>
{
    private static readonly Party Authorized = new("alice");
    private static readonly Party Unauthorized = new("mallory");

    protected override ILedgerClient CreateClient() => new ConformingFakeClient();

    protected override ILedgerClient CreateFaultingSnapshotClient() =>
        new ConformingFakeClient(faultsMidSnapshot: true);

    protected override SubmitterInfo Reader { get; } = Authorized;

    protected override WriteConformanceFixture CreateWriteFixture() => new(
        new AuthorizingFakeClient(Authorized),
        new CommandsSubmission([ExerciseCommand.For(
            new ContractId<ConformanceProbe>("c1"), new ChoiceName("Archive"), DamlRecord.Create())]),
        Authorized,
        Unauthorized);
}
