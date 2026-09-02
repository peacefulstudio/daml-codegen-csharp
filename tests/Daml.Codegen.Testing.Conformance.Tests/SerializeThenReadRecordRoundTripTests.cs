// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.Contractkeys;
using Daml.Codegen.Testing.Conformance.Richtypes;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Daml.Runtime.Stdlib;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

/// <summary>
/// Pipes the writer's output straight into the typed reader for the whole conformance corpus.
/// The writer and the untyped reader form a self-consistent pair, so only crossing the seam to
/// the typed reader — the one the ledger's own grammar is pinned against — can catch a wire-shape
/// divergence.
/// </summary>
public class SerializeThenReadRecordRoundTripTests
{
    private static readonly IReadOnlyDictionary<string, (Type RecordType, DamlRecord Record)> Corpus =
        new Dictionary<string, (Type, DamlRecord)>(StringComparer.Ordinal)
        {
            [nameof(RichRecord)] = (typeof(RichRecord), RichRecordSample(new Outcome.Win(
                new Outcome_Win(Prize: 12.34m, Tier: "gold"))).ToRecord()),
            ["RichRecord_nullary_variant_arm"] = (typeof(RichRecord),
                RichRecordSample(new Outcome.Pending()).ToRecord()),
            [nameof(TypeCorners)] = (typeof(TypeCorners), TypeCornersSample().ToRecord()),
            [nameof(Profile)] = (typeof(Profile), new Profile("ace", 7).ToRecord()),
            [nameof(Outcome_Win)] = (typeof(Outcome_Win), new Outcome_Win(Prize: 12.34m, Tier: "gold").ToRecord()),
            [nameof(Account)] = (typeof(Account),
                new Account(new Party("alice"), "savings", 1_000).ToRecord()),
            [nameof(AccountKey)] = (typeof(AccountKey), new AccountKey(new Party("alice"), "savings").ToRecord()),
        };

    /// <summary>
    /// The generated generic records <c>Box</c>, <c>Crate</c> and <c>Slot</c> take their field
    /// converters as arguments, so the emitter cannot put <c>IDamlRecord</c> on them and the typed
    /// reader has no slot mapping for them. <c>TypeCorners</c> carries three of them, which puts the
    /// whole template outside the reader today; the gap is pinned here so that closing it fails this
    /// test and forces <c>TypeCorners</c> into the round-trip theory.
    /// </summary>
    private const string TypeCornersReaderGap =
        "CLR type 'Daml.Codegen.Testing.Conformance.Richtypes.Box`1[System.String]' at "
        + "'TypeCorners.boxedText' lies outside the Daml type mapping";

    public static TheoryData<string> RoundTrippableCorpusEntries =>
        [.. Corpus.Keys.Where(entry => entry != nameof(TypeCorners))];

    [Theory]
    [MemberData(nameof(RoundTrippableCorpusEntries))]
    public void ReadRecord_round_trips_the_writer_output_for_every_corpus_record(string entry)
    {
        var (recordType, record) = Corpus[entry];

        var restored = DamlLfJsonReader.ReadRecord(DamlJsonSerializer.Serialize(record), recordType);

        restored.Should().Be(WithoutTypeIdentifiers(record));
    }

    [Fact]
    public void ReadRecord_pins_the_reader_gap_that_keeps_TypeCorners_out_of_the_round_trip()
    {
        var act = () => DamlLfJsonReader.ReadRecord<TypeCorners>(
            DamlJsonSerializer.Serialize(Corpus[nameof(TypeCorners)].Record));

        act.Should().Throw<NotSupportedException>().WithMessage($"*{TypeCornersReaderGap}*");
    }

    [Fact]
    public void SerializeThenReadRecordRoundTrip_covers_every_arm_of_the_writer_switch()
    {
        var reached = Corpus.Values
            .SelectMany(entry => ValuesReachableFrom(entry.Record))
            .Select(value => value.GetType().Name)
            .Distinct();

        reached.Should().BeEquivalentTo(
            WriterSwitchArms(),
            "the corpus is only a wire-grammar gate for the arms it actually exercises, and a new "
            + "DamlValue arm that no corpus record reaches would ship unchecked");
    }

    private static IEnumerable<string> WriterSwitchArms() =>
        typeof(DamlValue).Assembly.GetTypes()
            .Where(type => type.IsSubclassOf(typeof(DamlValue)) && !type.IsAbstract)
            .Select(type => type.Name);

