// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using Daml.Runtime.Serialization;
using Daml.Runtime.Stdlib;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

/// <summary>
/// Pins the <see cref="System.Text.Json"/> contract for <see cref="Map{TKey, TValue}"/>: the object
/// <see cref="System.Text.Json"/> derives from its members, read back as the map that wrote it. The
/// contract is a CLR round trip, not the Daml-LF record <c>{"map":[["a",1]]}</c> the ledger
/// encoding uses — see ADR 0028. What <see cref="DamlJsonConverters.AddDamlConverters"/> adds on top
/// is requiredness: a member the payload omits is refused rather than bound to a null the slot
/// forbids.
/// </summary>
public class MapJsonTests
{
    private static readonly JsonSerializerOptions Registered =
        new JsonSerializerOptions().AddDamlConverters();

    private static readonly JsonSerializerOptions HostBuiltWithoutDamlConverters = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new JsonStringEnumConverter() },
    };

    private static Map<string, long> Balances =>
        new([new("alice", 10L), new("bob", 20L)]);

    private sealed record Book(string Name, Map<string, long> Balances);

    private sealed record OptionalBook(string Name, Map<string, long>? Balances);

    [Fact]
    public void Map_round_trips_inside_a_record_under_AddDamlConverters()
    {
        var book = new Book("ledger", Balances);

        var written = JsonSerializer.Serialize(book, Registered);

        JsonSerializer.Deserialize<Book>(written, Registered).Should().Be(book);
    }

    [Fact]
    public void Map_round_trips_inside_a_record_on_options_that_register_nothing()
    {
        var book = new Book("ledger", Balances);

        var written = JsonSerializer.Serialize(book, HostBuiltWithoutDamlConverters);

        JsonSerializer.Deserialize<Book>(written, HostBuiltWithoutDamlConverters).Should().Be(book);
    }

    [Fact]
    public void Map_refuses_a_payload_that_omits_a_member_the_slot_forbids_a_null_in_under_AddDamlConverters()
    {
        var act = () => JsonSerializer.Deserialize<Book>("""{"Name":"ledger"}""", Registered);

        act.Should().Throw<JsonException>().WithMessage("*Balances*");
    }

    [Fact]
    public void Map_reads_an_omitted_member_as_the_absent_map_where_the_slot_permits_one()
    {
        var book = JsonSerializer.Deserialize<OptionalBook>("""{"Name":"ledger"}""", Registered);

        book!.Balances.Should().BeNull(
            "codegen renders Optional (Map k v) as a nullable Map<TKey, TValue> slot, so an omitted "
            + "member is the None the payload meant rather than a member it forgot");
    }

    [Fact]
    public void Map_reads_an_omitted_member_as_null_even_where_the_slot_forbids_one_on_options_that_register_nothing()
    {
        var book = JsonSerializer.Deserialize<Book>(
            """{"Name":"ledger"}""", HostBuiltWithoutDamlConverters);

        book!.Balances.Should().BeNull(
            "the modifier that marks the member required rides on AddDamlConverters, and options "
            + "that register nothing never install it");
    }
}
