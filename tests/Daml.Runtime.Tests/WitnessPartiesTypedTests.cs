// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using FluentAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class WitnessPartiesTypedTests
{
    private static readonly Party Alice = new("alice");
    private static readonly Party Bob = new("bob");

    [Fact]
    public void Created_WitnessParties_should_be_Party_list()
    {
        var ev = new ContractStreamEvent<TestTemplate>.Created(
            new ContractId<TestTemplate>("c1"),
            DamlRecord.Create(),
            1L,
            [Alice, Bob]);

        ev.WitnessParties.Should().BeAssignableTo<IReadOnlyList<Party>>();
        ev.WitnessParties.Should().Equal(Alice, Bob);
    }

    [Fact]
    public void Archived_WitnessParties_should_be_Party_list()
    {
        var ev = new ContractStreamEvent<TestTemplate>.Archived(
            new ContractId<TestTemplate>("c1"),
            2L,
            [Alice]);

        ev.WitnessParties.Should().BeAssignableTo<IReadOnlyList<Party>>();
        ev.WitnessParties.Should().ContainSingle().Which.Should().Be(Alice);
    }

    [Fact]
    public void Exercised_WitnessParties_should_be_Party_list()
    {
        var ev = new ContractStreamEvent<TestTemplate>.Exercised(
            new ContractId<TestTemplate>("c1"),
            "Accept",
            DamlUnit.Instance,
            DamlUnit.Instance,
            Consuming: true,
            Offset: 3L,
            WitnessParties: [Alice, Bob]);

        ev.WitnessParties.Should().BeAssignableTo<IReadOnlyList<Party>>();
        ev.WitnessParties.Should().Equal(Alice, Bob);
    }

    [Fact]
    public void Assigned_WitnessParties_should_be_Party_list()
    {
        var ev = new ContractStreamEvent<TestTemplate>.Assigned(
            new ContractId<TestTemplate>("c1"),
            DamlRecord.Create(),
            4L,
            new SynchronizerId("src"),
            new SynchronizerId("tgt"),
            [Alice]);

        ev.WitnessParties.Should().BeAssignableTo<IReadOnlyList<Party>>();
        ev.WitnessParties.Should().ContainSingle().Which.Should().Be(Alice);
    }

    [Fact]
    public void Unassigned_WitnessParties_should_be_Party_list()
    {
        var ev = new ContractStreamEvent<TestTemplate>.Unassigned(
            new ContractId<TestTemplate>("c1"),
            5L,
            new SynchronizerId("src"),
            new SynchronizerId("tgt"),
            [Alice]);

        ev.WitnessParties.Should().BeAssignableTo<IReadOnlyList<Party>>();
        ev.WitnessParties.Should().ContainSingle().Which.Should().Be(Alice);
    }

    private sealed record TestTemplate(string Owner) : ITemplate
    {
        public static Identifier TemplateId { get; } = new("pkg", "M", "TestTemplate");
        public static string PackageId => "pkg";
        public static string PackageName => "test";
        public static Version PackageVersion { get; } = new(0, 1, 0);

        public DamlRecord ToRecord() => DamlRecord.Create();
    }
}
