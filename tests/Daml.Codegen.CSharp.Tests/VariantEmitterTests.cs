// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class VariantEmitterTests
{
    private const string LocalPackageId = "pkg-id";
    private const string ModuleName = "Test.Module";

    private sealed class StubResolver : ICrossPackageResolver
    {
        public string Resolve(DamlTypeRef typeRef, PackageEmitContext context) => typeRef.Name;

        public IReadOnlySet<string> DiscoveredExternalPackageIds => new HashSet<string>();

        public DamlPackage? LookupPackage(string packageId) => null;
    }

    private static DamlPackage Package(params DamlDataType[] dataTypes) =>
        new()
        {
            PackageId = LocalPackageId,
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules =
            [
                new DamlModule
                {
                    Name = ModuleName,
                    Templates = [],
                    DataTypes = dataTypes,
                    Interfaces = [],
                },
            ],
            DependencyReferences = [],
        };

    private static CodeGenOptions Options(bool generateXmlDocs) =>
        new() { RootNamespace = "Test.Package", GenerateXmlDocs = generateXmlDocs };

    private static string Emit(string targetName, DamlDataType[] packageTypes, bool generateXmlDocs = true)
    {
        var options = Options(generateXmlDocs);
        var context = PackageEmitContext.ForPackage(Package(packageTypes), options);
        var resolver = new StubResolver();
        var mapper = new DamlTypeMapper(context, resolver);
        var emitter = new VariantEmitter(context, resolver, options, mapper);
        var target = packageTypes.First(d => d.Name == targetName);
        var sb = new StringBuilder();
        emitter.WriteVariantType(new IndentWriter(sb), target, (DamlVariantDefinition)target.Definition!);
        return sb.ToString();
    }

    private static DamlDataType Variant(string name, params DamlVariantConstructor[] constructors) =>
        new() { Name = name, Definition = new DamlVariantDefinition(constructors) };

    private static DamlVariantConstructor Ctor(string name, DamlType? argument = null) =>
        new(name, argument);

    private static DamlType Text => new DamlPrimitiveType(DamlPrimitive.Text);

    private static string EmitVariant(DamlDataType target, bool generateXmlDocs = true) =>
        Emit(target.Name, [target], generateXmlDocs);

    [Fact]
    public void emits_the_abstract_base_record_and_every_constructor()
    {
        var output = EmitVariant(Variant(
            "PaymentMethod",
            Ctor("Cash"),
            Ctor("Card", Text),
            Ctor("BankTransfer", Text)));

        output.Should().Contain("public abstract record PaymentMethod : IDamlVariant");
        output.Should().Contain("public abstract string Tag { get; }");

        output.Should().Contain("public sealed record Cash() : PaymentMethod");
        output.Should().Contain("public sealed record Card(string Value) : PaymentMethod");
        output.Should().Contain("public sealed record BankTransfer(string Value) : PaymentMethod");

        output.Should().Contain("public override string Tag => \"Cash\"");
        output.Should().Contain("public override string Tag => \"Card\"");
        output.Should().Contain("public override string Tag => \"BankTransfer\"");
    }

    [Fact]
    public void documents_case_Tag_and_ToVariant_overrides_with_inheritdoc()
    {
        var output = EmitVariant(Variant("PaymentMethod", Ctor("Cash"), Ctor("Card", Text)));

        output.Should().MatchRegex(@"/// <inheritdoc />\s*\n\s*public override string Tag => ""Cash"";");
        output.Should().MatchRegex(@"/// <inheritdoc />\s*\n\s*public override string Tag => ""Card"";");
        output.Should().MatchRegex(@"/// <inheritdoc />\s*\n\s*public override (global::)?(Daml\.Runtime\.Data\.)?DamlVariant ToVariant\(\)");
    }

    [Fact]
    public void maps_numeric_payload_constructors_to_decimal()
    {
        var numeric = new DamlPrimitiveType(DamlPrimitive.Numeric);
        var output = EmitVariant(Variant("Amount", Ctor("Fixed", numeric), Ctor("Percentage", numeric)));

        output.Should().Contain("public sealed record Fixed(decimal Value) : Amount");
        output.Should().Contain("public sealed record Percentage(decimal Value) : Amount");
    }

    [Fact]
    public void round_trips_primitive_payload_through_ToVariant_and_FromVariant()
    {
        var output = EmitVariant(Variant("Maybe", Ctor("Nothing"), Ctor("Just", Text)));

        output.Should().Contain("public abstract record Maybe : IDamlVariant");
        output.Should().Contain("public abstract DamlVariant ToVariant();");
        output.Should().NotContain("ToRecord");
        output.Should().NotContain("NotImplementedException");

        output.Should().Contain("public override DamlVariant ToVariant() => DamlVariant.Create(\"Nothing\", DamlUnit.Instance);");
        output.Should().Contain("public override DamlVariant ToVariant() => DamlVariant.Create(\"Just\", new DamlText(Value));");

        output.Should().Contain("public static Maybe FromVariant(DamlVariant variant) =>");
        output.Should().Contain("\"Nothing\" => new Nothing(),");
        output.Should().Contain("\"Just\" => new Just(variant.Value.As<DamlText>().Value),");
    }

    [Fact]
    public void routes_variant_payload_through_ToVariant_and_FromVariant()
    {
        var inner = Variant("Inner", Ctor("Lit", new DamlPrimitiveType(DamlPrimitive.Int64)));
        var outer = Variant("Outer", Ctor("Wrap", new DamlTypeRef(string.Empty, ModuleName, "Inner")));

        var output = Emit("Outer", [inner, outer]);

        output.Should().NotContain(".ToRecord()");
        output.Should().NotContain(".FromRecord(");
        output.Should().Contain("public override DamlVariant ToVariant() => DamlVariant.Create(\"Wrap\", Value.ToVariant());");
        output.Should().Contain("\"Wrap\" => new Wrap(Inner.FromVariant(variant.Value.As<DamlVariant>())),");
    }

    [Fact]
    public void uses_indefinite_article_an_for_vowel_initial_variant_in_FromVariant_summary()
    {
        var output = EmitVariant(Variant("Outcome", Ctor("Win", Text), Ctor("Pending")));

        output.Should().Contain("/// <summary>Reconstructs an Outcome by dispatching on the DamlVariant constructor tag.</summary>");
        output.Should().NotContain("Reconstructs a Outcome");
    }

    [Fact]
    public void uses_indefinite_article_a_for_consonant_initial_variant_in_FromVariant_summary()
    {
        var output = EmitVariant(Variant("PaymentMethod", Ctor("Cash"), Ctor("Card", Text)));

        output.Should().Contain("/// <summary>Reconstructs a PaymentMethod by dispatching on the DamlVariant constructor tag.</summary>");
        output.Should().NotContain("Reconstructs an PaymentMethod");
    }

    [Fact]
    public void emits_every_xml_doc_when_enabled()
    {
        var output = EmitVariant(Variant("Maybe", Ctor("Nothing"), Ctor("Just", Text)), generateXmlDocs: true);

        output.Should().Contain("/// Generated from Daml variant Maybe");
        output.Should().Contain("/// <summary>Gets the variant constructor name.</summary>");
        output.Should().Contain("/// <summary>Converts to a DamlVariant.</summary>");
        output.Should().Contain("/// <summary>Reconstructs a Maybe by dispatching on the DamlVariant constructor tag.</summary>");
        output.Should().Contain("/// <summary>Nothing constructor (no arguments).</summary>");
        output.Should().Contain("/// <summary>Just constructor.</summary>");
        output.Should().Contain("/// <inheritdoc />");
    }

    [Fact]
    public void omits_every_xml_doc_when_disabled()
    {
        var output = EmitVariant(Variant("Maybe", Ctor("Nothing"), Ctor("Just", Text)), generateXmlDocs: false);

        output.Should().NotContain("/// Generated from Daml variant Maybe");
        output.Should().NotContain("/// <summary>Gets the variant constructor name.</summary>");
        output.Should().NotContain("/// <summary>Converts to a DamlVariant.</summary>");
        output.Should().NotContain("Reconstructs a Maybe");
        output.Should().NotContain("constructor (no arguments).</summary>");
        output.Should().NotContain("Just constructor.</summary>");
        output.Should().NotContain("/// <inheritdoc />");

        output.Should().Contain("public abstract record Maybe : IDamlVariant");
        output.Should().Contain("public sealed record Just(string Value) : Maybe");
    }
}
