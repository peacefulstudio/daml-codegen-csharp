// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using AwesomeAssertions;
using NSubstitute;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class DarCrossPackageResolverTests
{
    private sealed class FakeDarSource(DamlPackage main, params DamlPackage[] deps) : IDarSource
    {
        public DamlPackage MainPackage => main;

        public IReadOnlyList<DamlPackage> Dependencies => deps;

        public DamlPackage? GetPackageById(string packageId)
        {
            if (packageId == main.PackageId)
            {
                return main;
            }
            return deps.FirstOrDefault(d => d.PackageId == packageId);
        }
    }

    private sealed class CountingModules(IReadOnlyList<DamlModule> inner) : IReadOnlyList<DamlModule>
    {
        public int EnumerationCount { get; private set; }

        public DamlModule this[int index] => inner[index];

        public int Count => inner.Count;

        public IEnumerator<DamlModule> GetEnumerator()
        {
            EnumerationCount++;
            return inner.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static DamlPackage Package(string id, string name, params DamlModule[] modules) =>
        new()
        {
            PackageId = id,
            Name = name,
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = modules,
            DependencyReferences = []
        };

    private static DamlModule Module(string name, params DamlDataType[] dataTypes) =>
        new() { Name = name, DataTypes = dataTypes, Templates = [], Interfaces = [] };

    private static DamlDataType Record(string name) =>
        new() { Name = name, Definition = new DamlRecordDefinition([]) };

    private static DamlModule InterfaceModule(string moduleName, string interfaceName) =>
        new()
        {
            Name = moduleName,
            DataTypes = [Record(interfaceName)],
            Templates = [],
            Interfaces = [new DamlInterface { Name = interfaceName, Choices = [], ViewType = null }]
        };

    private static PackageEmitContext ContextFor(DamlPackage main) =>
        PackageEmitContext.ForPackage(main, new CodeGenOptions());

    [Fact]
    public void resolve_returns_the_bare_name_for_a_local_ref()
    {
        var main = Package("main-id", "my-pkg", Module("M", Record("Widget")));
        var resolver = new DarCrossPackageResolver(new FakeDarSource(main), Substitute.For<ICodegenLogger>());

        var result = resolver.Resolve(new DamlTypeRef("main-id", "M", "Widget"), ContextFor(main));

        result.Should().Be("Widget");
    }

    [Fact]
    public void resolve_returns_the_interface_marker_for_a_local_interface_ref()
    {
        var main = Package("main-id", "my-pkg", InterfaceModule("M", "Holding"));
        var resolver = new DarCrossPackageResolver(new FakeDarSource(main), Substitute.For<ICodegenLogger>());

        var result = resolver.Resolve(new DamlTypeRef("main-id", "M", "Holding"), ContextFor(main));

        result.Should().Be("IHolding");
    }

    [Fact]
    public void resolve_returns_the_qualified_interface_marker_for_a_cross_package_interface_ref()
    {
        var main = Package("main-id", "my-pkg", Module("M", Record("Widget")));
        var foreign = Package("foreign-id", "foreign-pkg", InterfaceModule("Splice.Holding", "Holding"));
        var resolver = new DarCrossPackageResolver(
            new FakeDarSource(main, foreign), Substitute.For<ICodegenLogger>());

        var result = resolver.Resolve(new DamlTypeRef("foreign-id", "Splice.Holding", "Holding"), ContextFor(main));

        result.Should().Be("Foreign.Pkg.IHolding");
        resolver.DiscoveredExternalPackageIds.Should().Contain("foreign-id");
    }

    [Fact]
    public void resolve_treats_an_empty_package_id_as_local()
    {
        var main = Package("main-id", "my-pkg", Module("M", Record("Widget")));
        var resolver = new DarCrossPackageResolver(new FakeDarSource(main), Substitute.For<ICodegenLogger>());

        var result = resolver.Resolve(new DamlTypeRef("", "M", "Widget"), ContextFor(main));

        result.Should().Be("Widget");
    }

    [Fact]
    public void resolve_qualifies_a_local_nested_choice_argument_with_its_parent_template()
    {
        var argType = Record("TransferArg");
        var choice = new DamlChoice
        {
            Name = "Transfer",
            Consuming = true,
            ArgumentType = new DamlTypeRef("", "M", "TransferArg"),
            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit)
        };
        var template = new DamlTemplate { Name = "Account", Fields = [], Choices = [choice] };
        var main = new DamlPackage
        {
            PackageId = "main-id",
            Name = "my-pkg",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [new DamlModule { Name = "M", DataTypes = [argType], Templates = [template], Interfaces = [] }],
            DependencyReferences = []
        };
        var resolver = new DarCrossPackageResolver(new FakeDarSource(main), Substitute.For<ICodegenLogger>());

        var result = resolver.Resolve(new DamlTypeRef("main-id", "M", "TransferArg"), ContextFor(main));

        result.Should().Be("Account.TransferArg");
    }

    [Fact]
    public void resolve_disambiguates_same_named_choice_args_declared_in_different_modules()
    {
        DamlModule ModuleWithTransferChoice(string moduleName, string templateName) => new()
        {
            Name = moduleName,
            DataTypes = [Record("Transfer")],
            Templates =
            [
                new DamlTemplate
                {
                    Name = templateName,
                    Fields = [],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Do",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef("", moduleName, "Transfer"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit)
                        }
                    ]
                }
            ],
            Interfaces = []
        };
        var main = Package(
            "main-id",
            "my-pkg",
            ModuleWithTransferChoice("Banking", "Account"),
            ModuleWithTransferChoice("Custody", "Vault"));
        var resolver = new DarCrossPackageResolver(new FakeDarSource(main), Substitute.For<ICodegenLogger>());
        var context = ContextFor(main);

        resolver.Resolve(new DamlTypeRef("main-id", "Banking", "Transfer"), context)
            .Should().Be("Account.Transfer");
        resolver.Resolve(new DamlTypeRef("main-id", "Custody", "Transfer"), context)
            .Should().Be("Vault.Transfer");
    }

    [Fact]
    public void resolve_disambiguates_same_named_choice_args_in_a_foreign_package()
    {
        DamlModule ModuleWithTransferChoice(string moduleName, string templateName) => new()
        {
            Name = moduleName,
            DataTypes = [Record("Transfer")],
            Templates =
            [
                new DamlTemplate
                {
                    Name = templateName,
                    Fields = [],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Do",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef("", moduleName, "Transfer"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit)
                        }
                    ]
                }
            ],
            Interfaces = []
        };
        var main = Package("main-id", "my-pkg", Module("M", Record("Widget")));
        var foreign = Package(
            "foreign-id",
            "foreign-pkg",
            ModuleWithTransferChoice("Banking", "Account"),
            ModuleWithTransferChoice("Custody", "Vault"));
        var resolver = new DarCrossPackageResolver(
            new FakeDarSource(main, foreign), Substitute.For<ICodegenLogger>());
        var context = ContextFor(main);

        resolver.Resolve(new DamlTypeRef("foreign-id", "Banking", "Transfer"), context)
            .Should().Be("Foreign.Pkg.Account.Transfer");
        resolver.Resolve(new DamlTypeRef("foreign-id", "Custody", "Transfer"), context)
            .Should().Be("Foreign.Pkg.Vault.Transfer");
    }

    [Fact]
    public void resolve_warns_and_keeps_first_on_same_module_foreign_choice_arg_name_clash()
    {
        DamlTemplate TemplateWithTransferChoice(string templateName) => new()
        {
            Name = templateName,
            Fields = [],
            Choices =
            [
                new DamlChoice
                {
                    Name = "Do",
                    Consuming = true,
                    ArgumentType = new DamlTypeRef("foreign-id", "Banking", "Transfer"),
                    ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit)
                }
            ]
        };
        var foreign = new DamlPackage
        {
            PackageId = "foreign-id",
            Name = "foreign-pkg",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules =
            [
                new DamlModule
                {
                    Name = "Banking",
                    DataTypes = [Record("Transfer")],
                    Templates = [TemplateWithTransferChoice("Account"), TemplateWithTransferChoice("Vault")],
                    Interfaces = []
                }
            ],
            DependencyReferences = []
        };
        var main = Package("main-id", "my-pkg", Module("M", Record("Widget")));
        var logger = Substitute.For<ICodegenLogger>();
        var resolver = new DarCrossPackageResolver(new FakeDarSource(main, foreign), logger);
        var context = ContextFor(main);

        resolver.Resolve(new DamlTypeRef("foreign-id", "Banking", "Transfer"), context)
            .Should().Be("Foreign.Pkg.Account.Transfer");
        logger.Received(1).Warning(Arg.Is<string>(m => m.Contains("Banking:Transfer") && m.Contains("Account") && m.Contains("Vault") && m.Contains("in the same package")));
    }

    [Fact]
    public void resolve_maps_a_stdlib_ref_to_its_runtime_stdlib_name()
    {
        var main = Package("main-id", "my-pkg", Module("M", Record("Widget")));
        var stdlib = Package("stdlib-id", "daml-stdlib", Module("DA.Time.Types", Record("RelTime")));
        var resolver = new DarCrossPackageResolver(
            new FakeDarSource(main, stdlib), Substitute.For<ICodegenLogger>());

        var result = resolver.Resolve(new DamlTypeRef("stdlib-id", "DA.Time.Types", "RelTime"), ContextFor(main));

        result.Should().Be("RelTime");
        resolver.DiscoveredExternalPackageIds.Should().NotContain("stdlib-id");
    }

    [Fact]
    public void resolve_qualifies_a_cross_package_ref_and_records_the_package_id()
    {
        var main = Package("main-id", "my-pkg", Module("M", Record("Widget")));
        var other = Package("other-id", "other-pkg", Module("N", Record("Gadget")));
        var resolver = new DarCrossPackageResolver(
            new FakeDarSource(main, other), Substitute.For<ICodegenLogger>());

        var result = resolver.Resolve(new DamlTypeRef("other-id", "N", "Gadget"), ContextFor(main));

        result.Should().Be("Other.Pkg.Gadget");
        resolver.DiscoveredExternalPackageIds.Should().Contain("other-id");
    }

    [Fact]
    public void resolve_throws_when_the_target_package_is_absent_from_the_dar()
    {
        var main = Package("main-id", "my-pkg", Module("M", Record("Widget")));
        var resolver = new DarCrossPackageResolver(new FakeDarSource(main), Substitute.For<ICodegenLogger>());

        var act = () => resolver.Resolve(new DamlTypeRef("missing-id", "N", "Gadget"), ContextFor(main));

        act.Should().Throw<InvalidOperationException>().WithMessage("*not present in the DAR*");
    }

    [Fact]
    public void discovered_external_package_ids_accumulates_across_a_sequence_of_resolves()
    {
        var main = Package("main-id", "my-pkg", Module("M", Record("Widget")));
        var a = Package("a-id", "a-pkg", Module("A", Record("Alpha")));
        var b = Package("b-id", "b-pkg", Module("B", Record("Beta")));
        var resolver = new DarCrossPackageResolver(
            new FakeDarSource(main, a, b), Substitute.For<ICodegenLogger>());
        var context = ContextFor(main);

        resolver.Resolve(new DamlTypeRef("a-id", "A", "Alpha"), context);
        resolver.Resolve(new DamlTypeRef("b-id", "B", "Beta"), context);
        resolver.Resolve(new DamlTypeRef("main-id", "M", "Widget"), context);

        resolver.DiscoveredExternalPackageIds.Should().BeEquivalentTo("a-id", "b-id");
    }

    [Fact]
    public void resolve_qualifies_a_cross_package_nested_choice_argument_with_its_parent_template()
    {
        var argType = Record("ForeignArg");
        var foreignChoice = new DamlChoice
        {
            Name = "Do",
            Consuming = true,
            ArgumentType = new DamlTypeRef("other-id", "N", "ForeignArg"),
            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit)
        };
        var foreignTemplate = new DamlTemplate { Name = "Thing", Fields = [], Choices = [foreignChoice] };
        var other = new DamlPackage
        {
            PackageId = "other-id",
            Name = "other-pkg",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [new DamlModule { Name = "N", DataTypes = [argType], Templates = [foreignTemplate], Interfaces = [] }],
            DependencyReferences = []
        };
        var main = Package("main-id", "my-pkg", Module("M", Record("Widget")));
        var resolver = new DarCrossPackageResolver(new FakeDarSource(main, other), Substitute.For<ICodegenLogger>());
        var context = ContextFor(main);

        var first = resolver.Resolve(new DamlTypeRef("other-id", "N", "ForeignArg"), context);
        var second = resolver.Resolve(new DamlTypeRef("other-id", "N", "ForeignArg"), context);

        first.Should().Be("Other.Pkg.Thing.ForeignArg");
        second.Should().Be(first);
    }

    [Fact]
    public void the_foreign_choice_arg_memo_builds_the_map_once_across_repeated_resolves()
    {
        var argType = Record("ForeignArg");
        var foreignChoice = new DamlChoice
        {
            Name = "Do",
            Consuming = true,
            ArgumentType = new DamlTypeRef("other-id", "N", "ForeignArg"),
            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit)
        };
        var foreignTemplate = new DamlTemplate { Name = "Thing", Fields = [], Choices = [foreignChoice] };
        var countingModules = new CountingModules(
            [new DamlModule { Name = "N", DataTypes = [argType], Templates = [foreignTemplate], Interfaces = [] }]);
        var other = new DamlPackage
        {
            PackageId = "other-id",
            Name = "other-pkg",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = countingModules,
            DependencyReferences = []
        };
        var main = Package("main-id", "my-pkg", Module("M", Record("Widget")));
        var resolver = new DarCrossPackageResolver(new FakeDarSource(main, other), Substitute.For<ICodegenLogger>());
        var context = ContextFor(main);

        var first = resolver.Resolve(new DamlTypeRef("other-id", "N", "ForeignArg"), context);
        var enumerationsAfterFirst = countingModules.EnumerationCount;
        var second = resolver.Resolve(new DamlTypeRef("other-id", "N", "ForeignArg"), context);

        first.Should().Be("Other.Pkg.Thing.ForeignArg");
        second.Should().Be(first);
        enumerationsAfterFirst.Should().BeGreaterThan(0, "the first resolve builds the foreign-choice-arg map by walking the package's modules");
        countingModules.EnumerationCount.Should().Be(enumerationsAfterFirst,
            "the memo must serve the second resolve without rebuilding the map — so the foreign package's modules are not walked again");
    }

    [Fact]
    public void the_foreign_interface_memo_builds_the_set_once_across_repeated_resolves()
    {
        var countingModules = new CountingModules([InterfaceModule("Splice.Holding", "Holding")]);
        var other = new DamlPackage
        {
            PackageId = "other-id",
            Name = "other-pkg",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = countingModules,
            DependencyReferences = []
        };
        var main = Package("main-id", "my-pkg", Module("M", Record("Widget")));
        var resolver = new DarCrossPackageResolver(new FakeDarSource(main, other), Substitute.For<ICodegenLogger>());
        var context = ContextFor(main);

        var first = resolver.Resolve(new DamlTypeRef("other-id", "Splice.Holding", "Holding"), context);
        var enumerationsAfterFirst = countingModules.EnumerationCount;
        var second = resolver.Resolve(new DamlTypeRef("other-id", "Splice.Holding", "Holding"), context);

        first.Should().Be("Other.Pkg.IHolding");
        second.Should().Be(first);
        enumerationsAfterFirst.Should().BeGreaterThan(0, "the first resolve builds the foreign-interface set by walking the package's modules");
        countingModules.EnumerationCount.Should().Be(enumerationsAfterFirst,
            "the memo must serve the second resolve without rebuilding the set — so the foreign package's modules are not walked again");
    }

    [Fact]
    public void the_foreign_choice_arg_memo_is_dar_scoped_across_packages_in_one_generate()
    {
        var argType = Record("ForeignArg");
        var foreignChoice = new DamlChoice
        {
            Name = "Do",
            Consuming = true,
            ArgumentType = new DamlTypeRef("other-id", "N", "ForeignArg"),
            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit)
        };
        var foreignTemplate = new DamlTemplate { Name = "Thing", Fields = [], Choices = [foreignChoice] };
        var other = new DamlPackage
        {
            PackageId = "other-id",
            Name = "other-pkg",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [new DamlModule { Name = "N", DataTypes = [argType], Templates = [foreignTemplate], Interfaces = [] }],
            DependencyReferences = []
        };
        var main = Package("main-id", "my-pkg", Module("M", Record("Widget")));
        var dep = Package("dep-id", "dep-pkg", Module("D", Record("DepThing")));
        var resolver = new DarCrossPackageResolver(new FakeDarSource(main, other, dep), Substitute.For<ICodegenLogger>());

        var fromMain = resolver.Resolve(new DamlTypeRef("other-id", "N", "ForeignArg"), ContextFor(main));
        var fromDep = resolver.Resolve(new DamlTypeRef("other-id", "N", "ForeignArg"), ContextFor(dep));

        fromMain.Should().Be("Other.Pkg.Thing.ForeignArg");
        fromDep.Should().Be(fromMain);
        resolver.DiscoveredExternalPackageIds.Should().BeEquivalentTo("other-id");
    }

    [Fact]
    public void resolve_returns_the_bare_name_and_records_nothing_for_an_unmapped_stdlib_type()
    {
        var main = Package("main-id", "my-pkg", Module("M", Record("Widget")));
        var stdlib = Package("stdlib-id", "daml-stdlib", Module("DA.Mystery.Types", Record("Mystery")));
        var resolver = new DarCrossPackageResolver(
            new FakeDarSource(main, stdlib), Substitute.For<ICodegenLogger>());

        var result = resolver.Resolve(new DamlTypeRef("stdlib-id", "DA.Mystery.Types", "Mystery"), ContextFor(main));

        result.Should().Be("Mystery");
        resolver.DiscoveredExternalPackageIds.Should().NotContain("stdlib-id");
        resolver.DiscoveredExternalPackageIds.Should().BeEmpty();
    }
}
