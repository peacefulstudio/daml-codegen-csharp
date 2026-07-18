// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using Daml.Runtime.Data;

namespace Daml.Ledger.Abstractions.Testing.Conformance.Tests;

public sealed class FakeConformanceTests : LedgerClientConformanceTests<ConformanceProbe>
{
    protected override ILedgerClient CreateClient() => new ConformingFakeClient();

    protected override ILedgerClient CreateFaultingSnapshotClient() =>
        new ConformingFakeClient(faultsMidSnapshot: true);

    protected override SubmitterInfo Reader { get; } = new Party("alice");
}
