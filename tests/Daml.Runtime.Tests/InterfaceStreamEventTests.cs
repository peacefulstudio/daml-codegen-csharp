// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Xunit;

namespace Daml.Runtime.Tests;

public sealed class InterfaceStreamEventTests
{
    [Fact]
    public void Unclassified_should_expose_offset_and_enumerated_kind()
    {
        var unclassified = new InterfaceStreamEvent<TestInterface, TestView>.Unclassified(
            LedgerOffset.At(7), UnclassifiedKind.DecodeFailure);

        unclassified.Offset.Should().Be(
            LedgerOffset.At(7),
            "a projector that does know where the unclassifiable event sat must still report it, so the nullable slot has to round-trip a real offset unchanged");
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        unclassified.RawKind.Should().BeNull();
    }

    [Fact]
    public void Unclassified_should_expose_InterfaceViewUnavailable_without_a_raw_descriptor()
    {
        var unclassified = new InterfaceStreamEvent<TestInterface, TestView>.Unclassified(
            LedgerOffset.At(7), UnclassifiedKind.InterfaceViewUnavailable);

        unclassified.Kind.Should().Be(
            UnclassifiedKind.InterfaceViewUnavailable,
            "the participant computes the interface view, so a matching event without one is the interface family's own unclassified reason and must survive as an enumerated kind");
        unclassified.RawKind.Should().BeNull();
    }

    [Fact]
    public void Unclassified_unknown_kind_preserves_the_raw_descriptor()
    {
        var unclassified = new InterfaceStreamEvent<TestInterface, TestView>.Unclassified(
            LedgerOffset.At(7), UnclassifiedKind.Unknown, "TopologyEvent");

        unclassified.Kind.Should().Be(UnclassifiedKind.Unknown);
        unclassified.RawKind.Should().Be("TopologyEvent");
    }

    [Fact]
    public void Unclassified_rejects_a_raw_descriptor_on_an_enumerated_kind()
    {
        var act = () => new InterfaceStreamEvent<TestInterface, TestView>.Unclassified(
            LedgerOffset.At(7), UnclassifiedKind.DecodeFailure, "EventCase_1");

        act.Should().Throw<ArgumentException>()
            .WithMessage("An Unclassified event with the enumerated Kind 'DecodeFailure' must not carry a RawKind; RawKind is populated only for Unknown.*")
            .WithParameterName("RawKind");
    }

    [Fact]
    public void Unclassified_rejects_an_unknown_kind_without_a_raw_descriptor()
    {
        var act = () => new InterfaceStreamEvent<TestInterface, TestView>.Unclassified(
            LedgerOffset.At(7), UnclassifiedKind.Unknown, null);

        act.Should().Throw<ArgumentException>()
            .WithMessage("An Unclassified event with Kind Unknown must carry the transport's raw descriptor in RawKind.*")
            .WithParameterName("RawKind");
    }

    [Fact]
    public void Unclassified_accepts_a_null_offset_and_preserves_it()
    {
        var unclassified = new InterfaceStreamEvent<TestInterface, TestView>.Unclassified(
            null, UnclassifiedKind.DecodeFailure);

        unclassified.Offset.Should().BeNull(
            "an event the projector could not place on the ledger has no offset to report, and the only alternative is fabricating LedgerOffset.Begin, which is indistinguishable from a genuine ledger start: a consumer persisting it as resume state checkpoints at the beginning of the ledger and re-reads the entire stream");
        unclassified.Kind.Should().Be(UnclassifiedKind.DecodeFailure);
        unclassified.RawKind.Should().BeNull();
    }

    [Fact]
    public void Unclassified_without_an_offset_still_rejects_a_raw_descriptor_on_an_enumerated_kind()
    {
        var act = () => new InterfaceStreamEvent<TestInterface, TestView>.Unclassified(
            null, UnclassifiedKind.DecodeFailure, "EventCase_1");

        act.Should().Throw<ArgumentException>(
            "widening Offset to a nullable must not relax the Kind and RawKind invariant: an enumerated kind carrying a raw descriptor stays a construction error")
            .WithMessage("An Unclassified event with the enumerated Kind 'DecodeFailure' must not carry a RawKind; RawKind is populated only for Unknown.*")
            .WithParameterName("RawKind");
    }

    [Fact]
    public void Unclassified_without_an_offset_still_rejects_an_unknown_kind_without_a_raw_descriptor()
    {
        var act = () => new InterfaceStreamEvent<TestInterface, TestView>.Unclassified(
            null, UnclassifiedKind.Unknown, null);

        act.Should().Throw<ArgumentException>(
            "widening Offset to a nullable must not relax the Kind and RawKind invariant: Unknown without the transport's raw descriptor stays a construction error")
            .WithMessage("An Unclassified event with Kind Unknown must carry the transport's raw descriptor in RawKind.*")
            .WithParameterName("RawKind");
    }

    [Fact]
    public void Unclassified_with_new_offset_preserves_kind_and_raw_descriptor()
    {
        var original = new InterfaceStreamEvent<TestInterface, TestView>.Unclassified(
            LedgerOffset.At(7), UnclassifiedKind.Unknown, "TopologyEvent");

        var moved = original with { Offset = LedgerOffset.At(9) };

        moved.Offset.Should().Be(LedgerOffset.At(9));
        moved.Kind.Should().Be(
            UnclassifiedKind.Unknown,
            "Kind and RawKind are get-only, so a with expression that moves the offset cannot bypass the pair's invariant");
        moved.RawKind.Should().Be("TopologyEvent");
    }

    private interface TestInterface : IDamlInterface, IHasView<TestView>
    {
        static Identifier IDamlInterface.InterfaceId => new("pkg", "M", "TestInterface");
        static string IDamlInterface.PackageId => "pkg";
        static string IDamlInterface.PackageName => "test";
        static Version IDamlInterface.PackageVersion => new(0, 1, 0);
        static DamlTypeDescriptor IDamlType.DamlTypeId =>
            new(new Identifier("pkg", "M", "TestInterface"), DamlTypeKind.Interface, "test");
    }

    private sealed record TestView : IDamlRecord<TestView>
    {
        public DamlRecord ToRecord() => DamlRecord.Create();

        public static TestView FromRecord(DamlRecord record) => new();
    }
}
