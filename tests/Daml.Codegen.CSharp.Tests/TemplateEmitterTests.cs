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

public class TemplateEmitterTests
{
    private const string LocalPackageId = "test-package-id";
    private const string ModuleName = "Test.Module";

    private sealed class StubResolver : ICrossPackageResolver
    {
        public string Resolve(DamlTypeRef typeRef, PackageEmitContext context) => Identifiers.Sanitize(typeRef.Name);

        public IReadOnlySet<string> DiscoveredExternalPackageIds => new HashSet<string>();

        public DamlPackage? LookupPackage(string packageId) => null;
    }

    private static DamlPackage Package(DamlModule module, Version? version = null, string? upgradedPackageId = null) =>
        new()
        {
            PackageId = LocalPackageId,
            Name = "test-package",
            Version = version ?? new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
            UpgradedPackageId = upgradedPackageId,
        };

    private static CodeGenOptions Options(
        bool generateXmlDocs = true,
        bool useRecordTypes = true,
        bool usePrimaryConstructors = true) =>
        new()
        {
            RootNamespace = "Test.Package",
            GenerateXmlDocs = generateXmlDocs,
            UseRecordTypes = useRecordTypes,
            UsePrimaryConstructors = usePrimaryConstructors,
        };

    private static string EmitTemplate(
        DamlTemplate template,
        DamlDataType[]? dataTypes = null,
        CodeGenOptions? options = null,
        Version? version = null,
        string? upgradedPackageId = null)
    {
        var module = new DamlModule
        {
            Name = ModuleName,
            Templates = [template],
            DataTypes = dataTypes ?? [],
            Interfaces = [],
        };
        options ??= Options();
        var package = Package(module, version, upgradedPackageId);
        var context = PackageEmitContext.ForPackage(package, options);
        var resolver = new StubResolver();
        var mapper = new DamlTypeMapper(context, resolver);
        var party = new PartyAnalysis();
        var recordSerialization = new RecordSerializationEmitter(context, resolver, options, mapper);
        var choiceEmitter = new ChoiceEmitter(context, resolver, options, mapper, party);
        var submissionExtensions = new SubmissionExtensionsEmitter(context, options, party);
        var emitter = new TemplateEmitter(context, resolver, mapper, recordSerialization, choiceEmitter, submissionExtensions, options);
        var sb = new StringBuilder();
        emitter.WriteTemplateType(new IndentWriter(sb), package, module, template, template.Fields);
        return sb.ToString();
    }

    private static DamlTemplate Template(
        string name,
        IReadOnlyList<DamlFieldDefinition>? fields = null,
        IReadOnlyList<DamlChoice>? choices = null,
        DamlType? key = null) =>
        new()
        {
            Name = name,
            Fields = fields ?? [],
            Choices = choices ?? [],
            Key = key,
        };

    private static DamlFieldDefinition Field(string name, DamlPrimitive primitive) =>
        new(name, new DamlPrimitiveType(primitive));

    [Fact]
    public void emits_the_template_record_with_the_ITemplate_facet()
    {
        var output = EmitTemplate(Template("SimpleTemplate", [Field("owner", DamlPrimitive.Party)]));

        output.Should().Contain("public sealed partial record SimpleTemplate");
        output.Should().Contain(": ITemplate");
    }

    [Fact]
    public void emits_static_template_metadata()
    {
        var output = EmitTemplate(Template("Asset", [Field("owner", DamlPrimitive.Party)]));

        output.Should().Contain("public static Identifier TemplateId { get; }");
        output.Should().Contain("\"test-package-id\"");
        output.Should().Contain("\"Test.Module\"");
        output.Should().Contain("\"Asset\"");
        output.Should().Contain("public static string PackageId => \"test-package-id\";");
        output.Should().Contain("public static string PackageName => \"test-package\";");
        output.Should().Contain("public static Version PackageVersion { get; }");
    }

    [Fact]
    public void emits_static_daml_type_descriptor()
    {
        var output = EmitTemplate(Template("Asset", [Field("owner", DamlPrimitive.Party)]));

        output.Should().Contain(
            "public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);");
    }

