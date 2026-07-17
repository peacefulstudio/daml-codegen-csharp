// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Xunit;

namespace Daml.Runtime.Tests;

public sealed class AcsSnapshotEntryTests
{
    [Fact]
    public void Checkpoint_carries_the_terminal_offset()
    {
        var entry = new AcsSnapshotEntry<TestTemplate>.Checkpoint(LedgerOffset.At(9));
        entry.Offset.Should().Be(LedgerOffset.At(9));
    }

    [Fact]
    public void Variants_are_distinguishable_via_pattern_match()
    {
        AcsSnapshotEntry<TestTemplate> entry =
            new AcsSnapshotEntry<TestTemplate>.Unclassified(LedgerOffset.At(3), "ACTIVE_CONTRACT");

        var matched = entry switch
        {
            AcsSnapshotEntry<TestTemplate>.Created => "created",
            AcsSnapshotEntry<TestTemplate>.Unclassified => "unclassified",
            AcsSnapshotEntry<TestTemplate>.Checkpoint => "checkpoint",
            _ => "other",
        };

        matched.Should().Be("unclassified");
    }

    [Fact]
    public void Created_carries_its_contract_and_fields()
    {
        var contractId = new ContractId<TestTemplate>("c1");
        var payload = DamlRecord.Create();
        var offset = LedgerOffset.At(4);
        var synchronizerId = new SynchronizerId("sync");
        IReadOnlyList<Party> witnessParties = [new Party("alice")];

        var entry = new AcsSnapshotEntry<TestTemplate>.Created(contractId, payload, offset, synchronizerId, witnessParties);

        entry.ContractId.Should().Be(contractId);
        entry.Offset.Should().Be(offset);
        entry.Payload.Should().Be(payload);
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
