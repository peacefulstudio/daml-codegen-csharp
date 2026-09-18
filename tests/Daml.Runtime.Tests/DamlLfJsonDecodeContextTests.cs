// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Runtime.Tests;

public class DamlLfJsonDecodeContextTests
{
    private const int MaximumSupportedDepth = 128;

    [Fact]
    public void Root_should_expose_the_supplied_path_at_depth_zero()
    {
        var context = DamlLfJsonDecodeContext.Root("Widget");

        context.Path.Should().Be("Widget");
        context.Depth.Should().Be(0);
    }

    [Fact]
    public void Root_should_default_to_the_shared_hardened_limits_when_none_are_supplied()
    {
        var context = DamlLfJsonDecodeContext.Root("Widget");

        context.Limits.Should().Be(new DamlJsonDeserializationLimits(16 * 1024 * 1024, 100_000));
    }

    [Fact]
    public void Root_should_expose_the_supplied_limits_when_given()
    {
        var limits = new DamlJsonDeserializationLimits(MaxInputCharacters: 4096, MaxArrayElements: 8);

        var context = DamlLfJsonDecodeContext.Root("Widget", limits);

        context.Limits.Should().Be(limits);
    }

    [Fact]
    public void Root_should_reject_a_null_path()
    {
        var act = () => DamlLfJsonDecodeContext.Root(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("path");
    }

    [Fact]
    public void Root_should_reject_limits_with_a_non_positive_MaxInputCharacters()
    {
        var act = () => DamlLfJsonDecodeContext.Root("Widget", new DamlJsonDeserializationLimits(MaxInputCharacters: 0));

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("limits");
    }

    [Fact]
    public void Root_should_reject_limits_with_a_negative_MaxArrayElements()
    {
        var act = () => DamlLfJsonDecodeContext.Root("Widget", new DamlJsonDeserializationLimits(MaxArrayElements: -1));

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("limits");
    }

    [Fact]
    public void Root_should_accept_limits_with_a_zero_MaxArrayElements()
    {
        var context = DamlLfJsonDecodeContext.Root("Widget", new DamlJsonDeserializationLimits(MaxArrayElements: 0));

        context.Limits.Should().Be(new DamlJsonDeserializationLimits(16 * 1024 * 1024, 0));
    }

    [Fact]
    public void Field_should_preserve_a_zero_MaxArrayElements_from_a_Root_context_instead_of_falling_back()
    {
        var root = DamlLfJsonDecodeContext.Root("Widget", new DamlJsonDeserializationLimits(MaxArrayElements: 0));

        var field = root.Field("items");

        field.Limits.Should().Be(new DamlJsonDeserializationLimits(16 * 1024 * 1024, 0));
    }

    [Fact]
    public void Default_should_fall_back_to_the_root_path_and_shared_hardened_limits()
    {
        var context = default(DamlLfJsonDecodeContext);

        context.Path.Should().Be("$");
        context.Depth.Should().Be(0);
        context.Limits.Should().Be(new DamlJsonDeserializationLimits(16 * 1024 * 1024, 100_000));
    }

    [Fact]
    public void Constructor_should_fall_back_to_the_root_path_and_shared_hardened_limits()
    {
        var context = new DamlLfJsonDecodeContext();

        context.Path.Should().Be("$");
        context.Depth.Should().Be(0);
        context.Limits.Should().Be(new DamlJsonDeserializationLimits(16 * 1024 * 1024, 100_000));
    }

    [Fact]
    public void Field_should_append_to_the_fallback_root_path_when_starting_from_a_default_context()
    {
        var context = default(DamlLfJsonDecodeContext);

        var field = context.Field("count");

        field.Path.Should().Be("$.count");
        field.Depth.Should().Be(1);
        field.Limits.Should().Be(new DamlJsonDeserializationLimits(16 * 1024 * 1024, 100_000));
    }

    [Fact]
    public void Field_should_append_a_dot_and_the_label_to_the_path_and_increment_depth()
    {
        var root = DamlLfJsonDecodeContext.Root("Widget");

        var field = root.Field("count");

        field.Path.Should().Be("Widget.count");
        field.Depth.Should().Be(1);
        field.Limits.Should().Be(root.Limits);
    }

    [Fact]
    public void Element_should_append_the_bracketed_index_to_the_path_and_increment_depth()
    {
        var root = DamlLfJsonDecodeContext.Root("Widget.items");

        var element = root.Element(2);

        element.Path.Should().Be("Widget.items[2]");
        element.Depth.Should().Be(1);
        element.Limits.Should().Be(root.Limits);
    }

    [Fact]
    public void Field_and_Element_should_chain_to_build_a_nested_path()
    {
        var root = DamlLfJsonDecodeContext.Root("Widget");

        var nested = root.Field("items").Element(0).Field("label");

        nested.Path.Should().Be("Widget.items[0].label");
        nested.Depth.Should().Be(3);
    }

    [Fact]
    public void Field_should_allow_exactly_the_maximum_supported_depth()
    {
        var context = DamlLfJsonDecodeContext.Root("Widget");
        for (var level = 0; level < MaximumSupportedDepth; level++)
        {
            context = context.Field("f");
        }

        context.Depth.Should().Be(128);
    }

    [Fact]
    public void Field_should_reject_one_level_past_the_maximum_supported_depth()
    {
        var context = DamlLfJsonDecodeContext.Root("Widget");
        for (var level = 0; level < MaximumSupportedDepth; level++)
        {
            context = context.Field("f");
        }

        var act = () => context.Field("f");

        act.Should().Throw<JsonException>().WithMessage("Value nesting exceeds the maximum supported depth of 128");
    }

    [Fact]
    public void Element_should_reject_one_level_past_the_maximum_supported_depth()
    {
        var context = DamlLfJsonDecodeContext.Root("Widget");
        for (var level = 0; level < MaximumSupportedDepth; level++)
        {
            context = context.Element(level);
        }

        var act = () => context.Element(0);

        act.Should().Throw<JsonException>().WithMessage("Value nesting exceeds the maximum supported depth of 128");
    }
}
