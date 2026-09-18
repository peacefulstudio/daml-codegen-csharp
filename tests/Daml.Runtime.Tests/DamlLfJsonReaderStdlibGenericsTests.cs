// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Daml.Runtime.Stdlib;
using Daml.Runtime.Streams;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Daml.Runtime.Tests;

public class DamlLfJsonReaderStdlibGenericsTests
{
    public sealed record PairHolder(
        [property: DamlFieldAttribute("pair")] Tuple2<long, string> Pair) : IDamlRecord<PairHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "pair",
            Pair.ToRecord(count => new DamlInt64(count), label => new DamlText(label))));

        public static PairHolder FromRecord(DamlRecord record) => new(
            Tuple2<long, string>.FromRecord(
                record.GetRequiredField("pair").As<DamlRecord>(),
                value => value.As<DamlInt64>().Value,
                value => value.As<DamlText>().Value));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var pairJson = DamlLfJsonDecoders.RequireField(json, context, "pair");
            var pairContext = context.Field("pair");
            return DamlRecord.Create(DamlField.Create(
                "pair",
                DamlLfJsonDecoders.ReadTuple2(
                    pairJson, pairContext, DamlLfJsonDecoders.ReadInt64, DamlLfJsonDecoders.ReadText)));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_a_tuple2_field_from_its_wire_record_form()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"pair":{"_1":"42","_2":"gold"}}""", recordType: typeof(PairHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        record.GetRequiredField("pair").Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("_1", new DamlInt64(42L)),
            new DamlField("_2", new DamlText("gold")));
    }

    [Fact]
    public void ReadRecord_should_leave_a_decoded_tuple2_without_a_record_id()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"pair":{"_1":"42","_2":"gold"}}""", recordType: typeof(PairHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        record.GetRequiredField("pair").Should().BeOfType<DamlRecord>().Which.RecordId.Should().BeNull();
    }

    [Fact]
    public void ReadRecord_should_reject_a_tuple2_field_missing_a_component()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"pair":{"_1":"42"}}""", recordType: typeof(PairHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Required Daml field 'PairHolder.pair._2' is missing from the JSON object");
    }

    [Fact]
    public void ReadRecord_should_reject_a_tuple2_field_encoded_as_a_json_array()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"pair":["42","gold"]}""", recordType: typeof(PairHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON Object at 'PairHolder.pair' but found Array");
    }

    [Fact]
    public void ReadRecord_should_report_the_component_path_when_a_tuple2_component_has_the_wrong_wire_shape()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"pair":{"_1":42,"_2":"gold"}}""", recordType: typeof(PairHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON String at 'PairHolder.pair._1' but found Number");
    }

    public sealed record TripleHolder(
        [property: DamlFieldAttribute("triple")] Tuple3<long, string, bool> Triple) : IDamlRecord<TripleHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "triple",
            Triple.ToRecord(
                count => new DamlInt64(count),
                label => new DamlText(label),
                active => new DamlBool(active))));

        public static TripleHolder FromRecord(DamlRecord record) => new(
            Tuple3<long, string, bool>.FromRecord(
                record.GetRequiredField("triple").As<DamlRecord>(),
                value => value.As<DamlInt64>().Value,
                value => value.As<DamlText>().Value,
                value => value.As<DamlBool>().Value));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var tripleJson = DamlLfJsonDecoders.RequireField(json, context, "triple");
            var tripleContext = context.Field("triple");
            return DamlRecord.Create(DamlField.Create(
                "triple",
                DamlLfJsonDecoders.ReadTuple3(
                    tripleJson,
                    tripleContext,
                    DamlLfJsonDecoders.ReadInt64,
                    DamlLfJsonDecoders.ReadText,
                    DamlLfJsonDecoders.ReadBool)));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_a_tuple3_field_from_its_wire_record_form()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"triple":{"_1":"42","_2":"gold","_3":true}}""", recordType: typeof(TripleHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        record.GetRequiredField("triple").Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("_1", new DamlInt64(42L)),
            new DamlField("_2", new DamlText("gold")),
            new DamlField("_3", new DamlBool(true)));
    }

    [Fact]
    public void ReadRecord_should_reject_a_tuple3_field_missing_its_last_component()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"triple":{"_1":"42","_2":"gold"}}""", recordType: typeof(TripleHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Required Daml field 'TripleHolder.triple._3' is missing from the JSON object");
    }

    public sealed record OptionalPairHolder(
        [property: DamlFieldAttribute("pair")] Tuple2<long, Optional<string>> Pair) : IDamlRecord<OptionalPairHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "pair",
            Pair.ToRecord(
                count => new DamlInt64(count),
                label => label.ToValue(text => new DamlText(text)))));

        public static OptionalPairHolder FromRecord(DamlRecord record) => new(
            Tuple2<long, Optional<string>>.FromRecord(
                record.GetRequiredField("pair").As<DamlRecord>(),
                value => value.As<DamlInt64>().Value,
                value => Optional<string>.FromValue(value, note => note.As<DamlText>().Value)));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var pairJson = DamlLfJsonDecoders.RequireField(json, context, "pair");
            var pairContext = context.Field("pair");
            return DamlRecord.Create(DamlField.Create(
                "pair",
                DamlLfJsonDecoders.ReadTuple2(
                    pairJson,
                    pairContext,
                    DamlLfJsonDecoders.ReadInt64,
                    (element, elementContext) => DamlLfJsonDecoders.ReadOptional(
                        element, elementContext, DamlLfJsonDecoders.ReadText))));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_a_present_optional_tuple2_component_from_its_bare_wire_value()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"pair":{"_1":"42","_2":"gold"}}""", recordType: typeof(OptionalPairHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        record.GetRequiredField("pair").Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("_1", new DamlInt64(42L)),
            new DamlField("_2", DamlOptional.Some(new DamlText("gold"))));
    }

    [Fact]
    public void ReadRecord_should_decode_an_absent_optional_tuple2_component_from_json_null()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"pair":{"_1":"42","_2":null}}""", recordType: typeof(OptionalPairHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        record.GetRequiredField("pair").Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("_1", new DamlInt64(42L)),
            new DamlField("_2", DamlOptional.None));
    }

    public sealed record Profile(
        [property: DamlFieldAttribute("nickname")] string Nickname,
        [property: DamlFieldAttribute("level")] long Level) : IDamlRecord<Profile>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("nickname", new DamlText(Nickname)),
            DamlField.Create("level", new DamlInt64(Level)));

        public static Profile FromRecord(DamlRecord record) => new(
            record.GetRequiredField("nickname").As<DamlText>().Value,
            record.GetRequiredField("level").As<DamlInt64>().Value);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            return DamlRecord.Create(
                DamlField.Create("nickname", DamlLfJsonDecoders.ReadText(
                    DamlLfJsonDecoders.RequireField(json, context, "nickname"), context.Field("nickname"))),
                DamlField.Create("level", DamlLfJsonDecoders.ReadInt64(
                    DamlLfJsonDecoders.RequireField(json, context, "level"), context.Field("level"))));
        }
    }

    public sealed record ProfilePairHolder(
        [property: DamlFieldAttribute("pair")] Tuple2<Profile, string> Pair) : IDamlRecord<ProfilePairHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "pair",
            Pair.ToRecord(profile => profile.ToRecord(), label => new DamlText(label))));

        public static ProfilePairHolder FromRecord(DamlRecord record) => new(
            Tuple2<Profile, string>.FromRecord(
                record.GetRequiredField("pair").As<DamlRecord>(),
                value => Profile.FromRecord(value.As<DamlRecord>()),
                value => value.As<DamlText>().Value));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var pairJson = DamlLfJsonDecoders.RequireField(json, context, "pair");
            var pairContext = context.Field("pair");
            return DamlRecord.Create(DamlField.Create(
                "pair",
                DamlLfJsonDecoders.ReadTuple2(
                    pairJson,
                    pairContext,
                    (element, elementContext) => Profile.__ReadDamlLfJson(element, elementContext),
                    DamlLfJsonDecoders.ReadText)));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_a_generated_record_carried_by_a_tuple2_component()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"pair":{"_1":{"nickname":"nick","level":"3"},"_2":"gold"}}""", recordType: typeof(ProfilePairHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        record.GetRequiredField("pair").Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("_1", DamlRecord.Create(
                DamlField.Create("nickname", new DamlText("nick")),
                DamlField.Create("level", new DamlInt64(3L)))),
            new DamlField("_2", new DamlText("gold")));
    }

    public sealed record PairListHolder(
        [property: DamlFieldAttribute("pairs")] IReadOnlyList<Tuple2<long, string>> Pairs) : IDamlRecord<PairListHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "pairs",
            new DamlList(Pairs
                .Select(pair => (DamlValue)pair.ToRecord(count => new DamlInt64(count), label => new DamlText(label)))
                .ToList())));

        public static PairListHolder FromRecord(DamlRecord record) => new(
            record.GetRequiredField("pairs").As<DamlList>().Values
                .Select(value => Tuple2<long, string>.FromRecord(
                    value.As<DamlRecord>(),
                    component => component.As<DamlInt64>().Value,
                    component => component.As<DamlText>().Value))
                .ToList());

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var pairsJson = DamlLfJsonDecoders.RequireField(json, context, "pairs");
            var pairsContext = context.Field("pairs");
            return DamlRecord.Create(DamlField.Create(
                "pairs",
                DamlLfJsonDecoders.ReadList(
                    pairsJson,
                    pairsContext,
                    (element, elementContext) => DamlLfJsonDecoders.ReadTuple2(
                        element, elementContext, DamlLfJsonDecoders.ReadInt64, DamlLfJsonDecoders.ReadText))));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_tuple2_elements_nested_inside_a_list()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"pairs":[{"_1":"1","_2":"a"},{"_1":"2","_2":"b"}]}""", recordType: typeof(PairListHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        record.GetRequiredField("pairs").Should().BeOfType<DamlList>().Which.Values.Should().Equal(
            DamlRecord.Create(new DamlField("_1", new DamlInt64(1L)), new DamlField("_2", new DamlText("a"))),
            DamlRecord.Create(new DamlField("_1", new DamlInt64(2L)), new DamlField("_2", new DamlText("b"))));
    }

    public sealed record ChoiceHolder(
        [property: DamlFieldAttribute("choice")] Either<string, long> Choice) : IDamlRecord<ChoiceHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "choice",
            Choice.ToValue(label => new DamlText(label), count => new DamlInt64(count))));

        public static ChoiceHolder FromRecord(DamlRecord record) => new(
            Either<string, long>.FromValue(
                record.GetRequiredField("choice"),
                value => value.As<DamlText>().Value,
                value => value.As<DamlInt64>().Value));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var choiceJson = DamlLfJsonDecoders.RequireField(json, context, "choice");
            var choiceContext = context.Field("choice");
            return DamlRecord.Create(DamlField.Create(
                "choice",
                DamlLfJsonDecoders.ReadEither(
                    choiceJson, choiceContext, DamlLfJsonDecoders.ReadText, DamlLfJsonDecoders.ReadInt64)));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_an_either_left_arm_from_its_wire_variant_form()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"choice":{"tag":"Left","value":"gold"}}""", recordType: typeof(ChoiceHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        var variant = record.GetRequiredField("choice").Should().BeOfType<DamlVariant>().Which;
        variant.Constructor.Should().Be("Left");
        variant.Value.Should().Be(new DamlText("gold"));
    }

    [Fact]
    public void ReadRecord_should_decode_an_either_right_arm_from_its_wire_variant_form()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"choice":{"tag":"Right","value":"42"}}""", recordType: typeof(ChoiceHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        var variant = record.GetRequiredField("choice").Should().BeOfType<DamlVariant>().Which;
        variant.Constructor.Should().Be("Right");
        variant.Value.Should().Be(new DamlInt64(42L));
    }

    [Fact]
    public void ReadRecord_should_reject_an_unknown_either_constructor()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"choice":{"tag":"Middle","value":"42"}}""", recordType: typeof(ChoiceHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Unknown Daml variant constructor 'Middle' at 'ChoiceHolder.choice'; "
                + "expected one of Left, Right");
    }

    [Fact]
    public void ReadRecord_should_reject_an_either_field_without_a_tag()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"choice":{"value":"gold"}}""", recordType: typeof(ChoiceHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Required Daml variant field 'ChoiceHolder.choice.tag' is missing from the JSON object");
    }

    [Fact]
    public void ReadRecord_should_reject_an_either_field_missing_its_value()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"choice":{"tag":"Left"}}""", recordType: typeof(ChoiceHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Required Daml variant field 'ChoiceHolder.choice.value' is missing from the JSON object");
    }

    [Fact]
    public void ReadRecord_should_reject_an_either_field_encoded_as_a_bare_string()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"choice":"Left"}""", recordType: typeof(ChoiceHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON Object at 'ChoiceHolder.choice' but found String");
    }

    public sealed record EitherPairHolder(
        [property: DamlFieldAttribute("choice")] Either<Tuple2<long, string>, string> Choice) : IDamlRecord<EitherPairHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "choice",
            Choice.ToValue(
                pair => pair.ToRecord(count => new DamlInt64(count), label => new DamlText(label)),
                label => new DamlText(label))));

        public static EitherPairHolder FromRecord(DamlRecord record) => new(
            Either<Tuple2<long, string>, string>.FromValue(
                record.GetRequiredField("choice"),
                value => Tuple2<long, string>.FromRecord(
                    value.As<DamlRecord>(),
                    component => component.As<DamlInt64>().Value,
                    component => component.As<DamlText>().Value),
                value => value.As<DamlText>().Value));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var choiceJson = DamlLfJsonDecoders.RequireField(json, context, "choice");
            var choiceContext = context.Field("choice");
            return DamlRecord.Create(DamlField.Create(
                "choice",
                DamlLfJsonDecoders.ReadEither(
                    choiceJson,
                    choiceContext,
                    (element, elementContext) => DamlLfJsonDecoders.ReadTuple2(
                        element, elementContext, DamlLfJsonDecoders.ReadInt64, DamlLfJsonDecoders.ReadText),
                    DamlLfJsonDecoders.ReadText)));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_a_tuple2_carried_by_an_either_arm()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"choice":{"tag":"Left","value":{"_1":"7","_2":"gold"}}}""", recordType: typeof(EitherPairHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        var variant = record.GetRequiredField("choice").Should().BeOfType<DamlVariant>().Which;
        variant.Constructor.Should().Be("Left");
        variant.Value.Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("_1", new DamlInt64(7L)),
            new DamlField("_2", new DamlText("gold")));
    }

    public sealed record OptionalChoiceHolder(
        [property: DamlFieldAttribute("choice")] Either<string, Optional<string>> Choice) : IDamlRecord<OptionalChoiceHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "choice",
            Choice.ToValue(
                label => new DamlText(label),
                note => note.ToValue(text => new DamlText(text)))));

        public static OptionalChoiceHolder FromRecord(DamlRecord record) => new(
            Either<string, Optional<string>>.FromValue(
                record.GetRequiredField("choice"),
                value => value.As<DamlText>().Value,
                value => Optional<string>.FromValue(value, note => note.As<DamlText>().Value)));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var choiceJson = DamlLfJsonDecoders.RequireField(json, context, "choice");
            var choiceContext = context.Field("choice");
            return DamlRecord.Create(DamlField.Create(
                "choice",
                DamlLfJsonDecoders.ReadEither(
                    choiceJson,
                    choiceContext,
                    DamlLfJsonDecoders.ReadText,
                    (element, elementContext) => DamlLfJsonDecoders.ReadOptional(
                        element, elementContext, DamlLfJsonDecoders.ReadText))));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_a_present_optional_carried_by_an_either_arm()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"choice":{"tag":"Right","value":"gold"}}""", recordType: typeof(OptionalChoiceHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        var variant = record.GetRequiredField("choice").Should().BeOfType<DamlVariant>().Which;
        variant.Constructor.Should().Be("Right");
        variant.Value.Should().Be(DamlOptional.Some(new DamlText("gold")));
    }

    [Fact]
    public void ReadRecord_should_decode_an_absent_optional_carried_by_an_either_arm()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"choice":{"tag":"Right","value":null}}""", recordType: typeof(OptionalChoiceHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        var variant = record.GetRequiredField("choice").Should().BeOfType<DamlVariant>().Which;
        variant.Constructor.Should().Be("Right");
        variant.Value.Should().Be(DamlOptional.None);
    }

    public sealed record EitherUnitHolder(
        [property: DamlFieldAttribute("outcome")] Either<DamlUnit, long> Outcome) : IDamlRecord<EitherUnitHolder>
    {
        public DamlRecord ToRecord() =>
            throw new NotSupportedException("Reader-shape stand-ins in this suite are decode-only.");

        public static EitherUnitHolder FromRecord(DamlRecord record) =>
            throw new NotSupportedException("Reader-shape stand-ins in this suite are decode-only.");

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var outcomeJson = DamlLfJsonDecoders.RequireField(json, context, "outcome");
            var outcomeContext = context.Field("outcome");
            return DamlRecord.Create(DamlField.Create(
                "outcome",
                DamlLfJsonDecoders.ReadEither(
                    outcomeJson, outcomeContext, DamlLfJsonDecoders.ReadUnit, DamlLfJsonDecoders.ReadInt64)));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_an_either_left_arm_carrying_daml_unit()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"outcome":{"tag":"Left","value":{}}}""", recordType: typeof(EitherUnitHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        var variant = record.GetRequiredField("outcome").Should().BeOfType<DamlVariant>().Which;
        variant.Constructor.Should().Be("Left");
        variant.Value.Should().Be(DamlUnit.Instance);
    }

    public abstract record Result<TValue>
    {
        public sealed record Ok(TValue Value) : Result<TValue>
        {
            public string Tag => "Ok";
        }
    }

    public sealed record ResultHolder(
        [property: DamlFieldAttribute("result")] Result<string> Outcome) : IDamlRecord<ResultHolder>
    {
        public DamlRecord ToRecord() =>
            throw new NotSupportedException("Reader-shape stand-ins in this suite are decode-only.");

        public static ResultHolder FromRecord(DamlRecord record) =>
            throw new NotSupportedException("Reader-shape stand-ins in this suite are decode-only.");

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var resultContext = context.Field("result");
            throw new NotSupportedException(
                $"CLR type '{typeof(Result<string>)}' at '{resultContext.Path}' lies outside the Daml type mapping; "
                + "give the property a mapped Daml type or decode this field without the reader.");
        }
    }

    [Fact]
    public void ReadRecord_should_still_refuse_a_generic_variant_outside_the_stdlib_types()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"result":{"tag":"Ok","value":"gold"}}""", recordType: typeof(ResultHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*at 'ResultHolder.result' lies outside the Daml type mapping*");
    }

    public sealed record Box<TItem>(
        [property: DamlFieldAttribute("item")] TItem Item) : IDamlRecord
        where TItem : notnull
    {
        public DamlRecord ToRecord() =>
            throw new NotSupportedException("Reader-shape stand-ins in this suite are decode-only.");
    }

    public sealed record AnnotatedBox<TItem>(
        [property: DamlFieldAttribute("item")] TItem Item,
        [property: DamlFieldAttribute("note")] string? Note) : IDamlRecord
        where TItem : notnull
    {
        public DamlRecord ToRecord() =>
            throw new NotSupportedException("Reader-shape stand-ins in this suite are decode-only.");
    }

    public sealed record UnconstrainedBox<TItem>(
        [property: DamlFieldAttribute("item")] TItem Item) : IDamlRecord
    {
        public DamlRecord ToRecord() =>
            throw new NotSupportedException("Reader-shape stand-ins in this suite are decode-only.");
    }

    [Fact]
    public void ReadRecord_should_read_a_notnull_type_parameter_slot_as_required_at_a_reference_type_instantiation()
    {
#pragma warning disable DAMLRT0001
#pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"item":"gold"}""", typeof(Box<string>));
#pragma warning restore CA2263
#pragma warning restore DAMLRT0001

        record.GetRequiredField("item").Should().Be(new DamlText("gold"));
    }

    [Fact]
    public void ReadRecord_should_read_a_notnull_type_parameter_slot_as_required_at_a_value_type_instantiation()
    {
#pragma warning disable DAMLRT0001
#pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"item":"42"}""", typeof(Box<long>));
#pragma warning restore CA2263
#pragma warning restore DAMLRT0001

        record.GetRequiredField("item").Should().Be(new DamlInt64(42L));
    }

    [Fact]
    public void ReadRecord_should_keep_an_annotated_optional_slot_optional_beside_a_notnull_type_parameter()
    {
#pragma warning disable DAMLRT0001
#pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"item":"gold","note":null}""", typeof(AnnotatedBox<string>));
#pragma warning restore CA2263
#pragma warning restore DAMLRT0001

        record.GetRequiredField("item").Should().Be(new DamlText("gold"));
        record.GetRequiredField("note").Should().Be(DamlOptional.None);
    }

    [Fact]
    public void ReadRecord_should_wrap_the_unconstrained_type_parameter_slot_the_emitter_no_longer_produces()
    {
#pragma warning disable DAMLRT0001
#pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"item":"gold"}""", typeof(UnconstrainedBox<string>));
#pragma warning restore CA2263
#pragma warning restore DAMLRT0001

        record.GetRequiredField("item").Should().Be(
            DamlOptional.Some(new DamlText("gold")),
            "an unconstrained type parameter reports NullabilityState.Nullable at every reference-type "
            + "instantiation, and inventing that Optional is exactly what the emitted notnull constraint prevents");
    }

    [Fact]
    public void ReadRecord_should_reject_a_null_in_a_notnull_type_parameter_slot_that_an_unconstrained_slot_absorbs()
    {
#pragma warning disable DAMLRT0001
#pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"item":null}""", typeof(Box<string>));
#pragma warning restore CA2263
#pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON String at 'Box`1.item' but found Null");

#pragma warning disable DAMLRT0001
#pragma warning disable CA2263
        DamlLfJsonReader.ReadRecord("""{"item":null}""", typeof(UnconstrainedBox<string>))
#pragma warning restore CA2263
#pragma warning restore DAMLRT0001
            .GetRequiredField("item").Should().Be(
                DamlOptional.None,
                "the very same payload decodes to an absent Optional once the type parameter loses its "
                + "notnull constraint, so the rejection above is caused by the constraint and not by the payload");
    }

    public sealed record WrappedOptionalHolder(
        [property: DamlFieldAttribute("note")] Optional<string> Note) : IDamlRecord<WrappedOptionalHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "note",
            Note.ToValue(value => new DamlText(value))));

        public static WrappedOptionalHolder FromRecord(DamlRecord record) => new(
            Optional<string>.FromValue(record.GetRequiredField("note"), value => value.As<DamlText>().Value));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var noteJson = DamlLfJsonDecoders.RequireField(json, context, "note");
            var noteContext = context.Field("note");
            return DamlRecord.Create(DamlField.Create(
                "note", DamlLfJsonDecoders.ReadOptional(noteJson, noteContext, DamlLfJsonDecoders.ReadText)));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_a_wrapped_optional_field_carrying_a_value()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"note":"deep"}""", recordType: typeof(WrappedOptionalHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        record.GetRequiredField("note").Should().Be(DamlOptional.Some(new DamlText("deep")));
    }

    [Fact]
    public void ReadRecord_should_decode_a_wrapped_optional_field_carrying_nothing()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"note":null}""", recordType: typeof(WrappedOptionalHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        record.GetRequiredField("note").Should().Be(DamlOptional.None);
    }

    [Fact]
    public void ReadRecord_should_round_trip_a_wrapped_optional_field_through_the_generated_shape()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var carried = DamlLfJsonReader.ReadRecord("""{"note":"deep"}""", recordType: typeof(WrappedOptionalHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var absent = DamlLfJsonReader.ReadRecord("""{"note":null}""", recordType: typeof(WrappedOptionalHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        Optional<string>.FromValue(carried.GetRequiredField("note"), v => ((DamlText)v).Value)
            .Should().Be(new Optional<string>.Some("deep"));
        Optional<string>.FromValue(absent.GetRequiredField("note"), v => ((DamlText)v).Value)
            .Should().Be(new Optional<string>.None());
    }

    public sealed record TagSetHolder(
        [property: DamlFieldAttribute("tags")] Set<string> Tags) : IDamlRecord<TagSetHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "tags",
            Tags.ToRecord(tag => new DamlText(tag))));

        public static TagSetHolder FromRecord(DamlRecord record) => new(
            Set<string>.FromRecord(record.GetRequiredField("tags").As<DamlRecord>(), value => value.As<DamlText>().Value));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var tagsJson = DamlLfJsonDecoders.RequireField(json, context, "tags");
            var tagsContext = context.Field("tags");
            return DamlRecord.Create(DamlField.Create(
                "tags", DamlLfJsonDecoders.ReadSet(tagsJson, tagsContext, DamlLfJsonDecoders.ReadText)));
        }
    }

    private const string TwoTagSetJson = """{"tags":{"map":[["a",{}],["b",{}]]}}""";

    [Fact]
    public void ReadRecord_should_decode_a_set_field_from_its_wire_record_form()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord(TwoTagSetJson, recordType: typeof(TagSetHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        record.GetRequiredField("tags").Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("map", DamlGenMap.Create(
                (new DamlText("a"), DamlUnit.Instance),
                (new DamlText("b"), DamlUnit.Instance))));
    }

    [Fact]
    public void ReadRecord_should_decode_an_empty_set_field()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"tags":{"map":[]}}""", recordType: typeof(TagSetHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        record.GetRequiredField("tags").Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("map", DamlGenMap.Create()));
    }

    [Fact]
    public void ReadRecord_should_hand_a_decoded_set_field_to_the_stdlib_shape()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord(TwoTagSetJson, recordType: typeof(TagSetHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        Set<string>.FromRecord((DamlRecord)record.GetRequiredField("tags"), value => ((DamlText)value).Value)
            .Elements.Should().BeEquivalentTo(["a", "b"]);
    }

    [Fact]
    public void ReadRecord_should_reject_a_set_field_missing_its_map()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"tags":{}}""", recordType: typeof(TagSetHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Required Daml field 'TagSetHolder.tags.map' is missing from the JSON object");
    }

    [Fact]
    public void ReadRecord_should_reject_a_set_field_encoded_as_a_bare_entry_array()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"tags":[["a",{}]]}""", recordType: typeof(TagSetHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON Object at 'TagSetHolder.tags' but found Array");
    }

    [Fact]
    public void ReadRecord_should_reject_a_set_whose_map_is_not_an_entry_array()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"tags":{"map":{"a":{}}}}""", recordType: typeof(TagSetHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON Array at 'TagSetHolder.tags.map' but found Object");
    }

    [Fact]
    public void ReadRecord_should_reject_a_set_carrying_the_same_element_twice()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"tags":{"map":[["a",{}],["a",{}]]}}""", recordType: typeof(TagSetHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Duplicate key at 'TagSetHolder.tags.map[1]' in a Daml Set");
    }

    [Fact]
    public void ReadRecord_should_reject_a_set_whose_element_carries_a_value_other_than_unit()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"tags":{"map":[["a","b"]]}}""", recordType: typeof(TagSetHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON Object at 'TagSetHolder.tags.map[0].value' but found String");
    }

    [Fact]
    public void ReadRecord_should_decode_a_set_field_into_the_record_the_stdlib_shape_writes()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var decoded = DamlLfJsonReader.ReadRecord(TwoTagSetJson, recordType: typeof(TagSetHolder))
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001
            .GetRequiredField("tags").Should().BeOfType<DamlRecord>().Which;

        decoded.RecordId.Should().BeNull();
        decoded.Should().Be(new Set<string>(["a", "b"]).ToRecord(tag => new DamlText(tag)));
    }

    public sealed record ProfileSetHolder(
        [property: DamlFieldAttribute("profiles")] Set<Profile> Profiles) : IDamlRecord<ProfileSetHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "profiles",
            Profiles.ToRecord(profile => profile.ToRecord())));

        public static ProfileSetHolder FromRecord(DamlRecord record) => new(
            Set<Profile>.FromRecord(
                record.GetRequiredField("profiles").As<DamlRecord>(), value => Profile.FromRecord(value.As<DamlRecord>())));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var profilesJson = DamlLfJsonDecoders.RequireField(json, context, "profiles");
            var profilesContext = context.Field("profiles");
            return DamlRecord.Create(DamlField.Create(
                "profiles",
                DamlLfJsonDecoders.ReadSet(
                    profilesJson, profilesContext, (element, elementContext) => Profile.__ReadDamlLfJson(element, elementContext))));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_a_generated_record_carried_by_a_set_element()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"profiles":{"map":[[{"nickname":"nick","level":"3"},{}]]}}""", recordType: typeof(ProfileSetHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        var entry = record.GetRequiredField("profiles").Should().BeOfType<DamlRecord>()
            .Which.GetRequiredField("map").Should().BeOfType<DamlGenMap>()
            .Which.Entries.Should().ContainSingle().Which;
        entry.Key.Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("nickname", new DamlText("nick")),
            new DamlField("level", new DamlInt64(3L)));
        entry.Value.Should().Be(DamlUnit.Instance);
    }

    public sealed record HistoryHolder(
        [property: DamlFieldAttribute("history")] NonEmpty<string> History) : IDamlRecord<HistoryHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "history",
            History.ToRecord(entry => new DamlText(entry))));

        public static HistoryHolder FromRecord(DamlRecord record) => new(
            NonEmpty<string>.FromRecord(
                record.GetRequiredField("history").As<DamlRecord>(), value => value.As<DamlText>().Value));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var historyJson = DamlLfJsonDecoders.RequireField(json, context, "history");
            var historyContext = context.Field("history");
            return DamlRecord.Create(DamlField.Create(
                "history", DamlLfJsonDecoders.ReadNonEmpty(historyJson, historyContext, DamlLfJsonDecoders.ReadText)));
        }
    }

    private const string HeadAndTailHistoryJson = """{"history":{"hd":"a","tl":["b","c"]}}""";

    [Fact]
    public void ReadRecord_should_decode_a_non_empty_field_from_its_wire_record_form()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord(HeadAndTailHistoryJson, recordType: typeof(HistoryHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        record.GetRequiredField("history").Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("hd", new DamlText("a")),
            new DamlField("tl", new DamlList([new DamlText("b"), new DamlText("c")])));
    }

    [Fact]
    public void ReadRecord_should_decode_a_non_empty_field_carrying_only_a_head()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"history":{"hd":"a","tl":[]}}""", recordType: typeof(HistoryHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        record.GetRequiredField("history").Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("hd", new DamlText("a")),
            new DamlField("tl", new DamlList([])));
    }

    [Fact]
    public void ReadRecord_should_hand_a_decoded_non_empty_field_to_the_stdlib_shape()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord(HeadAndTailHistoryJson, recordType: typeof(HistoryHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        NonEmpty<string>.FromRecord((DamlRecord)record.GetRequiredField("history"), value => ((DamlText)value).Value)
            .All.Should().Equal("a", "b", "c");
    }

    [Fact]
    public void ReadRecord_should_decode_a_non_empty_field_into_the_record_the_stdlib_shape_writes()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var decoded = DamlLfJsonReader.ReadRecord(HeadAndTailHistoryJson, recordType: typeof(HistoryHolder))
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001
            .GetRequiredField("history").Should().BeOfType<DamlRecord>().Which;

        decoded.RecordId.Should().BeNull();
        decoded.Should().Be(new NonEmpty<string>("a", ["b", "c"]).ToRecord(entry => new DamlText(entry)));
    }

    [Fact]
    public void ReadRecord_should_reject_a_non_empty_field_missing_its_head()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"history":{"tl":["b"]}}""", recordType: typeof(HistoryHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Required Daml field 'HistoryHolder.history.hd' is missing from the JSON object");
    }

    [Fact]
    public void ReadRecord_should_reject_a_non_empty_field_missing_its_tail()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"history":{"hd":"a"}}""", recordType: typeof(HistoryHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Required Daml field 'HistoryHolder.history.tl' is missing from the JSON object");
    }

    [Fact]
    public void ReadRecord_should_reject_a_non_empty_field_whose_tail_is_not_an_array()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"history":{"hd":"a","tl":"b"}}""", recordType: typeof(HistoryHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON Array at 'HistoryHolder.history.tl' but found String");
    }

    public sealed record PairHistoryHolder(
        [property: DamlFieldAttribute("history")] NonEmpty<Tuple2<long, string>> History) : IDamlRecord<PairHistoryHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "history",
            History.ToRecord(pair => pair.ToRecord(count => new DamlInt64(count), label => new DamlText(label)))));

        public static PairHistoryHolder FromRecord(DamlRecord record) => new(
            NonEmpty<Tuple2<long, string>>.FromRecord(
                record.GetRequiredField("history").As<DamlRecord>(),
                value => Tuple2<long, string>.FromRecord(
                    value.As<DamlRecord>(),
                    component => component.As<DamlInt64>().Value,
                    component => component.As<DamlText>().Value)));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var historyJson = DamlLfJsonDecoders.RequireField(json, context, "history");
            var historyContext = context.Field("history");
            return DamlRecord.Create(DamlField.Create(
                "history",
                DamlLfJsonDecoders.ReadNonEmpty(
                    historyJson,
                    historyContext,
                    (element, elementContext) => DamlLfJsonDecoders.ReadTuple2(
                        element, elementContext, DamlLfJsonDecoders.ReadInt64, DamlLfJsonDecoders.ReadText))));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_tuple2_elements_carried_by_a_non_empty_field()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"history":{"hd":{"_1":"1","_2":"a"},"tl":[{"_1":"2","_2":"b"}]}}""", recordType: typeof(PairHistoryHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        record.GetRequiredField("history").Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("hd", DamlRecord.Create(
                new DamlField("_1", new DamlInt64(1L)),
                new DamlField("_2", new DamlText("a")))),
            new DamlField("tl", new DamlList([
                DamlRecord.Create(
                    new DamlField("_1", new DamlInt64(2L)),
                    new DamlField("_2", new DamlText("b")))])));
    }

    public sealed record TallyHolder(
        [property: DamlFieldAttribute("tally")] Map<string, long> Tally) : IDamlRecord<TallyHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "tally",
            Tally.ToRecord(label => new DamlText(label), count => new DamlInt64(count))));

        public static TallyHolder FromRecord(DamlRecord record) => new(
            Map<string, long>.FromRecord(
                record.GetRequiredField("tally").As<DamlRecord>(),
                key => key.As<DamlText>().Value,
                value => value.As<DamlInt64>().Value));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var tallyJson = DamlLfJsonDecoders.RequireField(json, context, "tally");
            var tallyContext = context.Field("tally");
            return DamlRecord.Create(DamlField.Create(
                "tally",
                DamlLfJsonDecoders.ReadStdlibMap(
                    tallyJson, tallyContext, DamlLfJsonDecoders.ReadText, DamlLfJsonDecoders.ReadInt64)));
        }
    }

    private const string TwoEntryTallyJson = """{"tally":{"map":[["alice","1"],["bob","2"]]}}""";

    [Fact]
    public void ReadRecord_should_decode_a_stdlib_map_field_from_its_wire_record_form()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord(TwoEntryTallyJson, recordType: typeof(TallyHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        record.GetRequiredField("tally").Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("map", DamlGenMap.Create(
                (new DamlText("alice"), new DamlInt64(1L)),
                (new DamlText("bob"), new DamlInt64(2L)))));
    }

    [Fact]
    public void ReadRecord_should_decode_an_empty_stdlib_map_field()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"tally":{"map":[]}}""", recordType: typeof(TallyHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        record.GetRequiredField("tally").Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("map", DamlGenMap.Create()));
    }

    [Fact]
    public void ReadRecord_should_hand_a_decoded_stdlib_map_field_to_the_stdlib_shape()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord(TwoEntryTallyJson, recordType: typeof(TallyHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        Map<string, long>.FromRecord(
                (DamlRecord)record.GetRequiredField("tally"),
                key => ((DamlText)key).Value,
                value => ((DamlInt64)value).Value)
            .Entries.Should().Equal(
                new KeyValuePair<string, long>("alice", 1L),
                new KeyValuePair<string, long>("bob", 2L));
    }

    [Fact]
    public void ReadRecord_should_decode_a_stdlib_map_field_into_the_record_the_stdlib_shape_writes()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var decoded = DamlLfJsonReader.ReadRecord(TwoEntryTallyJson, recordType: typeof(TallyHolder))
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001
            .GetRequiredField("tally").Should().BeOfType<DamlRecord>().Which;

        decoded.RecordId.Should().BeNull();
        decoded.Should().Be(new Map<string, long>([
                new KeyValuePair<string, long>("alice", 1L),
                new KeyValuePair<string, long>("bob", 2L)])
            .ToRecord(label => new DamlText(label), count => new DamlInt64(count)));
    }

    [Fact]
    public void ReadRecord_should_reject_a_stdlib_map_carrying_the_same_key_twice()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"tally":{"map":[["alice","1"],["alice","2"]]}}""", recordType: typeof(TallyHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Duplicate key at 'TallyHolder.tally.map[1]' in a Daml Map");
    }

    [Fact]
    public void ReadRecord_should_reject_a_stdlib_map_field_missing_its_map()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"tally":{}}""", recordType: typeof(TallyHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Required Daml field 'TallyHolder.tally.map' is missing from the JSON object");
    }

    [Fact]
    public void ReadRecord_should_reject_a_stdlib_map_field_encoded_as_a_text_map_object()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"tally":{"map":{"alice":"1"}}}""", recordType: typeof(TallyHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON Array at 'TallyHolder.tally.map' but found Object");
    }

    public sealed record ProfileTallyMapHolder(
        [property: DamlFieldAttribute("tally")] Map<Profile, Optional<string>> Tally) : IDamlRecord<ProfileTallyMapHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "tally",
            Tally.ToRecord(
                profile => profile.ToRecord(),
                note => note.ToValue(text => new DamlText(text)))));

        public static ProfileTallyMapHolder FromRecord(DamlRecord record) => new(
            Map<Profile, Optional<string>>.FromRecord(
                record.GetRequiredField("tally").As<DamlRecord>(),
                key => Profile.FromRecord(key.As<DamlRecord>()),
                value => Optional<string>.FromValue(value, note => note.As<DamlText>().Value)));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var tallyJson = DamlLfJsonDecoders.RequireField(json, context, "tally");
            var tallyContext = context.Field("tally");
            return DamlRecord.Create(DamlField.Create(
                "tally",
                DamlLfJsonDecoders.ReadStdlibMap(
                    tallyJson,
                    tallyContext,
                    (element, elementContext) => Profile.__ReadDamlLfJson(element, elementContext),
                    (element, elementContext) => DamlLfJsonDecoders.ReadOptional(
                        element, elementContext, DamlLfJsonDecoders.ReadText))));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_the_key_and_value_types_of_a_stdlib_map_field()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"tally":{"map":[[{"nickname":"nick","level":"3"},"gold"],[{"nickname":"nack","level":"4"},null]]}}""", recordType: typeof(ProfileTallyMapHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        record.GetRequiredField("tally").Should().BeOfType<DamlRecord>()
            .Which.GetRequiredField("map").Should().BeOfType<DamlGenMap>()
            .Which.Entries.Should().Equal(
                (DamlRecord.Create(
                    new DamlField("nickname", new DamlText("nick")),
                    new DamlField("level", new DamlInt64(3L))),
                    (DamlValue)DamlOptional.Some(new DamlText("gold"))),
                (DamlRecord.Create(
                    new DamlField("nickname", new DamlText("nack")),
                    new DamlField("level", new DamlInt64(4L))),
                    DamlOptional.None));
    }

    public sealed record BothMapShapesHolder(
        [property: DamlFieldAttribute("wrapped")] Map<string, long> Wrapped,
        [property: DamlFieldAttribute("primitive")] IReadOnlyDictionary<string, long> Primitive)
        : IDamlRecord<BothMapShapesHolder>
    {
        public DamlRecord ToRecord() =>
            throw new NotSupportedException("Reader-shape stand-ins in this suite are decode-only.");

        public static BothMapShapesHolder FromRecord(DamlRecord record) =>
            throw new NotSupportedException("Reader-shape stand-ins in this suite are decode-only.");

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var wrappedJson = DamlLfJsonDecoders.RequireField(json, context, "wrapped");
            var wrappedContext = context.Field("wrapped");
            var wrapped = DamlLfJsonDecoders.ReadStdlibMap(
                wrappedJson, wrappedContext, DamlLfJsonDecoders.ReadText, DamlLfJsonDecoders.ReadInt64);

            var primitiveJson = DamlLfJsonDecoders.RequireField(json, context, "primitive");
            var primitiveContext = context.Field("primitive");
            DamlValue primitive = primitiveJson.ValueKind == JsonValueKind.Array
                ? DamlLfJsonDecoders.ReadGenMap(
                    primitiveJson, primitiveContext, DamlLfJsonDecoders.ReadText, DamlLfJsonDecoders.ReadInt64)
                : DamlLfJsonDecoders.ReadTextMap(primitiveJson, primitiveContext, DamlLfJsonDecoders.ReadInt64);

            return DamlRecord.Create(
                DamlField.Create("wrapped", wrapped),
                DamlField.Create("primitive", primitive));
        }
    }

    [Fact]
    public void ReadRecord_should_keep_the_genmap_primitive_out_of_the_stdlib_map_record_wrapper()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"wrapped":{"map":[["alice","1"]]},"primitive":{"alice":"1"}}""", recordType: typeof(BothMapShapesHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        record.GetRequiredField("wrapped").Should().BeOfType<DamlRecord>()
            .Which.GetRequiredField("map").Should().Be(
                DamlGenMap.Create((new DamlText("alice"), new DamlInt64(1L))));
        record.GetRequiredField("primitive").Should().Be(
            DamlTextMap.Create(("alice", new DamlInt64(1L))));
    }

    [Fact]
    public void ReadRecord_should_decode_the_genmap_primitive_in_its_array_form_beside_a_stdlib_map_field()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"wrapped":{"map":[["alice","1"]]},"primitive":[["alice","1"]]}""", recordType: typeof(BothMapShapesHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        record.GetRequiredField("wrapped").Should().BeOfType<DamlRecord>()
            .Which.GetRequiredField("map").Should().Be(
                DamlGenMap.Create((new DamlText("alice"), new DamlInt64(1L))));
        record.GetRequiredField("primitive").Should().Be(
            DamlGenMap.Create((new DamlText("alice"), new DamlInt64(1L))));
    }

    public sealed record TagsByOwnerHolder(
        [property: DamlFieldAttribute("tags")] Map<string, Set<string>> Tags) : IDamlRecord<TagsByOwnerHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "tags",
            Tags.ToRecord(owner => new DamlText(owner), tags => tags.ToRecord(tag => new DamlText(tag)))));

        public static TagsByOwnerHolder FromRecord(DamlRecord record) => new(
            Map<string, Set<string>>.FromRecord(
                record.GetRequiredField("tags").As<DamlRecord>(),
                key => key.As<DamlText>().Value,
                value => Set<string>.FromRecord(value.As<DamlRecord>(), element => element.As<DamlText>().Value)));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var tagsJson = DamlLfJsonDecoders.RequireField(json, context, "tags");
            var tagsContext = context.Field("tags");
            return DamlRecord.Create(DamlField.Create(
                "tags",
                DamlLfJsonDecoders.ReadStdlibMap(
                    tagsJson,
                    tagsContext,
                    DamlLfJsonDecoders.ReadText,
                    (element, elementContext) => DamlLfJsonDecoders.ReadSet(
                        element, elementContext, DamlLfJsonDecoders.ReadText))));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_a_set_carried_by_a_stdlib_map_value()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"tags":{"map":[["alice",{"map":[["gold",{}],["silver",{}]]}],["bob",{"map":[]}]]}}""", recordType: typeof(TagsByOwnerHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        record.GetRequiredField("tags").Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("map", DamlGenMap.Create(
                (new DamlText("alice"), DamlRecord.Create(DamlField.Create("map", DamlGenMap.Create(
                    (new DamlText("gold"), DamlUnit.Instance),
                    (new DamlText("silver"), DamlUnit.Instance))))),
                (new DamlText("bob"), DamlRecord.Create(DamlField.Create("map", DamlGenMap.Create()))))));
    }

    private sealed record PinnedTemplate : ITemplate, IDamlRecord<PinnedTemplate>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("pkg", "M", "PinnedTemplate");
        public static string PackageId => "pkg";
        public static string PackageName => "pinned";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } =
            new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create();
        public static PinnedTemplate FromRecord(DamlRecord record) => new();

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            throw new NotSupportedException();
    }

    private sealed record PinnedView : IDamlRecord<PinnedView>
    {
        public DamlRecord ToRecord() => DamlRecord.Create();
        public static PinnedView FromRecord(DamlRecord record) => new();

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            throw new NotSupportedException();
    }

    private sealed record PinnedInterface : IDamlInterface, IHasView<PinnedView>
    {
        public static RuntimeIdentifier InterfaceId { get; } = new("pkg", "M", "PinnedInterface");
        public static string PackageId => "pkg";
        public static string PackageName => "pinned";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } =
            new(InterfaceId, DamlTypeKind.Interface, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create();
    }

    private sealed record InterfaceConstrainedGenericHolder(
        Contract<PinnedTemplate> Contract,
        AcsSnapshotEntry<PinnedTemplate> SnapshotEntry,
        ContractStreamEvent<PinnedTemplate> StreamEvent,
        InterfaceStreamEvent<PinnedInterface, PinnedView> InterfaceStreamEvent,
        InterfaceAcsSnapshotEntry<PinnedInterface, PinnedView> InterfaceSnapshotEntry,
        ViewDescriptor<PinnedInterface, PinnedView> ViewDescriptor,
        ContractId<PinnedTemplate> ContractId);

    private sealed record ContractIdHolder(
        [property: DamlFieldAttribute("contractId")] ContractId<PinnedTemplate> ContractId)
        : IDamlRecord<ContractIdHolder>
    {
        public DamlRecord ToRecord() => throw new NotSupportedException("decode-only shape");

        public static ContractIdHolder FromRecord(DamlRecord record) =>
            throw new NotSupportedException("decode-only shape");

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            return DamlRecord.Create(
                DamlField.Create("contractId", DamlLfJsonDecoders.ReadContractId(
                    DamlLfJsonDecoders.RequireField(json, context, "contractId"), context.Field("contractId"))));
        }
    }

    [Fact]
    public void ReadRecord_should_treat_every_interface_constrained_generic_slot_as_non_optional()
    {
        var nullability = new NullabilityInfoContext();
        var slots = typeof(InterfaceConstrainedGenericHolder).GetProperties();

        slots.Should().NotBeEmpty(
            "a holder that lost its properties would leave the projection below empty and pass "
            + "while exercising no interface-constrained generic at all");

        var nullableSlots = slots
            .Where(property => nullability.Create(property).ReadState == NullabilityState.Nullable)
            .Select(property => property.Name);

        nullableSlots.Should().BeEmpty(
            "an interface-constrained generic reports NullabilityState.Unknown, and the reader's "
            + "optionality predicate tests for Nullable rather than for not-NotNull");
    }

    [Fact]
    public void ReadRecord_should_reject_rather_than_silently_absorb_a_null_in_an_interface_constrained_generic_slot()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"contractId":null}""", recordType: typeof(ContractIdHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON String at 'ContractIdHolder.contractId' but found Null");
    }

    public static TheoryData<Type> GenericFamiliesRefusedAsTopLevelValues =>
    [
        typeof(Optional<string>),
        typeof(Optional<Optional<string>>),
        typeof(Tuple2<Party, string>),
        typeof(Tuple3<Party, string, long>),
        typeof(Either<string, long>),
        typeof(Set<string>),
        typeof(NonEmpty<string>),
        typeof(Map<string, long>),
    ];

    [Theory]
    [MemberData(nameof(GenericFamiliesRefusedAsTopLevelValues))]
    public void ReadValue_should_refuse_a_generic_family_as_a_top_level_value(Type valueType)
    {
        #pragma warning disable DAMLRT0001
        var act = () => DamlLfJsonReader.ReadValue("[]", valueType);
        #pragma warning restore DAMLRT0001

        act.Should().Throw<NotSupportedException>().WithMessage(
            $"Type '{valueType}' at '{valueType.Name}' names a generic Daml type family the reader does not decode "
            + "as a top-level value; decode it as a field of a generated Daml record, or decode this value without "
            + "the reader.");
    }

    [Theory]
    [MemberData(nameof(GenericFamiliesRefusedAsTopLevelValues))]
    public void ReadValue_should_refuse_a_generic_family_as_a_top_level_value_from_a_parsed_element(
        Type valueType)
    {
        using var document = JsonDocument.Parse("[]");
        var element = document.RootElement;

        #pragma warning disable DAMLRT0001
        var act = () => DamlLfJsonReader.ReadValue(element, valueType);
        #pragma warning restore DAMLRT0001

        act.Should().Throw<NotSupportedException>().WithMessage(
            $"Type '{valueType}' at '{valueType.Name}' names a generic Daml type family the reader does not decode "
            + "as a top-level value; decode it as a field of a generated Daml record, or decode this value without "
            + "the reader.");
    }

    [Fact]
    public void ReadValue_should_refuse_a_nullable_scalar_as_a_top_level_value()
    {
        #pragma warning disable DAMLRT0001
        var act = () => DamlLfJsonReader.ReadValue<long?>("\"42\"");
        #pragma warning restore DAMLRT0001

        act.Should().Throw<NotSupportedException>().WithMessage(
            $"Type '{typeof(long?)}' at '{typeof(long?).Name}' names a generic Daml type family the reader does "
            + "not decode as a top-level value; decode it as a field of a generated Daml record, or decode this "
            + "value without the reader.");
    }

    [Fact]
    public void ReadValue_should_refuse_a_nullable_generated_enum_as_a_top_level_value()
    {
        #pragma warning disable DAMLRT0001
        var act = () => DamlLfJsonReader.ReadValue<Direction?>("\"Forward\"");
        #pragma warning restore DAMLRT0001

        act.Should().Throw<NotSupportedException>().WithMessage(
            $"Type '{typeof(Direction?)}' at '{typeof(Direction?).Name}' names a generic Daml type family the "
            + "reader does not decode as a top-level value; decode it as a field of a generated Daml record, or "
            + "decode this value without the reader.");
    }

    private static readonly Type TextListKnownOnlyAtRuntime = typeof(IReadOnlyList<string>);

    private static readonly Type TextMapKnownOnlyAtRuntime = typeof(IReadOnlyDictionary<string, string>);

    private static readonly Type LongTextMapKnownOnlyAtRuntime = typeof(IReadOnlyDictionary<string, long>);

    private static readonly Type GenMapKnownOnlyAtRuntime = typeof(IReadOnlyDictionary<Party, long>);

    [Fact]
    public void ReadValue_should_refuse_a_top_level_list_even_when_the_json_matches_its_shape()
    {
        #pragma warning disable DAMLRT0001
        var act = () => DamlLfJsonReader.ReadValue("""["a","b"]""", TextListKnownOnlyAtRuntime);
        #pragma warning restore DAMLRT0001

        act.Should().Throw<NotSupportedException>(
            "a bare Type cannot say whether the element is Optional, so decoding it would silently "
            + "drop optionality rather than fail");
    }

    [Fact]
    public void ReadValue_should_decode_an_empty_top_level_text_map()
    {
        #pragma warning disable DAMLRT0001
        var value = DamlLfJsonReader.ReadValue<IReadOnlyDictionary<string, string>>("{}");
        #pragma warning restore DAMLRT0001

        value.Should().BeOfType<DamlTextMap>().Which.Count.Should().Be(0);
    }

    [Fact]
    public void ReadValue_should_decode_an_empty_top_level_list()
    {
        #pragma warning disable DAMLRT0001
        var value = DamlLfJsonReader.ReadValue("[]", TextListKnownOnlyAtRuntime);
        #pragma warning restore DAMLRT0001

        value.Should().BeOfType<DamlList>().Which.Count.Should().Be(0);
    }

    [Fact]
    public void ReadValue_should_decode_an_empty_top_level_gen_map()
    {
        #pragma warning disable DAMLRT0001
        var value = DamlLfJsonReader.ReadValue("[]", GenMapKnownOnlyAtRuntime);
        #pragma warning restore DAMLRT0001

        value.Should().BeOfType<DamlGenMap>().Which.Count.Should().Be(0);
    }

    [Fact]
    public void ReadValue_should_decode_the_array_wire_form_of_an_empty_string_keyed_dictionary_as_a_gen_map()
    {
        #pragma warning disable DAMLRT0001
        var value = DamlLfJsonReader.ReadValue("[]", TextMapKnownOnlyAtRuntime);
        #pragma warning restore DAMLRT0001

        value.Should().BeOfType<DamlGenMap>().Which.Count.Should().Be(0);
    }

    [Fact]
    public void ReadValue_should_decode_an_empty_top_level_collection_from_a_parsed_element()
    {
        using var document = JsonDocument.Parse("{}");

        #pragma warning disable DAMLRT0001
        var value = DamlLfJsonReader.ReadValue(document.RootElement, LongTextMapKnownOnlyAtRuntime);
        #pragma warning restore DAMLRT0001

        value.Should().BeOfType<DamlTextMap>().Which.Count.Should().Be(0);
    }

    [Fact]
    public void ReadValue_should_refuse_a_non_empty_top_level_text_map()
    {
        #pragma warning disable DAMLRT0001
        var act = () => DamlLfJsonReader.ReadValue("""{"a":"b"}""", TextMapKnownOnlyAtRuntime);
        #pragma warning restore DAMLRT0001

        act.Should().Throw<NotSupportedException>().WithMessage(
            $"Type '{TextMapKnownOnlyAtRuntime}' at "
            + $"'{TextMapKnownOnlyAtRuntime.Name}' names a generic Daml type family the reader "
            + "does not decode as a top-level value; decode it as a field of a generated Daml record, or decode "
            + "this value without the reader.");
    }

    [Fact]
    public void ReadValue_should_refuse_a_non_empty_top_level_gen_map()
    {
        #pragma warning disable DAMLRT0001
        var act = () => DamlLfJsonReader.ReadValue("""[["alice","1"]]""", GenMapKnownOnlyAtRuntime);
        #pragma warning restore DAMLRT0001

        act.Should().Throw<NotSupportedException>().WithMessage(
            $"Type '{GenMapKnownOnlyAtRuntime}' at "
            + $"'{GenMapKnownOnlyAtRuntime.Name}' names a generic Daml type family the reader "
            + "does not decode as a top-level value; decode it as a field of a generated Daml record, or decode "
            + "this value without the reader.");
    }

    [Fact]
    public void ReadValue_should_refuse_the_object_wire_form_of_an_empty_non_string_keyed_dictionary()
    {
        #pragma warning disable DAMLRT0001
        var act = () => DamlLfJsonReader.ReadValue("{}", GenMapKnownOnlyAtRuntime);
        #pragma warning restore DAMLRT0001

        act.Should().Throw<NotSupportedException>().WithMessage(
            $"Type '{GenMapKnownOnlyAtRuntime}' at "
            + $"'{GenMapKnownOnlyAtRuntime.Name}' names a generic Daml type family the reader "
            + "does not decode as a top-level value; decode it as a field of a generated Daml record, or decode "
            + "this value without the reader.");
    }

    [Fact]
    public void ReadValue_should_refuse_the_object_wire_form_of_a_top_level_list()
    {
        #pragma warning disable DAMLRT0001
        var act = () => DamlLfJsonReader.ReadValue("{}", TextListKnownOnlyAtRuntime);
        #pragma warning restore DAMLRT0001

        act.Should().Throw<NotSupportedException>().WithMessage(
            $"Type '{TextListKnownOnlyAtRuntime}' at '{TextListKnownOnlyAtRuntime.Name}' names a generic Daml "
            + "type family the reader does not decode as a top-level value; decode it as a field of a generated "
            + "Daml record, or decode this value without the reader.");
    }

    [Theory]
    [MemberData(nameof(GenericFamiliesRefusedAsTopLevelValues))]
    public void ReadValue_should_refuse_a_generic_family_whose_empty_wire_form_is_an_object(Type valueType)
    {
        #pragma warning disable DAMLRT0001
        var act = () => DamlLfJsonReader.ReadValue("{}", valueType);
        #pragma warning restore DAMLRT0001

        act.Should().Throw<NotSupportedException>().WithMessage(
            $"Type '{valueType}' at '{valueType.Name}' names a generic Daml type family the reader does not decode "
            + "as a top-level value; decode it as a field of a generated Daml record, or decode this value without "
            + "the reader.");
    }
}
