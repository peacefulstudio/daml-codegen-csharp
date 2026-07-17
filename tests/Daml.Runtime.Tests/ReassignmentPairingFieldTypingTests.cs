// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class ReassignmentPairingFieldTypingTests
{
    private static readonly SynchronizerId Source = new("global_sync::abc::35-0");
    private static readonly SynchronizerId Target = new("local_sync::def::35-0");
    private static readonly Party Alice = new("alice");

    [Fact]
    public void Unassigned_should_carry_ReassignmentId_and_ReassignmentCounter()
    {
        var ev = new ContractStreamEvent<TestTemplate>.Unassigned(
            new ContractId<TestTemplate>("c1"),
            LedgerOffset.At(5),
            Source,
            Target,
            "reassignment-42",
            9L,
            [Alice]);

        ev.ReassignmentId.Should().Be("reassignment-42");
        ev.ReassignmentCounter.Should().Be(9L);
    }

    [Fact]
    public void Assigned_should_carry_ReassignmentId_and_ReassignmentCounter()
    {
        var ev = new ContractStreamEvent<TestTemplate>.Assigned(
            new ContractId<TestTemplate>("c1"),
            DamlRecord.Create(),
            LedgerOffset.At(6),
            Source,
            Target,
            "reassignment-42",
            9L,
            [Alice]);

        ev.ReassignmentId.Should().Be("reassignment-42");
        ev.ReassignmentCounter.Should().Be(9L);
    }

    [Theory]
    [InlineData(typeof(ContractStreamEvent<TestTemplate>.Assigned))]
    [InlineData(typeof(ContractStreamEvent<TestTemplate>.Unassigned))]
    public void Reassignment_fields_are_string_and_long_positioned_between_Target_and_WitnessParties(Type variant)
    {
        var parameters = variant.GetConstructors()
            .OrderByDescending(constructor => constructor.GetParameters().Length)
            .First()
            .GetParameters()
            .ToDictionary(parameter => parameter.Name!);

        parameters["ReassignmentId"].ParameterType.Should().Be<string>();
        parameters["ReassignmentCounter"].ParameterType.Should().Be<long>();

        parameters["ReassignmentId"].Position.Should().Be(parameters["Target"].Position + 1);
        parameters["ReassignmentCounter"].Position.Should().Be(parameters["ReassignmentId"].Position + 1);
        parameters["WitnessParties"].Position.Should().Be(parameters["ReassignmentCounter"].Position + 1);
    }

    private sealed record TestTemplate(string Owner) : ITemplate
    {
        public static Identifier TemplateId { get; } = new("pkg", "M", "TestTemplate");
        public static string PackageId => "pkg";
        public static string PackageName => "test";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create();
    }
}