    [Fact]
    public void emits_the_nested_ContractId_record()
    {
        var output = EmitTemplate(Template("Token", [Field("issuer", DamlPrimitive.Party)]));

        output.Should().Contain("public sealed record ContractId(string Value)");
        output.Should().Contain(": ContractId<Token>(Value)");
        output.Should().Contain("IExercises<Token>");
    }

    [Fact]
    public void emits_the_nested_Contract_record()
    {
        var output = EmitTemplate(Template("Holding", [Field("amount", DamlPrimitive.Numeric)]));

        output.Should().Contain("public sealed record Contract(ContractId Id, Holding Data)");
        output.Should().Contain(": IContract<ContractId, Holding>");
        output.Should().Contain("public static Contract FromCreatedEvent(CreatedEvent @event)");
    }

    [Fact]
    public void maps_all_primitive_fields_to_their_csharp_types()
    {
        var output = EmitTemplate(Template("AllPrimitives",
        [
            Field("textField", DamlPrimitive.Text),
            Field("intField", DamlPrimitive.Int64),
            Field("boolField", DamlPrimitive.Bool),
            Field("numericField", DamlPrimitive.Numeric),
            Field("partyField", DamlPrimitive.Party),
            Field("dateField", DamlPrimitive.Date),
            Field("timestampField", DamlPrimitive.Timestamp),
        ]));

        output.Should().Contain("string TextField");
        output.Should().Contain("long IntField");
        output.Should().Contain("bool BoolField");
        output.Should().Contain("decimal NumericField");
        output.Should().Contain("Party PartyField");
        output.Should().Contain("DateOnly DateField");
        output.Should().Contain("DateTimeOffset TimestampField");
    }

    [Fact]
    public void maps_complex_container_fields()
    {
        var output = EmitTemplate(Template("ComplexFields",
        [
            new DamlFieldDefinition("items", new DamlTypeApp(
                new DamlPrimitiveType(DamlPrimitive.List),
                [new DamlPrimitiveType(DamlPrimitive.Text)])),
            new DamlFieldDefinition("maybeValue", new DamlTypeApp(
                new DamlPrimitiveType(DamlPrimitive.Optional),
                [new DamlPrimitiveType(DamlPrimitive.Int64)])),
            new DamlFieldDefinition("metadata", new DamlTypeApp(
                new DamlPrimitiveType(DamlPrimitive.TextMap),
                [new DamlPrimitiveType(DamlPrimitive.Text)])),
        ]));

        output.Should().Contain("IReadOnlyList<string> Items");
        output.Should().Contain("long? MaybeValue");
        output.Should().Contain("IReadOnlyDictionary<string, string> Metadata");
    }

    [Fact]
    public void emits_the_ToRecord_method()
    {
        var output = EmitTemplate(Template("Item",
        [
            Field("name", DamlPrimitive.Text),
            Field("count", DamlPrimitive.Int64),
        ]));

        output.Should().Contain("public DamlRecord ToRecord()");
        output.Should().Contain("DamlField.Create(\"name\", new DamlText(Name))");
        output.Should().Contain("DamlField.Create(\"count\", new DamlInt64(Count))");
    }

    [Fact]
    public void emits_the_FromRecord_method()
    {
        var output = EmitTemplate(Template("Status",
        [
            Field("isActive", DamlPrimitive.Bool),
            Field("amount", DamlPrimitive.Numeric),
        ]));

        output.Should().Contain("public static Status FromRecord(DamlRecord record)");
        output.Should().Contain("IsActive: record.GetRequiredField(\"isActive\").As<DamlBool>().Value");
        output.Should().Contain("Amount: record.GetRequiredField(\"amount\").As<DamlNumeric>().Value");
    }

    [Fact]
    public void serializes_list_fields_through_the_shared_serializer()
    {
        var output = EmitTemplate(Template("Tagged",
        [
            new DamlFieldDefinition("tags", new DamlTypeApp(
                new DamlPrimitiveType(DamlPrimitive.List),
                [new DamlPrimitiveType(DamlPrimitive.Text)])),
        ]));

        output.Should().Contain("new DamlList(Tags.Select(x => (DamlValue)new DamlText(x)).ToList())");
    }

