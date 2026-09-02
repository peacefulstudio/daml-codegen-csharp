// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Daml.Runtime.Stdlib;
using Xunit;

namespace Daml.Runtime.Tests;

public class DamlLfJsonReaderWireSamplesTests
{
    private const string WireTypesPackageId = "f7230672ba7d77a5adba89f092d50b7c957d0eb5030c1e4f96706dcca93f2200";
    private const string WireTypesPackageName = "wiretypes23";
    private const string WireTypesModuleName = "WireTypes";

    internal static readonly string CorpusDirectory =
        Path.Combine(AppContext.BaseDirectory, "wire-samples", "data");

    private static readonly string[] FactConsumedWireSamples =
    [
        "acs_wildcard_verbose_true.json",
        "acs_wildcard_verbose_false.json",
        "probe_nested_optional_matrix.json",
    ];

    [Theory]
    [MemberData(nameof(SupportedWireSamplePayloads))]
    public void ReadRecord_should_decode_captured_payloads_inside_the_reader_type_mapping(
        string fileName, string payloadPath, string shapeName, (string Label, Type DamlType)[] expectedFields)
    {
        using var document = LoadWireSample(fileName);
        var payload = ResolvePayload(document.RootElement, payloadPath);

        var record = DamlLfJsonReader.ReadRecord(payload, DeclaredShapes[shapeName]);

        record.RecordId.Should().BeNull();
        record.Fields.Select(field => (field.Label, DamlType: field.Value.GetType()))
            .Should().Equal(expectedFields);
    }

    [Theory]
    [MemberData(nameof(UnsupportedWireSamplePayloads))]
    public void ReadRecord_should_pin_the_first_gap_for_captured_payloads_outside_the_reader_type_mapping(
        string fileName, string payloadPath, string shapeName, string expectedGap)
    {
        using var document = LoadWireSample(fileName);
        var payload = ResolvePayload(document.RootElement, payloadPath);

        var act = () => DamlLfJsonReader.ReadRecord(payload, DeclaredShapes[shapeName]);

        act.Should().Throw<NotSupportedException>().WithMessage($"*{expectedGap}*");
    }

    [Fact]
    public void ReadRecord_should_decode_the_captured_tuple_key_as_a_stdlib_tuple_field()
    {
        using var document = LoadWireSample("create_keyed_contract_key.json");
        var capturedKey = ResolvePayload(
            document.RootElement, "response/transaction/events/0/CreatedEvent/contractKey");

        var record = DamlLfJsonReader.ReadRecord<TupleKeyHolder>(
            """{"key":""" + capturedKey.GetRawText() + "}");

        record.GetRequiredField("key").Should().BeOfType<DamlRecord>().Which.Fields.Should().Equal(
            new DamlField("_1", new DamlParty(capturedKey.GetProperty("_1").GetString()!)),
            new DamlField("_2", new DamlText(capturedKey.GetProperty("_2").GetString()!)));
    }

    [Fact]
    public void WireSamplesCorpus_should_show_identical_contract_entries_for_verbose_true_and_false()
    {
        using var verboseTrue = LoadWireSample("acs_wildcard_verbose_true.json");
        using var verboseFalse = LoadWireSample("acs_wildcard_verbose_false.json");

        var trueEntries = verboseTrue.RootElement.GetProperty("response").EnumerateArray()
            .Select(entry => entry.GetProperty("contractEntry")).ToList();
        var falseEntries = verboseFalse.RootElement.GetProperty("response").EnumerateArray()
            .Select(entry => entry.GetProperty("contractEntry")).ToList();

        trueEntries.Should().HaveSameCount(falseEntries);
        foreach (var (verboseEntry, terseEntry) in trueEntries.Zip(falseEntries))
        {
            JsonElement.DeepEquals(verboseEntry, terseEntry).Should().BeTrue(
                "the corpus README records that the verbose flag leaves ACS payloads untouched");
        }
    }

    [Fact]
    public void WireSamplesCorpus_should_keep_the_nested_optional_probe_matrix_self_consistent()
    {
        using var document = LoadWireSample("probe_nested_optional_matrix.json");

        var acceptedCandidates = new List<string>();
        foreach (var candidate in document.RootElement.GetProperty("response").EnumerateObject())
        {
            if (candidate.Value.GetProperty("accepted").GetBoolean())
            {
                acceptedCandidates.Add(candidate.Name);
                JsonElement.DeepEquals(
                        candidate.Value.GetProperty("sent"),
                        candidate.Value.GetProperty("echoed"))
                    .Should().BeTrue($"the participant echoed accepted candidate '{candidate.Name}' back verbatim");
            }
            else
            {
                candidate.Value.GetProperty("error").GetString().Should().NotBeNullOrEmpty();
                candidate.Value.TryGetProperty("echoed", out _).Should().BeFalse();
            }
        }

        acceptedCandidates.Should().Equal("empty_array", "array_of_empty_array", "array_of_array_of_text");
    }

