// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Daml.Runtime.Commands;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class ChoiceNameTests
{
    [Fact]
    public void Construct_should_store_value_verbatim()
    {
        var name = new ChoiceName("Transfer");
        name.Value.Should().Be("Transfer");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Construct_should_throw_when_value_is_null_empty_or_whitespace(string? value)
    {
        Action act = () => _ = new ChoiceName(value!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Default_uninitialized_value_should_throw_on_Value_access()
    {
        var defaulted = default(ChoiceName);
        Action act = () => _ = defaulted.Value;
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Explicit_conversion_to_string_should_return_value()
    {
        var name = new ChoiceName("Transfer");
        var s = (string)name;
        s.Should().Be("Transfer");
    }

    [Fact]
    public void Explicit_conversion_from_string_should_construct()
    {
        var name = (ChoiceName)"Transfer";
        name.Value.Should().Be("Transfer");
    }

    [Fact]
    public void Json_should_serialize_as_a_bare_string()
    {
        JsonSerializer.Serialize(new ChoiceName("Transfer")).Should().Be("\"Transfer\"");
    }

    [Fact]
    public void Json_should_read_back_the_name_it_wrote()
    {
        var restored = JsonSerializer.Deserialize<ChoiceName>(JsonSerializer.Serialize(new ChoiceName("Transfer")));

        restored.Value.Should().Be("Transfer");
    }

    [Fact]
    public void Json_should_throw_on_empty_string()
    {
        var act = () => JsonSerializer.Deserialize<ChoiceName>("\"\"");

        act.Should().Throw<JsonException>().WithMessage("*ChoiceName*empty*");
    }

    [Fact]
    public void Json_should_throw_on_whitespace_only_string()
    {
        var act = () => JsonSerializer.Deserialize<ChoiceName>("\"   \"");

        act.Should().Throw<JsonException>().WithMessage("*ChoiceName*whitespace*");
    }

    [Fact]
    public void Json_should_throw_on_non_string_token()
    {
        var act = () => JsonSerializer.Deserialize<ChoiceName>("1");

        act.Should().Throw<JsonException>().WithMessage("*ChoiceName*Number*");
    }

    [Fact]
    public void Json_should_throw_on_null_for_a_non_nullable_member()
    {
        var act = () => JsonSerializer.Deserialize<Holder>("{\"Name\":null}");

        act.Should().Throw<JsonException>().WithMessage("*ChoiceName*Null*");
    }

    [Fact]
    public void Json_should_read_a_member_back_with_the_name_it_wrote()
    {
        var holder = new Holder(new ChoiceName("Transfer"));

        var restored = JsonSerializer.Deserialize<Holder>(JsonSerializer.Serialize(holder));

        restored.Should().Be(holder);
    }

    private sealed record Holder(ChoiceName Name);
}
