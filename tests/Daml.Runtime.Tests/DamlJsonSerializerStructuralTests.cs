// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class DamlJsonSerializerStructuralTests
{
    [Fact]
    public void Serialize_should_render_DamlGenMap_as_array_of_two_element_arrays()
    {
        var genMap = DamlGenMap.Create(
            (new DamlParty("Alice"), new DamlInt64(1)),
            (new DamlParty("Bob"), new DamlInt64(2))
        );
        var record = DamlRecord.Create(DamlField.Create("entries", genMap));

        var json = DamlJsonSerializer.Serialize(record);

        json.Should().Contain("\"entries\":[[\"Alice\",1],[\"Bob\",2]]");
    }

    [Fact]
    public void RoundTrip_should_preserve_DamlGenMap_entries()
    {
        var original = DamlGenMap.Create(
            (new DamlParty("Alice"), new DamlInt64(1)),
            (new DamlParty("Bob"), new DamlInt64(2))
        );
        var record = DamlRecord.Create(DamlField.Create("entries", original));

        var json = DamlJsonSerializer.Serialize(record);
        var deserialized = DamlJsonSerializer.DeserializeRecord(json);

        var entries = deserialized.GetField("entries")!;
        entries.Should().BeOfType<DamlGenMap>();
        entries.As<DamlGenMap>().Entries.Should().BeEquivalentTo(original.Entries);
    }

    [Fact]
    public void RoundTrip_top_level_Deserialize_should_preserve_DamlGenMap()
    {
        var original = DamlGenMap.Create(
            (new DamlParty("Alice"), new DamlInt64(1)),
            (new DamlParty("Bob"), new DamlInt64(2))
        );

        var json = DamlJsonSerializer.Serialize(original);
        var deserialized = DamlJsonSerializer.Deserialize(json);

        deserialized.Should().BeOfType<DamlGenMap>();
        deserialized.As<DamlGenMap>().Entries.Should().BeEquivalentTo(original.Entries);
    }

    [Fact]
    public void Deserialize_empty_array_should_resolve_to_DamlList_per_documented_contract()
    {
        var deserialized = DamlJsonSerializer.Deserialize("[]");

        deserialized.Should().BeOfType<DamlList>();
        deserialized.As<DamlList>().Values.Should().BeEmpty();
    }

    [Fact]
    public void Deserialize_pair_with_null_key_should_surface_array_null_error_not_GenMap_error()
    {
        var act = () => DamlJsonSerializer.Deserialize("[[null, 5]]");

        act.Should().Throw<JsonException>()
            .WithMessage("Null array elements not supported");
    }

    [Fact]
    public void Deserialize_pair_with_null_value_should_surface_array_null_error_not_GenMap_error()
    {
        var act = () => DamlJsonSerializer.Deserialize("[[5, null]]");

        act.Should().Throw<JsonException>()
            .WithMessage("Null array elements not supported");
    }

    [Fact]
    public void DeserializeRecord_should_throw_JsonException_when_top_level_json_is_not_an_object()
    {
        var act = () => DamlJsonSerializer.DeserializeRecord("[1,2,3]");

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void DeserializeRecord_should_throw_JsonException_naming_null_for_top_level_null_literal()
    {
        var act = () => DamlJsonSerializer.DeserializeRecord("null");

        act.Should().Throw<JsonException>().WithMessage("*null*");
    }

    [Fact]
    public void Serialize_should_throw_JsonException_for_duplicate_DamlField_labels()
    {
        var record = DamlRecord.Create(
            DamlField.Create("amount", new DamlNumeric(1.0m)),
            DamlField.Create("amount", new DamlNumeric(2.0m)));

        var act = () => DamlJsonSerializer.Serialize(record);

        act.Should().Throw<JsonException>().WithMessage("*amount*");
    }

    [Fact]
    public void DeserializeRecord_should_throw_JsonException_for_duplicate_json_properties()
    {
        var act = () => DamlJsonSerializer.DeserializeRecord("""{"amount":"1.0","amount":"2.0"}""");

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Deserialize_should_throw_JsonException_for_duplicate_json_properties()
    {
        var act = () => DamlJsonSerializer.Deserialize("""{"amount":"1.0","amount":"2.0"}""");

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Deserialize_should_throw_JsonException_for_duplicate_json_properties_in_nested_object()
    {
        var act = () => DamlJsonSerializer.Deserialize("""{"outer":{"a":1,"a":2}}""");

        act.Should().Throw<JsonException>();
    }

    private const int SupportedNestingDepth = 128;
    private const int DamlLfValueDepthLimit = 100;

    private static DamlRecord NestRecord(int levels)
    {
        DamlValue value = new DamlInt64(1);
        for (var i = 0; i < levels; i++)
        {
            value = DamlRecord.Create(DamlField.Create("inner", value));
        }
        return (DamlRecord)value;
    }

    private static string NestJson(int levels) =>
        string.Concat(Enumerable.Repeat("""{"inner":""", levels))
        + "1"
        + new string('}', levels);

    [Fact]
    public void Serialize_should_allow_nesting_at_exactly_the_supported_depth()
    {
        var act = () => DamlJsonSerializer.Serialize(NestRecord(SupportedNestingDepth));

        act.Should().NotThrow();
    }

    [Fact]
    public void Serialize_should_throw_JsonException_one_level_beyond_the_supported_depth()
    {
        var act = () => DamlJsonSerializer.Serialize(NestRecord(SupportedNestingDepth + 1));

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Deserialize_should_throw_JsonException_when_nesting_exceeds_supported_depth()
    {
        var act = () => DamlJsonSerializer.Deserialize(NestJson(SupportedNestingDepth + 1));

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void RoundTrip_should_support_values_at_the_DamlLf_depth_limit()
    {
        var json = DamlJsonSerializer.Serialize(NestRecord(DamlLfValueDepthLimit));
        var deserialized = DamlJsonSerializer.Deserialize(json);

        deserialized.Should().Be(NestRecord(DamlLfValueDepthLimit));
    }

    [Fact]
    public void DeserializeRecord_should_support_json_at_the_DamlLf_depth_limit()
    {
        var act = () => DamlJsonSerializer.DeserializeRecord(NestJson(DamlLfValueDepthLimit));

        act.Should().NotThrow();
    }
}
