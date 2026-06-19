// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using FluentAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class ChoiceEmitterDescriptorTests
{
    private const string LocalPackageId = "pkg-id";
    private const string StdlibPackageId = "stdlib-pkg";

    private sealed class StubResolver(
        string resolvedName = "Resolved",
        IReadOnlyDictionary<string, DamlPackage>? packages = null) : ICrossPackageResolver
    {
        private readonly IReadOnlyDictionary<string, DamlPackage> _packages = packages ?? new Dictionary<string, DamlPackage>();

        public string Resolve(DamlTypeRef typeRef, PackageEmitContext context) => resolvedName;

        public IReadOnlySet<string> DiscoveredExternalPackageIds => new HashSet<string>();

        public DamlPackage? LookupPackage(string packageId) =>
            _packages.TryGetValue(packageId, out var package) ? package : null;
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
                    Name = "Main",
                    Templates = [],
                    DataTypes = dataTypes,
                    Interfaces = [],
                },
            ],
            DependencyReferences = [],
        };

    private static PackageEmitContext Context(DamlPackage package) =>
        PackageEmitContext.ForPackage(package, new CodeGenOptions { RootNamespace = "Test.Package" });

    private static ChoiceEmitter Emitter(PackageEmitContext context, StubResolver resolver) =>
        new(context, resolver, new CodeGenOptions { RootNamespace = "Test.Package" }, new DamlTypeMapper(context, resolver), new PartyAnalysis());

    private static string EmitDescriptors(DamlTemplate template, DamlPackage package, StubResolver? resolver = null)
    {
        var context = Context(package);
        var actualResolver = resolver ?? new StubResolver();
        var emitter = Emitter(context, actualResolver);
        var sb = new StringBuilder();
        var indent = new IndentWriter(sb) { CurrentTypeName = template.Name };
        emitter.WriteChoiceDescriptors(indent, template);
        return sb.ToString();
    }

    private static DamlChoice Choice(string name, DamlType argumentType, DamlType returnType, bool consuming = true) =>
        new()
        {
            Name = name,
            ArgumentType = argumentType,
            ReturnType = returnType,
            Consuming = consuming,
            Controllers = DamlPartyAnalysis.Dynamic,
            Observers = DamlPartyAnalysis.Dynamic,
        };

    private static DamlTemplate Template(params DamlChoice[] choices) =>
        new()
        {
            Name = "Asset",
            Fields = [],
            Choices = choices,
            Signatories = DamlPartyAnalysis.Dynamic,
            Observers = DamlPartyAnalysis.Dynamic,
        };

    [Fact]
    public void emits_a_choice_descriptor_property_for_a_unit_returning_choice()
    {
        var choice = Choice("Accept", new DamlPrimitiveType(DamlPrimitive.Unit), new DamlPrimitiveType(DamlPrimitive.Unit));

        var output = EmitDescriptors(Template(choice), Package());

        output.Should().Contain("public static Choice<Asset, DamlUnit, DamlUnit> ChoiceAccept { get; } = new()");
        output.Should().Contain("Name = new ChoiceName(\"Accept\"),");
        output.Should().Contain("Consuming = true,");
        output.Should().Contain("ArgumentEncoder = _ => DamlUnit.Instance,");
        output.Should().Contain("ResultDecoder = _ => DamlUnit.Instance");
    }

    [Fact]
    public void emits_a_fallback_arg_record_for_an_unresolvable_argument_type()
    {
        var choice = Choice("Mystery", new DamlTypeVar("a"), new DamlPrimitiveType(DamlPrimitive.Unit));

        var output = EmitDescriptors(Template(choice), Package());

        output.Should().Contain("public sealed record MysteryArg");
        output.Should().NotContain("ChoiceMystery");
    }

    [Fact]
    public void get_choice_argument_info_classifies_unit_as_non_fallback_damlunit()
    {
        var context = Context(Package());
        var resolver = new StubResolver();
        var emitter = Emitter(context, resolver);
        var choice = Choice("Accept", new DamlPrimitiveType(DamlPrimitive.Unit), new DamlPrimitiveType(DamlPrimitive.Unit));

        var (typeName, _, isFallback, _) = emitter.GetChoiceArgumentInfo(choice, context.DataTypes);

        typeName.Should().Be("DamlUnit");
        isFallback.Should().BeFalse();
    }

    [Fact]
    public void get_choice_argument_info_flags_an_unresolvable_argument_as_fallback()
    {
        var context = Context(Package());
        var resolver = new StubResolver();
        var emitter = Emitter(context, resolver);
        var choice = Choice("Mystery", new DamlTypeVar("a"), new DamlPrimitiveType(DamlPrimitive.Unit));

        var (typeName, _, isFallback, _) = emitter.GetChoiceArgumentInfo(choice, context.DataTypes);

        typeName.Should().Be("MysteryArg");
        isFallback.Should().BeTrue();
    }

    [Fact]
    public void get_choice_argument_info_resolves_a_local_record_argument_to_a_nested_type()
    {
        var argRecord = new DamlDataType
        {
            Name = "TransferArg",
            Definition = new DamlRecordDefinition([new DamlFieldDefinition("newOwner", new DamlPrimitiveType(DamlPrimitive.Party))]),
        };
        var package = Package(argRecord);
        var context = Context(package);
        var resolver = new StubResolver();
        var emitter = Emitter(context, resolver);
        var choice = Choice("Transfer", new DamlTypeRef(LocalPackageId, "Main", "TransferArg"), new DamlPrimitiveType(DamlPrimitive.Unit));

        var (typeName, fields, isFallback, isNested) = emitter.GetChoiceArgumentInfo(choice, context.DataTypes);

        typeName.Should().Be("Transfer");
        isFallback.Should().BeFalse();
        isNested.Should().BeTrue();
        fields.Should().NotBeNull();
    }
}