    [Fact]
    public void WireSamplesCorpus_should_fail_when_a_capture_is_not_consumed_by_these_tests()
    {
        var consumed = FileNamesOf(SupportedWireSamplePayloads)
            .Concat(FileNamesOf(UnsupportedWireSamplePayloads))
            .Concat(FactConsumedWireSamples)
            .Distinct();

        var captured = Directory.EnumerateFiles(CorpusDirectory, "*.json").Select(Path.GetFileName);

        captured.Should().BeEquivalentTo(
            consumed,
            "every capture under tests/wire-samples/data must be exercised by a reader test");
    }

    [Fact]
    public void ReadRecord_should_decode_the_captured_nested_optional_encodings_as_a_chain()
    {
        using var matrix = LoadWireSample("probe_nested_optional_matrix.json");
        var someNone = ResolvePayload(matrix.RootElement, "response/array_of_empty_array/sent");

        var record = DamlLfJsonReader.ReadRecord<NestedNoteHolder>(
            """{"nestedNote":""" + someNone.GetRawText() + "}");

        record.GetRequiredField("nestedNote")
            .Should().Be(DamlOptionalChain.Some(DamlOptionalChain.None));
    }

    [Fact]
    public void ReadRecord_should_distinguish_the_three_accepted_nested_optional_encodings()
    {
        DamlLfJsonReader.ReadRecord<NestedNoteHolder>("""{"nestedNote":[]}""")
            .GetRequiredField("nestedNote").Should().Be(DamlOptionalChain.None);
        DamlLfJsonReader.ReadRecord<NestedNoteHolder>("""{"nestedNote":[[]]}""")
            .GetRequiredField("nestedNote").Should().Be(DamlOptionalChain.Some(DamlOptionalChain.None));
        DamlLfJsonReader.ReadRecord<NestedNoteHolder>("""{"nestedNote":[["deep"]]}""")
            .GetRequiredField("nestedNote")
            .Should().Be(DamlOptionalChain.Some(DamlOptionalChain.Some(new DamlText("deep"))));
    }

    [Fact]
    public void ReadRecord_should_reject_every_nested_optional_encoding_the_participant_rejected()
    {
        var rejected = new[]
        {
            ("null", "Expected JSON Array at 'NestedNoteHolder.nestedNote' but found Null"),
            ("[null]", "Expected JSON Array at 'NestedNoteHolder.nestedNote[0]' but found Null"),
            ("""["deep"]""", "Expected JSON Array at 'NestedNoteHolder.nestedNote[0]' but found String"),
        };

        foreach (var (encoding, message) in rejected)
        {
            var act = () => DamlLfJsonReader.ReadRecord<NestedNoteHolder>(
                """{"nestedNote":""" + encoding + "}");

            act.Should().Throw<JsonException>($"the participant rejected {encoding} with HTTP 500")
                .WithMessage(message);
        }
    }

    [Fact]
    public void ReadRecord_should_reject_a_nested_optional_level_carrying_more_than_one_element()
    {
        var act = () => DamlLfJsonReader.ReadRecord<NestedNoteHolder>("""{"nestedNote":[[],[]]}""");

        act.Should().Throw<JsonException>().WithMessage(
            "A nested Daml Optional at 'NestedNoteHolder.nestedNote' encodes as an array of at most "
            + "one element but found 2");
    }

    [Fact]
    public void ReadRecord_should_name_the_chain_level_that_failed()
    {
        var outerLevel = () => DamlLfJsonReader.ReadRecord<DeepNoteHolder>("""{"deepNote":[null]}""");
        var innerLevel = () => DamlLfJsonReader.ReadRecord<DeepNoteHolder>("""{"deepNote":[[null]]}""");

        outerLevel.Should().Throw<JsonException>().WithMessage(
            "Expected JSON Array at 'DeepNoteHolder.deepNote[0]' but found Null");
        innerLevel.Should().Throw<JsonException>().WithMessage(
            "Expected JSON Array at 'DeepNoteHolder.deepNote[0][0]' but found Null");
    }

