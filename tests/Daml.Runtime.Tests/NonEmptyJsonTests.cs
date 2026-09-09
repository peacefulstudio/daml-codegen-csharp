// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Daml.Runtime.Stdlib;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

/// <summary>
/// Pins the <see cref="System.Text.Json"/> contract for <see cref="NonEmpty{T}"/>: the object
/// <see cref="System.Text.Json"/> derives from its <c>Hd</c> and <c>Tl</c> members, read back as the
/// list that wrote it. The contract is a CLR round trip, not the Daml-LF record the ledger encoding
/// uses — see ADR 0028. What <see cref="DamlJsonConverters.AddDamlConverters"/> adds on top is
/// requiredness: a member the payload omits is refused rather than bound to a null the slot forbids.
/// </summary>
public class NonEmptyJsonTests
{
    private static readonly Party Alice = new("Alice::122012ab");

    private static readonly Party Bob = new("Bob::122034cd");

    private static readonly JsonSerializerOptions Registered =
        new JsonSerializerOptions().AddDamlConverters();

    private static readonly JsonSerializerOptions HostBuiltWithoutDamlConverters = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed record Committee(string Name, NonEmpty<Party> Members);

    private sealed record OptionalCommittee(string Name, NonEmpty<Party>? Members);

    [Fact]
    public void NonEmpty_round_trips_inside_a_record_under_AddDamlConverters()
    {
        var committee = new Committee("board", new NonEmpty<Party>(Alice, [Bob]));

        var written = JsonSerializer.Serialize(committee, Registered);

        JsonSerializer.Deserialize<Committee>(written, Registered).Should().Be(committee);
    }

    [Fact]
    public void NonEmpty_round_trips_inside_a_record_on_options_that_register_nothing()
    {
        var committee = new Committee("board", new NonEmpty<Party>(Alice, [Bob]));

        var written = JsonSerializer.Serialize(committee, HostBuiltWithoutDamlConverters);

        JsonSerializer.Deserialize<Committee>(written, HostBuiltWithoutDamlConverters)
            .Should().Be(committee);
    }

    [Fact]
    public void NonEmpty_refuses_a_payload_that_omits_a_member_the_slot_forbids_a_null_in_under_AddDamlConverters()
    {
        var act = () => JsonSerializer.Deserialize<Committee>("""{"Name":"board"}""", Registered);

        act.Should().Throw<JsonException>().WithMessage("*Members*");
    }

    [Fact]
    public void NonEmpty_reads_an_omitted_member_as_the_absent_list_where_the_slot_permits_one()
    {
        var committee = JsonSerializer.Deserialize<OptionalCommittee>(
            """{"Name":"board"}""", Registered);

        committee!.Members.Should().BeNull(
            "codegen renders Optional (NonEmpty a) as a nullable NonEmpty<T> slot, so an omitted "
            + "member is the None the payload meant rather than a member it forgot");
    }

    [Fact]
    public void NonEmpty_reads_an_omitted_member_as_null_even_where_the_slot_forbids_one_on_options_that_register_nothing()
    {
        var committee = JsonSerializer.Deserialize<Committee>(
            """{"Name":"board"}""", HostBuiltWithoutDamlConverters);

        committee!.Members.Should().BeNull(
            "the modifier that marks the member required rides on AddDamlConverters, and options "
            + "that register nothing never install it");
    }
}
