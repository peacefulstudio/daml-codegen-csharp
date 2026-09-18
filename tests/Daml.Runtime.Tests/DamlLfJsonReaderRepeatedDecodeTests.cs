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
#pragma warning disable DAMLRT0001
#pragma warning disable CA2263
        var countBox = DamlLfJsonReader.ReadRecord("""{"content":"42"}""", typeof(GenericBox<long>));
        var flagBox = DamlLfJsonReader.ReadRecord("""{"content":true}""", typeof(GenericBox<bool>));
#pragma warning restore CA2263
#pragma warning restore DAMLRT0001

        countBox.GetRequiredField("content").Should().BeOfType<DamlInt64>().Which.Value.Should().Be(42L);
        flagBox.GetRequiredField("content").Should().BeOfType<DamlBool>().Which.Value.Should().BeTrue();
    }

    public sealed record ScoredProfile(
        [property: DamlFieldAttribute("nickname")] string Nickname,
        [property: DamlFieldAttribute("score")] long Score) : IDamlRecord<ScoredProfile>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("nickname", new DamlText(Nickname)),
            DamlField.Create("score", new DamlInt64(Score)));

        public static ScoredProfile FromRecord(DamlRecord record) => new(
            record.GetRequiredField("nickname").As<DamlText>().Value,
            record.GetRequiredField("score").As<DamlInt64>().Value);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            return DamlRecord.Create(
                DamlField.Create("nickname", DamlLfJsonDecoders.ReadText(
                    DamlLfJsonDecoders.RequireField(json, context, "nickname"), context.Field("nickname"))),
                DamlField.Create("score", DamlLfJsonDecoders.ReadInt64(
                    DamlLfJsonDecoders.RequireField(json, context, "score"), context.Field("score"))));
        }
    }

    [Fact]
    public void ReadRecord_should_produce_equal_records_across_repeated_decodes_of_one_type()
    {
        const string json = """{"nickname":"nick","score":"7"}""";

        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var first = DamlLfJsonReader.ReadRecord(json, recordType: typeof(ScoredProfile));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var second = DamlLfJsonReader.ReadRecord(json, recordType: typeof(ScoredProfile));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        second.Should().Be(first);
        second.Should().Be(new ScoredProfile("nick", 7L).ToRecord());
    }

    [Fact]
    public void ReadRecord_should_order_fields_by_declaration_rather_than_by_json_property_order()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"score":"7","nickname":"nick"}""", recordType: typeof(ScoredProfile));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        record.Should().Be(new ScoredProfile("nick", 7L).ToRecord());
    }

    [Fact]
    public void ReadRecord_should_still_refuse_a_missing_field_once_the_type_has_been_decoded_before()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        DamlLfJsonReader.ReadRecord("""{"nickname":"nick","score":"7"}""", recordType: typeof(ScoredProfile));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"nickname":"nick"}""", recordType: typeof(ScoredProfile));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Required Daml field 'ScoredProfile.score' is missing from the JSON object");
    }

    public sealed record RaceProfile(
        [property: DamlFieldAttribute("nickname")] string Nickname,
        [property: DamlFieldAttribute("score")] long Score) : IDamlRecord<RaceProfile>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("nickname", new DamlText(Nickname)),
            DamlField.Create("score", new DamlInt64(Score)));

        public static RaceProfile FromRecord(DamlRecord record) => new(
            record.GetRequiredField("nickname").As<DamlText>().Value,
            record.GetRequiredField("score").As<DamlInt64>().Value);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            return DamlRecord.Create(
                DamlField.Create("nickname", DamlLfJsonDecoders.ReadText(
                    DamlLfJsonDecoders.RequireField(json, context, "nickname"), context.Field("nickname"))),
                DamlField.Create("score", DamlLfJsonDecoders.ReadInt64(
                    DamlLfJsonDecoders.RequireField(json, context, "score"), context.Field("score"))));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_one_type_concurrently_from_its_first_touch_onwards()
    {
        const string json = """{"nickname":"racer","score":"9"}""";
        var expected = new RaceProfile("racer", 9L).ToRecord();

        var records = Enumerable.Range(0, 64)
            .AsParallel()
            .WithDegreeOfParallelism(8)
            #pragma warning disable DAMLRT0001
            #pragma warning disable CA2263
            .Select(_ => DamlLfJsonReader.ReadRecord(json, recordType: typeof(RaceProfile)))
            #pragma warning restore CA2263
            #pragma warning restore DAMLRT0001
            .ToList();

        records.Should().AllSatisfy(record => record.Should().Be(expected));
    }

    public abstract record Verdict : IDamlVariant
    {
        public abstract string Tag { get; }

        public abstract DamlVariant ToVariant();

        public sealed record Cleared(string Value) : Verdict
        {
            public override string Tag => "Cleared";

            public override DamlVariant ToVariant() => DamlVariant.Create("Cleared", new DamlText(Value));
        }
    }

    [Fact]
    public void ReadValue_should_produce_equal_variants_across_repeated_decodes_of_one_type()
    {
        const string json = """{"tag":"Cleared","value":"ok"}""";

        #pragma warning disable DAMLRT0001
        var first = DamlLfJsonReader.ReadValue<Verdict>(json);
        var second = DamlLfJsonReader.ReadValue<Verdict>(json);
        #pragma warning restore DAMLRT0001

        second.Should().Be(first);
        second.Should().Be(new Verdict.Cleared("ok").ToVariant());
    }

    [Fact]
    public void ReadValue_should_decode_one_variant_concurrently_from_its_first_touch_onwards()
    {
        const string json = """{"tag":"Cleared","value":"ok"}""";
        var expected = new Verdict.Cleared("ok").ToVariant();

        #pragma warning disable DAMLRT0001
        var variants = Enumerable.Range(0, 64)
            .AsParallel()
            .WithDegreeOfParallelism(8)
            .Select(_ => DamlLfJsonReader.ReadValue<Verdict>(json))
            .ToList();
        #pragma warning restore DAMLRT0001

        variants.Should().AllSatisfy(variant => variant.Should().Be(expected));
    }
}
