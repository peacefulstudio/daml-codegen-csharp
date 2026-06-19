// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using FluentAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class PartyAnalysisTests
{
    private sealed record UnknownPartyReference : DamlPartyReference;

    private readonly PartyAnalysis _party = new();

    private static DamlPartyAnalysis StaticWith(params DamlPartyReference[] parties) =>
        DamlPartyAnalysis.Static(parties.ToList());

    private static DamlPartyAnalysis Static(params string[] fieldNames) =>
        DamlPartyAnalysis.Static(fieldNames.Select(n => (DamlPartyReference)new DamlPartyPayloadField(n)).ToList());

    private static IReadOnlyDictionary<string, DamlFieldDefinition> PartyFields(params string[] fieldNames) =>
        fieldNames.ToDictionary(
            n => n,
            n => new DamlFieldDefinition(n, new DamlPrimitiveType(DamlPrimitive.Party)),
            StringComparer.Ordinal);

    [Fact]
    public void union_of_two_static_sets_merges_payload_fields_in_declaration_order()
    {
        var result = _party.UnionStaticParties(Static("platform", "initiator"), Static("counterparty"));

        result.Source.Should().Be(DamlPartySource.Static);
        result.Parties.Should().Equal(
            new DamlPartyPayloadField("platform"),
            new DamlPartyPayloadField("initiator"),
            new DamlPartyPayloadField("counterparty"));
    }

    [Fact]
    public void union_dedups_a_field_named_on_both_sides_keeping_first_occurrence()
    {
        var result = _party.UnionStaticParties(Static("platform", "initiator"), Static("initiator", "counterparty"));

        result.Parties.Should().Equal(
            new DamlPartyPayloadField("platform"),
            new DamlPartyPayloadField("initiator"),
            new DamlPartyPayloadField("counterparty"));
    }

    [Fact]
    public void union_is_dynamic_when_the_left_side_is_dynamic()
    {
        var result = _party.UnionStaticParties(DamlPartyAnalysis.Dynamic, Static("counterparty"));

        result.Should().Be(DamlPartyAnalysis.Dynamic);
    }

    [Fact]
    public void union_is_dynamic_when_the_right_side_is_dynamic()
    {
        var result = _party.UnionStaticParties(Static("platform"), DamlPartyAnalysis.Dynamic);

        result.Should().Be(DamlPartyAnalysis.Dynamic);
    }

    [Fact]
    public void partition_camel_cases_controllers_and_omits_readas_when_no_observers()
    {
        var (controllers, readAs) = _party.PartitionControllersAndObservers(
            Static("Platform", "initiator"), DamlPartyAnalysis.Dynamic);

        controllers.Should().Equal("platform", "initiator");
        readAs.Should().BeEmpty();
    }

    [Fact]
    public void partition_routes_observer_only_parties_into_readas()
    {
        var (controllers, readAs) = _party.PartitionControllersAndObservers(
            Static("platform"), Static("regulator", "auditor"));

        controllers.Should().Equal("platform");
        readAs.Should().Equal("regulator", "auditor");
    }

    [Fact]
    public void partition_does_not_duplicate_an_observer_that_is_also_a_controller()
    {
        var (controllers, readAs) = _party.PartitionControllersAndObservers(
            Static("platform", "counterparty"), Static("platform", "regulator"));

        controllers.Should().Equal("platform", "counterparty");
        readAs.Should().Equal("regulator");
    }

    [Fact]
    public void partition_dedups_a_controller_field_named_twice()
    {
        var (controllers, readAs) = _party.PartitionControllersAndObservers(
            Static("platform", "platform"), DamlPartyAnalysis.Dynamic);

        controllers.Should().Equal("platform");
        readAs.Should().BeEmpty();
    }

    [Fact]
    public void partition_emits_no_readas_when_observers_are_dynamic()
    {
        var (controllers, readAs) = _party.PartitionControllersAndObservers(
            Static("platform"), DamlPartyAnalysis.Dynamic);

        controllers.Should().Equal("platform");
        readAs.Should().BeEmpty();
    }

    [Fact]
    public void partition_emits_no_readas_when_observers_are_static_empty()
    {
        var (controllers, readAs) = _party.PartitionControllersAndObservers(
            Static("platform"), DamlPartyAnalysis.Static([]));

        controllers.Should().Equal("platform");
        readAs.Should().BeEmpty();
    }

    [Fact]
    public void validate_keeps_a_static_analysis_when_every_field_is_a_party_field()
    {
        var analysis = Static("platform", "counterparty");

        var result = _party.ValidatePayloadParties(analysis, PartyFields("platform", "counterparty"));

        result.Should().BeSameAs(analysis);
    }

    [Fact]
    public void validate_demotes_to_dynamic_when_a_named_field_is_absent()
    {
        var result = _party.ValidatePayloadParties(
            Static("platform", "ghost"), PartyFields("platform", "counterparty"));

        result.Should().Be(DamlPartyAnalysis.Dynamic);
    }

    [Fact]
    public void validate_leaves_a_dynamic_analysis_untouched()
    {
        var result = _party.ValidatePayloadParties(DamlPartyAnalysis.Dynamic, PartyFields("platform"));

        result.Should().Be(DamlPartyAnalysis.Dynamic);
    }

    [Fact]
    public void validate_keeps_a_static_empty_analysis()
    {
        var analysis = DamlPartyAnalysis.Static([]);

        var result = _party.ValidatePayloadParties(analysis, PartyFields("platform"));

        result.Should().BeSameAs(analysis);
    }

    [Fact]
    public void union_demotes_to_dynamic_when_the_left_side_holds_an_unknown_subtype()
    {
        var result = _party.UnionStaticParties(
            StaticWith(new DamlPartyPayloadField("platform"), new UnknownPartyReference()),
            Static("counterparty"));

        result.Should().Be(DamlPartyAnalysis.Dynamic);
    }

    [Fact]
    public void union_demotes_to_dynamic_when_the_right_side_holds_an_unknown_subtype()
    {
        var result = _party.UnionStaticParties(
            Static("platform"),
            StaticWith(new UnknownPartyReference()));

        result.Should().Be(DamlPartyAnalysis.Dynamic);
    }

    [Fact]
    public void validate_demotes_to_dynamic_when_an_unknown_subtype_is_present()
    {
        var result = _party.ValidatePayloadParties(
            StaticWith(new DamlPartyPayloadField("platform"), new UnknownPartyReference()),
            PartyFields("platform"));

        result.Should().Be(DamlPartyAnalysis.Dynamic);
    }

    [Fact]
    public void partition_throws_when_static_controllers_carry_an_unknown_subtype()
    {
        var act = () => _party.PartitionControllersAndObservers(
            StaticWith(new DamlPartyPayloadField("platform"), new UnknownPartyReference()),
            DamlPartyAnalysis.Dynamic);

        act.Should().Throw<InvalidOperationException>().WithMessage("*UnknownPartyReference*");
    }

    [Fact]
    public void partition_throws_when_static_observers_carry_an_unknown_subtype()
    {
        var act = () => _party.PartitionControllersAndObservers(
            Static("platform"),
            StaticWith(new UnknownPartyReference()));

        act.Should().Throw<InvalidOperationException>().WithMessage("*UnknownPartyReference*");
    }
}
