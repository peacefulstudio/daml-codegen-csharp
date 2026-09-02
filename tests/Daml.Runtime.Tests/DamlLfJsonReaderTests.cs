// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Runtime.Tests;

public class DamlLfJsonReaderTests
{
    private const string OwnerJson = """{"owner":"alice::1220ab"}""";
    private const string OwnerParty = "alice::1220ab";
    private static readonly Type PartyHolderKnownOnlyAtRuntime = typeof(PartyHolder);

    public sealed record PartyHolder([property: DamlFieldAttribute("owner")] Party Owner) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("owner", Owner.ToDamlValue()));
    }

    public sealed record PartyHolderEnvelope([property: DamlFieldAttribute("holder")] PartyHolder Holder) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("holder", Holder.ToRecord()));
    }

    private static void ShouldCarryTheOwnerParty(DamlRecord record)
    {
        record.RecordId.Should().BeNull();
        record.Fields.Should().ContainSingle().Which.Label.Should().Be("owner");
        record.GetRequiredField("owner").Should().BeOfType<DamlParty>()
            .Which.Value.Should().Be(OwnerParty);
    }

    [Fact]
    public void ReadRecord_should_arm_a_party_field_when_given_json_text_and_a_type_argument()
    {
        ShouldCarryTheOwnerParty(DamlLfJsonReader.ReadRecord<PartyHolder>(OwnerJson));
    }

    [Fact]
    public void ReadRecord_should_arm_a_party_field_when_given_a_parsed_element_and_a_type_argument()
    {
        using var document = JsonDocument.Parse(OwnerJson);

        ShouldCarryTheOwnerParty(DamlLfJsonReader.ReadRecord<PartyHolder>(document.RootElement));
    }

    [Fact]
    public void ReadRecord_should_arm_a_party_field_when_given_json_text_and_a_runtime_type()
    {
        ShouldCarryTheOwnerParty(DamlLfJsonReader.ReadRecord(OwnerJson, PartyHolderKnownOnlyAtRuntime));
    }

    [Fact]
    public void ReadRecord_should_arm_a_party_field_when_given_a_parsed_element_and_a_runtime_type()
    {
        using var document = JsonDocument.Parse(OwnerJson);

        ShouldCarryTheOwnerParty(DamlLfJsonReader.ReadRecord(document.RootElement, PartyHolderKnownOnlyAtRuntime));
    }

    [Fact]
    public void ReadRecord_should_arm_a_party_field_nested_inside_a_record_field()
    {
        var record = DamlLfJsonReader.ReadRecord<PartyHolderEnvelope>("""{"holder":{"owner":"alice::1220ab"}}""");

        record.Fields.Should().ContainSingle().Which.Label.Should().Be("holder");
        ShouldCarryTheOwnerParty(record.GetRequiredField("holder").Should().BeOfType<DamlRecord>().Which);
    }

    public sealed record HoldingView([property: DamlFieldAttribute("owner")] Party Owner) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("owner", Owner.ToDamlValue()));
    }

    public interface IHolding : IDamlInterface, IHasView<HoldingView>;

    [Fact]
    public void ReadRecord_should_refuse_an_interface_marker_and_point_at_its_view_type()
    {
        var act = () => DamlLfJsonReader.ReadRecord(OwnerJson, typeof(IHolding));

        act.Should().Throw<NotSupportedException>()
            .WithMessage($"Type '{typeof(IHolding)}' at 'IHolding' is a Daml interface marker, which has no wire "
                + "record of its own; read the interface's view type instead.");
    }

    [Fact]
    public void ReadRecord_should_refuse_a_type_argument_that_is_not_a_daml_record()
    {
        var act = () => DamlLfJsonReader.ReadRecord(OwnerJson, typeof(int));

        act.Should().Throw<NotSupportedException>()
            .WithMessage($"Type '{typeof(int)}' at 'Int32' is not a generated Daml record; "
                + "pass a concrete type implementing IDamlRecord whose properties carry DamlFieldAttribute.");
    }
}
