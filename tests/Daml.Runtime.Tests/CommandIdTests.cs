// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class CommandIdTests
{
    [Fact]
    public void Construct_should_store_value_verbatim()
    {
        var id = new CommandId("cmd-1");
        id.Value.Should().Be("cmd-1");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Construct_should_throw_when_value_is_null_empty_or_whitespace(string? value)
    {
        Action act = () => _ = new CommandId(value!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Default_uninitialized_value_should_throw_on_Value_access()
    {
        var defaulted = default(CommandId);
        Action act = () => _ = defaulted.Value;
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Explicit_conversion_to_string_should_return_value()
    {
        var id = new CommandId("cmd-1");
        var s = (string)id;
        s.Should().Be("cmd-1");
    }

    [Fact]
    public void Explicit_conversion_from_string_should_construct()
    {
        var id = (CommandId)"cmd-1";
        id.Value.Should().Be("cmd-1");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromWire_should_return_null_when_wire_value_is_null_empty_or_whitespace(string? value)
    {
        (CommandId.FromWire(value) is null).Should().BeTrue();
    }

    [Fact]
    public void FromWire_should_round_trip_a_non_blank_wire_value_through_Value()
    {
        var id = CommandId.FromWire("cmd-1");

        (id is null).Should().BeFalse();
        id!.Value.Value.Should().Be("cmd-1");
    }

    [Fact]
    public void FromWire_should_store_a_padded_value_verbatim()
    {
        var id = CommandId.FromWire("  cmd-1  ");

        id!.Value.Value.Should().Be("  cmd-1  ");
    }

    [Fact]
    public void Default_returned_from_a_CommandId_typed_helper_should_wrap_non_null_unlike_FromWire()
    {
        static CommandId LegacyTransportHelper(string wire) =>
            wire.Length == 0 ? default : (CommandId)wire;

        CommandId? viaHelper = LegacyTransportHelper("");
        Action readViaHelper = () => _ = viaHelper?.Value;

        (viaHelper is null).Should().BeFalse();
        readViaHelper.Should().Throw<InvalidOperationException>();
        (CommandId.FromWire("") is null).Should().BeTrue();
    }
}
