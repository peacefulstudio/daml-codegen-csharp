// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using FluentAssertions;
using Richtypes;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

public class SubmissionExtensionsTests
{
    [Fact]
    public async Task marker_create_async_projects_a_created_contract_id()
    {
        using var client = new FakeLedgerClient(
            create: _ => new ExerciseOutcome<object>.One("marker-cid"));
        var payload = new Marker(new Party("alice"));

        var outcome = await client.CreateAsync(payload, new Party("alice"), TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ContractId<Marker>>.One>();
        ((ExerciseOutcome<ContractId<Marker>>.One)outcome).Result.Value.Should().Be("marker-cid");
    }

    [Fact]
    public async Task marker_create_async_throws_on_null_payload()
    {
        using var client = new FakeLedgerClient();

        var act = async () => await client.CreateAsync((Marker)null!, new Party("alice"));

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task rich_record_create_async_projects_a_created_contract_id()
    {
        using var client = new FakeLedgerClient(
            create: _ => new ExerciseOutcome<object>.One("rich-cid"));
        var payload = new RichRecord(
            Owner: new Party("alice"),
            Count: 1,
            Amount: 1m,
            Label: "l",
            Active: false,
            AsOf: new DateOnly(2026, 1, 1),
            ObservedAt: DateTimeOffset.UnixEpoch,
            Note: null,
            Tags: new List<string>(),
            Attributes: new Dictionary<string, string>(),
            Marker: new ContractId<Marker>("m"),
            Profile: new Profile("n", 0));

        var outcome = await client.CreateAsync(payload, new Party("alice"), TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ContractId<RichRecord>>.One>();
        ((ExerciseOutcome<ContractId<RichRecord>>.One)outcome).Result.Value.Should().Be("rich-cid");
    }
}