    [Fact]
    public void serializes_optional_fields_through_the_shared_serializer()
    {
        var output = EmitTemplate(Template("OptionalTemplate",
        [
            new DamlFieldDefinition("maybeText", new DamlTypeApp(
                new DamlPrimitiveType(DamlPrimitive.Optional),
                [new DamlPrimitiveType(DamlPrimitive.Text)])),
        ]));

        output.Should().Contain("MaybeText is { } __MaybeText ? new DamlOptional(new DamlText(__MaybeText)) : DamlOptional.None");
    }

    [Fact]
    public void emits_required_properties_when_primary_constructors_are_disabled()
    {
        var output = EmitTemplate(
            Template("NoConstructor", [Field("value", DamlPrimitive.Text)]),
            options: Options(usePrimaryConstructors: false));

        output.Should().Contain("public sealed partial record NoConstructor : ITemplate");
        output.Should().Contain("public required string Value { get; init; }");
    }

    [Fact]
    public void emits_a_class_when_record_types_are_disabled()
    {
        var output = EmitTemplate(
            Template("ClassTemplate", [Field("value", DamlPrimitive.Text)]),
            options: Options(useRecordTypes: false, usePrimaryConstructors: false));

        output.Should().Contain("public sealed partial class ClassTemplate : ITemplate");
    }

    [Fact]
    public void handles_a_template_with_no_fields()
    {
        var output = EmitTemplate(Template("EmptyTemplate"));

        output.Should().Contain("public sealed partial record EmptyTemplate : ITemplate");
        output.Should().Contain("public DamlRecord ToRecord()");
        output.Should().Contain("DamlRecord.Create(");
    }

    [Fact]
    public void uses_the_package_version_in_metadata()
    {
        var output = EmitTemplate(
            Template("Versioned", [Field("value", DamlPrimitive.Text)]),
            version: new Version(2, 3, 4));

        output.Should().Contain("new(2, 3, 4)");
    }

    [Fact]
    public void adds_the_IHasKey_facet_and_a_throwing_key_accessor_when_the_template_has_a_key()
    {
        var output = EmitTemplate(
            Template("Keyed", [Field("owner", DamlPrimitive.Party)], key: new DamlPrimitiveType(DamlPrimitive.Party)));

        output.Should().Contain(": ITemplate, IHasKey<Party>");
        output.Should().Contain("public Party Key => throw new global::System.NotImplementedException(");
    }

    [Fact]
    public void adds_the_IUpgradeable_facet_when_the_package_is_an_upgrade()
    {
        var output = EmitTemplate(
            Template("Upgraded", [Field("owner", DamlPrimitive.Party)]),
            upgradedPackageId: "old-package-id");

        output.Should().Contain("IUpgradeable");
        output.Should().Contain("public static string? UpgradedPackageId => \"old-package-id\";");
    }

    [Fact]
    public void delegates_choice_descriptor_emission_to_the_choice_emitter()
    {
        var output = EmitTemplate(Template(
            "WithChoice",
            [Field("owner", DamlPrimitive.Party)],
            choices:
            [
                new DamlChoice
                {
                    Name = "DoIt",
                    Consuming = true,
                    ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                    ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                },
            ]));

        output.Should().Contain("ChoiceDoIt");
    }

    [Fact]
    public void delegates_submission_extension_emission_to_the_submission_emitter()
    {
        var output = EmitTemplate(Template("Submittable", [Field("owner", DamlPrimitive.Party)]));

        output.Should().Contain("public static class SubmittableSubmissionExtensions");
        output.Should().Contain("CreateAsync");
    }

    [Fact]
    public void emits_every_xml_doc_when_enabled()
    {
        var output = EmitTemplate(
            Template("Documented", [Field("owner", DamlPrimitive.Party)], key: new DamlPrimitiveType(DamlPrimitive.Party)));

        output.Should().Contain("/// Generated from Daml template Test.Module:Documented");
        output.Should().Contain("/// <summary>Gets the template identifier.</summary>");
        output.Should().Contain("/// <summary>Gets the package ID.</summary>");
        output.Should().Contain("/// <summary>Gets the package name.</summary>");
        output.Should().Contain("/// <summary>Gets the package version.</summary>");
        output.Should().Contain("/// Gets the contract key of type");
        output.Should().Contain("/// <summary>Contract ID for Documented.</summary>");
        output.Should().Contain("/// <summary>Active contract for Documented.</summary>");
        output.Should().Contain("/// <summary>Creates a Contract from a CreatedEvent.</summary>");
    }

