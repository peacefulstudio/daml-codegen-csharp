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
        [property: DamlFieldAttribute("pair")] Tuple2<long, string> Pair) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "pair",
            Pair.ToRecord(count => new DamlInt64(count), label => new DamlText(label))));
    }

    [Fact]
    public void ReadRecord_should_decode_a_tuple2_field_from_its_wire_record_form()
    {
        var record = DamlLfJsonReader.ReadRecord<PairHolder>("""{"pair":{"_1":"42","_2":"gold"}}""");

        record.GetRequiredField("pair").Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("_1", new DamlInt64(42L)),
            new DamlField("_2", new DamlText("gold")));
    }

    [Fact]
    public void ReadRecord_should_leave_a_decoded_tuple2_without_a_record_id()
    {
        var record = DamlLfJsonReader.ReadRecord<PairHolder>("""{"pair":{"_1":"42","_2":"gold"}}""");

        record.GetRequiredField("pair").Should().BeOfType<DamlRecord>().Which.RecordId.Should().BeNull();
    }

    [Fact]
    public void ReadRecord_should_reject_a_tuple2_field_missing_a_component()
    {
        var act = () => DamlLfJsonReader.ReadRecord<PairHolder>("""{"pair":{"_1":"42"}}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Required Daml field 'PairHolder.pair._2' is missing from the JSON object");
    }

    [Fact]
    public void ReadRecord_should_reject_a_tuple2_field_encoded_as_a_json_array()
    {
        var act = () => DamlLfJsonReader.ReadRecord<PairHolder>("""{"pair":["42","gold"]}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON Object at 'PairHolder.pair' but found Array");
    }

    [Fact]
    public void ReadRecord_should_report_the_component_path_when_a_tuple2_component_has_the_wrong_wire_shape()
    {
        var act = () => DamlLfJsonReader.ReadRecord<PairHolder>("""{"pair":{"_1":42,"_2":"gold"}}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON String at 'PairHolder.pair._1' but found Number");
    }

    public sealed record TripleHolder(
        [property: DamlFieldAttribute("triple")] Tuple3<long, string, bool> Triple) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "triple",
            Triple.ToRecord(
                count => new DamlInt64(count),
                label => new DamlText(label),
                active => new DamlBool(active))));
    }

    [Fact]
    public void ReadRecord_should_decode_a_tuple3_field_from_its_wire_record_form()
    {
        var record = DamlLfJsonReader.ReadRecord<TripleHolder>(
            """{"triple":{"_1":"42","_2":"gold","_3":true}}""");

        record.GetRequiredField("triple").Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("_1", new DamlInt64(42L)),
            new DamlField("_2", new DamlText("gold")),
            new DamlField("_3", new DamlBool(true)));
    }

    [Fact]
    public void ReadRecord_should_reject_a_tuple3_field_missing_its_last_component()
    {
        var act = () => DamlLfJsonReader.ReadRecord<TripleHolder>("""{"triple":{"_1":"42","_2":"gold"}}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Required Daml field 'TripleHolder.triple._3' is missing from the JSON object");
    }

    public sealed record OptionalPairHolder(
        [property: DamlFieldAttribute("pair")] Tuple2<long, Optional<string>> Pair) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "pair",
            Pair.ToRecord(
                count => new DamlInt64(count),
                label => label.ToValue(text => new DamlText(text)))));
    }

    [Fact]
    public void ReadRecord_should_decode_a_present_optional_tuple2_component_from_its_bare_wire_value()
    {
        var record = DamlLfJsonReader.ReadRecord<OptionalPairHolder>("""{"pair":{"_1":"42","_2":"gold"}}""");

        record.GetRequiredField("pair").Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("_1", new DamlInt64(42L)),
            new DamlField("_2", DamlOptional.Some(new DamlText("gold"))));
    }

    [Fact]
    public void ReadRecord_should_decode_an_absent_optional_tuple2_component_from_json_null()
    {
        var record = DamlLfJsonReader.ReadRecord<OptionalPairHolder>("""{"pair":{"_1":"42","_2":null}}""");

        record.GetRequiredField("pair").Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("_1", new DamlInt64(42L)),
            new DamlField("_2", DamlOptional.None));
    }

    public sealed record Profile(
        [property: DamlFieldAttribute("nickname")] string Nickname,
        [property: DamlFieldAttribute("level")] long Level) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("nickname", new DamlText(Nickname)),
            DamlField.Create("level", new DamlInt64(Level)));
    }

    public sealed record ProfilePairHolder(
        [property: DamlFieldAttribute("pair")] Tuple2<Profile, string> Pair) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "pair",
            Pair.ToRecord(profile => profile.ToRecord(), label => new DamlText(label))));
    }

    [Fact]
    public void ReadRecord_should_decode_a_generated_record_carried_by_a_tuple2_component()
    {
        var record = DamlLfJsonReader.ReadRecord<ProfilePairHolder>(
            """{"pair":{"_1":{"nickname":"nick","level":"3"},"_2":"gold"}}""");

        record.GetRequiredField("pair").Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("_1", DamlRecord.Create(
                DamlField.Create("nickname", new DamlText("nick")),
                DamlField.Create("level", new DamlInt64(3L)))),
            new DamlField("_2", new DamlText("gold")));
    }

    public sealed record PairListHolder(
        [property: DamlFieldAttribute("pairs")] IReadOnlyList<Tuple2<long, string>> Pairs) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "pairs",
            new DamlList(Pairs
                .Select(pair => (DamlValue)pair.ToRecord(count => new DamlInt64(count), label => new DamlText(label)))
                .ToList())));
    }

    [Fact]
    public void ReadRecord_should_decode_tuple2_elements_nested_inside_a_list()
    {
        var record = DamlLfJsonReader.ReadRecord<PairListHolder>(
            """{"pairs":[{"_1":"1","_2":"a"},{"_1":"2","_2":"b"}]}""");

        record.GetRequiredField("pairs").Should().BeOfType<DamlList>().Which.Values.Should().Equal(
            DamlRecord.Create(new DamlField("_1", new DamlInt64(1L)), new DamlField("_2", new DamlText("a"))),
            DamlRecord.Create(new DamlField("_1", new DamlInt64(2L)), new DamlField("_2", new DamlText("b"))));
    }

    public sealed record ChoiceHolder(
        [property: DamlFieldAttribute("choice")] Either<string, long> Choice) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "choice",
            Choice.ToValue(label => new DamlText(label), count => new DamlInt64(count))));
    }

    [Fact]
    public void ReadRecord_should_decode_an_either_left_arm_from_its_wire_variant_form()
    {
        var record = DamlLfJsonReader.ReadRecord<ChoiceHolder>("""{"choice":{"tag":"Left","value":"gold"}}""");

        var variant = record.GetRequiredField("choice").Should().BeOfType<DamlVariant>().Which;
        variant.Constructor.Should().Be("Left");
        variant.Value.Should().Be(new DamlText("gold"));
    }

    [Fact]
    public void ReadRecord_should_decode_an_either_right_arm_from_its_wire_variant_form()
    {
        var record = DamlLfJsonReader.ReadRecord<ChoiceHolder>("""{"choice":{"tag":"Right","value":"42"}}""");

        var variant = record.GetRequiredField("choice").Should().BeOfType<DamlVariant>().Which;
        variant.Constructor.Should().Be("Right");
        variant.Value.Should().Be(new DamlInt64(42L));
    }

    [Fact]
    public void ReadRecord_should_reject_an_unknown_either_constructor()
    {
        var act = () => DamlLfJsonReader.ReadRecord<ChoiceHolder>("""{"choice":{"tag":"Middle","value":"42"}}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Unknown Daml variant constructor 'Middle' at 'ChoiceHolder.choice'; "
                + "expected one of Left, Right");
    }

    [Fact]
    public void ReadRecord_should_reject_an_either_field_without_a_tag()
    {
        var act = () => DamlLfJsonReader.ReadRecord<ChoiceHolder>("""{"choice":{"value":"gold"}}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Required Daml variant field 'ChoiceHolder.choice.tag' is missing from the JSON object");
    }

    [Fact]
    public void ReadRecord_should_reject_an_either_field_missing_its_value()
    {
        var act = () => DamlLfJsonReader.ReadRecord<ChoiceHolder>("""{"choice":{"tag":"Left"}}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Required Daml variant field 'ChoiceHolder.choice.value' is missing from the JSON object");
    }

    [Fact]
    public void ReadRecord_should_reject_an_either_field_encoded_as_a_bare_string()
    {
        var act = () => DamlLfJsonReader.ReadRecord<ChoiceHolder>("""{"choice":"Left"}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON Object at 'ChoiceHolder.choice' but found String");
    }

    public sealed record EitherPairHolder(
        [property: DamlFieldAttribute("choice")] Either<Tuple2<long, string>, string> Choice) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "choice",
            Choice.ToValue(
                pair => pair.ToRecord(count => new DamlInt64(count), label => new DamlText(label)),
                label => new DamlText(label))));
    }

    [Fact]
    public void ReadRecord_should_decode_a_tuple2_carried_by_an_either_arm()
    {
        var record = DamlLfJsonReader.ReadRecord<EitherPairHolder>(
            """{"choice":{"tag":"Left","value":{"_1":"7","_2":"gold"}}}""");

        var variant = record.GetRequiredField("choice").Should().BeOfType<DamlVariant>().Which;
        variant.Constructor.Should().Be("Left");
        variant.Value.Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("_1", new DamlInt64(7L)),
            new DamlField("_2", new DamlText("gold")));
    }

    public sealed record OptionalChoiceHolder(
        [property: DamlFieldAttribute("choice")] Either<string, Optional<string>> Choice) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "choice",
            Choice.ToValue(
                label => new DamlText(label),
                note => note.ToValue(text => new DamlText(text)))));
    }

    [Fact]
    public void ReadRecord_should_decode_a_present_optional_carried_by_an_either_arm()
    {
        var record = DamlLfJsonReader.ReadRecord<OptionalChoiceHolder>(
            """{"choice":{"tag":"Right","value":"gold"}}""");

        var variant = record.GetRequiredField("choice").Should().BeOfType<DamlVariant>().Which;
        variant.Constructor.Should().Be("Right");
        variant.Value.Should().Be(DamlOptional.Some(new DamlText("gold")));
    }

    [Fact]
    public void ReadRecord_should_decode_an_absent_optional_carried_by_an_either_arm()
    {
        var record = DamlLfJsonReader.ReadRecord<OptionalChoiceHolder>(
            """{"choice":{"tag":"Right","value":null}}""");

        var variant = record.GetRequiredField("choice").Should().BeOfType<DamlVariant>().Which;
        variant.Constructor.Should().Be("Right");
        variant.Value.Should().Be(DamlOptional.None);
    }

    public sealed record EitherUnitHolder(
        [property: DamlFieldAttribute("outcome")] Either<DamlUnit, long> Outcome) : IDamlRecord
    {
        public DamlRecord ToRecord() =>
            throw new NotSupportedException("Reader-shape stand-ins in this suite are decode-only.");
    }

    [Fact]
    public void ReadRecord_should_decode_an_either_left_arm_carrying_daml_unit()
    {
        var record = DamlLfJsonReader.ReadRecord<EitherUnitHolder>("""{"outcome":{"tag":"Left","value":{}}}""");

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
        [property: DamlFieldAttribute("result")] Result<string> Outcome) : IDamlRecord
    {
        public DamlRecord ToRecord() =>
            throw new NotSupportedException("Reader-shape stand-ins in this suite are decode-only.");
    }

    [Fact]
    public void ReadRecord_should_still_refuse_a_generic_variant_outside_the_stdlib_types()
    {
        var act = () => DamlLfJsonReader.ReadRecord<ResultHolder>("""{"result":{"tag":"Ok","value":"gold"}}""");

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
        var record = DamlLfJsonReader.ReadRecord<Box<string>>("""{"item":"gold"}""");

        record.GetRequiredField("item").Should().Be(new DamlText("gold"));
    }

    [Fact]
    public void ReadRecord_should_read_a_notnull_type_parameter_slot_as_required_at_a_value_type_instantiation()
    {
        var record = DamlLfJsonReader.ReadRecord<Box<long>>("""{"item":"42"}""");

        record.GetRequiredField("item").Should().Be(new DamlInt64(42L));
    }

    [Fact]
    public void ReadRecord_should_keep_an_annotated_optional_slot_optional_beside_a_notnull_type_parameter()
    {
        var record = DamlLfJsonReader.ReadRecord<AnnotatedBox<string>>("""{"item":"gold","note":null}""");

        record.GetRequiredField("item").Should().Be(new DamlText("gold"));
        record.GetRequiredField("note").Should().Be(DamlOptional.None);
    }

    [Fact]
    public void ReadRecord_should_wrap_the_unconstrained_type_parameter_slot_the_emitter_no_longer_produces()
    {
        var record = DamlLfJsonReader.ReadRecord<UnconstrainedBox<string>>("""{"item":"gold"}""");

        record.GetRequiredField("item").Should().Be(
            DamlOptional.Some(new DamlText("gold")),
            "an unconstrained type parameter reports NullabilityState.Nullable at every reference-type "
            + "instantiation, and inventing that Optional is exactly what the emitted notnull constraint prevents");
    }

    [Fact]
    public void ReadRecord_should_reject_a_null_in_a_notnull_type_parameter_slot_that_an_unconstrained_slot_absorbs()
    {
        var act = () => DamlLfJsonReader.ReadRecord<Box<string>>("""{"item":null}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON String at 'Box`1.item' but found Null");

        DamlLfJsonReader.ReadRecord<UnconstrainedBox<string>>("""{"item":null}""")
            .GetRequiredField("item").Should().Be(
                DamlOptional.None,
                "the very same payload decodes to an absent Optional once the type parameter loses its "
                + "notnull constraint, so the rejection above is caused by the constraint and not by the payload");
    }

    public sealed record WrappedOptionalHolder(
        [property: DamlFieldAttribute("note")] Optional<string> Note) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "note",
            Note.ToValue(value => new DamlText(value))));
    }

    [Fact]
    public void ReadRecord_should_decode_a_wrapped_optional_field_carrying_a_value()
    {
        var record = DamlLfJsonReader.ReadRecord<WrappedOptionalHolder>("""{"note":"deep"}""");

        record.GetRequiredField("note").Should().Be(DamlOptional.Some(new DamlText("deep")));
    }

    [Fact]
    public void ReadRecord_should_decode_a_wrapped_optional_field_carrying_nothing()
    {
        var record = DamlLfJsonReader.ReadRecord<WrappedOptionalHolder>("""{"note":null}""");

        record.GetRequiredField("note").Should().Be(DamlOptional.None);
    }

    [Fact]
    public void ReadRecord_should_round_trip_a_wrapped_optional_field_through_the_generated_shape()
    {
        var carried = DamlLfJsonReader.ReadRecord<WrappedOptionalHolder>("""{"note":"deep"}""");
        var absent = DamlLfJsonReader.ReadRecord<WrappedOptionalHolder>("""{"note":null}""");

        Optional<string>.FromValue(carried.GetRequiredField("note"), v => ((DamlText)v).Value)
            .Should().Be(new Optional<string>.Some("deep"));
        Optional<string>.FromValue(absent.GetRequiredField("note"), v => ((DamlText)v).Value)
            .Should().Be(new Optional<string>.None());
    }

    public sealed record TagSetHolder(
        [property: DamlFieldAttribute("tags")] Set<string> Tags) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "tags",
            Tags.ToRecord(tag => new DamlText(tag))));
    }

    private const string TwoTagSetJson = """{"tags":{"map":[["a",{}],["b",{}]]}}""";

    [Fact]
    public void ReadRecord_should_decode_a_set_field_from_its_wire_record_form()
    {
        var record = DamlLfJsonReader.ReadRecord<TagSetHolder>(TwoTagSetJson);

        record.GetRequiredField("tags").Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("map", DamlGenMap.Create(
                (new DamlText("a"), DamlUnit.Instance),
                (new DamlText("b"), DamlUnit.Instance))));
    }

    [Fact]
    public void ReadRecord_should_decode_an_empty_set_field()
    {
        var record = DamlLfJsonReader.ReadRecord<TagSetHolder>("""{"tags":{"map":[]}}""");

        record.GetRequiredField("tags").Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("map", DamlGenMap.Create()));
    }

    [Fact]
    public void ReadRecord_should_hand_a_decoded_set_field_to_the_stdlib_shape()
    {
        var record = DamlLfJsonReader.ReadRecord<TagSetHolder>(TwoTagSetJson);

        Set<string>.FromRecord((DamlRecord)record.GetRequiredField("tags"), value => ((DamlText)value).Value)
            .Elements.Should().BeEquivalentTo(["a", "b"]);
    }

    [Fact]
    public void ReadRecord_should_reject_a_set_field_missing_its_map()
    {
        var act = () => DamlLfJsonReader.ReadRecord<TagSetHolder>("""{"tags":{}}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Required Daml field 'TagSetHolder.tags.map' is missing from the JSON object");
    }

    [Fact]
    public void ReadRecord_should_reject_a_set_field_encoded_as_a_bare_entry_array()
    {
        var act = () => DamlLfJsonReader.ReadRecord<TagSetHolder>("""{"tags":[["a",{}]]}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON Object at 'TagSetHolder.tags' but found Array");
    }

    [Fact]
    public void ReadRecord_should_reject_a_set_whose_map_is_not_an_entry_array()
    {
        var act = () => DamlLfJsonReader.ReadRecord<TagSetHolder>("""{"tags":{"map":{"a":{}}}}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON Array at 'TagSetHolder.tags.map' but found Object");
    }

    [Fact]
    public void ReadRecord_should_reject_a_set_carrying_the_same_element_twice()
    {
        var act = () => DamlLfJsonReader.ReadRecord<TagSetHolder>("""{"tags":{"map":[["a",{}],["a",{}]]}}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Duplicate key at 'TagSetHolder.tags.map[1]' in a Daml Set");
    }

    [Fact]
    public void ReadRecord_should_reject_a_set_whose_element_carries_a_value_other_than_unit()
    {
        var act = () => DamlLfJsonReader.ReadRecord<TagSetHolder>("""{"tags":{"map":[["a","b"]]}}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON Object at 'TagSetHolder.tags.map[0].value' but found String");
    }

    [Fact]
    public void ReadRecord_should_decode_a_set_field_into_the_record_the_stdlib_shape_writes()
    {
        var decoded = DamlLfJsonReader.ReadRecord<TagSetHolder>(TwoTagSetJson)
            .GetRequiredField("tags").Should().BeOfType<DamlRecord>().Which;

        decoded.RecordId.Should().BeNull();
        decoded.Should().Be(new Set<string>(["a", "b"]).ToRecord(tag => new DamlText(tag)));
    }

    public sealed record ProfileSetHolder(
        [property: DamlFieldAttribute("profiles")] Set<Profile> Profiles) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "profiles",
            Profiles.ToRecord(profile => profile.ToRecord())));
    }

    [Fact]
    public void ReadRecord_should_decode_a_generated_record_carried_by_a_set_element()
    {
        var record = DamlLfJsonReader.ReadRecord<ProfileSetHolder>(
            """{"profiles":{"map":[[{"nickname":"nick","level":"3"},{}]]}}""");

        var entry = record.GetRequiredField("profiles").Should().BeOfType<DamlRecord>()
            .Which.GetRequiredField("map").Should().BeOfType<DamlGenMap>()
            .Which.Entries.Should().ContainSingle().Which;
        entry.Key.Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("nickname", new DamlText("nick")),
            new DamlField("level", new DamlInt64(3L)));
        entry.Value.Should().Be(DamlUnit.Instance);
    }

    public sealed record HistoryHolder(
        [property: DamlFieldAttribute("history")] NonEmpty<string> History) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "history",
            History.ToRecord(entry => new DamlText(entry))));
    }

    private const string HeadAndTailHistoryJson = """{"history":{"hd":"a","tl":["b","c"]}}""";

    [Fact]
    public void ReadRecord_should_decode_a_non_empty_field_from_its_wire_record_form()
    {
        var record = DamlLfJsonReader.ReadRecord<HistoryHolder>(HeadAndTailHistoryJson);

        record.GetRequiredField("history").Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("hd", new DamlText("a")),
            new DamlField("tl", new DamlList([new DamlText("b"), new DamlText("c")])));
    }

    [Fact]
    public void ReadRecord_should_decode_a_non_empty_field_carrying_only_a_head()
    {
        var record = DamlLfJsonReader.ReadRecord<HistoryHolder>("""{"history":{"hd":"a","tl":[]}}""");

        record.GetRequiredField("history").Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("hd", new DamlText("a")),
            new DamlField("tl", new DamlList([])));
    }

    [Fact]
    public void ReadRecord_should_hand_a_decoded_non_empty_field_to_the_stdlib_shape()
    {
        var record = DamlLfJsonReader.ReadRecord<HistoryHolder>(HeadAndTailHistoryJson);

        NonEmpty<string>.FromRecord((DamlRecord)record.GetRequiredField("history"), value => ((DamlText)value).Value)
            .All.Should().Equal("a", "b", "c");
    }

    [Fact]
    public void ReadRecord_should_decode_a_non_empty_field_into_the_record_the_stdlib_shape_writes()
    {
        var decoded = DamlLfJsonReader.ReadRecord<HistoryHolder>(HeadAndTailHistoryJson)
            .GetRequiredField("history").Should().BeOfType<DamlRecord>().Which;

        decoded.RecordId.Should().BeNull();
        decoded.Should().Be(new NonEmpty<string>("a", ["b", "c"]).ToRecord(entry => new DamlText(entry)));
    }

    [Fact]
    public void ReadRecord_should_reject_a_non_empty_field_missing_its_head()
    {
        var act = () => DamlLfJsonReader.ReadRecord<HistoryHolder>("""{"history":{"tl":["b"]}}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Required Daml field 'HistoryHolder.history.hd' is missing from the JSON object");
    }

    [Fact]
    public void ReadRecord_should_reject_a_non_empty_field_missing_its_tail()
    {
        var act = () => DamlLfJsonReader.ReadRecord<HistoryHolder>("""{"history":{"hd":"a"}}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Required Daml field 'HistoryHolder.history.tl' is missing from the JSON object");
    }

    [Fact]
    public void ReadRecord_should_reject_a_non_empty_field_whose_tail_is_not_an_array()
    {
        var act = () => DamlLfJsonReader.ReadRecord<HistoryHolder>("""{"history":{"hd":"a","tl":"b"}}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON Array at 'HistoryHolder.history.tl' but found String");
    }

    public sealed record PairHistoryHolder(
        [property: DamlFieldAttribute("history")] NonEmpty<Tuple2<long, string>> History) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "history",
            History.ToRecord(pair => pair.ToRecord(count => new DamlInt64(count), label => new DamlText(label)))));
    }

    [Fact]
    public void ReadRecord_should_decode_tuple2_elements_carried_by_a_non_empty_field()
    {
        var record = DamlLfJsonReader.ReadRecord<PairHistoryHolder>(
            """{"history":{"hd":{"_1":"1","_2":"a"},"tl":[{"_1":"2","_2":"b"}]}}""");

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
        [property: DamlFieldAttribute("tally")] Map<string, long> Tally) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "tally",
            Tally.ToRecord(label => new DamlText(label), count => new DamlInt64(count))));
    }

    private const string TwoEntryTallyJson = """{"tally":{"map":[["alice","1"],["bob","2"]]}}""";

    [Fact]
    public void ReadRecord_should_decode_a_stdlib_map_field_from_its_wire_record_form()
    {
        var record = DamlLfJsonReader.ReadRecord<TallyHolder>(TwoEntryTallyJson);

        record.GetRequiredField("tally").Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("map", DamlGenMap.Create(
                (new DamlText("alice"), new DamlInt64(1L)),
                (new DamlText("bob"), new DamlInt64(2L)))));
    }

    [Fact]
    public void ReadRecord_should_decode_an_empty_stdlib_map_field()
    {
        var record = DamlLfJsonReader.ReadRecord<TallyHolder>("""{"tally":{"map":[]}}""");

        record.GetRequiredField("tally").Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("map", DamlGenMap.Create()));
    }

    [Fact]
    public void ReadRecord_should_hand_a_decoded_stdlib_map_field_to_the_stdlib_shape()
    {
        var record = DamlLfJsonReader.ReadRecord<TallyHolder>(TwoEntryTallyJson);

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
        var decoded = DamlLfJsonReader.ReadRecord<TallyHolder>(TwoEntryTallyJson)
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
        var act = () => DamlLfJsonReader.ReadRecord<TallyHolder>("""{"tally":{"map":[["alice","1"],["alice","2"]]}}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Duplicate key at 'TallyHolder.tally.map[1]' in a Daml Map");
    }

    [Fact]
    public void ReadRecord_should_reject_a_stdlib_map_field_missing_its_map()
    {
        var act = () => DamlLfJsonReader.ReadRecord<TallyHolder>("""{"tally":{}}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Required Daml field 'TallyHolder.tally.map' is missing from the JSON object");
    }

    [Fact]
    public void ReadRecord_should_reject_a_stdlib_map_field_encoded_as_a_text_map_object()
    {
        var act = () => DamlLfJsonReader.ReadRecord<TallyHolder>("""{"tally":{"map":{"alice":"1"}}}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON Array at 'TallyHolder.tally.map' but found Object");
    }

    public sealed record ProfileTallyMapHolder(
        [property: DamlFieldAttribute("tally")] Map<Profile, Optional<string>> Tally) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "tally",
            Tally.ToRecord(
                profile => profile.ToRecord(),
                note => note.ToValue(text => new DamlText(text)))));
    }

    [Fact]
    public void ReadRecord_should_decode_the_key_and_value_types_of_a_stdlib_map_field()
    {
        var record = DamlLfJsonReader.ReadRecord<ProfileTallyMapHolder>(
            """{"tally":{"map":[[{"nickname":"nick","level":"3"},"gold"],[{"nickname":"nack","level":"4"},null]]}}""");

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
        [property: DamlFieldAttribute("primitive")] IReadOnlyDictionary<string, long> Primitive) : IDamlRecord
    {
        public DamlRecord ToRecord() =>
            throw new NotSupportedException("Reader-shape stand-ins in this suite are decode-only.");
    }

    [Fact]
    public void ReadRecord_should_keep_the_genmap_primitive_out_of_the_stdlib_map_record_wrapper()
    {
        var record = DamlLfJsonReader.ReadRecord<BothMapShapesHolder>(
            """{"wrapped":{"map":[["alice","1"]]},"primitive":{"alice":"1"}}""");

        record.GetRequiredField("wrapped").Should().BeOfType<DamlRecord>()
            .Which.GetRequiredField("map").Should().Be(
                DamlGenMap.Create((new DamlText("alice"), new DamlInt64(1L))));
        record.GetRequiredField("primitive").Should().Be(
            DamlTextMap.Create(("alice", new DamlInt64(1L))));
    }

    [Fact]
    public void ReadRecord_should_decode_the_genmap_primitive_in_its_array_form_beside_a_stdlib_map_field()
    {
        var record = DamlLfJsonReader.ReadRecord<BothMapShapesHolder>(
            """{"wrapped":{"map":[["alice","1"]]},"primitive":[["alice","1"]]}""");

        record.GetRequiredField("wrapped").Should().BeOfType<DamlRecord>()
            .Which.GetRequiredField("map").Should().Be(
                DamlGenMap.Create((new DamlText("alice"), new DamlInt64(1L))));
        record.GetRequiredField("primitive").Should().Be(
            DamlGenMap.Create((new DamlText("alice"), new DamlInt64(1L))));
    }

    public sealed record TagsByOwnerHolder(
        [property: DamlFieldAttribute("tags")] Map<string, Set<string>> Tags) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "tags",
            Tags.ToRecord(owner => new DamlText(owner), tags => tags.ToRecord(tag => new DamlText(tag)))));
    }

    [Fact]
    public void ReadRecord_should_decode_a_set_carried_by_a_stdlib_map_value()
    {
        var record = DamlLfJsonReader.ReadRecord<TagsByOwnerHolder>(
            """{"tags":{"map":[["alice",{"map":[["gold",{}],["silver",{}]]}],["bob",{"map":[]}]]}}""");

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
    }

    private sealed record PinnedView : IDamlRecord<PinnedView>
    {
        public DamlRecord ToRecord() => DamlRecord.Create();
        public static PinnedView FromRecord(DamlRecord record) => new();
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
        [property: DamlFieldAttribute("contractId")] ContractId<PinnedTemplate> ContractId) : IDamlRecord
    {
        public DamlRecord ToRecord() => throw new NotSupportedException("decode-only shape");
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
        var act = () => DamlLfJsonReader.ReadRecord<ContractIdHolder>("""{"contractId":null}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON String at 'ContractIdHolder.contractId' but found Null");
    }
}
