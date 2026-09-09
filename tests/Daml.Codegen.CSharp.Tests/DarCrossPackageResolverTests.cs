// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.CSharp.Tests.TestHelpers;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class DarCrossPackageResolverTests
{
    private sealed class FakeDarSource(DamlPackage main, params DamlPackage[] deps) : IDarSource
    {
        public DamlPackage MainPackage => main;

        public IReadOnlyList<DamlPackage> Dependencies => deps;
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

    private static DamlModule InterfaceModuleWithMarkerReservingTemplate(string moduleName, string interfaceName) =>
        new()
        {
            Name = moduleName,
            DataTypes = [Record(interfaceName), Record("I" + interfaceName)],
            Templates = [new DamlTemplate { Name = "I" + interfaceName, Choices = [] }],
            Interfaces = [new DamlInterface { Name = interfaceName, Choices = [], ViewType = null }]
        };

    private static PackageEmitContext ContextFor(DamlPackage package, CodeGenOptions? options = null, bool isMainPackage = true) =>
        PackageEmitContext.ForPackage(package, options ?? new CodeGenOptions(), isMainPackage).Single();

    private static PackageEmitContext ContextFor(DamlPackage package, string moduleName) =>
        PackageEmitContext.ForPackage(package, new CodeGenOptions(), isMainPackage: true)
            .Single(context => context.Module.Name == moduleName);

    [Fact]
    public void Resolve_returns_the_bare_name_for_a_local_ref()
    {
        var main = Package("main-id", "my-pkg", Module("M", Record("Widget")));
        var resolver = new DarCrossPackageResolver(new FakeDarSource(main), new CodeGenOptions());

        var result = resolver.Resolve(new DamlTypeRef("main-id", "M", "Widget"), ContextFor(main));

        result.Should().Be("Widget");
    }

    [Fact]
    public void Resolve_returns_the_interface_marker_for_a_local_interface_ref()
    {
        var main = Package("main-id", "my-pkg", InterfaceModule("M", "Holding"));
        var resolver = new DarCrossPackageResolver(new FakeDarSource(main), new CodeGenOptions());

        var result = resolver.Resolve(new DamlTypeRef("main-id", "M", "Holding"), ContextFor(main));

        result.Should().Be("IHolding");
    }

    [Fact]
    public void Resolve_returns_the_qualified_interface_marker_for_a_cross_package_interface_ref()
    {
        var main = Package("main-id", "my-pkg", Module("M", Record("Widget")));
        var foreign = Package("foreign-id", "foreign-pkg", InterfaceModule("Splice.Holding", "Holding"));
        var resolver = new DarCrossPackageResolver(
            new FakeDarSource(main, foreign), new CodeGenOptions());

        var result = resolver.Resolve(new DamlTypeRef("foreign-id", "Splice.Holding", "Holding"), ContextFor(main));

        result.Should().Be("global::Splice.Holding.IHolding");
        resolver.DiscoveredExternalPackageIds.Should().Contain("foreign-id");
    }

    [Fact]
    public void Resolve_disambiguates_a_local_interface_marker_reserved_by_a_template()
    {
        var main = Package("main-id", "my-pkg", InterfaceModuleWithMarkerReservingTemplate("M", "Holding"));
        var resolver = new DarCrossPackageResolver(new FakeDarSource(main), new CodeGenOptions());

        var result = resolver.Resolve(new DamlTypeRef("main-id", "M", "Holding"), ContextFor(main));

        result.Should().Be("IHolding_");
    }

    [Fact]
    public void Resolve_disambiguates_a_cross_package_interface_marker_reserved_by_a_foreign_template()
    {
        var main = Package("main-id", "my-pkg", Module("M", Record("Widget")));
        var foreign = Package(
            "foreign-id", "foreign-pkg", InterfaceModuleWithMarkerReservingTemplate("Splice.Holding", "Holding"));
        var resolver = new DarCrossPackageResolver(
            new FakeDarSource(main, foreign), new CodeGenOptions());

        var result = resolver.Resolve(new DamlTypeRef("foreign-id", "Splice.Holding", "Holding"), ContextFor(main));

        result.Should().Be("global::Splice.Holding.IHolding_");
    }

    [Fact]
    public void Resolve_qualifies_a_local_ref_declared_in_another_module_with_that_module_namespace()
    {
        var main = Package("main-id", "my-pkg", Module("M1", Record("Widget")), Module("M2", Record("Gadget")));
        var resolver = new DarCrossPackageResolver(new FakeDarSource(main), new CodeGenOptions());

        var result = resolver.Resolve(new DamlTypeRef("main-id", "M1", "Widget"), ContextFor(main, "M2"));

        result.Should().Be("global::M1.Widget");
    }

    [Fact]
    public void Resolve_applies_the_namespace_prefix_to_main_package_refs_only()
    {
        var options = new CodeGenOptions { NamespacePrefix = "Acme" };
        var main = Package("main-id", "my-pkg", Module("M", Record("Widget")));
        var dep = Package("dep-id", "dep-pkg", Module("D", Record("Thing")));
        var resolver = new DarCrossPackageResolver(new FakeDarSource(main, dep), options);

        var mainRefFromDependency = resolver.Resolve(new DamlTypeRef("main-id", "M", "Widget"), ContextFor(dep, options, isMainPackage: false));
        var dependencyRefFromMain = resolver.Resolve(new DamlTypeRef("dep-id", "D", "Thing"), ContextFor(main, options));

        mainRefFromDependency.Should().Be("global::Acme.M.Widget");
        dependencyRefFromMain.Should().Be("global::D.Thing");
    }

    [Fact]
    public void Resolve_qualifies_a_foreign_choice_argument_with_the_namespace_of_the_template_it_nests_inside()
    {
        var foreignChoice = new DamlChoice
        {
            Name = "Do",
            Consuming = true,
            ArgumentType = new DamlTypeRef("other-id", "Args", "ForeignArg"),
            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit)
        };
        var other = Package(
            "other-id",
            "other-pkg",
            Module("Args", Record("ForeignArg")),
            new DamlModule { Name = "N", DataTypes = [], Templates = [new DamlTemplate { Name = "Thing", Choices = [foreignChoice] }], Interfaces = [] });
        var main = Package("main-id", "my-pkg", Module("M", Record("Widget")));
        var resolver = new DarCrossPackageResolver(new FakeDarSource(main, other), new CodeGenOptions());

        var result = resolver.Resolve(new DamlTypeRef("other-id", "Args", "ForeignArg"), ContextFor(main));

        result.Should().Be("global::N.Thing.ForeignArg");
    }

    [Fact]
    public void Resolve_treats_an_empty_package_id_as_local()
    {
        var main = Package("main-id", "my-pkg", Module("M", Record("Widget")));
        var resolver = new DarCrossPackageResolver(new FakeDarSource(main), new CodeGenOptions());

        var result = resolver.Resolve(new DamlTypeRef("", "M", "Widget"), ContextFor(main));

        result.Should().Be("Widget");
    }

    [Fact]
    public void Resolve_qualifies_a_local_nested_choice_argument_with_its_parent_template()
    {
        var argType = Record("TransferArg");
        var choice = new DamlChoice
        {
            Name = "Transfer",
            Consuming = true,
            ArgumentType = new DamlTypeRef("", "M", "TransferArg"),
            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit)
        };
        var template = new DamlTemplate { Name = "Account", Choices = [choice] };
        var main = new DamlPackage
        {
            PackageId = "main-id",
            Name = "my-pkg",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [new DamlModule { Name = "M", DataTypes = [argType], Templates = [template], Interfaces = [] }],
            DependencyReferences = []
        };
        var resolver = new DarCrossPackageResolver(new FakeDarSource(main), new CodeGenOptions());

        var result = resolver.Resolve(new DamlTypeRef("main-id", "M", "TransferArg"), ContextFor(main));

        result.Should().Be("Account.TransferArg");
    }

    [Fact]
    public void Resolve_disambiguates_same_named_choice_args_declared_in_different_modules()
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
        var resolver = new DarCrossPackageResolver(new FakeDarSource(main), new CodeGenOptions());
        var context = ContextFor(main, "Banking");

        resolver.Resolve(new DamlTypeRef("main-id", "Banking", "Transfer"), context)
            .Should().Be("Account.Transfer");
        resolver.Resolve(new DamlTypeRef("main-id", "Custody", "Transfer"), context)
            .Should().Be("global::Custody.Vault.Transfer");
    }

    [Fact]
    public void Resolve_disambiguates_same_named_choice_args_in_a_foreign_package()
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
            new FakeDarSource(main, foreign), new CodeGenOptions());
        var context = ContextFor(main);

        resolver.Resolve(new DamlTypeRef("foreign-id", "Banking", "Transfer"), context)
            .Should().Be("global::Banking.Account.Transfer");
        resolver.Resolve(new DamlTypeRef("foreign-id", "Custody", "Transfer"), context)
            .Should().Be("global::Custody.Vault.Transfer");
    }

    [Fact]
    public void Resolve_warns_and_keeps_first_on_same_module_foreign_choice_arg_name_clash()
    {
        DamlTemplate TemplateWithTransferChoice(string templateName) => new()
        {
            Name = templateName,
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
        var logger = new CapturingLogger();
        var resolver = new DarCrossPackageResolver(new FakeDarSource(main, foreign), new CodeGenOptions(), logger);
        var context = ContextFor(main);

        resolver.Resolve(new DamlTypeRef("foreign-id", "Banking", "Transfer"), context)
            .Should().Be("global::Banking.Account.Transfer");
        logger.Warnings.Should().ContainSingle()
            .Which.Should().Contain("Banking:Transfer").And.Contain("Account").And.Contain("Vault").And.Contain("in the same package");
    }

    [Fact]
    public void Resolve_maps_a_stdlib_ref_to_its_runtime_stdlib_name()
    {
        var main = Package("main-id", "my-pkg", Module("M", Record("Widget")));
        var stdlib = Package("stdlib-id", "daml-stdlib", Module("DA.Time.Types", Record("RelTime")));
        var resolver = new DarCrossPackageResolver(
            new FakeDarSource(main, stdlib), new CodeGenOptions());

        var result = resolver.Resolve(new DamlTypeRef("stdlib-id", "DA.Time.Types", "RelTime"), ContextFor(main));

        result.Should().Be("RelTime");
        resolver.DiscoveredExternalPackageIds.Should().NotContain("stdlib-id");
    }

    [Fact]
    public void Resolve_qualifies_a_cross_package_ref_and_records_the_package_id()
    {
        var main = Package("main-id", "my-pkg", Module("M", Record("Widget")));
        var other = Package("other-id", "other-pkg", Module("N", Record("Gadget")));
        var resolver = new DarCrossPackageResolver(
            new FakeDarSource(main, other), new CodeGenOptions());

        var result = resolver.Resolve(new DamlTypeRef("other-id", "N", "Gadget"), ContextFor(main));

        result.Should().Be("global::N.Gadget");
        resolver.DiscoveredExternalPackageIds.Should().Contain("other-id");
    }

    [Fact]
    public void Resolve_throws_when_the_target_package_is_absent_from_the_dar()
    {
        var main = Package("main-id", "my-pkg", Module("M", Record("Widget")));
        var resolver = new DarCrossPackageResolver(new FakeDarSource(main), new CodeGenOptions());

        var act = () => resolver.Resolve(new DamlTypeRef("missing-id", "N", "Gadget"), ContextFor(main));

        act.Should().Throw<InvalidOperationException>().WithMessage("*not present in the DAR*");
    }

    [Fact]
    public void DarCrossPackageResolver_discovered_external_package_ids_accumulates_across_a_sequence_of_resolves()
    {
        var main = Package("main-id", "my-pkg", Module("M", Record("Widget")));
        var a = Package("a-id", "a-pkg", Module("A", Record("Alpha")));
        var b = Package("b-id", "b-pkg", Module("B", Record("Beta")));
        var resolver = new DarCrossPackageResolver(
            new FakeDarSource(main, a, b), new CodeGenOptions());
        var context = ContextFor(main);

        resolver.Resolve(new DamlTypeRef("a-id", "A", "Alpha"), context);
        resolver.Resolve(new DamlTypeRef("b-id", "B", "Beta"), context);
        resolver.Resolve(new DamlTypeRef("main-id", "M", "Widget"), context);

        resolver.DiscoveredExternalPackageIds.Should().BeEquivalentTo("a-id", "b-id");
    }

    [Fact]
    public void Resolve_qualifies_a_cross_package_nested_choice_argument_with_its_parent_template()
    {
        var argType = Record("ForeignArg");
        var foreignChoice = new DamlChoice
        {
            Name = "Do",
            Consuming = true,
            ArgumentType = new DamlTypeRef("other-id", "N", "ForeignArg"),
            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit)
        };
        var foreignTemplate = new DamlTemplate { Name = "Thing", Choices = [foreignChoice] };
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
        var resolver = new DarCrossPackageResolver(new FakeDarSource(main, other), new CodeGenOptions());
        var context = ContextFor(main);

        var first = resolver.Resolve(new DamlTypeRef("other-id", "N", "ForeignArg"), context);
        var second = resolver.Resolve(new DamlTypeRef("other-id", "N", "ForeignArg"), context);

        first.Should().Be("global::N.Thing.ForeignArg");
        second.Should().Be(first);
    }

    [Fact]
    public void DarCrossPackageResolver_the_foreign_choice_arg_memo_builds_the_map_once_across_repeated_resolves()
    {
        var argType = Record("ForeignArg");
        var foreignChoice = new DamlChoice
        {
            Name = "Do",
            Consuming = true,
            ArgumentType = new DamlTypeRef("other-id", "N", "ForeignArg"),
            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit)
        };
        var foreignTemplate = new DamlTemplate { Name = "Thing", Choices = [foreignChoice] };
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
        var resolver = new DarCrossPackageResolver(new FakeDarSource(main, other), new CodeGenOptions());
        var context = ContextFor(main);

        var first = resolver.Resolve(new DamlTypeRef("other-id", "N", "ForeignArg"), context);
        var enumerationsAfterFirst = countingModules.EnumerationCount;
        var second = resolver.Resolve(new DamlTypeRef("other-id", "N", "ForeignArg"), context);

        first.Should().Be("global::N.Thing.ForeignArg");
        second.Should().Be(first);
        enumerationsAfterFirst.Should().BeGreaterThan(0, "the first resolve builds the foreign-choice-arg map by walking the package's modules");
        countingModules.EnumerationCount.Should().Be(enumerationsAfterFirst,
            "the memo must serve the second resolve without rebuilding the map — so the foreign package's modules are not walked again");
    }

    [Fact]
    public void DarCrossPackageResolver_the_foreign_interface_memo_builds_the_set_once_across_repeated_resolves()
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
        var resolver = new DarCrossPackageResolver(new FakeDarSource(main, other), new CodeGenOptions());
        var context = ContextFor(main);

        var first = resolver.Resolve(new DamlTypeRef("other-id", "Splice.Holding", "Holding"), context);
        var enumerationsAfterFirst = countingModules.EnumerationCount;
        var second = resolver.Resolve(new DamlTypeRef("other-id", "Splice.Holding", "Holding"), context);

        first.Should().Be("global::Splice.Holding.IHolding");
        second.Should().Be(first);
        enumerationsAfterFirst.Should().BeGreaterThan(0, "the first resolve builds the foreign-interface set by walking the package's modules");
        countingModules.EnumerationCount.Should().Be(enumerationsAfterFirst,
            "the memo must serve the second resolve without rebuilding the set — so the foreign package's modules are not walked again");
    }

    [Fact]
    public void DarCrossPackageResolver_the_foreign_choice_arg_memo_is_dar_scoped_across_packages_in_one_generate()
    {
        var argType = Record("ForeignArg");
        var foreignChoice = new DamlChoice
        {
            Name = "Do",
            Consuming = true,
            ArgumentType = new DamlTypeRef("other-id", "N", "ForeignArg"),
            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit)
        };
        var foreignTemplate = new DamlTemplate { Name = "Thing", Choices = [foreignChoice] };
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
        var resolver = new DarCrossPackageResolver(new FakeDarSource(main, other, dep), new CodeGenOptions());

        var fromMain = resolver.Resolve(new DamlTypeRef("other-id", "N", "ForeignArg"), ContextFor(main));
        var fromDep = resolver.Resolve(new DamlTypeRef("other-id", "N", "ForeignArg"), ContextFor(dep));

        fromMain.Should().Be("global::N.Thing.ForeignArg");
        fromDep.Should().Be(fromMain);
        resolver.DiscoveredExternalPackageIds.Should().BeEquivalentTo("other-id");
    }

    [Fact]
    public void DarCrossPackageResolver_the_foreign_data_type_index_is_built_once_across_repeated_lookups()
    {
        var countingModules = new CountingModules([Module("N", Record("Widget"))]);
        var other = new DamlPackage
        {
            PackageId = "other-id",
            Name = "other-pkg",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = countingModules,
            DependencyReferences = []
        };
        var main = Package("main-id", "my-pkg", Module("M", Record("Thing")));
        var resolver = new DarCrossPackageResolver(new FakeDarSource(main, other), new CodeGenOptions());

        var first = resolver.DataTypeDefinitions("other-id")[("N", "Widget")];
        var enumerationsAfterFirst = countingModules.EnumerationCount;
        var second = resolver.DataTypeDefinitions("other-id")[("N", "Widget")];

        first.Should().ContainSingle().Which.Should().BeOfType<DamlRecordDefinition>();
        second.Should().BeEquivalentTo(first);
        enumerationsAfterFirst.Should().BeGreaterThan(0, "the first lookup builds the index by walking the package's modules");
        countingModules.EnumerationCount.Should().Be(enumerationsAfterFirst,
            "the memo must serve the second lookup without rebuilding the index — so the foreign package's modules are not walked again");
    }

    [Fact]
    public void DarCrossPackageResolver_the_data_type_index_of_a_package_absent_from_the_dar_is_empty()
    {
        var main = Package("main-id", "my-pkg", Module("M", Record("Widget")));
        var resolver = new DarCrossPackageResolver(new FakeDarSource(main), new CodeGenOptions());

        resolver.DataTypeDefinitions("absent-id").Should().BeEmpty();
    }

    [Fact]
    public void Resolve_returns_the_bare_name_and_records_nothing_for_an_unmapped_stdlib_type()
    {
        var main = Package("main-id", "my-pkg", Module("M", Record("Widget")));
        var stdlib = Package("stdlib-id", "daml-stdlib", Module("DA.Mystery.Types", Record("Mystery")));
        var resolver = new DarCrossPackageResolver(
            new FakeDarSource(main, stdlib), new CodeGenOptions());

        var result = resolver.Resolve(new DamlTypeRef("stdlib-id", "DA.Mystery.Types", "Mystery"), ContextFor(main));

        result.Should().Be("Mystery");
        resolver.DiscoveredExternalPackageIds.Should().NotContain("stdlib-id");
        resolver.DiscoveredExternalPackageIds.Should().BeEmpty();
    }
}
