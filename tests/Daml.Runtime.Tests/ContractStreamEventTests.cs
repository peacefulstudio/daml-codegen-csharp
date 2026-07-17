// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class ContractStreamEventTests
{
    [Fact]
    public void Variants_should_be_distinguishable_via_pattern_match()
    {
        ContractStreamEvent<TestTemplate>[] events =
        [
            new ContractStreamEvent<TestTemplate>.Created(new ContractId<TestTemplate>("c1"), DamlRecord.Create(), LedgerOffset.At(1), new SynchronizerId("sync"), [new Party("alice")]),
            new ContractStreamEvent<TestTemplate>.Archived(new ContractId<TestTemplate>("c1"), LedgerOffset.At(2), new SynchronizerId("sync"), [new Party("alice")]),
            new ContractStreamEvent<TestTemplate>.Exercised(new ContractId<TestTemplate>("c1"), "Accept", DamlUnit.Instance, DamlUnit.Instance, true, LedgerOffset.At(3), new SynchronizerId("sync"), [new Party("alice")]),
            new ContractStreamEvent<TestTemplate>.Assigned(new ContractId<TestTemplate>("c1"), DamlRecord.Create(), LedgerOffset.At(4), new SynchronizerId("src"), new SynchronizerId("tgt"), "reassignment-1", 7L, [new Party("alice")]),
            new ContractStreamEvent<TestTemplate>.Unassigned(new ContractId<TestTemplate>("c1"), LedgerOffset.At(5), new SynchronizerId("src"), new SynchronizerId("tgt"), "reassignment-1", 7L, [new Party("alice")]),
            new ContractStreamEvent<TestTemplate>.Checkpoint(LedgerOffset.At(6)),
            new ContractStreamEvent<TestTemplate>.StreamError(14, "unavailable"),
            new ContractStreamEvent<TestTemplate>.Unclassified(LedgerOffset.At(7), "TopologyEvent"),
        ];

        var seen = events.Select(e => e switch
        {
            ContractStreamEvent<TestTemplate>.Created => "created",
            ContractStreamEvent<TestTemplate>.Archived => "archived",
            ContractStreamEvent<TestTemplate>.Exercised => "exercised",
            ContractStreamEvent<TestTemplate>.Assigned => "assigned",
            ContractStreamEvent<TestTemplate>.Unassigned => "unassigned",
            ContractStreamEvent<TestTemplate>.Checkpoint => "checkpoint",
            ContractStreamEvent<TestTemplate>.StreamError => "error",
            ContractStreamEvent<TestTemplate>.Unclassified => "unclassified",
            _ => "other",
        }).ToList();

        seen.Should().Equal("created", "archived", "exercised", "assigned", "unassigned", "checkpoint", "error", "unclassified");
    }

    [Fact]
    public void Variants_with_same_payload_should_be_value_equal()
    {
        var a = new ContractStreamEvent<TestTemplate>.Checkpoint(LedgerOffset.At(42));
        var b = new ContractStreamEvent<TestTemplate>.Checkpoint(LedgerOffset.At(42));
        a.Should().Be(b);
    }

    [Fact]
    public void Unclassified_with_same_payload_should_be_value_equal()
    {
        var a = new ContractStreamEvent<TestTemplate>.Unclassified(LedgerOffset.At(7), "TopologyEvent");
        var b = new ContractStreamEvent<TestTemplate>.Unclassified(LedgerOffset.At(7), "TopologyEvent");
        a.Should().Be(b);
    }

    [Fact]
    public void StreamError_StatusCode_is_int_so_no_transport_dep_leaks()
    {
        // Held as int so this type doesn't require any consumer to take a
        // dep on Grpc.Core (or any other transport library) to switch on it.
        var err = new ContractStreamEvent<TestTemplate>.StreamError(14, "transient");
        err.StatusCode.Should().BeOfType(typeof(int));
        err.StatusCode.Should().Be(14);
    }

    [Fact]
    public void Unclassified_should_expose_offset_and_kind()
    {
        var unclassified = new ContractStreamEvent<TestTemplate>.Unclassified(LedgerOffset.At(7), "TopologyEvent");

        unclassified.Offset.Should().Be(LedgerOffset.At(7));
        unclassified.Kind.Should().Be("TopologyEvent");
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
