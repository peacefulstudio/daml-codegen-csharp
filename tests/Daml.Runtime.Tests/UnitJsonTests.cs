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
/// Pins the <see cref="System.Text.Json"/> contract for <see cref="Unit"/>: an empty JSON
/// object that reads back as <see cref="Unit.Value"/>. Without its converter the private
/// constructor leaves the reflection-based deserializer nothing to call — see
/// <see cref="Unit"/>'s remarks.
/// </summary>
public class UnitJsonTests
{
    private static readonly JsonSerializerOptions Registered = new JsonSerializerOptions().AddDamlConverters();

    private sealed record Wrapper(string Name, Unit Outcome);

    [Fact]
    public void Unit_reads_its_own_write_back()
    {
        var written = JsonSerializer.Serialize(Unit.Value);

        JsonSerializer.Deserialize<Unit>(written).Should().Be(Unit.Value);
    }

    [Fact]
    public void Unit_writes_the_empty_object()
    {
        var json = JsonSerializer.Serialize(Unit.Value);

        json.Should().Be("{}");
    }

    [Fact]
    public void Unit_reads_the_empty_object_literal()
    {
        JsonSerializer.Deserialize<Unit>("{}").Should().Be(Unit.Value);
    }

    [Fact]
    public void Unit_round_trips_inside_a_record_on_options_that_register_nothing()
    {
        var wrapper = new Wrapper("alice", Unit.Value);

        var written = JsonSerializer.Serialize(wrapper);

        JsonSerializer.Deserialize<Wrapper>(written).Should().Be(wrapper);
    }

    [Fact]
    public void Unit_round_trips_inside_a_record_under_AddDamlConverters()
    {
        var wrapper = new Wrapper("alice", Unit.Value);

        var written = JsonSerializer.Serialize(wrapper, Registered);

        JsonSerializer.Deserialize<Wrapper>(written, Registered).Should().Be(wrapper);
    }

    [Fact]
    public void Unit_refuses_a_payload_that_is_not_an_object()
    {
        var act = () => JsonSerializer.Deserialize<Unit>("\"c\"");

        act.Should().Throw<JsonException>().WithMessage("*Expected a JSON object for Unit*");
    }

    [Fact]
    public void Unit_ignores_members_a_forward_compatible_producer_might_add()
    {
        JsonSerializer.Deserialize<Unit>("""{"future":"field"}""").Should().Be(Unit.Value);
    }

    [Fact]
    public void Unit_writes_the_empty_object_even_under_a_property_naming_policy()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        var json = JsonSerializer.Serialize(Unit.Value, options);

        json.Should().Be("{}", "Unit writes no property for a naming policy to rename");
        JsonSerializer.Deserialize<Unit>(json, options).Should().Be(Unit.Value);
    }

    [Fact]
    public void Unit_ignores_JsonUnmappedMemberHandling_Disallow_because_its_converter_bypasses_the_reflection_contract()
    {
        var options = new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

        var written = JsonSerializer.Serialize(Unit.Value, options);

        written.Should().Be("{}", "Unit's own write declares no member, so Disallow has nothing to reject");
        JsonSerializer.Deserialize<Unit>("""{"future":"field"}""", options).Should().Be(
            Unit.Value,
            "UnitJsonConverter.Read walks and skips every member itself rather than deserializing "
            + "through System.Text.Json's reflection contract, so UnmappedMemberHandling never reaches "
            + "it; this is the same tolerant read as the no-options case above, pinned again under the "
            + "stricter options to document it as intentional rather than an accidental gap");
    }

    public static IEnumerable<object[]> ReferenceHandlers =>
    [
        [ReferenceHandler.Preserve],
        [ReferenceHandler.IgnoreCycles],
    ];

    [Theory]
    [MemberData(nameof(ReferenceHandlers))]
    public void Unit_round_trips_under_every_ReferenceHandler(ReferenceHandler handler)
    {
        var options = new JsonSerializerOptions { ReferenceHandler = handler };

        var written = JsonSerializer.Serialize(Unit.Value, options);

        written.Should().Be(
            "{}", "UnitJsonConverter.Write never makes a nested JsonSerializer call, so there is no "
            + "independent reference resolver for a ReferenceHandler to disrupt");
        JsonSerializer.Deserialize<Unit>(written, options).Should().Be(Unit.Value);
    }

    [Theory]
    [MemberData(nameof(ReferenceHandlers))]
    public void Unit_round_trips_two_aliased_references_under_every_ReferenceHandler(ReferenceHandler handler)
    {
        var options = new JsonSerializerOptions { ReferenceHandler = handler };
        var wrapper = new TwoUnits(Unit.Value, Unit.Value);

        var written = JsonSerializer.Serialize(wrapper, options);

        JsonSerializer.Deserialize<TwoUnits>(written, options).Should().Be(
            wrapper,
            "two references to the same Unit.Value singleton are exactly the aliasing shape "
            + "ReferenceHandler.Preserve exists to track, and Unit's converter must not be disrupted "
            + "by it either way");
    }

    private sealed record TwoUnits(Unit A, Unit B);
}
