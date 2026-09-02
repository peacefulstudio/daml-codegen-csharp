// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Runtime.Tests;

public class DamlLfJsonReaderRepeatedDecodeTests
{
    public sealed record GenericBox<TContent>(
        [property: DamlFieldAttribute("content")] TContent Content) : IDamlRecord
    {
        public DamlRecord ToRecord() =>
            throw new NotSupportedException("Reader-shape stand-ins in this suite are decode-only.");
    }

    [Fact]
    public void ReadRecord_should_decode_each_instantiation_of_a_generic_record_by_its_own_field_shape()
    {
        var countBox = DamlLfJsonReader.ReadRecord<GenericBox<long>>("""{"content":"42"}""");
        var flagBox = DamlLfJsonReader.ReadRecord<GenericBox<bool>>("""{"content":true}""");

        countBox.GetRequiredField("content").Should().BeOfType<DamlInt64>().Which.Value.Should().Be(42L);
        flagBox.GetRequiredField("content").Should().BeOfType<DamlBool>().Which.Value.Should().BeTrue();
    }

    public sealed record ScoredProfile(
        [property: DamlFieldAttribute("nickname")] string Nickname,
        [property: DamlFieldAttribute("score")] long Score) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("nickname", new DamlText(Nickname)),
            DamlField.Create("score", new DamlInt64(Score)));
    }

    [Fact]
    public void ReadRecord_should_produce_equal_records_across_repeated_decodes_of_one_type()
    {
        const string json = """{"nickname":"nick","score":"7"}""";

        var first = DamlLfJsonReader.ReadRecord<ScoredProfile>(json);
        var second = DamlLfJsonReader.ReadRecord<ScoredProfile>(json);

        second.Should().Be(first);
        second.Should().Be(new ScoredProfile("nick", 7L).ToRecord());
    }

    [Fact]
    public void ReadRecord_should_order_fields_by_declaration_rather_than_by_json_property_order()
    {
        var record = DamlLfJsonReader.ReadRecord<ScoredProfile>("""{"score":"7","nickname":"nick"}""");

        record.Should().Be(new ScoredProfile("nick", 7L).ToRecord());
    }

    [Fact]
    public void ReadRecord_should_still_refuse_a_missing_field_once_the_type_has_been_decoded_before()
    {
        DamlLfJsonReader.ReadRecord<ScoredProfile>("""{"nickname":"nick","score":"7"}""");

        var act = () => DamlLfJsonReader.ReadRecord<ScoredProfile>("""{"nickname":"nick"}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Required Daml field 'ScoredProfile.score' is missing from the JSON object");
    }

    public sealed record RaceProfile(
        [property: DamlFieldAttribute("nickname")] string Nickname,
        [property: DamlFieldAttribute("score")] long Score) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("nickname", new DamlText(Nickname)),
            DamlField.Create("score", new DamlInt64(Score)));
    }

    [Fact]
    public void ReadRecord_should_decode_one_type_concurrently_from_its_first_touch_onwards()
    {
        const string json = """{"nickname":"racer","score":"9"}""";
        var expected = new RaceProfile("racer", 9L).ToRecord();

        var records = Enumerable.Range(0, 64)
            .AsParallel()
            .WithDegreeOfParallelism(8)
            .Select(_ => DamlLfJsonReader.ReadRecord<RaceProfile>(json))
            .ToList();

        records.Should().AllSatisfy(record => record.Should().Be(expected));
    }
}