    [Fact]
    public void ReadRecord_should_name_the_chain_level_carrying_more_than_one_element()
    {
        var act = () => DamlLfJsonReader.ReadRecord<DeepNoteHolder>("""{"deepNote":[[[],[]]]}""");

        act.Should().Throw<JsonException>().WithMessage(
            "A nested Daml Optional at 'DeepNoteHolder.deepNote[0]' encodes as an array of at most "
            + "one element but found 2");
    }

    [Fact]
    public void ReadRecord_should_keep_a_flat_wrapper_slot_on_the_flat_encoding()
    {
        DamlLfJsonReader.ReadRecord<FlatNoteHolder>("""{"note":null}""")
            .GetRequiredField("note").Should().Be(DamlOptional.None);
        DamlLfJsonReader.ReadRecord<FlatNoteHolder>("""{"note":"present"}""")
            .GetRequiredField("note").Should().Be(DamlOptional.Some(new DamlText("present")));
    }

    [Fact]
    public void ReadRecord_should_read_a_wrapper_reached_through_a_generic_at_exactly_its_own_level()
    {
        var item = DamlLfJsonReader.ReadRecord<BoxedNoteHolder>("""{"boxed":{"item":"deep"}}""")
            .GetRequiredField("boxed").As<DamlRecord>()
            .GetRequiredField("item");

        item.Should().Be(
            DamlOptional.Some(new DamlText("deep")),
            "the notnull constraint the emitter puts on every generated type parameter keeps the "
            + "slot's read-state non-nullable, so only the wrapper the field actually carries is read");

        Optional<string>.FromValue(item, value => value.As<DamlText>().Value)
            .Should().Be(new Optional<string>.Some("deep"),
                "the generated FromRecord a caller hands this to has to be able to consume it");
    }

    [Fact]
    public void ReadRecord_should_read_every_level_of_a_chain_as_a_chain_level()
    {
        var innerAbsent = DamlLfJsonReader.ReadRecord<NestedNoteHolder>("""{"nestedNote":[[]]}""")
            .GetRequiredField("nestedNote").As<DamlOptionalChain>();

        innerAbsent.Value.Should().BeOfType<DamlOptionalChain>(
            "the interior level of a chain is CLR-identical to a flat wrapper and can only be "
            + "reached by recursion from the root; deciding it structurally reads it as flat");
    }

    private static IEnumerable<string> FileNamesOf(IEnumerable<ITheoryDataRow> rows) =>
        rows.Select(row => (string)row.GetData()[0]!);

