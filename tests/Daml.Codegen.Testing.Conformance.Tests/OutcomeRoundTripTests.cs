// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using FluentAssertions;
using Daml.Codegen.Testing.Conformance.Richtypes;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

public class OutcomeRoundTripTests
{
    [Fact]
    public void win_constructor_round_trips_through_DamlVariant()
    {
        Outcome original = new Outcome.Win(new Outcome_Win(Prize: 12.34m, Tier: "gold"));

        var restored = Outcome.FromVariant(original.ToVariant());

        restored.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void pending_constructor_round_trips_through_DamlVariant()
    {
        Outcome original = new Outcome.Pending();

        var restored = Outcome.FromVariant(original.ToVariant());

        restored.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void win_variant_serializes_to_tag_value_wire_shape()
    {
        Outcome outcome = new Outcome.Win(new Outcome_Win(Prize: 1.5m, Tier: "gold"));

        var json = DamlJsonSerializer.Serialize(outcome.ToVariant());

        json.Should().Be("""{"tag":"Win","value":{"prize":"1.5","tier":"gold"}}""");
    }

    [Fact]
    public void pending_variant_serializes_to_tag_value_wire_shape()
    {
        Outcome outcome = new Outcome.Pending();

        var json = DamlJsonSerializer.Serialize(outcome.ToVariant());

        json.Should().Be("""{"tag":"Pending","value":{}}""");
    }

    [Fact]
    public void pending_round_trips_through_json_deserialization()
    {
        Outcome original = new Outcome.Pending();

        var json = DamlJsonSerializer.Serialize(original.ToVariant());
        var restored = Outcome.FromVariant(DamlJsonSerializer.Deserialize(json).As<DamlVariant>());

        restored.Should().Be(original);
    }
}
