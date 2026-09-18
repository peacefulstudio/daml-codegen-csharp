// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Daml.Runtime.Stdlib;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

/// <summary>
/// Pins <see cref="Daml.Runtime.Serialization.DiscriminatedUnionJson"/>'s depth behaviour on both
/// sides of the wire — see the type's remarks for why <see cref="DiscriminatedUnionJson.Write{TUnion}"/>
/// needs its own re-entrant guard while <see cref="DiscriminatedUnionJson.Read{TUnion}"/> does not.
/// </summary>
public class DiscriminatedUnionDepthTests
{
    private const int NestingLevelsPastDefaultMaxDepth = 70;
    private const int ExplicitMaxDepth = 8;

    private static readonly JsonSerializerOptions ExplicitSmallMaxDepthOptions = new() { MaxDepth = ExplicitMaxDepth };
    private static readonly JsonSerializerOptions ExplicitLargeMaxDepthOptions =
        new() { MaxDepth = NestingLevelsPastDefaultMaxDepth + 10 };
    private static readonly JsonSerializerOptions HonouredDeeperMaxDepthOptions = new() { MaxDepth = 200 };

    private static (object Value, Type DeclaredType) BuildNestedOptional(int levels)
    {
        object value = new Optional<int>.Some(1);
        var declaredType = typeof(Optional<int>);
        var openSome = typeof(Optional<>).GetNestedType("Some")!;
        for (var i = 0; i < levels; i++)
        {
            var closedSome = openSome.MakeGenericType(declaredType);
            value = closedSome.GetConstructors()[0].Invoke(new[] { value });
            declaredType = typeof(Optional<>).MakeGenericType(declaredType);
        }

        return (value, declaredType);
    }

    private sealed record Layer<T>(T Next);

    private static (object Value, Type DeclaredType) BuildOptionalHopsSeparatedByPlainLayers(int hops, int layersPerHop)
    {
        object value = new Optional<int>.Some(1);
        var declaredType = typeof(Optional<int>);
        var openSome = typeof(Optional<>).GetNestedType("Some")!;

        for (var hop = 0; hop < hops; hop++)
        {
            for (var layer = 0; layer < layersPerHop; layer++)
            {
                var layerType = typeof(Layer<>).MakeGenericType(declaredType);
                value = layerType.GetConstructors()[0].Invoke(new[] { value });
                declaredType = layerType;
            }

            var closedSome = openSome.MakeGenericType(declaredType);
            value = closedSome.GetConstructors()[0].Invoke(new[] { value });
            declaredType = typeof(Optional<>).MakeGenericType(declaredType);
        }

        return (value, declaredType);
    }

    [Fact]
    public void Optional_write_counts_ordinary_layers_between_nested_unions_toward_MaxDepth()
    {
        var (value, declaredType) = BuildOptionalHopsSeparatedByPlainLayers(hops: 2, layersPerHop: 3);

        var act = () => JsonSerializer.Serialize(value, declaredType, ExplicitSmallMaxDepthOptions);

        act.Should().Throw<JsonException>().WithMessage("*exceeded the maximum union nesting depth of 8*", because:
            "three ordinary Layer<T> records written between each pair of unions still consume real "
            + "call-stack depth through System.Text.Json's own recursive object writer even though "
            + "none of those hops is itself a re-entrant Write<TUnion> call a hop-only counter could "
            + "see; with only 3 union hops total the old counter stayed at 3, comfortably under an "
            + "explicit MaxDepth of 8, while the real cumulative nesting depth had already reached 8, "
            + "so a caller relying on MaxDepth to bound worst-case stack usage was not actually "
            + "protected until every level — union or not — counted toward the guard");
    }

    [Fact]
    public void Optional_write_refuses_to_exceed_the_default_MaxDepth_instead_of_risking_a_stack_overflow()
    {
        var (value, declaredType) = BuildNestedOptional(NestingLevelsPastDefaultMaxDepth);

        var act = () => JsonSerializer.Serialize(value, declaredType);

        act.Should().Throw<JsonException>().WithMessage("*exceeded the maximum union nesting depth of 64*", because:
            "System.Text.Json.SerializeToNode starts a fresh serialization per arm, so its own "
            + "MaxDepth check only runs once the whole nested node tree is already built in memory — "
            + "deep enough nesting can exhaust the real call stack before that check ever executes; "
            + "DiscriminatedUnionJson's own re-entrant counter must catch it first, well short of "
            + "that danger zone, at System.Text.Json's own default MaxDepth of 64");
    }

    [Fact]
    public void Optional_write_enforces_an_explicit_MaxDepth_smaller_than_its_own_default()
    {
        var (value, declaredType) = BuildNestedOptional(ExplicitMaxDepth + 2);

        var act = () => JsonSerializer.Serialize(value, declaredType, ExplicitSmallMaxDepthOptions);

        act.Should().Throw<JsonException>().WithMessage("*exceeded the maximum union nesting depth of 8*", because:
            "Write must honour a caller-supplied JsonSerializerOptions.MaxDepth exactly as it honours "
            + "System.Text.Json's own default, not only the default itself");
    }

    [Fact]
    public void Optional_write_honours_a_caller_supplied_MaxDepth_deeper_than_its_own_default_instead_of_capping_at_it()
    {
        var (value, declaredType) = BuildNestedOptional(100);

        var act = () => JsonSerializer.Serialize(value, declaredType, HonouredDeeperMaxDepthOptions);

        act.Should().NotThrow(because:
            "a caller who raises JsonSerializerOptions.MaxDepth past its default of 64 opts into deeper "
            + "recursion exactly as System.Text.Json itself allows; Write must not silently cap at 64 "
            + "regardless of what the caller asked for");
    }

    [Fact]
    public void Optional_read_relies_on_System_Text_Json_own_reader_depth_guard_past_the_default_MaxDepth()
    {
        var (value, declaredType) = BuildNestedOptional(NestingLevelsPastDefaultMaxDepth);
        var json = JsonSerializer.Serialize(value, declaredType, ExplicitLargeMaxDepthOptions);

        var act = () => JsonSerializer.Deserialize(json, declaredType);

        act.Should().Throw<JsonException>().WithMessage("*maximum configured depth of 64*", because:
            "DiscriminatedUnionJson.Read has no depth guard of its own — JsonDocument.ParseValue(ref "
            + "reader) continues the ambient Utf8JsonReader's own token-depth tracking, so System.Text."
            + "Json's own reader depth guard (threaded from JsonSerializerOptions.MaxDepth, default 64) "
            + "already throws before any of this type's code runs");
    }

    [Fact]
    public void Optional_read_enforces_an_explicit_MaxDepth_smaller_than_the_default_via_System_Text_Json_itself()
    {
        var (value, declaredType) = BuildNestedOptional(ExplicitMaxDepth + 2);
        var json = JsonSerializer.Serialize(value, declaredType);

        var act = () => JsonSerializer.Deserialize(json, declaredType, ExplicitSmallMaxDepthOptions);

        act.Should().Throw<JsonException>().WithMessage("*maximum configured depth of 8*", because:
            "System.Text.Json's reader depth guard must honour an explicit MaxDepth exactly as it "
            + "would for any other type, with no help needed from DiscriminatedUnionJson");
    }
}