    internal static JsonDocument LoadWireSample(string fileName) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(CorpusDirectory, fileName)));

    internal static JsonElement ResolvePayload(JsonElement root, string payloadPath)
    {
        var current = root;
        foreach (var segment in payloadPath.Split('/'))
        {
            current = int.TryParse(segment, out var index) ? current[index] : current.GetProperty(segment);
        }
        return current;
    }

    private static readonly (string Label, Type DamlType)[] WireRecordFields =
    [
        ("owner", typeof(DamlParty)),
        ("count", typeof(DamlInt64)),
        ("amount", typeof(DamlNumeric)),
        ("label", typeof(DamlText)),
        ("active", typeof(DamlBool)),
        ("asOf", typeof(DamlDate)),
        ("observedAt", typeof(DamlTimestamp)),
        ("note", typeof(DamlOptional)),
        ("nestedNote", typeof(DamlOptionalChain)),
        ("tags", typeof(DamlList)),
        ("attributes", typeof(DamlTextMap)),
        ("genMap", typeof(DamlGenMap)),
        ("unitField", typeof(DamlUnit)),
        ("marker", typeof(DamlContractId)),
        ("holdingCid", typeof(DamlContractId)),
        ("holdingCids", typeof(DamlList)),
        ("profile", typeof(DamlRecord)),
        ("outcome", typeof(DamlVariant)),
        ("suit", typeof(DamlEnum)),
        ("fee", typeof(DamlNumeric)),
    ];

    public static TheoryData<string, string, string, (string Label, Type DamlType)[]> SupportedWireSamplePayloads => new()
    {
        { "create_marker.json", "response/transaction/events/0/CreatedEvent/createArgument", nameof(Marker), [("owner", typeof(DamlParty))] },
        { "acs_wildcard_verbose_false.json", "response/0/contractEntry/JsActiveContract/createdEvent/createArgument", nameof(Marker), [("owner", typeof(DamlParty))] },
        { "acs_wildcard_verbose_true.json", "response/0/contractEntry/JsActiveContract/createdEvent/createArgument", nameof(Marker), [("owner", typeof(DamlParty))] },
        { "exercise_ping.json", "response/transaction/events/0/ExercisedEvent/choiceArgument", nameof(Ping), [] },
        { "create_asset_numeric_edges.json", "response/transaction/events/0/CreatedEvent/createArgument", nameof(Asset), [("issuer", typeof(DamlParty)), ("amount", typeof(DamlNumeric))] },
        { "create_keyed_contract_key.json", "response/transaction/events/0/CreatedEvent/createArgument", nameof(Keyed), [("owner", typeof(DamlParty)), ("label", typeof(DamlText))] },
        { "create_keyed_contract_key.json", "response/transaction/events/0/CreatedEvent/contractKey", nameof(KeyedKey), [("_1", typeof(DamlParty)), ("_2", typeof(DamlText))] },
        { "create_wirerecord_empty.json", "response/transaction/events/0/CreatedEvent/createArgument", nameof(WireRecord), WireRecordFields },
        { "create_wirerecord_populated.json", "response/transaction/events/0/CreatedEvent/createArgument", nameof(WireRecord), WireRecordFields },
        { "exercise_describe.json", "response/transaction/events/0/ExercisedEvent/choiceArgument", nameof(Describe), [("probe", typeof(DamlOptional))] },
        { "exercise_relabel.json", "response/transaction/events/0/ExercisedEvent/choiceArgument", nameof(Relabel), [("newLabel", typeof(DamlText))] },
        { "acs_interface_view_holding.json", "response/0/contractEntry/JsActiveContract/createdEvent/interfaceViews/0/viewValue", nameof(HoldingView), [("amount", typeof(DamlNumeric))] },
    };

    public static TheoryData<string, string, string, string> UnsupportedWireSamplePayloads => new()
    {
        { "exercise_describe.json", "response/transaction/events/0/ExercisedEvent/exerciseResult", nameof(Outcome), "at 'Outcome' is not a generated Daml record" },
        { "exercise_ping.json", "response/transaction/events/0/ExercisedEvent/exerciseResult", nameof(Unit), "at 'Unit' is not a generated Daml record" },
    };

    internal static readonly IReadOnlyDictionary<string, Type> DeclaredShapes = new Dictionary<string, Type>
    {
        [nameof(Marker)] = typeof(Marker),
        [nameof(Ping)] = typeof(Ping),
        [nameof(Asset)] = typeof(Asset),
        [nameof(Keyed)] = typeof(Keyed),
        [nameof(KeyedKey)] = typeof(KeyedKey),
        [nameof(WireRecord)] = typeof(WireRecord),
        [nameof(Relabel)] = typeof(Relabel),
        [nameof(Describe)] = typeof(Describe),
        [nameof(Outcome)] = typeof(Outcome),
        [nameof(Unit)] = typeof(Unit),
        [nameof(HoldingView)] = typeof(HoldingView),
    };

    private static NotSupportedException DecodeOnlyShape() =>
        new("Wire-sample shapes are decode-only stand-ins for the corpus's generated types.");

    public sealed record Marker([property: DamlFieldAttribute("owner")] Party Owner) : IDamlRecord, IDamlType
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new Identifier(WireTypesPackageId, WireTypesModuleName, nameof(Marker)),
            DamlTypeKind.Template,
            WireTypesPackageName);

        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("owner", Owner.ToDamlValue()));
    }

    public sealed record Ping : IDamlRecord
    {
        public DamlRecord ToRecord() => DamlRecord.Create();
    }

    public interface IHolding : IDamlType
    {
        static DamlTypeDescriptor IDamlType.DamlTypeId =>
            new(new Identifier(WireTypesPackageId, WireTypesModuleName, "Holding"),
                DamlTypeKind.Interface,
                WireTypesPackageName);
    }

    public sealed record Asset(
        [property: DamlFieldAttribute("issuer")] Party Issuer,
        [property: DamlFieldAttribute("amount")] decimal Amount) : IDamlRecord
    {
        public DamlRecord ToRecord() => throw DecodeOnlyShape();
    }

    public sealed record Keyed(
        [property: DamlFieldAttribute("owner")] Party Owner,
        [property: DamlFieldAttribute("label")] string Label) : IDamlRecord
    {
        public DamlRecord ToRecord() => throw DecodeOnlyShape();
    }

    public sealed record KeyedKey(
        [property: DamlFieldAttribute("_1")] Party Maintainer,
        [property: DamlFieldAttribute("_2")] string Label) : IDamlRecord
    {
        public DamlRecord ToRecord() => throw DecodeOnlyShape();
    }

    public sealed record TupleKeyHolder(
        [property: DamlFieldAttribute("key")] Tuple2<Party, string> Key) : IDamlRecord
    {
        public DamlRecord ToRecord() => throw DecodeOnlyShape();
    }

    public sealed record Profile(
        [property: DamlFieldAttribute("nickname")] string Nickname,
        [property: DamlFieldAttribute("level")] long Level) : IDamlRecord
    {
        public DamlRecord ToRecord() => throw DecodeOnlyShape();
    }

    public sealed record HoldingView(
        [property: DamlFieldAttribute("amount")] decimal Amount) : IDamlRecord
    {
        public DamlRecord ToRecord() => throw DecodeOnlyShape();
    }

    public sealed record WinDetails(
        [property: DamlFieldAttribute("prize")] decimal Prize,
        [property: DamlFieldAttribute("tier")] string Tier) : IDamlRecord
    {
        public DamlRecord ToRecord() => throw DecodeOnlyShape();
    }

    public abstract record Outcome : IDamlVariant
    {
        public DamlVariant ToVariant() => throw DecodeOnlyShape();

        public sealed record Win(WinDetails Details) : Outcome
        {
            public string Tag => "Win";
        }

        public sealed record Pending : Outcome
        {
            public string Tag => "Pending";
        }
    }

    public enum Suit
    {
        Clubs,
        Diamonds,
        Hearts,
        Spades,
    }

    public sealed record WireRecord(
        [property: DamlFieldAttribute("owner")] Party Owner,
        [property: DamlFieldAttribute("count")] long Count,
        [property: DamlFieldAttribute("amount")] decimal Amount,
        [property: DamlFieldAttribute("label")] string Label,
        [property: DamlFieldAttribute("active")] bool Active,
        [property: DamlFieldAttribute("asOf")] DateOnly AsOf,
        [property: DamlFieldAttribute("observedAt")] DateTimeOffset ObservedAt,
        [property: DamlFieldAttribute("note")] string? Note,
        [property: DamlFieldAttribute("nestedNote")] Optional<Optional<string>> NestedNote,
        [property: DamlFieldAttribute("tags")] IReadOnlyList<string> Tags,
        [property: DamlFieldAttribute("attributes")] IReadOnlyDictionary<string, string> Attributes,
        [property: DamlFieldAttribute("genMap")] IReadOnlyDictionary<Party, long> GenMap,
        [property: DamlFieldAttribute("unitField")] DamlUnit UnitField,
        [property: DamlFieldAttribute("marker")] ContractId<Marker> Marker,
        [property: DamlFieldAttribute("holdingCid")] ContractId<IHolding> HoldingCid,
        [property: DamlFieldAttribute("holdingCids")] IReadOnlyList<ContractId<IHolding>> HoldingCids,
        [property: DamlFieldAttribute("profile")] Profile Profile,
        [property: DamlFieldAttribute("outcome")] Outcome Outcome,
        [property: DamlFieldAttribute("suit")] Suit Suit,
        [property: DamlFieldAttribute("fee")] decimal Fee) : IDamlRecord
    {
        public DamlRecord ToRecord() => throw DecodeOnlyShape();
    }

    public sealed record Relabel(
        [property: DamlFieldAttribute("newLabel")] string NewLabel) : IDamlRecord
    {
        public DamlRecord ToRecord() => throw DecodeOnlyShape();
    }

    public sealed record Describe(
        [property: DamlFieldAttribute("probe")] long? Probe) : IDamlRecord
    {
        public DamlRecord ToRecord() => throw DecodeOnlyShape();
    }

    public sealed record NestedNoteHolder(
        [property: DamlFieldAttribute("nestedNote")] Optional<Optional<string>> NestedNote) : IDamlRecord
    {
        public DamlRecord ToRecord() => throw DecodeOnlyShape();
    }

    public sealed record DeepNoteHolder(
        [property: DamlFieldAttribute("deepNote")] Optional<Optional<Optional<string>>> DeepNote) : IDamlRecord
    {
        public DamlRecord ToRecord() => throw DecodeOnlyShape();
    }

    public sealed record FlatNoteHolder(
        [property: DamlFieldAttribute("note")] Optional<string> Note) : IDamlRecord
    {
        public DamlRecord ToRecord() => throw DecodeOnlyShape();
    }

    public sealed record Boxed<TA>(
        [property: DamlFieldAttribute("item")] TA Item) : IDamlRecord
        where TA : notnull
    {
        public DamlRecord ToRecord() => throw DecodeOnlyShape();
    }

    public sealed record BoxedNoteHolder(
        [property: DamlFieldAttribute("boxed")] Boxed<Optional<string>> Boxed) : IDamlRecord
    {
        public DamlRecord ToRecord() => throw DecodeOnlyShape();
    }
}
