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

    [Fact]
    public void ReadValue_should_arm_a_record_when_given_json_text_and_a_type_argument()
    {
        ShouldCarryTheOwnerParty(
            DamlLfJsonReader.ReadValue<PartyHolder>(OwnerJson).Should().BeOfType<DamlRecord>().Which);
    }

    [Fact]
    public void ReadValue_should_arm_a_record_when_given_a_parsed_element_and_a_type_argument()
    {
        using var document = JsonDocument.Parse(OwnerJson);

        ShouldCarryTheOwnerParty(
            DamlLfJsonReader.ReadValue<PartyHolder>(document.RootElement)
                .Should().BeOfType<DamlRecord>().Which);
    }

    [Fact]
    public void ReadValue_should_arm_a_record_when_given_json_text_and_a_runtime_type()
    {
        ShouldCarryTheOwnerParty(
            DamlLfJsonReader.ReadValue(OwnerJson, PartyHolderKnownOnlyAtRuntime)
                .Should().BeOfType<DamlRecord>().Which);
    }

    [Fact]
    public void ReadValue_should_arm_a_record_when_given_a_parsed_element_and_a_runtime_type()
    {
        using var document = JsonDocument.Parse(OwnerJson);

        ShouldCarryTheOwnerParty(
            DamlLfJsonReader.ReadValue(document.RootElement, PartyHolderKnownOnlyAtRuntime)
                .Should().BeOfType<DamlRecord>().Which);
    }

    [Fact]
    public void ReadValue_should_throw_ArgumentNullException_for_null_json_text_and_a_type_argument()
    {
        var act = () => DamlLfJsonReader.ReadValue<PartyHolder>((string)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("json");
    }

    [Fact]
    public void ReadValue_should_throw_ArgumentNullException_for_null_json_text_and_a_runtime_type()
    {
        var act = () => DamlLfJsonReader.ReadValue(null!, PartyHolderKnownOnlyAtRuntime);

        act.Should().Throw<ArgumentNullException>().WithParameterName("json");
    }

    [Fact]
    public void ReadValue_should_throw_ArgumentNullException_for_a_null_value_type_from_json_text()
    {
        var act = () => DamlLfJsonReader.ReadValue(OwnerJson, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("valueType");
    }

    [Fact]
    public void ReadValue_should_throw_ArgumentNullException_for_a_null_value_type_from_a_parsed_element()
    {
        using var document = JsonDocument.Parse(OwnerJson);
        var element = document.RootElement;

        var act = () => DamlLfJsonReader.ReadValue(element, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("valueType");
    }

    [Fact]
    public void ReadValue_should_refuse_an_interface_marker_and_point_at_its_view_type()
    {
        var act = () => DamlLfJsonReader.ReadValue(OwnerJson, typeof(IHolding));

        act.Should().Throw<NotSupportedException>()
            .WithMessage($"Type '{typeof(IHolding)}' at 'IHolding' is a Daml interface marker, which has no wire "
                + "record of its own; read the interface's view type instead.");
    }

    public sealed record ConverterBox<TItem>(
        [property: DamlFieldAttribute("item")] TItem Item)
        where TItem : notnull
    {
        public DamlRecord ToRecord(Func<TItem, DamlValue> convertItem) =>
            DamlRecord.Create(DamlField.Create("item", convertItem(Item)));
    }

    public static TheoryData<Type> TypesOutsideTheDamlTypeMapping =>
    [
        typeof(int),
        typeof(Uri),
        typeof(object),
        typeof(DamlValue),
        typeof(DamlRecord),
        typeof(DamlVariant),
        typeof(DamlEnum),
        typeof(DamlList),
        typeof(DamlTextMap),
        typeof(DamlGenMap),
        typeof(DamlOptional),
        typeof(DamlOptionalChain),
        typeof(ConverterBox<string>),
        typeof(DamlLfJsonReaderStdlibGenericsTests.Result<string>),
    ];

    [Theory]
    [MemberData(nameof(TypesOutsideTheDamlTypeMapping))]
    public void ReadValue_should_refuse_a_value_type_outside_the_Daml_type_mapping(Type valueType)
    {
        var act = () => DamlLfJsonReader.ReadValue(OwnerJson, valueType);

        act.Should().Throw<NotSupportedException>().WithMessage(
            $"Type '{valueType}' at '{valueType.Name}' lies outside the Daml type mapping for a top-level value; "
            + "pass a generated Daml record, variant or enum, a Daml scalar, a ContractId or Unit.");
    }
}