    [Fact]
    public void omits_every_xml_doc_when_disabled()
    {
        var output = EmitTemplate(
            Template("Documented", [Field("owner", DamlPrimitive.Party)], key: new DamlPrimitiveType(DamlPrimitive.Party)),
            options: Options(generateXmlDocs: false));

        output.Should().NotContain("/// Generated from Daml template Test.Module:Documented");
        output.Should().NotContain("Gets the template identifier");
        output.Should().NotContain("Gets the package ID");
        output.Should().NotContain("Gets the package name");
        output.Should().NotContain("Gets the package version");
        output.Should().NotContain("Gets the contract key of type");
        output.Should().NotContain("Contract ID for Documented");
        output.Should().NotContain("Active contract for Documented");
        output.Should().NotContain("Creates a Contract from a CreatedEvent");

        output.Should().Contain("public sealed partial record Documented");
        output.Should().Contain("public static Identifier TemplateId { get; }");
        output.Should().Contain("public sealed record ContractId(string Value)");
        output.Should().Contain("public Party Key => throw new global::System.NotImplementedException(");
    }

    [Fact]
    public void emits_the_nested_choice_argument_partial_record()
    {
        var module = new DamlModule
        {
            Name = ModuleName,
            Templates = [],
            DataTypes = [],
            Interfaces = [],
        };
        var options = Options();
        var package = Package(module);
        var context = PackageEmitContext.ForPackage(package, options);
        var resolver = new StubResolver();
        var mapper = new DamlTypeMapper(context, resolver);
        var party = new PartyAnalysis();
        var recordSerialization = new RecordSerializationEmitter(context, resolver, options, mapper);
        var choiceEmitter = new ChoiceEmitter(context, resolver, options, mapper, party);
        var submissionExtensions = new SubmissionExtensionsEmitter(context, options, party);
        var emitter = new TemplateEmitter(context, resolver, mapper, recordSerialization, choiceEmitter, submissionExtensions, options);

        var template = Template("Account", [Field("owner", DamlPrimitive.Party)]);
        var choice = new DamlChoice
        {
            Name = "Transfer",
            Consuming = true,
            ArgumentType = new DamlTypeRef("", ModuleName, "TransferArgs"),
            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
        };
        var argDataType = new DamlDataType
        {
            Name = "TransferArgs",
            Definition = new DamlRecordDefinition([Field("newOwner", DamlPrimitive.Party)]),
        };

        var sb = new StringBuilder();
        emitter.WriteNestedChoiceArgumentType(new IndentWriter(sb), template, choice, argDataType);
        var output = sb.ToString();

        output.Should().Contain("public sealed partial record Account");
        output.Should().Contain("public sealed record Transfer(");
        output.Should().Contain("public DamlRecord ToRecord()");
        output.Should().Contain("public static Transfer FromRecord(DamlRecord record)");
    }

    [Fact]
    public void filters_templates_with_the_root_filter()
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
            Templates =
            [
                Template("IncludeMe", [Field("owner", DamlPrimitive.Party)]),
                Template("ExcludeMe", [Field("owner", DamlPrimitive.Party)]),
            ],
            DataTypes = [],
            Interfaces = [],
        };

        var files = CreateGenerator(options).Generate(CreateTestDar(module));

        var templateFiles = files
            .Where(f => f.RelativePath.EndsWith("IncludeMe.cs", StringComparison.Ordinal)
                     || f.RelativePath.EndsWith("ExcludeMe.cs", StringComparison.Ordinal))
            .ToList();
        templateFiles.Should().HaveCount(1);
        templateFiles[0].RelativePath.Should().EndWith("IncludeMe.cs");
    }
}
