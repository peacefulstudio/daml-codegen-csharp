// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using FluentAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class ChoiceCreatedSlotsTests
{
    private const string LocalPackageId = "pkg-id";

    private sealed class StubResolver(string resolvedName = "Resolved") : ICrossPackageResolver
    {
        public string Resolve(DamlTypeRef typeRef, PackageEmitContext context) => resolvedName;

        public IReadOnlySet<string> DiscoveredExternalPackageIds => new HashSet<string>();

        public DamlPackage? LookupPackage(string packageId) => null;
    }

    private static DamlPackage Package() =>
        new()
        {
            PackageId = LocalPackageId,
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = [],
        };

    private static PackageEmitContext Context() =>
        PackageEmitContext.ForPackage(Package(), new CodeGenOptions { RootNamespace = "Test.Package" });

    private static IReadOnlyList<ChoiceCreatedSlot> Extract(DamlType returnType, StubResolver? resolver = null)
    {
        var context = Context();
        var actualResolver = resolver ?? new StubResolver();
        var mapper = new DamlTypeMapper(context, actualResolver);
        return ChoiceCreatedSlots.Extract(context, actualResolver, mapper, returnType);
    }

    private static DamlTypeRef Ref(string name) => new(LocalPackageId, "Main", name);

    private static DamlTypeApp ContractIdOf(DamlType arg) =>
        new(new DamlPrimitiveType(DamlPrimitive.ContractId), [arg]);

    private static DamlTypeApp OptionalOf(DamlType arg) =>
        new(new DamlPrimitiveType(DamlPrimitive.Optional), [arg]);

    private static DamlTypeApp ListOf(DamlType arg) =>
        new(new DamlPrimitiveType(DamlPrimitive.List), [arg]);

    private static DamlTypeApp Tuple(params DamlType[] components) =>
        new(new DamlTypeRef(LocalPackageId, "DA.Types", $"Tuple{components.Length}"), components);

    [Fact]
    public void single_contract_id_yields_one_single_slot()
    {
        var slots = Extract(ContractIdOf(Ref("Agreement")));

        slots.Should().ContainSingle();
        slots[0].FieldName.Should().Be("Agreement");
        slots[0].Cardinality.Should().Be(CreatedCardinality.Single);
    }

    [Fact]
    public void optional_contract_id_yields_an_optional_slot()
    {
        var slots = Extract(OptionalOf(ContractIdOf(Ref("Agreement"))));

        slots.Should().ContainSingle();
        slots[0].Cardinality.Should().Be(CreatedCardinality.Optional);
    }

    [Fact]
    public void list_of_contract_id_yields_a_list_slot()
    {
        var slots = Extract(ListOf(ContractIdOf(Ref("Agreement"))));

        slots.Should().ContainSingle();
        slots[0].Cardinality.Should().Be(CreatedCardinality.List);
    }

    [Fact]
    public void tuple_is_flattened_across_components()
    {
        var slots = Extract(Tuple(ContractIdOf(Ref("Buyer")), ContractIdOf(Ref("Seller"))));

        slots.Should().HaveCount(2);
        slots[0].FieldName.Should().Be("Buyer");
        slots[1].FieldName.Should().Be("Seller");
    }

    [Fact]
    public void same_template_twice_disambiguates_field_names()
    {
        var slots = Extract(Tuple(ContractIdOf(Ref("Half")), ContractIdOf(Ref("Half"))));

        slots.Should().HaveCount(2);
        slots[0].FieldName.Should().Be("Half");
        slots[1].FieldName.Should().Be("Half2");
    }

    [Fact]
    public void base_name_ending_in_digit_is_left_untouched_when_unique()
    {
        var slots = Extract(Tuple(ContractIdOf(Ref("Half2")), ContractIdOf(Ref("Whole"))));

        slots.Should().HaveCount(2);
        slots[0].FieldName.Should().Be("Half2");
        slots[1].FieldName.Should().Be("Whole");
    }

    [Fact]
    public void synthesized_suffix_does_not_steal_a_later_real_name()
    {
        var slots = Extract(Tuple(
            ContractIdOf(Ref("Half")),
            ContractIdOf(Ref("Half")),
            ContractIdOf(Ref("Half2"))));

        slots.Select(s => s.FieldName).Should().Equal("Half", "Half3", "Half2");
    }

    [Fact]
    public void cascading_collisions_each_get_a_distinct_free_suffix()
    {
        var slots = Extract(Tuple(
            ContractIdOf(Ref("Half")),
            ContractIdOf(Ref("Half")),
            ContractIdOf(Ref("Half")),
            ContractIdOf(Ref("Half2")),
            ContractIdOf(Ref("Half3"))));

        slots.Select(s => s.FieldName).Should().Equal("Half", "Half4", "Half5", "Half2", "Half3");
    }

    [Fact]
    public void non_contract_return_yields_no_slots()
    {
        Extract(new DamlPrimitiveType(DamlPrimitive.Int64)).Should().BeEmpty();
        Extract(new DamlPrimitiveType(DamlPrimitive.Unit)).Should().BeEmpty();
        Extract(Ref("SomeRecord")).Should().BeEmpty();
    }
}
