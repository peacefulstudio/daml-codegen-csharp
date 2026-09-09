// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class ModuleNamespaceGuardsTests
{
    private static EmittedModule Module(string package, string module, string ns, params string[] typeNames) =>
        new(package, module, ns, typeNames);

    private static EmittedModule ModuleWithHashedTypeNames(string package, string module, string ns, params string[] typeNames) =>
        new(package, module, ns, new HashSet<string>(typeNames, StringComparer.Ordinal));

    [Fact]
    public void Check_accepts_a_prefix_family_whose_types_do_not_collide_with_the_child_namespace()
    {
        var act = () => ModuleNamespaceGuards.Check(
        [
            Module("splice-amulet-name-service", "Splice.Ans", "Splice.Ans", "AnsEntry", "ContractIdentifiers"),
            Module("splice-amulet-name-service", "Splice.Ans.AmuletConversionRateFeed", "Splice.Ans.AmuletConversionRateFeed", "Feed"),
        ]);

        act.Should().NotThrow();
    }

    [Fact]
    public void Check_accepts_a_type_named_after_the_last_segment_of_its_own_module()
    {
        var act = () => ModuleNamespaceGuards.Check(
        [
            Module("splice-amulet", "Splice.Amulet", "Splice.Amulet", "Amulet"),
        ]);

        act.Should().NotThrow();
    }

    [Fact]
    public void Check_fails_when_two_modules_map_to_one_namespace_and_names_both()
    {
        var act = () => ModuleNamespaceGuards.Check(
        [
            Module("main-package", "M", "X.M"),
            Module("dep-package", "X.M", "X.M"),
        ]);

        act.Should().Throw<CodegenException>()
            .WithMessage(
                "Daml modules dep-package:X.M and main-package:M map to the same C# namespace 'X.M'. " +
                "Modules cannot share a namespace: their same-named types would collide and their files would " +
                "overwrite each other. Rename the modules apart, or choose a --namespace prefix that no longer " +
                "makes them coincide.");
    }

    [Fact]
    public void Check_names_every_module_when_three_of_them_map_to_one_namespace()
    {
        var act = () => ModuleNamespaceGuards.Check(
        [
            Module("main-package", "M", "X.M"),
            Module("other-package", "X.M", "X.M"),
            Module("dep-package", "X.M", "X.M"),
        ]);

        act.Should().Throw<CodegenException>()
            .WithMessage(
                "Daml modules dep-package:X.M, main-package:M and other-package:X.M map to the same C# namespace " +
                "'X.M'. Modules cannot share a namespace: their same-named types would collide and their files would " +
                "overwrite each other. Rename the modules apart, or choose a --namespace prefix that no longer " +
                "makes them coincide.");
    }

    [Fact]
    public void Check_reports_the_first_shared_namespace_in_ordinal_order_when_two_namespaces_are_shared()
    {
        var act = () => ModuleNamespaceGuards.Check(
        [
            Module("p2", "Z", "Z.Ns"),
            Module("p1", "Y", "A.Ns"),
            Module("p3", "W", "Z.Ns"),
            Module("p4", "V", "A.Ns"),
        ]);

        act.Should().Throw<CodegenException>()
            .WithMessage(
                "Daml modules p1:Y and p4:V map to the same C# namespace 'A.Ns'. Modules cannot share a namespace: " +
                "their same-named types would collide and their files would overwrite each other. Rename the modules " +
                "apart, or choose a --namespace prefix that no longer makes them coincide.");
    }

    [Fact]
    public void Check_fails_when_an_ancestor_namespace_implied_by_a_module_equals_the_fully_qualified_name_of_an_emitted_type()
    {
        var act = () => ModuleNamespaceGuards.Check(
        [
            Module("pkg", "A.B", "A.B", "C"),
            Module("pkg", "A.B.C.D", "A.B.C.D", "E"),
        ]);

        act.Should().Throw<CodegenException>()
            .WithMessage("*pkg:A.B.C.D*")
            .WithMessage("*pkg:A.B*")
            .WithMessage("*'A.B.C'*");
    }

    [Fact]
    public void Check_names_every_module_declaring_the_namespace_an_emitted_type_is_spelled_like()
    {
        var act = () => ModuleNamespaceGuards.Check(
        [
            Module("pkg", "A.B.C.E", "A.B.C.E", "F"),
            Module("pkg", "A.B", "A.B", "C"),
            Module("pkg", "A.B.C", "A.B.C", "D"),
        ]);

        act.Should().Throw<CodegenException>()
            .WithMessage(
                "The C# namespace 'A.B.C' emitted for Daml module pkg:A.B.C and implied by the namespace 'A.B.C.E' " +
                "emitted for Daml module pkg:A.B.C.E is also the fully qualified name of type C declared by Daml module " +
                "pkg:A.B (namespace 'A.B'). C# does not allow a namespace and a type to share a fully qualified name " +
                "(CS0101). Rename the module or the type.");
    }

    [Fact]
    public void Check_reports_the_outermost_collision_when_two_modules_each_spell_a_type_like_a_namespace()
    {
        var act = () => ModuleNamespaceGuards.Check(
        [
            Module("pkg", "A.B.C", "A.B.C", "D"),
            Module("pkg", "A.B.C.D", "A.B.C.D", "E"),
            Module("pkg", "A.B", "A.B", "C"),
        ]);

        act.Should().Throw<CodegenException>()
            .WithMessage(
                "The C# namespace 'A.B.C' emitted for Daml module pkg:A.B.C and implied by the namespace 'A.B.C.D' " +
                "emitted for Daml module pkg:A.B.C.D is also the fully qualified name of type C declared by Daml module " +
                "pkg:A.B (namespace 'A.B'). C# does not allow a namespace and a type to share a fully qualified name " +
                "(CS0101). Rename the module or the type.");
    }

    [Fact]
    public void Check_reports_the_first_colliding_type_name_in_ordinal_order_however_the_module_hashes_them()
    {
        var act = () => ModuleNamespaceGuards.Check(
        [
            ModuleWithHashedTypeNames("pkg", "A.B", "A.B", "Zeta", "Alpha"),
            Module("pkg", "A.B.Alpha", "A.B.Alpha", "T"),
            Module("pkg", "A.B.Zeta", "A.B.Zeta", "U"),
        ]);

        act.Should().Throw<CodegenException>()
            .WithMessage(
                "The C# namespace 'A.B.Alpha' emitted for Daml module pkg:A.B.Alpha is also the fully qualified name " +
                "of type Alpha declared by Daml module pkg:A.B (namespace 'A.B'). C# does not allow a namespace and a " +
                "type to share a fully qualified name (CS0101). Rename the module or the type.");
    }

    [Fact]
    public void Check_fails_when_a_namespace_equals_the_fully_qualified_name_of_an_emitted_type_and_names_both()
    {
        var act = () => ModuleNamespaceGuards.Check(
        [
            Module("pkg", "A.B", "A.B", "C"),
            Module("pkg", "A.B.C", "A.B.C", "D"),
        ]);

        act.Should().Throw<CodegenException>()
            .WithMessage("*pkg:A.B.C*")
            .WithMessage("*pkg:A.B*")
            .WithMessage("*A.B.C*");
    }
}
