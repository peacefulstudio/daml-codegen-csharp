// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using AwesomeAssertions;
using Xunit;
using static Daml.Codegen.CSharp.Tests.TestHelpers.DamlModelBuilder;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class InterfaceEmitterTests
{
    private const string LocalPackageId = "test-package-id";
    private const string ModuleName = "Test.Module";

    private sealed class StubResolver : ICrossPackageResolver
    {
        public string Resolve(DamlTypeRef typeRef, PackageEmitContext context) => Identifiers.Sanitize(typeRef.Name);

        public IReadOnlySet<string> DiscoveredExternalPackageIds => new HashSet<string>();

        public DamlPackage? LookupPackage(string packageId) => null;
    }

    private static DamlPackage Package(DamlModule module) =>
        new()
        {
            PackageId = LocalPackageId,
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

    private static CodeGenOptions Options(bool generateXmlDocs) =>
        new() { RootNamespace = "Test.Package", GenerateXmlDocs = generateXmlDocs };

    private static string EmitInterface(DamlInterface iface, DamlDataType[]? dataTypes = null, bool generateXmlDocs = true)
    {
        var module = new DamlModule
        {
            Name = ModuleName,
            Templates = [],
            DataTypes = dataTypes ?? [],
            Interfaces = [iface],
        };
        var options = Options(generateXmlDocs);
        var context = PackageEmitContext.ForPackage(Package(module), options);
        var resolver = new StubResolver();
        var mapper = new DamlTypeMapper(context, resolver);
        var choiceEmitter = new ChoiceEmitter(context, resolver, options, mapper, new PartyAnalysis());
        var emitter = new InterfaceEmitter(context, mapper, choiceEmitter, options);
        var sb = new StringBuilder();
        emitter.WriteInterfaceType(new IndentWriter(sb), Package(module), module, iface);
        return sb.ToString();
    }

    private static DamlInterface Interface(string name, DamlType? viewType = null, params DamlChoice[] choices) =>
        new() { Name = name, Choices = choices, ViewType = viewType };

    [Fact]
    public void emits_the_interface_declaration_with_an_I_prefix()
    {
        var output = EmitInterface(Interface("Transferable"));

        output.Should().Contain("public interface ITransferable");
    }

    [Fact]
    public void declares_IDamlInterface_as_the_base_facet()
    {
        var output = EmitInterface(Interface("Lockable"));

        output.Should().Contain(": IDamlInterface");
    }

    [Fact]
    public void emits_static_interface_metadata()
    {
        var output = EmitInterface(Interface("Holdable"));

        output.Should().Contain("static Identifier IDamlInterface.InterfaceId =>");
        output.Should().Contain("\"test-package-id\"");
        output.Should().Contain("\"Test.Module\"");
        output.Should().Contain("\"Holdable\"");
        output.Should().Contain("static string IDamlInterface.PackageId =>");
        output.Should().Contain("static string IDamlInterface.PackageName =>");
        output.Should().Contain("static Version IDamlInterface.PackageVersion =>");
    }

    [Fact]
    public void emits_explicit_daml_type_descriptor()
    {
        var output = EmitInterface(Interface("Holdable"));

        output.Should().Contain(
            "static DamlTypeDescriptor global::Daml.Runtime.IDamlType.DamlTypeId => new(new Identifier(\"test-package-id\", \"Test.Module\", \"Holdable\"), DamlTypeKind.Interface, \"test-package\");");
    }

    [Fact]
    public void adds_the_IHasView_facet_when_the_interface_has_a_view_type()
    {
        var output = EmitInterface(
            Interface("Asset", viewType: new DamlTypeRef("", ModuleName, "AssetView")),
            dataTypes:
            [
                new DamlDataType
                {
                    Name = "AssetView",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("amount", new DamlPrimitiveType(DamlPrimitive.Numeric)),
                    ]),
                },
            ]);

        output.Should().Contain("IHasView<AssetView>");
    }

    [Fact]
    public void omits_the_IHasView_facet_when_the_interface_has_no_view_type()
    {
        var output = EmitInterface(Interface("Lockable"));

        output.Should().NotContain("IHasView");
    }

    [Fact]
    public void renders_each_choice_as_a_signature_comment()
    {
        var output = EmitInterface(Interface(
            "Transferable",
            viewType: null,
            new DamlChoice
            {
                Name = "Transfer",
                Consuming = true,
                ArgumentType = new DamlPrimitiveType(DamlPrimitive.Party),
                ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
            }));

        output.Should().Contain("// Choice Transfer");
    }

    [Fact]
    public void emits_the_sibling_choice_exerciser_class_when_the_interface_has_choices()
    {
        var output = EmitInterface(Interface(
            "Transferable",
            viewType: null,
            new DamlChoice
            {
                Name = "Transfer",
                Consuming = true,
                ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                Controllers = DamlPartyAnalysis.Dynamic,
                Observers = DamlPartyAnalysis.Dynamic,
            }));

        output.Should().Contain("public static class ITransferableExtensions");
        output.Should().Contain("public static async Task<ExerciseOutcome<TransactionResult>> TransferAsync(");
    }

    [Fact]
    public void emits_no_choice_exerciser_class_when_the_interface_has_no_choices()
    {
        var output = EmitInterface(Interface("Lockable"));

        output.Should().NotContain("public static class");
    }

    [Fact]
    public void emits_every_xml_doc_when_enabled()
    {
        var output = EmitInterface(Interface("Documented"), generateXmlDocs: true);

        output.Should().Contain("/// <summary>");
        output.Should().Contain("/// Generated from Daml interface Test.Module:Documented");
        output.Should().Contain("/// <summary>Gets the interface identifier.</summary>");
        output.Should().Contain("/// <summary>Gets the package ID.</summary>");
        output.Should().Contain("/// <summary>Gets the package name.</summary>");
        output.Should().Contain("/// <summary>Gets the package version.</summary>");
    }

    [Fact]
    public void omits_every_xml_doc_when_disabled()
    {
        var output = EmitInterface(Interface("Documented"), generateXmlDocs: false);

        output.Should().NotContain("/// Generated from Daml interface Test.Module:Documented");
        output.Should().NotContain("Gets the interface identifier");
        output.Should().NotContain("Gets the package ID");
        output.Should().NotContain("Gets the package name");
        output.Should().NotContain("Gets the package version");

        output.Should().Contain("public interface IDocumented");
        output.Should().Contain("static Identifier IDamlInterface.InterfaceId =>");
        output.Should().Contain("static string IDamlInterface.PackageId =>");
    }

    [Fact]
    public void filters_interfaces_with_the_root_filter()
    {
        var options = new CodeGenOptions
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            UseRecordTypes = true,
            UsePrimaryConstructors = true,
            RootFilter = "Test\\.Module:Include.*",
        };

        var module = new DamlModule
        {
            Name = ModuleName,
            Templates = [],
            DataTypes = [],
            Interfaces =
            [
                Interface("IncludeMe"),
                Interface("ExcludeMe"),
            ],
        };

        var files = CreateGenerator(options).Generate(CreateTestDar(module));

        var interfaceFiles = files
            .Where(f => f.RelativePath.Contains("IIncludeMe") || f.RelativePath.Contains("IExcludeMe"))
            .ToList();
        interfaceFiles.Should().HaveCount(1);
        interfaceFiles[0].RelativePath.Should().Contain("IIncludeMe");
    }
}
