// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class ContractStreamEventTests
{
    private const string LedgerKeyHash = "6CgQL9eNNqIjS5cB6/kK1IsqdxjcgXl/3kxSiUEkiBA=";

    [Fact]
    public void Created_should_carry_the_key_hash_the_transport_read_off_the_wire()
    {
        var created = new ContractStreamEvent<TestTemplate>.Created(
            new ContractId<TestTemplate>("c1"),
            new TestTemplate("alice"),
            new ContractKey(new DamlText("savings"), TestTemplate.TemplateId) { KeyHash = LedgerKeyHash },
            LedgerOffset.At(1),
            new SynchronizerId("sync"),
            [new Party("alice")]);

        created.Key!.KeyHash.Should().Be(
            LedgerKeyHash,
            "the live stream is the fourth read shape a key travels, so the hash has to survive it "
            + "as well as the snapshot shapes");
    }

    [Fact]
    public void Created_should_leave_the_key_hash_null_when_the_event_carried_none()
    {
        var created = new ContractStreamEvent<TestTemplate>.Created(
            new ContractId<TestTemplate>("c1"),
            new TestTemplate("alice"),
            new ContractKey(new DamlText("savings"), TestTemplate.TemplateId),
            LedgerOffset.At(1),
            new SynchronizerId("sync"),
            [new Party("alice")]);

        created.Key!.KeyHash.Should().BeNull();
    }

    [Fact]
    public void Assigned_should_carry_the_key_of_the_contract_it_reassigns()
    {
        var key = new ContractKey(new DamlText("savings"), TestTemplate.TemplateId) { KeyHash = LedgerKeyHash };

        var assigned = new ContractStreamEvent<TestTemplate>.Assigned(
            new ContractId<TestTemplate>("c1"),
            new TestTemplate("alice"),
            key,
            LedgerOffset.At(4),
            new SynchronizerId("src"),
            new SynchronizerId("tgt"),
            "reassignment-1",
            7L,
            [new Party("alice")]);

        assigned.Key.Should().Be(
            key,
            "an assignment re-emits the whole created contract, so a consumer rebuilding state from "
            + "one stream would lose the key at every reassignment if this variant had nowhere to put it");
        assigned.Key!.KeyHash.Should().Be(LedgerKeyHash);
    }

    [Fact]
    public void Variants_should_be_distinguishable_via_pattern_match()
    {
        ContractStreamEvent<TestTemplate>[] events =
        [
            new ContractStreamEvent<TestTemplate>.Created(new ContractId<TestTemplate>("c1"), new TestTemplate("alice"), null, LedgerOffset.At(1), new SynchronizerId("sync"), [new Party("alice")]),
            new ContractStreamEvent<TestTemplate>.Archived(new ContractId<TestTemplate>("c1"), LedgerOffset.At(2), new SynchronizerId("sync"), [new Party("alice")]),
            new ContractStreamEvent<TestTemplate>.Exercised(new ContractId<TestTemplate>("c1"), "Accept", DamlUnit.Instance, DamlUnit.Instance, true, LedgerOffset.At(3), new SynchronizerId("sync"), [new Party("alice")]),
            new ContractStreamEvent<TestTemplate>.Assigned(new ContractId<TestTemplate>("c1"), new TestTemplate("alice"), null, LedgerOffset.At(4), new SynchronizerId("src"), new SynchronizerId("tgt"), "reassignment-1", 7L, [new Party("alice")]),
            new ContractStreamEvent<TestTemplate>.Unassigned(new ContractId<TestTemplate>("c1"), LedgerOffset.At(5), new SynchronizerId("src"), new SynchronizerId("tgt"), "reassignment-1", 7L, [new Party("alice")]),
            new ContractStreamEvent<TestTemplate>.Checkpoint(LedgerOffset.At(6)),
            new ContractStreamEvent<TestTemplate>.StreamError(14, "unavailable"),
            new ContractStreamEvent<TestTemplate>.Unclassified(LedgerOffset.At(7), UnclassifiedKind.Unknown, "TopologyEvent"),
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
        var a = new ContractStreamEvent<TestTemplate>.Unclassified(LedgerOffset.At(7), UnclassifiedKind.Unknown, "TopologyEvent");
        var b = new ContractStreamEvent<TestTemplate>.Unclassified(LedgerOffset.At(7), UnclassifiedKind.Unknown, "TopologyEvent");
        a.Should().Be(b);
    }

    [Fact]
    public void StreamError_StatusCode_is_int_so_no_transport_dep_leaks()
    {
        var err = new ContractStreamEvent<TestTemplate>.StreamError(14, "transient");

        err.StatusCode.Should().BeOfType(
            typeof(int),
            "holding the status code as an int is what spares every consumer a dependency on " +
            "Grpc.Core, or any other transport library, merely to switch on it");
        err.StatusCode.Should().Be(14);
    }

    [Fact]
    public void StreamError_carries_the_classification_the_transport_determined()
    {
        var err = new ContractStreamEvent<TestTemplate>.StreamError(
            14, "transient", DamlErrorCategory.TransientServerFailure);

        err.Category.Should().Be(DamlErrorCategory.TransientServerFailure);
    }

    [Fact]
    public void StreamError_leaves_the_classification_null_when_the_transport_determined_none()
    {
        var err = new ContractStreamEvent<TestTemplate>.StreamError(14, "transient");

        err.Category.Should().BeNull();
    }

    [Fact]
    public void Unclassified_should_expose_offset_and_enumerated_kind()
    {
        var unclassified = new ContractStreamEvent<TestTemplate>.Unclassified(LedgerOffset.At(7), UnclassifiedKind.DecodeFailure);

        unclassified.Offset.Should().Be(
            LedgerOffset.At(7),
            "a projector that does know where the unclassifiable event sat must still report it, so the nullable slot has to round-trip a real offset unchanged");
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        unclassified.RawKind.Should().BeNull();
    }

    [Fact]
    public void Unclassified_unknown_kind_preserves_the_raw_descriptor()
    {
        var unclassified = new ContractStreamEvent<TestTemplate>.Unclassified(LedgerOffset.At(7), UnclassifiedKind.Unknown, "TopologyEvent");

        unclassified.Kind.Should().Be(UnclassifiedKind.Unknown);
        unclassified.RawKind.Should().Be("TopologyEvent");
    }

    [Fact]
    public void Unclassified_rejects_a_raw_descriptor_on_an_enumerated_kind()
    {
        var act = () => new ContractStreamEvent<TestTemplate>.Unclassified(LedgerOffset.At(7), UnclassifiedKind.DecodeFailure, "EventCase_1");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Unclassified_rejects_an_unknown_kind_without_a_raw_descriptor()
    {
        var act = () => new ContractStreamEvent<TestTemplate>.Unclassified(LedgerOffset.At(7), UnclassifiedKind.Unknown, null);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Unclassified_accepts_a_null_offset_and_preserves_it()
    {
        var unclassified = new ContractStreamEvent<TestTemplate>.Unclassified(null, UnclassifiedKind.DecodeFailure);

        unclassified.Offset.Should().BeNull(
            "an event the projector could not place on the ledger has no offset to report, and the only alternative is fabricating LedgerOffset.Begin, which is indistinguishable from a genuine ledger start: a consumer persisting it as resume state checkpoints at the beginning of the ledger and re-reads the entire stream");
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        unclassified.RawKind.Should().BeNull();
    }

    [Fact]
    public void Unclassified_without_an_offset_still_rejects_a_raw_descriptor_on_an_enumerated_kind()
    {
        var act = () => new ContractStreamEvent<TestTemplate>.Unclassified(null, UnclassifiedKind.DecodeFailure, "EventCase_1");

        act.Should().Throw<ArgumentException>(
            "widening Offset to a nullable must not relax the Kind and RawKind invariant: an enumerated kind carrying a raw descriptor stays a construction error");
    }

    [Fact]
    public void Unclassified_without_an_offset_still_rejects_an_unknown_kind_without_a_raw_descriptor()
    {
        var act = () => new ContractStreamEvent<TestTemplate>.Unclassified(null, UnclassifiedKind.Unknown, null);

        act.Should().Throw<ArgumentException>(
            "widening Offset to a nullable must not relax the Kind and RawKind invariant: Unknown without the transport's raw descriptor stays a construction error");
    }

    [Fact]
    public void Unclassified_with_new_offset_preserves_kind_and_raw_descriptor()
    {
        var original = new ContractStreamEvent<TestTemplate>.Unclassified(LedgerOffset.At(7), UnclassifiedKind.Unknown, "TopologyEvent");

        var moved = original with { Offset = LedgerOffset.At(9) };

        moved.Offset.Should().Be(LedgerOffset.At(9));
        moved.Kind.Should().Be(UnclassifiedKind.Unknown);
        moved.RawKind.Should().Be("TopologyEvent");
    }

    [Fact]
    public void UnclassifiedKind_default_is_Unknown()
    {
        default(UnclassifiedKind).Should().Be(UnclassifiedKind.Unknown);
    }

    private sealed record TestTemplate(string Owner) : ITemplate, IDamlRecord<TestTemplate>
    {
        public static Identifier TemplateId { get; } = new("pkg", "M", "TestTemplate");
        public static string PackageId => "pkg";
        public static string PackageName => "test";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create(new DamlField("owner", new DamlText(Owner)));

        public static TestTemplate FromRecord(DamlRecord record) =>
            new((record.GetField("owner") as DamlText)?.Value ?? string.Empty);
    }
}
