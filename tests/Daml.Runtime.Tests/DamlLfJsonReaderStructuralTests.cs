// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Runtime.Tests;

public class DamlLfJsonReaderStructuralTests
{
    public sealed record NoteHolder([property: DamlFieldAttribute("note")] string? Note) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "note",
            Note is null ? DamlOptional.None : DamlOptional.Some(new DamlText(Note))));
    }

    [Fact]
    public void ReadRecord_should_decode_a_present_optional_field_from_its_bare_wire_value()
    {
        var record = DamlLfJsonReader.ReadRecord<NoteHolder>("""{"note":"present"}""");

        record.GetRequiredField("note").Should().BeOfType<DamlOptional>()
            .Which.Value.Should().BeOfType<DamlText>().Which.Value.Should().Be("present");
    }

    [Fact]
    public void ReadRecord_should_decode_an_absent_optional_field_from_json_null()
    {
        var record = DamlLfJsonReader.ReadRecord<NoteHolder>("""{"note":null}""");

        record.GetRequiredField("note").Should().BeOfType<DamlOptional>()
            .Which.Should().Be(DamlOptional.None);
    }

    public sealed record LevelHolder([property: DamlFieldAttribute("level")] long? Level) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "level",
            Level is null ? DamlOptional.None : DamlOptional.Some(new DamlInt64(Level.Value))));
    }

    [Fact]
    public void ReadRecord_should_decode_a_present_optional_value_type_field()
    {
        var record = DamlLfJsonReader.ReadRecord<LevelHolder>("""{"level":"3"}""");

        record.GetRequiredField("level").Should().BeOfType<DamlOptional>()
            .Which.Value.Should().BeOfType<DamlInt64>().Which.Value.Should().Be(3L);
    }

    [Fact]
    public void ReadRecord_should_decode_an_absent_optional_value_type_field()
    {
        var record = DamlLfJsonReader.ReadRecord<LevelHolder>("""{"level":null}""");

        record.GetRequiredField("level").Should().BeOfType<DamlOptional>()
            .Which.Should().Be(DamlOptional.None);
    }

    public sealed record NoteListHolder([property: DamlFieldAttribute("notes")] IReadOnlyList<string?> Notes) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "notes",
            new DamlList(Notes
                .Select(note => (DamlValue)(note is null ? DamlOptional.None : DamlOptional.Some(new DamlText(note))))
                .ToList())));
    }

    [Fact]
    public void ReadRecord_should_decode_optionals_nested_inside_a_list()
    {
        var record = DamlLfJsonReader.ReadRecord<NoteListHolder>("""{"notes":["present",null]}""");

        record.GetRequiredField("notes").Should().BeOfType<DamlList>()
            .Which.Values.Should().Equal(
                DamlOptional.Some(new DamlText("present")),
                DamlOptional.None);
    }

    public sealed record AttributesHolder(
        [property: DamlFieldAttribute("attributes")] IReadOnlyDictionary<string, string> Attributes) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "attributes",
            new DamlTextMap(Attributes.ToDictionary(entry => entry.Key, entry => (DamlValue)new DamlText(entry.Value)))));
    }

    [Fact]
    public void ReadRecord_should_decode_a_text_map_field_from_its_wire_object_form()
    {
        var record = DamlLfJsonReader.ReadRecord<AttributesHolder>("""{"attributes":{"a":"1"}}""");

        record.GetRequiredField("attributes").Should().BeOfType<DamlTextMap>()
            .Which.Should().Be(DamlTextMap.Create(("a", new DamlText("1"))));
    }

    [Fact]
    public void ReadRecord_should_decode_an_empty_text_map_field()
    {
        var record = DamlLfJsonReader.ReadRecord<AttributesHolder>("""{"attributes":{}}""");

        record.GetRequiredField("attributes").Should().BeOfType<DamlTextMap>()
            .Which.Values.Should().BeEmpty();
    }

    [Fact]
    public void ReadRecord_should_reject_duplicate_text_map_keys_in_a_caller_parsed_document()
    {
        using var document = JsonDocument.Parse("""{"attributes":{"a":"1","a":"2"}}""");

        var act = () => DamlLfJsonReader.ReadRecord<AttributesHolder>(document.RootElement);

        act.Should().Throw<JsonException>()
            .WithMessage("Duplicate key 'a' at 'AttributesHolder.attributes' in a Daml TextMap");
    }

    [Fact]
    public void ReadRecord_should_bracket_the_map_key_when_reporting_an_error_inside_a_text_map_value()
    {
        var act = () => DamlLfJsonReader.ReadRecord<AttributesHolder>("""{"attributes":{"a.b":5}}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON String at 'AttributesHolder.attributes['a.b']' but found Number");
    }

    [Fact]
    public void ReadRecord_should_escape_a_quote_inside_a_bracketed_map_key()
    {
        var act = () => DamlLfJsonReader.ReadRecord<AttributesHolder>("""{"attributes":{"o'brien":5}}""");

        act.Should().Throw<JsonException>()
            .WithMessage(@"Expected JSON String at 'AttributesHolder.attributes['o\'brien']' but found Number");
    }

    [Fact]
    public void ReadRecord_should_escape_a_backslash_inside_a_bracketed_map_key()
    {
        var act = () => DamlLfJsonReader.ReadRecord<AttributesHolder>("""{"attributes":{"a\\b":5}}""");

        act.Should().Throw<JsonException>()
            .WithMessage(@"Expected JSON String at 'AttributesHolder.attributes['a\\b']' but found Number");
    }

    [Fact]
    public void ReadRecord_should_escape_a_backslash_that_precedes_a_quote_inside_a_bracketed_map_key()
    {
        var act = () => DamlLfJsonReader.ReadRecord<AttributesHolder>("""{"attributes":{"a\\'b":5}}""");

        act.Should().Throw<JsonException>()
            .WithMessage(@"Expected JSON String at 'AttributesHolder.attributes['a\\\'b']' but found Number");
    }

    [Fact]
    public void ReadRecord_should_elide_an_oversized_map_key_in_a_bracketed_path()
    {
        var oversizedKey = new string('k', 70);

        var act = () => DamlLfJsonReader.ReadRecord<AttributesHolder>(
            $$$"""{"attributes":{"{{{oversizedKey}}}":5}}""");

        act.Should().Throw<JsonException>()
            .WithMessage(
                $"Expected JSON String at 'AttributesHolder.attributes['{new string('k', 64)}…']' but found Number");
    }

    private const string WireParty =
        "wiree3ed3454::1220141a01c00ef277c31ca4eb0e82ee3de7f790eb25f3787f8195f117af8668bf3b";

    public sealed record GenMapHolder(
        [property: DamlFieldAttribute("genMap")] IReadOnlyDictionary<Party, long> GenMap) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "genMap",
            new DamlGenMap(GenMap
                .Select(entry => ((DamlValue)entry.Key.ToDamlValue(), (DamlValue)new DamlInt64(entry.Value)))
                .ToList())));
    }

    [Fact]
    public void ReadRecord_should_decode_a_gen_map_field_from_its_wire_pair_array_form()
    {
        var record = DamlLfJsonReader.ReadRecord<GenMapHolder>($$"""{"genMap":[["{{WireParty}}","7"]]}""");

        record.GetRequiredField("genMap").Should().BeOfType<DamlGenMap>()
            .Which.Should().Be(DamlGenMap.Create((new DamlParty(WireParty), new DamlInt64(7))));
    }

    [Fact]
    public void ReadRecord_should_decode_an_empty_gen_map_field()
    {
        var record = DamlLfJsonReader.ReadRecord<GenMapHolder>("""{"genMap":[]}""");

        record.GetRequiredField("genMap").Should().BeOfType<DamlGenMap>()
            .Which.Entries.Should().BeEmpty();
    }

    [Fact]
    public void ReadRecord_should_reject_a_gen_map_entry_that_is_not_a_key_value_pair()
    {
        var act = () => DamlLfJsonReader.ReadRecord<GenMapHolder>($$"""{"genMap":[["{{WireParty}}"]]}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Expected a two-element key/value pair at 'GenMapHolder.genMap[0]' but found 1 element(s)");
    }

    [Fact]
    public void ReadRecord_should_reject_a_gen_map_with_a_duplicate_key()
    {
        var act = () => DamlLfJsonReader.ReadRecord<GenMapHolder>(
            $$"""{"genMap":[["{{WireParty}}","1"],["{{WireParty}}","2"]]}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Duplicate key at 'GenMapHolder.genMap[1]' in a Daml GenMap");
    }

    [Fact]
    public void ReadRecord_should_reject_a_gen_map_entry_that_is_not_an_array()
    {
        var act = () => DamlLfJsonReader.ReadRecord<GenMapHolder>("""{"genMap":["nope"]}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON Array at 'GenMapHolder.genMap[0]' but found String");
    }

    [Fact]
    public void ReadRecord_should_reject_a_gen_map_field_encoded_as_a_json_object()
    {
        var act = () => DamlLfJsonReader.ReadRecord<GenMapHolder>("""{"genMap":{"a":"1"}}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON array of entry pairs (GenMap) at 'GenMapHolder.genMap' but found Object");
    }

    [Fact]
    public void ReadRecord_should_name_both_map_wire_forms_when_rejecting_a_string_keyed_map()
    {
        var act = () => DamlLfJsonReader.ReadRecord<AttributesHolder>("""{"attributes":"nope"}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON object (TextMap) or array of entry pairs (GenMap) "
                + "at 'AttributesHolder.attributes' but found String");
    }

    public sealed record UnitHolder([property: DamlFieldAttribute("unitField")] DamlUnit UnitField) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("unitField", UnitField));
    }

    [Fact]
    public void ReadRecord_should_decode_a_unit_field_from_its_wire_empty_object_form()
    {
        var record = DamlLfJsonReader.ReadRecord<UnitHolder>("""{"unitField":{}}""");

        record.GetRequiredField("unitField").Should().BeOfType<DamlUnit>()
            .Which.Should().BeSameAs(DamlUnit.Instance);
    }

    [Fact]
    public void ReadRecord_should_reject_a_unit_field_encoded_as_a_json_string()
    {
        var act = () => DamlLfJsonReader.ReadRecord<UnitHolder>("""{"unitField":"nope"}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON Object at 'UnitHolder.unitField' but found String");
    }

    public enum Suit
    {
        Clubs,
        Diamonds,
        Hearts,
        Spades
    }

    public sealed record SuitHolder([property: DamlFieldAttribute("suit")] Suit Suit) : IDamlRecord
    {
        public DamlRecord ToRecord() =>
            DamlRecord.Create(DamlField.Create("suit", DamlEnum.Create(Suit.ToString())));
    }

    [Fact]
    public void ReadRecord_should_decode_an_enum_field_from_its_bare_wire_string()
    {
        var record = DamlLfJsonReader.ReadRecord<SuitHolder>("""{"suit":"Hearts"}""");

        record.GetRequiredField("suit").Should().BeOfType<DamlEnum>()
            .Which.Should().Be(DamlEnum.Create("Hearts"));
    }

    [Fact]
    public void ReadRecord_should_reject_an_unknown_enum_constructor()
    {
        var act = () => DamlLfJsonReader.ReadRecord<SuitHolder>("""{"suit":"Wands"}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Unknown Daml enum constructor 'Wands' at 'SuitHolder.suit'; "
                + "expected one of Clubs, Diamonds, Hearts, Spades");
    }

    [Fact]
    public void ReadRecord_should_reject_an_enum_field_encoded_as_a_json_number()
    {
        var act = () => DamlLfJsonReader.ReadRecord<SuitHolder>("""{"suit":2}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON String at 'SuitHolder.suit' but found Number");
    }

    public enum Zigzag
    {
        Zig,
        Alpha
    }

    public sealed record ZigzagHolder([property: DamlFieldAttribute("zigzag")] Zigzag Zigzag) : IDamlRecord
    {
        public DamlRecord ToRecord() =>
            DamlRecord.Create(DamlField.Create("zigzag", DamlEnum.Create(Zigzag.ToString())));
    }

    [Fact]
    public void ReadRecord_should_sort_the_expected_set_when_rejecting_an_unknown_enum_constructor()
    {
        var act = () => DamlLfJsonReader.ReadRecord<ZigzagHolder>("""{"zigzag":"Zag"}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Unknown Daml enum constructor 'Zag' at 'ZigzagHolder.zigzag'; "
                + "expected one of Alpha, Zig");
    }

    public sealed record OutcomeWin(
        [property: DamlFieldAttribute("prize")] decimal Prize,
        [property: DamlFieldAttribute("tier")] string Tier) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("prize", new DamlNumeric(Prize)),
            DamlField.Create("tier", new DamlText(Tier)));
    }

    public abstract record Outcome : IDamlVariant
    {
        public abstract string Tag { get; }

        public abstract DamlVariant ToVariant();

        public sealed record Win(OutcomeWin Value) : Outcome
        {
            public override string Tag => "Win";

            public override DamlVariant ToVariant() => DamlVariant.Create("Win", Value.ToRecord());
        }

        public sealed record Pending : Outcome
        {
            public override string Tag => "Pending";

            public override DamlVariant ToVariant() => DamlVariant.Create("Pending", DamlUnit.Instance);
        }
    }

    public sealed record OutcomeHolder([property: DamlFieldAttribute("outcome")] Outcome Outcome) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("outcome", Outcome.ToVariant()));
    }

    [Fact]
    public void ReadRecord_should_decode_a_tagged_variant_arm_with_its_record_payload()
    {
        var record = DamlLfJsonReader.ReadRecord<OutcomeHolder>(
            """{"outcome":{"tag":"Win","value":{"prize":"1.25","tier":"gold"}}}""");

        var variant = record.GetRequiredField("outcome").Should().BeOfType<DamlVariant>().Which;
        variant.Constructor.Should().Be("Win");
        variant.Value.Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("prize", new DamlNumeric(1.25m)),
            new DamlField("tier", new DamlText("gold")));
    }

    [Fact]
    public void ReadRecord_should_decode_a_nullary_variant_arm_from_its_empty_object_value()
    {
        var record = DamlLfJsonReader.ReadRecord<OutcomeHolder>("""{"outcome":{"tag":"Pending","value":{}}}""");

        var variant = record.GetRequiredField("outcome").Should().BeOfType<DamlVariant>().Which;
        variant.Constructor.Should().Be("Pending");
        variant.Value.Should().BeSameAs(DamlUnit.Instance);
    }

    [Fact]
    public void ReadRecord_should_reject_an_unknown_variant_constructor()
    {
        var act = () => DamlLfJsonReader.ReadRecord<OutcomeHolder>("""{"outcome":{"tag":"Draw","value":{}}}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Unknown Daml variant constructor 'Draw' at 'OutcomeHolder.outcome'; "
                + "expected one of Pending, Win");
    }

    [Fact]
    public void ReadRecord_should_reject_a_variant_without_a_tag()
    {
        var act = () => DamlLfJsonReader.ReadRecord<OutcomeHolder>("""{"outcome":{"value":{}}}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Required Daml variant field 'OutcomeHolder.outcome.tag' is missing from the JSON object");
    }

    [Fact]
    public void ReadRecord_should_reject_a_variant_tag_that_is_not_a_string()
    {
        var act = () => DamlLfJsonReader.ReadRecord<OutcomeHolder>("""{"outcome":{"tag":5,"value":{}}}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON String at 'OutcomeHolder.outcome.tag' but found Number");
    }

    [Fact]
    public void ReadRecord_should_reject_a_nullary_variant_value_that_is_not_an_object()
    {
        var act = () => DamlLfJsonReader.ReadRecord<OutcomeHolder>("""{"outcome":{"tag":"Pending","value":"nope"}}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON Object at 'OutcomeHolder.outcome.value' but found String");
    }

    [Fact]
    public void ReadRecord_should_reject_a_variant_missing_its_value_field()
    {
        var act = () => DamlLfJsonReader.ReadRecord<OutcomeHolder>("""{"outcome":{"tag":"Pending"}}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Required Daml variant field 'OutcomeHolder.outcome.value' is missing from the JSON object");
    }

    [Fact]
    public void ReadRecord_should_reject_a_variant_field_encoded_as_a_bare_string()
    {
        var act = () => DamlLfJsonReader.ReadRecord<OutcomeHolder>("""{"outcome":"Pending"}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON Object at 'OutcomeHolder.outcome' but found String");
    }

    public sealed record Profile(
        [property: DamlFieldAttribute("nickname")] string Nickname,
        [property: DamlFieldAttribute("level")] long Level) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("nickname", new DamlText(Nickname)),
            DamlField.Create("level", new DamlInt64(Level)));
    }

    public sealed record ProfileHolder([property: DamlFieldAttribute("profile")] Profile Profile) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("profile", Profile.ToRecord()));
    }

    [Fact]
    public void ReadRecord_should_decode_a_nested_record_field_keyed_by_field_name()
    {
        var record = DamlLfJsonReader.ReadRecord<ProfileHolder>("""{"profile":{"nickname":"nick","level":"3"}}""");

        record.GetRequiredField("profile").Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("nickname", new DamlText("nick")),
            new DamlField("level", new DamlInt64(3L)));
    }

    public sealed record TagsHolder([property: DamlFieldAttribute("tags")] IReadOnlyList<string> Tags) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "tags",
            new DamlList(Tags.Select(tag => (DamlValue)new DamlText(tag)).ToList())));
    }

    [Fact]
    public void ReadRecord_should_decode_a_list_field_from_its_wire_array_form()
    {
        var record = DamlLfJsonReader.ReadRecord<TagsHolder>("""{"tags":["x","y"]}""");

        record.GetRequiredField("tags").Should().BeOfType<DamlList>()
            .Which.Values.Should().Equal(new DamlText("x"), new DamlText("y"));
    }

    [Fact]
    public void ReadRecord_should_decode_an_empty_list_field()
    {
        var record = DamlLfJsonReader.ReadRecord<TagsHolder>("""{"tags":[]}""");

        record.GetRequiredField("tags").Should().BeOfType<DamlList>().Which.Values.Should().BeEmpty();
    }

    public sealed record ProfileTallyHolder(
        [property: DamlFieldAttribute("tally")] IReadOnlyDictionary<Profile, long> Tally) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "tally",
            new DamlGenMap(Tally
                .Select(entry => ((DamlValue)entry.Key.ToRecord(), (DamlValue)new DamlInt64(entry.Value)))
                .ToList())));
    }

    [Fact]
    public void ReadRecord_should_decode_a_gen_map_keyed_by_a_record()
    {
        var record = DamlLfJsonReader.ReadRecord<ProfileTallyHolder>(
            """{"tally":[[{"nickname":"nick","level":"3"},"7"]]}""");

        var entry = record.GetRequiredField("tally").Should().BeOfType<DamlGenMap>()
            .Which.Entries.Should().ContainSingle().Which;
        entry.Key.Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("nickname", new DamlText("nick")),
            new DamlField("level", new DamlInt64(3L)));
        entry.Value.Should().Be(new DamlInt64(7L));
    }

    public abstract record Reading : IDamlVariant
    {
        public abstract string Tag { get; }

        public abstract DamlVariant ToVariant();

        public sealed record Measured(decimal Value) : Reading
        {
            public override string Tag => "Measured";

            public override DamlVariant ToVariant() => DamlVariant.Create("Measured", new DamlNumeric(Value));
        }
    }

    public sealed record ReadingHolder([property: DamlFieldAttribute("reading")] Reading Reading) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("reading", Reading.ToVariant()));
    }

    [Fact]
    public void ReadRecord_should_decode_a_variant_arm_carrying_a_scalar_payload()
    {
        var record = DamlLfJsonReader.ReadRecord<ReadingHolder>("""{"reading":{"tag":"Measured","value":"1.25"}}""");

        var variant = record.GetRequiredField("reading").Should().BeOfType<DamlVariant>().Which;
        variant.Constructor.Should().Be("Measured");
        variant.Value.Should().BeOfType<DamlNumeric>().Which.Value.Should().Be(1.25m);
    }

    public abstract record Shape : IDamlVariant
    {
        public abstract string Tag { get; }

        public abstract DamlVariant ToVariant();

        public sealed record Shape_(string Value) : Shape
        {
            public override string Tag => "Shape";

            public override DamlVariant ToVariant() => DamlVariant.Create("Shape", new DamlText(Value));
        }

        public sealed record Blank : Shape
        {
            public override string Tag => "Blank";

            public override DamlVariant ToVariant() => DamlVariant.Create("Blank", DamlUnit.Instance);
        }
    }

    public sealed record ShapeHolder([property: DamlFieldAttribute("shape")] Shape Shape) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("shape", Shape.ToVariant()));
    }

    [Fact]
    public void ReadRecord_should_decode_a_variant_arm_whose_csharp_name_was_disambiguated_from_its_wire_tag()
    {
        var record = DamlLfJsonReader.ReadRecord<ShapeHolder>("""{"shape":{"tag":"Shape","value":"round"}}""");

        var variant = record.GetRequiredField("shape").Should().BeOfType<DamlVariant>().Which;
        variant.Constructor.Should().Be("Shape");
        variant.Value.Should().BeOfType<DamlText>().Which.Value.Should().Be("round");
    }

    [Fact]
    public void ReadRecord_should_list_wire_tags_rather_than_csharp_names_for_an_unknown_variant_constructor()
    {
        var act = () => DamlLfJsonReader.ReadRecord<ShapeHolder>("""{"shape":{"tag":"Round","value":{}}}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Unknown Daml variant constructor 'Round' at 'ShapeHolder.shape'; "
                + "expected one of Blank, Shape");
    }

    public abstract record Armless : IDamlVariant
    {
        public abstract DamlVariant ToVariant();
    }

    public sealed record ArmlessHolder([property: DamlFieldAttribute("armless")] Armless Armless) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("armless", Armless.ToVariant()));
    }

    [Fact]
    public void ReadRecord_should_refuse_a_variant_type_that_declares_no_arms()
    {
        var act = () => DamlLfJsonReader.ReadRecord<ArmlessHolder>("""{"armless":{"tag":"Whatever","value":{}}}""");

        act.Should().Throw<NotSupportedException>()
            .WithMessage($"Type '{typeof(Armless)}' at 'ArmlessHolder.armless' declares no variant arms; "
                + "pass a generated variant whose constructors are nested types carrying a Tag property.");
    }

    public abstract record Untagged : IDamlVariant
    {
        public abstract DamlVariant ToVariant();

        public sealed record Only : Untagged
        {
            public override DamlVariant ToVariant() => DamlVariant.Create("Only", DamlUnit.Instance);
        }
    }

    public sealed record UntaggedHolder([property: DamlFieldAttribute("untagged")] Untagged Untagged) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("untagged", Untagged.ToVariant()));
    }

    [Fact]
    public void ReadRecord_should_refuse_a_variant_arm_that_carries_no_wire_tag()
    {
        var act = () => DamlLfJsonReader.ReadRecord<UntaggedHolder>("""{"untagged":{"tag":"Only","value":{}}}""");

        act.Should().Throw<NotSupportedException>()
            .WithMessage($"Variant arm '{typeof(Untagged.Only)}' at 'UntaggedHolder.untagged' exposes no readable "
                + "Tag property, so its wire constructor cannot be determined; pass a generated variant.");
    }

    public abstract record Cursed : IDamlVariant
    {
        public abstract DamlVariant ToVariant();

        public sealed record Broken : Cursed
        {
            public string Tag => throw new InvalidOperationException("this fixture's tag getter always throws");

            public override DamlVariant ToVariant() => DamlVariant.Create("Broken", DamlUnit.Instance);
        }
    }

    public sealed record CursedHolder([property: DamlFieldAttribute("cursed")] Cursed Cursed) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("cursed", Cursed.ToVariant()));
    }

    [Fact]
    public void ReadRecord_should_refuse_a_variant_arm_whose_tag_getter_throws()
    {
        var act = () => DamlLfJsonReader.ReadRecord<CursedHolder>("""{"cursed":{"tag":"Broken","value":{}}}""");

        act.Should().Throw<NotSupportedException>()
            .WithMessage($"Variant arm '{typeof(Cursed.Broken)}' at 'CursedHolder.cursed' exposes no readable "
                + "Tag property, so its wire constructor cannot be determined; pass a generated variant.");
    }

    public sealed record DirectionHolder([property: DamlFieldAttribute("direction")] Direction Direction) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("direction", Direction.ToDamlEnum()));
    }

    [Fact]
    public void ReadRecord_should_decode_an_enum_constructor_whose_wire_name_differs_from_its_csharp_member()
    {
        var record = DamlLfJsonReader.ReadRecord<DirectionHolder>("""{"direction":"U$u0020Turn"}""");

        record.GetRequiredField("direction").Should().BeOfType<DamlEnum>()
            .Which.Should().Be(DamlEnum.Create("U$u0020Turn"));
    }

    [Fact]
    public void ReadRecord_should_decode_an_enum_constructor_whose_wire_name_survives_sanitization()
    {
        var record = DamlLfJsonReader.ReadRecord<DirectionHolder>("""{"direction":"Forward"}""");

        record.GetRequiredField("direction").Should().BeOfType<DamlEnum>()
            .Which.Should().Be(DamlEnum.Create("Forward"));
    }

    [Fact]
    public void ReadRecord_should_list_wire_constructors_rather_than_csharp_members_for_an_unknown_enum_constructor()
    {
        var act = () => DamlLfJsonReader.ReadRecord<DirectionHolder>("""{"direction":"Sideways"}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Unknown Daml enum constructor 'Sideways' at 'DirectionHolder.direction'; "
                + "expected one of Forward, U$u0020Turn");
    }

    public sealed record CadenceHolder([property: DamlFieldAttribute("cadence")] Cadence Cadence) : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("cadence", Cadence.ToDamlEnum()));
    }

    [Fact]
    public void ReadRecord_should_refuse_an_enum_whose_companion_cannot_name_every_wire_constructor()
    {
        var act = () => DamlLfJsonReader.ReadRecord<CadenceHolder>("""{"cadence":"Steady"}""");

        act.Should().Throw<NotSupportedException>()
            .WithMessage($"Enum '{typeof(Cadence)}' at 'CadenceHolder.cadence' has a companion whose ToDamlEnum "
                + "fails for member 'Broken', so its wire constructors cannot be determined; "
                + "pass a generated Daml enum.");
    }
}

public enum Direction
{
    Forward,
    U_u0020Turn
}

public static class DirectionExtensions
{
    public static DamlEnum ToDamlEnum(this Direction value) =>
        value switch
        {
            Direction.Forward => DamlEnum.Create("Forward"),
            Direction.U_u0020Turn => DamlEnum.Create("U$u0020Turn"),
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
        };
}

public enum Cadence
{
    Steady,
    Broken
}

public static class CadenceExtensions
{
    public static DamlEnum ToDamlEnum(this Cadence value) =>
        value switch
        {
            Cadence.Steady => DamlEnum.Create("Steady"),
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
        };
}