    private static IEnumerable<DamlValue> ValuesReachableFrom(DamlValue value) =>
        [value, .. DirectChildrenOf(value).SelectMany(ValuesReachableFrom)];

    private static IEnumerable<DamlValue> DirectChildrenOf(DamlValue value) => value switch
    {
        DamlRecord record => record.Fields.Select(field => field.Value),
        DamlVariant variant => [variant.Value],
        DamlList list => list.Values,
        DamlTextMap map => map.Values.Values,
        DamlGenMap map => map.Entries.SelectMany(entry => new[] { entry.Key, entry.Value }),
        DamlOptional { Value: { } inner } => [inner],
        DamlOptionalChain { Value: { } inner } => [inner],
        _ => [],
    };

    /// <summary>
    /// Drops the Daml type identifiers the LF-JSON wire never carries, so the two sides of the
    /// round trip are compared on wire content alone. <c>ContractId&lt;T&gt;.ToDamlValue()</c>
    /// stamps a template id and the generated enum and variant converters may stamp a type id;
    /// the reader can only ever produce them unstamped.
    /// </summary>
    private static DamlValue WithoutTypeIdentifiers(DamlValue value) => value switch
    {
        DamlRecord record => new DamlRecord(null,
            [.. record.Fields.Select(field => new DamlField(field.Label, WithoutTypeIdentifiers(field.Value)))]),
        DamlVariant variant => new DamlVariant(null, variant.Constructor, WithoutTypeIdentifiers(variant.Value)),
        DamlEnum enumValue => new DamlEnum(null, enumValue.Constructor),
        DamlContractId contractId => new DamlContractId(contractId.Value),
        DamlList list => new DamlList([.. list.Values.Select(WithoutTypeIdentifiers)]),
        DamlTextMap map => new DamlTextMap(map.Values.ToDictionary(
            entry => entry.Key, entry => WithoutTypeIdentifiers(entry.Value))),
        DamlGenMap map => new DamlGenMap([.. map.Entries.Select(entry =>
            (WithoutTypeIdentifiers(entry.Key), WithoutTypeIdentifiers(entry.Value)))]),
        DamlOptional { Value: { } inner } => new DamlOptional(WithoutTypeIdentifiers(inner)),
        DamlOptionalChain { Value: { } inner } => new DamlOptionalChain(WithoutTypeIdentifiers(inner)),
        _ => value,
    };

    private static RichRecord RichRecordSample(Outcome outcome) => new(
        Owner: new Party("alice"),
        Count: 42,
        Amount: 19.95m,
        Label: "first",
        Active: true,
        AsOf: new DateOnly(2026, 6, 4),
        ObservedAt: new DateTimeOffset(2026, 6, 4, 12, 30, 0, TimeSpan.Zero),
        Note: "hello",
        Tags: ["a", "b"],
        Attributes: new Dictionary<string, string> { ["k1"] = "v1", ["k2"] = "v2" },
        Marker: new ContractId<Marker>("marker-cid"),
        HoldingCid: new ContractId<IHolding>("00aabbccddeeff00112233445566778899aabbccddeeff00112233445566778899"),
        HoldingCids:
        [
            new ContractId<IHolding>("0011112222333344445555666677778888999900001111222233334444555566aa"),
        ],
        Profile: new Profile("ace", 7),
        Outcome: outcome,
        Suit: Suit.Hearts,
        Fee: 1.5m);

    private static TypeCorners TypeCornersSample() => new(
        Owner: new Party("alice"),
        BoxedText: new Box<string>("boxed"),
        BoxedProfile: new Box<Profile>(new Profile("ace", 7)),
        Slot: new Slot<long>.Filled(11),
        NestedNote: new Box<Optional<string>>(new Optional<string>.Some("inner")),
        MaybeMaybeNote: new Optional<Optional<string>>.Some(new Optional<string>.Some("nested")),
        Crate: new Crate<string>(new Optional<string>.Some("crated")),
        QuotaByParty: new Dictionary<Party, long> { [new Party("alice")] = 1 },
        LabelByRank: new Dictionary<long, string> { [1] = "gold" },
        RankOrLabel: new Either<long, string>.Right("runner-up"),
        NoteOrRank: new Either<Optional<string>, long>.Left(new Optional<string>.Some("noted")),
        Pair: new Tuple2<string, long>("pair", 3),
        Triple: new Tuple3<string, long, bool>("triple", 4, true),
        Branch: new Branch("root", [new Branch("leaf", [])]),
        Whole: 42m,
        Finest: 0.5m);
}
