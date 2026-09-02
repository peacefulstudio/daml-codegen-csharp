// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.Richtypes;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

public class SuitRoundTripTests
{
    [Theory]
    [InlineData(Suit.Clubs)]
    [InlineData(Suit.Diamonds)]
    [InlineData(Suit.Hearts)]
    [InlineData(Suit.Spades)]
    public void SuitRoundTrip_every_constructor_round_trips_through_DamlEnum(Suit original)
    {
        var restored = SuitExtensions.FromDamlEnum(original.ToDamlEnum());

        restored.Should().Be(original);
    }

    [Fact]
    public void SuitRoundTrip_clubs_serializes_to_bare_string_wire_shape()
    {
        var json = DamlJsonSerializer.Serialize(Suit.Clubs.ToDamlEnum());

        json.Should().Be("\"Clubs\"");
    }

    [Fact]
    public void SuitRoundTrip_spades_round_trips_through_json_deserialization_given_the_wire_type_back()
    {
        Suit original = Suit.Spades;

        var json = DamlJsonSerializer.Serialize(original.ToDamlEnum());
        var constructor = DamlJsonSerializer.Deserialize(json).As<DamlText>().Value;
        var restored = SuitExtensions.FromDamlEnum(DamlEnum.Create(constructor));

        restored.Should().Be(original);
    }

    [Fact]
    public void FromDamlEnum_throws_for_an_unrecognized_constructor()
    {
        var act = () => SuitExtensions.FromDamlEnum(DamlEnum.Create("Joker"));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ToDamlEnum_throws_for_a_value_outside_the_declared_range()
    {
        var invalid = (Suit)99;

        var act = () => invalid.ToDamlEnum();

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
