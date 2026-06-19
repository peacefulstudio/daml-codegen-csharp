// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class PackageEmitContextTests
{
    private static DamlPackage Package(string name, params DamlModule[] modules) =>
        new()
        {
            PackageId = "pkg-id",
            Name = name,
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = modules,
            DependencyReferences = []
        };

    private static DamlModule Module(
        string name,
        IReadOnlyList<DamlDataType>? dataTypes = null,
        IReadOnlyList<DamlTemplate>? templates = null,
        IReadOnlyList<DamlInterface>? interfaces = null) =>
        new()
        {
            Name = name,
            DataTypes = dataTypes ?? [],
            Templates = templates ?? [],
            Interfaces = interfaces ?? []
        };

    private static DamlDataType Record(string name, params DamlFieldDefinition[] fields) =>
        new() { Name = name, Definition = new DamlRecordDefinition(fields) };

    private static DamlDataType Enum(string name, params string[] ctors) =>
        new() { Name = name, Definition = new DamlEnumDefinition(ctors) };

    private static DamlDataType Variant(string name, params DamlVariantConstructor[] ctors) =>
        new() { Name = name, Definition = new DamlVariantDefinition(ctors) };

    private static CodeGenOptions Options(string? rootNamespace = null) =>
        new() { RootNamespace = rootNamespace };

    [Fact]
    public void for_package_derives_root_namespace_from_package_name()
    {
        var context = PackageEmitContext.ForPackage(Package("cats-markets"), Options());

        context.RootNamespace.Should().Be("Cats.Markets");
    }

    [Fact]
    public void for_package_honours_the_root_namespace_override()
    {
        var context = PackageEmitContext.ForPackage(Package("cats-markets"), Options("My.Override"));

        context.RootNamespace.Should().Be("My.Override");
    }

    [Fact]
    public void for_package_scopes_the_qualifier_to_the_root_namespace()
    {
        var context = PackageEmitContext.ForPackage(Package("canton-party-replication"), Options());

        context.Qualifier.AllNamespaces.Should().BeEquivalentTo(
            "Canton", "Canton.Party", "Canton.Party.Replication");
    }

    [Fact]
    public void for_package_collects_data_types_across_all_modules()
    {
        var context = PackageEmitContext.ForPackage(
            Package(
                "p",
                Module("M1", dataTypes: [Record("Alpha")]),
                Module("M2", dataTypes: [Record("Beta")])),
            Options());

        context.DataTypes.Keys.Should().BeEquivalentTo("Alpha", "Beta");
    }

    [Fact]
    public void for_package_data_type_lookup_is_last_wins_across_module_name_collisions()
    {
        var first = Record("Amulet", new DamlFieldDefinition("a", new DamlPrimitiveType(DamlPrimitive.Text)));
        var second = Enum("Amulet", "X");
        var context = PackageEmitContext.ForPackage(
            Package(
                "p",
                Module("Splice.Amulet", dataTypes: [first]),
                Module("Splice.AmuletConfig", dataTypes: [second])),
            Options());

        context.DataTypes["Amulet"].Should().BeSameAs(second);
    }

    [Fact]
    public void for_package_records_local_enums_module_qualified()
    {
        var context = PackageEmitContext.ForPackage(
            Package("p", Module("Splice.AmuletConfig", dataTypes: [Enum("Amulet", "Free", "Paid")])),
            Options());

        context.LocalEnumQualifiedNames.Should().BeEquivalentTo("Splice.AmuletConfig:Amulet");
    }

    [Fact]
    public void for_package_records_local_variants_module_qualified()
    {
        var context = PackageEmitContext.ForPackage(
            Package("p", Module("M", dataTypes:
            [
                Variant("Shape", new DamlVariantConstructor("Circle", null))
            ])),
            Options());

        context.LocalVariantQualifiedNames.Should().BeEquivalentTo("M:Shape");
    }

    [Fact]
    public void for_package_flags_interface_placeholder_records_module_local()
    {
        var holdingRecord = Record("Holding");
        var unrelatedHolding = Record("Holding");
        var iface = new DamlInterface { Name = "Holding", Choices = [] };
        var context = PackageEmitContext.ForPackage(
            Package(
                "p",
                Module("Splice.Holding", dataTypes: [holdingRecord], interfaces: [iface]),
                Module("Other", dataTypes: [unrelatedHolding])),
            Options());

        context.InterfacePlaceholderQualifiedNames.Should().BeEquivalentTo("Splice.Holding:Holding");
    }

    [Fact]
    public void for_package_maps_nested_choice_argument_types_to_their_parent_template()
    {
        var argType = Record("TransferArg", new DamlFieldDefinition("to", new DamlPrimitiveType(DamlPrimitive.Party)));
        var choice = new DamlChoice
        {
            Name = "Transfer",
            Consuming = true,
            ArgumentType = new DamlTypeRef("", "M", "TransferArg"),
            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit)
        };
        var template = new DamlTemplate
        {
            Name = "Account",
            Fields = [],
            Choices = [choice]
        };
        var context = PackageEmitContext.ForPackage(
            Package("p", Module("M", dataTypes: [argType], templates: [template])),
            Options());

        context.LocalChoiceArgToTemplate.Should().ContainKey("M:TransferArg")
            .WhoseValue.Should().Be("Account");
    }

    [Fact]
    public void for_package_does_not_map_choice_args_that_are_not_local_data_types()
    {
        var choice = new DamlChoice
        {
            Name = "Transfer",
            Consuming = true,
            ArgumentType = new DamlTypeRef("", "M", "NotDeclaredHere"),
            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit)
        };
        var template = new DamlTemplate
        {
            Name = "Account",
            Fields = [],
            Choices = [choice]
        };
        var context = PackageEmitContext.ForPackage(
            Package("p", Module("M", templates: [template])),
            Options());

        context.LocalChoiceArgToTemplate.Should().NotContainKey("M:NotDeclaredHere");
    }

    [Fact]
    public void for_package_disambiguates_same_named_choice_arg_types_across_modules()
    {
        DamlModule ModuleWithTransferChoice(string moduleName, string templateName) => Module(
            moduleName,
            dataTypes: [Record("Transfer", new DamlFieldDefinition("to", new DamlPrimitiveType(DamlPrimitive.Party)))],
            templates:
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
            ]);

        var context = PackageEmitContext.ForPackage(
            Package(
                "p",
                ModuleWithTransferChoice("Banking", "Account"),
                ModuleWithTransferChoice("Custody", "Vault")),
            Options());

        context.LocalChoiceArgToTemplate["Banking:Transfer"].Should().Be("Account");
        context.LocalChoiceArgToTemplate["Custody:Transfer"].Should().Be("Vault");
    }

    [Fact]
    public void for_package_warns_and_keeps_first_on_same_module_choice_arg_name_clash()
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
                    ArgumentType = new DamlTypeRef("", "M", "Transfer"),
                    ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit)
                }
            ]
        };
        var logger = Substitute.For<ICodegenLogger>();

        var context = PackageEmitContext.ForPackage(
            Package(
                "p",
                Module(
                    "M",
                    dataTypes: [Record("Transfer", new DamlFieldDefinition("to", new DamlPrimitiveType(DamlPrimitive.Party)))],
                    templates: [TemplateWithTransferChoice("Account"), TemplateWithTransferChoice("Vault")])),
            Options(),
            logger);

        context.LocalChoiceArgToTemplate["M:Transfer"].Should().Be("Account");
        logger.Received(1).Warning(Arg.Is<string>(m => m.Contains("M:Transfer") && m.Contains("Account") && m.Contains("Vault") && m.Contains("in the same package")));
    }
}
