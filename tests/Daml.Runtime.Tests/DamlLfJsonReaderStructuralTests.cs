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
    public sealed record NoteHolder([property: DamlFieldAttribute("note")] string? Note) : IDamlRecord<NoteHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "note",
            Note is null ? DamlOptional.None : DamlOptional.Some(new DamlText(Note))));

        public static NoteHolder FromRecord(DamlRecord record) =>
            new(record.GetRequiredField("note").As<DamlOptional>().GetValueOrDefault<DamlText>()?.Value);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var noteJson = DamlLfJsonDecoders.RequireField(json, context, "note");
            var noteContext = context.Field("note");
            var note = DamlLfJsonDecoders.ReadOptional(noteJson, noteContext, DamlLfJsonDecoders.ReadText);
            return DamlRecord.Create(DamlField.Create("note", note));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_a_present_optional_field_from_its_bare_wire_value()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"note":"present"}""", recordType: typeof(NoteHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        record.GetRequiredField("note").Should().BeOfType<DamlOptional>()
            .Which.Value.Should().BeOfType<DamlText>().Which.Value.Should().Be("present");
    }

    [Fact]
    public void ReadRecord_should_decode_an_absent_optional_field_from_json_null()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"note":null}""", recordType: typeof(NoteHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        record.GetRequiredField("note").Should().BeOfType<DamlOptional>()
            .Which.Should().Be(DamlOptional.None);
    }

    public sealed record LevelHolder([property: DamlFieldAttribute("level")] long? Level) : IDamlRecord<LevelHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "level",
            Level is null ? DamlOptional.None : DamlOptional.Some(new DamlInt64(Level.Value))));

        public static LevelHolder FromRecord(DamlRecord record) =>
            new(record.GetRequiredField("level").As<DamlOptional>().GetValueOrDefault<DamlInt64>()?.Value);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var levelJson = DamlLfJsonDecoders.RequireField(json, context, "level");
            var levelContext = context.Field("level");
            var level = DamlLfJsonDecoders.ReadOptional(levelJson, levelContext, DamlLfJsonDecoders.ReadInt64);
            return DamlRecord.Create(DamlField.Create("level", level));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_a_present_optional_value_type_field()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"level":"3"}""", recordType: typeof(LevelHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        record.GetRequiredField("level").Should().BeOfType<DamlOptional>()
            .Which.Value.Should().BeOfType<DamlInt64>().Which.Value.Should().Be(3L);
    }

    [Fact]
    public void ReadRecord_should_decode_an_absent_optional_value_type_field()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"level":null}""", recordType: typeof(LevelHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        record.GetRequiredField("level").Should().BeOfType<DamlOptional>()
            .Which.Should().Be(DamlOptional.None);
    }

    public sealed record NoteListHolder(
        [property: DamlFieldAttribute("notes")] IReadOnlyList<string?> Notes) : IDamlRecord<NoteListHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "notes",
            new DamlList(Notes
                .Select(note => (DamlValue)(note is null ? DamlOptional.None : DamlOptional.Some(new DamlText(note))))
                .ToList())));

        public static NoteListHolder FromRecord(DamlRecord record) => new(record
            .GetRequiredField("notes").As<DamlList>().Values
            .Select(value => value.As<DamlOptional>().GetValueOrDefault<DamlText>()?.Value)
            .ToList());

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var notesJson = DamlLfJsonDecoders.RequireField(json, context, "notes");
            var notesContext = context.Field("notes");
            var notes = DamlLfJsonDecoders.ReadList(notesJson, notesContext, (element, elementContext) =>
                DamlLfJsonDecoders.ReadOptional(element, elementContext, DamlLfJsonDecoders.ReadText));
            return DamlRecord.Create(DamlField.Create("notes", notes));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_optionals_nested_inside_a_list()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"notes":["present",null]}""", recordType: typeof(NoteListHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        record.GetRequiredField("notes").Should().BeOfType<DamlList>()
            .Which.Values.Should().Equal(
                DamlOptional.Some(new DamlText("present")),
                DamlOptional.None);
    }

    public sealed record AttributesHolder(
        [property: DamlFieldAttribute("attributes")] IReadOnlyDictionary<string, string> Attributes)
        : IDamlRecord<AttributesHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "attributes",
            new DamlTextMap(Attributes.ToDictionary(entry => entry.Key, entry => (DamlValue)new DamlText(entry.Value)))));

        public static AttributesHolder FromRecord(DamlRecord record) => new(record
            .GetRequiredField("attributes").As<DamlTextMap>().Values
            .ToDictionary(entry => entry.Key, entry => entry.Value.As<DamlText>().Value));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var attributesJson = DamlLfJsonDecoders.RequireField(json, context, "attributes");
            var attributesContext = context.Field("attributes");
            DamlValue attributes = attributesJson.ValueKind switch
            {
                JsonValueKind.Array => DamlLfJsonDecoders.ReadGenMap(
                    attributesJson, attributesContext, DamlLfJsonDecoders.ReadText, DamlLfJsonDecoders.ReadText),
                JsonValueKind.Object => DamlLfJsonDecoders.ReadTextMap(
                    attributesJson, attributesContext, DamlLfJsonDecoders.ReadText),
                _ => throw new JsonException(
                    "Expected JSON object (TextMap) or array of entry pairs (GenMap) "
                    + $"at '{attributesContext.Path}' but found {attributesJson.ValueKind}")
            };
            return DamlRecord.Create(DamlField.Create("attributes", attributes));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_a_text_map_field_from_its_wire_object_form()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"attributes":{"a":"1"}}""", recordType: typeof(AttributesHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        record.GetRequiredField("attributes").Should().BeOfType<DamlTextMap>()
            .Which.Should().Be(DamlTextMap.Create(("a", new DamlText("1"))));
    }

    [Fact]
    public void ReadRecord_should_decode_an_empty_text_map_field()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"attributes":{}}""", recordType: typeof(AttributesHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        record.GetRequiredField("attributes").Should().BeOfType<DamlTextMap>()
            .Which.Values.Should().BeEmpty();
    }

    [Fact]
    public void ReadRecord_should_reject_duplicate_text_map_keys_in_a_caller_parsed_document()
    {
        using var document = JsonDocument.Parse("""{"attributes":{"a":"1","a":"2"}}""");

        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord(document.RootElement, recordType: typeof(AttributesHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Duplicate key 'a' at 'AttributesHolder.attributes' in a Daml TextMap");
    }

    [Fact]
    public void ReadRecord_should_bracket_the_map_key_when_reporting_an_error_inside_a_text_map_value()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"attributes":{"a.b":5}}""", recordType: typeof(AttributesHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON String at 'AttributesHolder.attributes['a.b']' but found Number");
    }

    [Fact]
    public void ReadRecord_should_escape_a_quote_inside_a_bracketed_map_key()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"attributes":{"o'brien":5}}""", recordType: typeof(AttributesHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage(@"Expected JSON String at 'AttributesHolder.attributes['o\'brien']' but found Number");
    }

    [Fact]
    public void ReadRecord_should_escape_a_backslash_inside_a_bracketed_map_key()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"attributes":{"a\\b":5}}""", recordType: typeof(AttributesHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage(@"Expected JSON String at 'AttributesHolder.attributes['a\\b']' but found Number");
    }

    [Fact]
    public void ReadRecord_should_escape_a_backslash_that_precedes_a_quote_inside_a_bracketed_map_key()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"attributes":{"a\\'b":5}}""", recordType: typeof(AttributesHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage(@"Expected JSON String at 'AttributesHolder.attributes['a\\\'b']' but found Number");
    }

    [Fact]
    public void ReadRecord_should_elide_an_oversized_map_key_in_a_bracketed_path()
    {
        var oversizedKey = new string('k', 70);

        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord($$$"""{"attributes":{"{{{oversizedKey}}}":5}}""", recordType: typeof(AttributesHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage(
                $"Expected JSON String at 'AttributesHolder.attributes['{new string('k', 64)}…']' but found Number");
    }

    private const string WireParty =
        "wiree3ed3454::1220141a01c00ef277c31ca4eb0e82ee3de7f790eb25f3787f8195f117af8668bf3b";

    public sealed record GenMapHolder(
        [property: DamlFieldAttribute("genMap")] IReadOnlyDictionary<Party, long> GenMap) : IDamlRecord<GenMapHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "genMap",
            new DamlGenMap(GenMap
                .Select(entry => ((DamlValue)entry.Key.ToDamlValue(), (DamlValue)new DamlInt64(entry.Value)))
                .ToList())));

        public static GenMapHolder FromRecord(DamlRecord record) => new(record
            .GetRequiredField("genMap").As<DamlGenMap>().Entries
            .ToDictionary(entry => Party.FromDamlValue(entry.Key.As<DamlParty>()), entry => entry.Value.As<DamlInt64>().Value));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var genMapJson = DamlLfJsonDecoders.RequireField(json, context, "genMap");
            var genMapContext = context.Field("genMap");
            if (genMapJson.ValueKind != JsonValueKind.Array)
            {
                throw new JsonException(
                    $"Expected JSON array of entry pairs (GenMap) at '{genMapContext.Path}' but found {genMapJson.ValueKind}");
            }
            var genMap = DamlLfJsonDecoders.ReadGenMap(
                genMapJson, genMapContext, DamlLfJsonDecoders.ReadParty, DamlLfJsonDecoders.ReadInt64);
            return DamlRecord.Create(DamlField.Create("genMap", genMap));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_a_gen_map_field_from_its_wire_pair_array_form()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord($$"""{"genMap":[["{{WireParty}}","7"]]}""", recordType: typeof(GenMapHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        record.GetRequiredField("genMap").Should().BeOfType<DamlGenMap>()
            .Which.Should().Be(DamlGenMap.Create((new DamlParty(WireParty), new DamlInt64(7))));
    }

    [Fact]
    public void ReadRecord_should_decode_an_empty_gen_map_field()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"genMap":[]}""", recordType: typeof(GenMapHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        record.GetRequiredField("genMap").Should().BeOfType<DamlGenMap>()
            .Which.Entries.Should().BeEmpty();
    }

    [Fact]
    public void ReadRecord_should_reject_a_gen_map_entry_that_is_not_a_key_value_pair()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord($$"""{"genMap":[["{{WireParty}}"]]}""", recordType: typeof(GenMapHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Expected a two-element key/value pair at 'GenMapHolder.genMap[0]' but found 1 element(s)");
    }

    [Fact]
    public void ReadRecord_should_reject_a_gen_map_with_a_duplicate_key()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord($$"""{"genMap":[["{{WireParty}}","1"],["{{WireParty}}","2"]]}""", recordType: typeof(GenMapHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Duplicate key at 'GenMapHolder.genMap[1]' in a Daml GenMap");
    }

    [Fact]
    public void ReadRecord_should_reject_a_gen_map_entry_that_is_not_an_array()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"genMap":["nope"]}""", recordType: typeof(GenMapHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON Array at 'GenMapHolder.genMap[0]' but found String");
    }

    [Fact]
    public void ReadRecord_should_reject_a_gen_map_field_encoded_as_a_json_object()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"genMap":{"a":"1"}}""", recordType: typeof(GenMapHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON array of entry pairs (GenMap) at 'GenMapHolder.genMap' but found Object");
    }

    [Fact]
    public void ReadRecord_should_name_both_map_wire_forms_when_rejecting_a_string_keyed_map()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"attributes":"nope"}""", recordType: typeof(AttributesHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON object (TextMap) or array of entry pairs (GenMap) "
                + "at 'AttributesHolder.attributes' but found String");
    }

    public sealed record UnitHolder([property: DamlFieldAttribute("unitField")] DamlUnit UnitField) : IDamlRecord<UnitHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("unitField", UnitField));

        public static UnitHolder FromRecord(DamlRecord record) =>
            new(record.GetRequiredField("unitField").As<DamlUnit>());

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var unitFieldJson = DamlLfJsonDecoders.RequireField(json, context, "unitField");
            var unitFieldContext = context.Field("unitField");
            var unitField = DamlLfJsonDecoders.ReadUnit(unitFieldJson, unitFieldContext);
            return DamlRecord.Create(DamlField.Create("unitField", unitField));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_a_unit_field_from_its_wire_empty_object_form()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"unitField":{}}""", recordType: typeof(UnitHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        record.GetRequiredField("unitField").Should().BeOfType<DamlUnit>()
            .Which.Should().BeSameAs(DamlUnit.Instance);
    }

    [Fact]
    public void ReadRecord_should_reject_a_unit_field_encoded_as_a_json_string()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"unitField":"nope"}""", recordType: typeof(UnitHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

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

    public sealed record SuitHolder([property: DamlFieldAttribute("suit")] Suit Suit) : IDamlRecord<SuitHolder>
    {
        private static readonly IReadOnlyList<string> KnownConstructors =
            Enum.GetNames<Suit>().Order(StringComparer.Ordinal).ToList();

        public DamlRecord ToRecord() =>
            DamlRecord.Create(DamlField.Create("suit", DamlEnum.Create(Suit.ToString())));

        public static SuitHolder FromRecord(DamlRecord record) =>
            new(Enum.Parse<Suit>(record.GetRequiredField("suit").As<DamlEnum>().Constructor));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var suitJson = DamlLfJsonDecoders.RequireField(json, context, "suit");
            var suitContext = context.Field("suit");
            var suit = DamlLfJsonDecoders.ReadEnumConstructor(suitJson, suitContext, KnownConstructors);
            return DamlRecord.Create(DamlField.Create("suit", suit));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_an_enum_field_from_its_bare_wire_string()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"suit":"Hearts"}""", recordType: typeof(SuitHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        record.GetRequiredField("suit").Should().BeOfType<DamlEnum>()
            .Which.Should().Be(DamlEnum.Create("Hearts"));
    }

    [Fact]
    public void ReadRecord_should_reject_an_unknown_enum_constructor()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"suit":"Wands"}""", recordType: typeof(SuitHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Unknown Daml enum constructor 'Wands' at 'SuitHolder.suit'; "
                + "expected one of Clubs, Diamonds, Hearts, Spades");
    }

    [Fact]
    public void ReadRecord_should_reject_an_enum_field_encoded_as_a_json_number()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"suit":2}""", recordType: typeof(SuitHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON String at 'SuitHolder.suit' but found Number");
    }

    public enum Zigzag
    {
        Zig,
        Alpha
    }

    public sealed record ZigzagHolder([property: DamlFieldAttribute("zigzag")] Zigzag Zigzag) : IDamlRecord<ZigzagHolder>
    {
        private static readonly IReadOnlyList<string> KnownConstructors =
            Enum.GetNames<Zigzag>().Order(StringComparer.Ordinal).ToList();

        public DamlRecord ToRecord() =>
            DamlRecord.Create(DamlField.Create("zigzag", DamlEnum.Create(Zigzag.ToString())));

        public static ZigzagHolder FromRecord(DamlRecord record) =>
            new(Enum.Parse<Zigzag>(record.GetRequiredField("zigzag").As<DamlEnum>().Constructor));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var zigzagJson = DamlLfJsonDecoders.RequireField(json, context, "zigzag");
            var zigzagContext = context.Field("zigzag");
            var zigzag = DamlLfJsonDecoders.ReadEnumConstructor(zigzagJson, zigzagContext, KnownConstructors);
            return DamlRecord.Create(DamlField.Create("zigzag", zigzag));
        }
    }

    [Fact]
    public void ReadRecord_should_sort_the_expected_set_when_rejecting_an_unknown_enum_constructor()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"zigzag":"Zag"}""", recordType: typeof(ZigzagHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

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

    public sealed record OutcomeHolder([property: DamlFieldAttribute("outcome")] Outcome Outcome) : IDamlRecord<OutcomeHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("outcome", Outcome.ToVariant()));

        public static OutcomeHolder FromRecord(DamlRecord record)
        {
            var variant = record.GetRequiredField("outcome").As<DamlVariant>();
            Outcome outcome = variant.Constructor switch
            {
                "Win" => new Outcome.Win(new OutcomeWin(
                    variant.Value.As<DamlRecord>().GetRequiredField("prize").As<DamlNumeric>().Value,
                    variant.Value.As<DamlRecord>().GetRequiredField("tier").As<DamlText>().Value)),
                "Pending" => new Outcome.Pending(),
                _ => throw new ArgumentOutOfRangeException(nameof(record), variant.Constructor, null)
            };
            return new OutcomeHolder(outcome);
        }

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var outcomeJson = DamlLfJsonDecoders.RequireField(json, context, "outcome");
            var outcomeContext = context.Field("outcome");
            var tag = DamlLfJsonDecoders.ReadVariantTag(outcomeJson, outcomeContext);
            var valueJson = DamlLfJsonDecoders.RequireVariantValue(outcomeJson, outcomeContext);
            var valueContext = outcomeContext.Field("value");
            DamlValue payload = tag switch
            {
                "Win" => ReadOutcomeWin(valueJson, valueContext),
                "Pending" => DamlLfJsonDecoders.ReadUnit(valueJson, valueContext),
                _ => throw DamlLfJsonDecoders.UnknownConstructor(
                    "variant constructor", tag, outcomeContext, ["Pending", "Win"])
            };
            return DamlRecord.Create(DamlField.Create("outcome", DamlVariant.Create(tag, payload)));

            static DamlRecord ReadOutcomeWin(JsonElement json, DamlLfJsonDecodeContext context)
            {
                DamlLfJsonDecoders.RequireObject(json, context);
                return DamlRecord.Create(
                    DamlField.Create("prize", DamlLfJsonDecoders.ReadNumeric(
                        DamlLfJsonDecoders.RequireField(json, context, "prize"), context.Field("prize"))),
                    DamlField.Create("tier", DamlLfJsonDecoders.ReadText(
                        DamlLfJsonDecoders.RequireField(json, context, "tier"), context.Field("tier"))));
            }
        }
    }

    [Fact]
    public void ReadRecord_should_decode_a_tagged_variant_arm_with_its_record_payload()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"outcome":{"tag":"Win","value":{"prize":"1.25","tier":"gold"}}}""", recordType: typeof(OutcomeHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        var variant = record.GetRequiredField("outcome").Should().BeOfType<DamlVariant>().Which;
        variant.Constructor.Should().Be("Win");
        variant.Value.Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("prize", new DamlNumeric(1.25m)),
            new DamlField("tier", new DamlText("gold")));
    }

    [Fact]
    public void ReadRecord_should_decode_a_nullary_variant_arm_from_its_empty_object_value()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"outcome":{"tag":"Pending","value":{}}}""", recordType: typeof(OutcomeHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        var variant = record.GetRequiredField("outcome").Should().BeOfType<DamlVariant>().Which;
        variant.Constructor.Should().Be("Pending");
        variant.Value.Should().BeSameAs(DamlUnit.Instance);
    }

    [Fact]
    public void ReadRecord_should_reject_an_unknown_variant_constructor()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"outcome":{"tag":"Draw","value":{}}}""", recordType: typeof(OutcomeHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Unknown Daml variant constructor 'Draw' at 'OutcomeHolder.outcome'; "
                + "expected one of Pending, Win");
    }

    [Fact]
    public void ReadRecord_should_reject_a_variant_without_a_tag()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"outcome":{"value":{}}}""", recordType: typeof(OutcomeHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Required Daml variant field 'OutcomeHolder.outcome.tag' is missing from the JSON object");
    }

    [Fact]
    public void ReadRecord_should_reject_a_variant_tag_that_is_not_a_string()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"outcome":{"tag":5,"value":{}}}""", recordType: typeof(OutcomeHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON String at 'OutcomeHolder.outcome.tag' but found Number");
    }

    [Fact]
    public void ReadRecord_should_reject_a_nullary_variant_value_that_is_not_an_object()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"outcome":{"tag":"Pending","value":"nope"}}""", recordType: typeof(OutcomeHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON Object at 'OutcomeHolder.outcome.value' but found String");
    }

    [Fact]
    public void ReadRecord_should_reject_a_variant_missing_its_value_field()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"outcome":{"tag":"Pending"}}""", recordType: typeof(OutcomeHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Required Daml variant field 'OutcomeHolder.outcome.value' is missing from the JSON object");
    }

    [Fact]
    public void ReadRecord_should_reject_a_variant_field_encoded_as_a_bare_string()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"outcome":"Pending"}""", recordType: typeof(OutcomeHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON Object at 'OutcomeHolder.outcome' but found String");
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

    public sealed record ProfileHolder([property: DamlFieldAttribute("profile")] Profile Profile) : IDamlRecord<ProfileHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("profile", Profile.ToRecord()));

        public static ProfileHolder FromRecord(DamlRecord record) =>
            new(Profile.FromRecord(record.GetRequiredField("profile").As<DamlRecord>()));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var profileJson = DamlLfJsonDecoders.RequireField(json, context, "profile");
            var profileContext = context.Field("profile");
            var profile = Profile.__ReadDamlLfJson(profileJson, profileContext);
            return DamlRecord.Create(DamlField.Create("profile", profile));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_a_nested_record_field_keyed_by_field_name()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"profile":{"nickname":"nick","level":"3"}}""", recordType: typeof(ProfileHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        record.GetRequiredField("profile").Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("nickname", new DamlText("nick")),
            new DamlField("level", new DamlInt64(3L)));
    }

    public sealed record TagsHolder(
        [property: DamlFieldAttribute("tags")] IReadOnlyList<string> Tags) : IDamlRecord<TagsHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "tags",
            new DamlList(Tags.Select(tag => (DamlValue)new DamlText(tag)).ToList())));

        public static TagsHolder FromRecord(DamlRecord record) => new(record
            .GetRequiredField("tags").As<DamlList>().Values
            .Select(value => value.As<DamlText>().Value)
            .ToList());

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var tagsJson = DamlLfJsonDecoders.RequireField(json, context, "tags");
            var tagsContext = context.Field("tags");
            var tags = DamlLfJsonDecoders.ReadList(tagsJson, tagsContext, DamlLfJsonDecoders.ReadText);
            return DamlRecord.Create(DamlField.Create("tags", tags));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_a_list_field_from_its_wire_array_form()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"tags":["x","y"]}""", recordType: typeof(TagsHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        record.GetRequiredField("tags").Should().BeOfType<DamlList>()
            .Which.Values.Should().Equal(new DamlText("x"), new DamlText("y"));
    }

    [Fact]
    public void ReadRecord_should_decode_an_empty_list_field()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"tags":[]}""", recordType: typeof(TagsHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        record.GetRequiredField("tags").Should().BeOfType<DamlList>().Which.Values.Should().BeEmpty();
    }

    public sealed record ProfileTallyHolder(
        [property: DamlFieldAttribute("tally")] IReadOnlyDictionary<Profile, long> Tally)
        : IDamlRecord<ProfileTallyHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "tally",
            new DamlGenMap(Tally
                .Select(entry => ((DamlValue)entry.Key.ToRecord(), (DamlValue)new DamlInt64(entry.Value)))
                .ToList())));

        public static ProfileTallyHolder FromRecord(DamlRecord record) => new(record
            .GetRequiredField("tally").As<DamlGenMap>().Entries
            .ToDictionary(entry => Profile.FromRecord(entry.Key.As<DamlRecord>()), entry => entry.Value.As<DamlInt64>().Value));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var tallyJson = DamlLfJsonDecoders.RequireField(json, context, "tally");
            var tallyContext = context.Field("tally");
            var tally = DamlLfJsonDecoders.ReadGenMap(
                tallyJson, tallyContext, Profile.__ReadDamlLfJson, DamlLfJsonDecoders.ReadInt64);
            return DamlRecord.Create(DamlField.Create("tally", tally));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_a_gen_map_keyed_by_a_record()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"tally":[[{"nickname":"nick","level":"3"},"7"]]}""", recordType: typeof(ProfileTallyHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

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

    public sealed record ReadingHolder([property: DamlFieldAttribute("reading")] Reading Reading) : IDamlRecord<ReadingHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("reading", Reading.ToVariant()));

        public static ReadingHolder FromRecord(DamlRecord record)
        {
            var variant = record.GetRequiredField("reading").As<DamlVariant>();
            Reading reading = variant.Constructor switch
            {
                "Measured" => new Reading.Measured(variant.Value.As<DamlNumeric>().Value),
                _ => throw new ArgumentOutOfRangeException(nameof(record), variant.Constructor, null)
            };
            return new ReadingHolder(reading);
        }

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var readingJson = DamlLfJsonDecoders.RequireField(json, context, "reading");
            var readingContext = context.Field("reading");
            var tag = DamlLfJsonDecoders.ReadVariantTag(readingJson, readingContext);
            var valueJson = DamlLfJsonDecoders.RequireVariantValue(readingJson, readingContext);
            var valueContext = readingContext.Field("value");
            DamlValue payload = tag switch
            {
                "Measured" => DamlLfJsonDecoders.ReadNumeric(valueJson, valueContext),
                _ => throw DamlLfJsonDecoders.UnknownConstructor(
                    "variant constructor", tag, readingContext, ["Measured"])
            };
            return DamlRecord.Create(DamlField.Create("reading", DamlVariant.Create(tag, payload)));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_a_variant_arm_carrying_a_scalar_payload()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"reading":{"tag":"Measured","value":"1.25"}}""", recordType: typeof(ReadingHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

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

    public sealed record ShapeHolder([property: DamlFieldAttribute("shape")] Shape Shape) : IDamlRecord<ShapeHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("shape", Shape.ToVariant()));

        public static ShapeHolder FromRecord(DamlRecord record)
        {
            var variant = record.GetRequiredField("shape").As<DamlVariant>();
            Shape shape = variant.Constructor switch
            {
                "Shape" => new Shape.Shape_(variant.Value.As<DamlText>().Value),
                "Blank" => new Shape.Blank(),
                _ => throw new ArgumentOutOfRangeException(nameof(record), variant.Constructor, null)
            };
            return new ShapeHolder(shape);
        }

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var shapeJson = DamlLfJsonDecoders.RequireField(json, context, "shape");
            var shapeContext = context.Field("shape");
            var tag = DamlLfJsonDecoders.ReadVariantTag(shapeJson, shapeContext);
            var valueJson = DamlLfJsonDecoders.RequireVariantValue(shapeJson, shapeContext);
            var valueContext = shapeContext.Field("value");
            DamlValue payload = tag switch
            {
                "Shape" => DamlLfJsonDecoders.ReadText(valueJson, valueContext),
                "Blank" => DamlLfJsonDecoders.ReadUnit(valueJson, valueContext),
                _ => throw DamlLfJsonDecoders.UnknownConstructor(
                    "variant constructor", tag, shapeContext, ["Blank", "Shape"])
            };
            return DamlRecord.Create(DamlField.Create("shape", DamlVariant.Create(tag, payload)));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_a_variant_arm_whose_csharp_name_was_disambiguated_from_its_wire_tag()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"shape":{"tag":"Shape","value":"round"}}""", recordType: typeof(ShapeHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        var variant = record.GetRequiredField("shape").Should().BeOfType<DamlVariant>().Which;
        variant.Constructor.Should().Be("Shape");
        variant.Value.Should().BeOfType<DamlText>().Which.Value.Should().Be("round");
    }

    [Fact]
    public void ReadRecord_should_list_wire_tags_rather_than_csharp_names_for_an_unknown_variant_constructor()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"shape":{"tag":"Round","value":{}}}""", recordType: typeof(ShapeHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Unknown Daml variant constructor 'Round' at 'ShapeHolder.shape'; "
                + "expected one of Blank, Shape");
    }

    public abstract record Armless : IDamlVariant
    {
        public abstract DamlVariant ToVariant();
    }

    public sealed record ArmlessHolder([property: DamlFieldAttribute("armless")] Armless Armless) : IDamlRecord<ArmlessHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("armless", Armless.ToVariant()));

        public static ArmlessHolder FromRecord(DamlRecord record) =>
            throw new NotSupportedException("Reader-shape stand-ins in this suite are decode-only.");

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var armlessContext = context.Field("armless");
            throw new NotSupportedException(
                $"Type '{typeof(Armless)}' at '{armlessContext.Path}' declares no variant arms; "
                + "pass a generated variant whose constructors are nested types carrying a Tag property.");
        }
    }

    [Fact]
    public void ReadRecord_should_refuse_a_variant_type_that_declares_no_arms()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"armless":{"tag":"Whatever","value":{}}}""", recordType: typeof(ArmlessHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

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

    public sealed record UntaggedHolder(
        [property: DamlFieldAttribute("untagged")] Untagged Untagged) : IDamlRecord<UntaggedHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("untagged", Untagged.ToVariant()));

        public static UntaggedHolder FromRecord(DamlRecord record) =>
            throw new NotSupportedException("Reader-shape stand-ins in this suite are decode-only.");

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var untaggedContext = context.Field("untagged");
            throw new NotSupportedException(
                $"Variant arm '{typeof(Untagged.Only)}' at '{untaggedContext.Path}' exposes no readable "
                + "Tag property, so its wire constructor cannot be determined; pass a generated variant.");
        }
    }

    [Fact]
    public void ReadRecord_should_refuse_a_variant_arm_that_carries_no_wire_tag()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"untagged":{"tag":"Only","value":{}}}""", recordType: typeof(UntaggedHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

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

    public sealed record CursedHolder([property: DamlFieldAttribute("cursed")] Cursed Cursed) : IDamlRecord<CursedHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("cursed", Cursed.ToVariant()));

        public static CursedHolder FromRecord(DamlRecord record) =>
            throw new NotSupportedException("Reader-shape stand-ins in this suite are decode-only.");

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var cursedContext = context.Field("cursed");
            throw new NotSupportedException(
                $"Variant arm '{typeof(Cursed.Broken)}' at '{cursedContext.Path}' exposes no readable "
                + "Tag property, so its wire constructor cannot be determined; pass a generated variant.");
        }
    }

    [Fact]
    public void ReadRecord_should_refuse_a_variant_arm_whose_tag_getter_throws()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"cursed":{"tag":"Broken","value":{}}}""", recordType: typeof(CursedHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<NotSupportedException>()
            .WithMessage($"Variant arm '{typeof(Cursed.Broken)}' at 'CursedHolder.cursed' exposes no readable "
                + "Tag property, so its wire constructor cannot be determined; pass a generated variant.");
    }

    public sealed record DirectionHolder(
        [property: DamlFieldAttribute("direction")] Direction Direction) : IDamlRecord<DirectionHolder>
    {
        private static readonly IReadOnlyDictionary<string, Direction> ByConstructor = Enum.GetValues<Direction>()
            .ToDictionary(value => value.ToDamlEnum().Constructor);

        private static readonly IReadOnlyList<string> KnownConstructors =
            ByConstructor.Keys.Order(StringComparer.Ordinal).ToList();

        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("direction", Direction.ToDamlEnum()));

        public static DirectionHolder FromRecord(DamlRecord record) =>
            new(ByConstructor[record.GetRequiredField("direction").As<DamlEnum>().Constructor]);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var directionJson = DamlLfJsonDecoders.RequireField(json, context, "direction");
            var directionContext = context.Field("direction");
            var direction = DamlLfJsonDecoders.ReadEnumConstructor(directionJson, directionContext, KnownConstructors);
            return DamlRecord.Create(DamlField.Create("direction", direction));
        }
    }

    [Fact]
    public void ReadRecord_should_decode_an_enum_constructor_whose_wire_name_differs_from_its_csharp_member()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"direction":"U$u0020Turn"}""", recordType: typeof(DirectionHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        record.GetRequiredField("direction").Should().BeOfType<DamlEnum>()
            .Which.Should().Be(DamlEnum.Create("U$u0020Turn"));
    }

    [Fact]
    public void ReadRecord_should_decode_an_enum_constructor_whose_wire_name_survives_sanitization()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var record = DamlLfJsonReader.ReadRecord("""{"direction":"Forward"}""", recordType: typeof(DirectionHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        record.GetRequiredField("direction").Should().BeOfType<DamlEnum>()
            .Which.Should().Be(DamlEnum.Create("Forward"));
    }

    [Fact]
    public void ReadRecord_should_list_wire_constructors_rather_than_csharp_members_for_an_unknown_enum_constructor()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"direction":"Sideways"}""", recordType: typeof(DirectionHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Unknown Daml enum constructor 'Sideways' at 'DirectionHolder.direction'; "
                + "expected one of Forward, U$u0020Turn");
    }

    public sealed record CadenceHolder(
        [property: DamlFieldAttribute("cadence")] Cadence Cadence) : IDamlRecord<CadenceHolder>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("cadence", Cadence.ToDamlEnum()));

        public static CadenceHolder FromRecord(DamlRecord record) =>
            throw new NotSupportedException("Reader-shape stand-ins in this suite are decode-only.");

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            var cadenceContext = context.Field("cadence");
            foreach (var member in Enum.GetValues<Cadence>())
            {
                try
                {
                    _ = member.ToDamlEnum();
                }
                catch (ArgumentOutOfRangeException)
                {
                    throw new NotSupportedException(
                        $"Enum '{typeof(Cadence)}' at '{cadenceContext.Path}' has a companion whose ToDamlEnum fails "
                        + $"for member '{member}', so its wire constructors cannot be determined; "
                        + "pass a generated Daml enum.");
                }
            }
            throw new InvalidOperationException(
                "Unreachable: this fixture's CadenceExtensions.ToDamlEnum is expected to fail for Broken.");
        }
    }

    [Fact]
    public void ReadRecord_should_refuse_an_enum_whose_companion_cannot_name_every_wire_constructor()
    {
        #pragma warning disable DAMLRT0001
        #pragma warning disable CA2263
        var act = () => DamlLfJsonReader.ReadRecord("""{"cadence":"Steady"}""", recordType: typeof(CadenceHolder));
        #pragma warning restore CA2263
        #pragma warning restore DAMLRT0001

        act.Should().Throw<NotSupportedException>()
            .WithMessage($"Enum '{typeof(Cadence)}' at 'CadenceHolder.cadence' has a companion whose ToDamlEnum "
                + "fails for member 'Broken', so its wire constructors cannot be determined; "
                + "pass a generated Daml enum.");
    }

    [Fact]
    public void ReadValue_should_decode_a_top_level_variant_arm_with_its_record_payload()
    {
        #pragma warning disable DAMLRT0001
        var variant = DamlLfJsonReader
            .ReadValue<Outcome>("""{"tag":"Win","value":{"prize":"1.25","tier":"gold"}}""")
            .Should().BeOfType<DamlVariant>().Which;
        #pragma warning restore DAMLRT0001

        variant.Constructor.Should().Be("Win");
        variant.Value.Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("prize", new DamlNumeric(1.25m)),
            new DamlField("tier", new DamlText("gold")));
    }

    [Fact]
    public void ReadValue_should_decode_a_top_level_nullary_variant_arm_from_its_empty_object_value()
    {
        #pragma warning disable DAMLRT0001
        var variant = DamlLfJsonReader.ReadValue<Outcome>("""{"tag":"Pending","value":{}}""")
            .Should().BeOfType<DamlVariant>().Which;
        #pragma warning restore DAMLRT0001

        variant.Constructor.Should().Be("Pending");
        variant.Value.Should().BeSameAs(DamlUnit.Instance);
    }

    [Fact]
    public void ReadValue_should_reject_an_unknown_top_level_variant_constructor()
    {
        #pragma warning disable DAMLRT0001
        var act = () => DamlLfJsonReader.ReadValue<Outcome>("""{"tag":"Draw","value":{}}""");
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Unknown Daml variant constructor 'Draw' at 'Outcome'; expected one of Pending, Win");
    }

    [Fact]
    public void ReadValue_should_reject_a_top_level_variant_missing_its_tag()
    {
        #pragma warning disable DAMLRT0001
        var act = () => DamlLfJsonReader.ReadValue<Outcome>("""{"value":{}}""");
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Required Daml variant field 'Outcome.tag' is missing from the JSON object");
    }

    [Fact]
    public void ReadValue_should_name_the_payload_path_of_a_top_level_variant_arm()
    {
        #pragma warning disable DAMLRT0001
        var act = () => DamlLfJsonReader.ReadValue<Outcome>(
            """{"tag":"Win","value":{"prize":"1.25","tier":42}}""");
        #pragma warning restore DAMLRT0001

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON String at 'Outcome.value.tier' but found Number");
    }

    public abstract record Scribble : IDamlVariant
    {
        public abstract string Tag { get; }

        public abstract DamlVariant ToVariant();

        public sealed record Scrawled(string? Value) : Scribble
        {
            public override string Tag => "Scrawled";

            public override DamlVariant ToVariant() => DamlVariant.Create(
                "Scrawled",
                Value is null ? DamlOptional.None : DamlOptional.Some(new DamlText(Value)));
        }
    }

    [Fact]
    public void ReadValue_should_keep_a_top_level_variant_arm_payload_optional_when_the_arm_declares_it_nullable()
    {
        #pragma warning disable DAMLRT0001
        DamlLfJsonReader.ReadValue<Scribble>("""{"tag":"Scrawled","value":null}""")
            .Should().BeOfType<DamlVariant>().Which.Value.Should().Be(DamlOptional.None);
        #pragma warning restore DAMLRT0001

        #pragma warning disable DAMLRT0001
        DamlLfJsonReader.ReadValue<Scribble>("""{"tag":"Scrawled","value":"ink"}""")
            .Should().BeOfType<DamlVariant>().Which.Value
            .Should().Be(DamlOptional.Some(new DamlText("ink")));
        #pragma warning restore DAMLRT0001
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
