// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class ChoiceEmitterInterfaceDescriptorTests
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

    private static DamlPackage Package(params DamlInterface[] interfaces) =>
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
                    DataTypes = [],
                    Interfaces = interfaces,
                },
            ],
            DependencyReferences = [],
        };

    private static DamlPackage StdlibPackage() =>
        new()
        {
            PackageId = StdlibPackageId,
            Name = "daml-stdlib",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = [],
        };

    private static PackageEmitContext Context(DamlPackage package) =>
        PackageEmitContext.ForPackage(package, new CodeGenOptions { NamespacePrefix = "Test.Package" }, isMainPackage: true).Single();

    private static ChoiceEmitter Emitter(PackageEmitContext context, StubResolver resolver) =>
        new(context, resolver, new CodeGenOptions { NamespacePrefix = "Test.Package" }, new DamlTypeMapper(context, resolver), new PartyAnalysis());

    private static string EmitDescriptors(DamlInterface iface, string interfaceName, DamlPackage package, StubResolver? resolver = null)
    {
        var context = Context(package);
        var actualResolver = resolver ?? new StubResolver();
        var emitter = Emitter(context, actualResolver);
        var sb = new StringBuilder();
        var indent = new IndentWriter(sb) { CurrentTypeName = interfaceName };
        emitter.WriteInterfaceChoiceDescriptors(indent, iface, interfaceName);
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

    private static DamlInterface Interface(string name, params DamlChoice[] choices) =>
        new() { Name = name, Choices = choices, ViewType = null };

    [Fact]
    public void ChoiceEmitterInterfaceDescriptor_emits_a_choice_descriptor_property_for_a_unit_returning_choice()
    {
        var choice = Choice("Accept", new DamlPrimitiveType(DamlPrimitive.Unit), new DamlPrimitiveType(DamlPrimitive.Unit));

        var output = EmitDescriptors(Interface("IAsset", choice), "IAsset", Package());

        output.Should().Contain("public static Choice<IAsset, DamlUnit, DamlUnit> ChoiceAccept { get; } = new()");
        output.Should().Contain("Name = new ChoiceName(\"Accept\"),");
        output.Should().Contain("Consuming = true,");
        output.Should().Contain("ArgumentEncoder = _ => DamlUnit.Instance,");
        output.Should().Contain("ArgumentDecoder = val => val is DamlUnit u ? u : throw new global::System.InvalidOperationException(\"Choice 'Accept' argument must decode to DamlUnit.\"),");
        output.Should().Contain("ResultDecoder = _ => DamlUnit.Instance");
        output.Should().Contain("ArgumentJsonReader = (json, context) => global::Daml.Runtime.Serialization.DamlLfJsonDecoders.ReadUnit(json, context),");
    }

    [Fact]
    public void ChoiceEmitterInterfaceDescriptor_requires_the_namespace_for_a_bare_primitive_argument_type_not_only_the_return_type()
    {
        var choice = Choice("SetDate", new DamlPrimitiveType(DamlPrimitive.Date), new DamlPrimitiveType(DamlPrimitive.Unit));
        var context = Context(Package());
        var emitter = Emitter(context, new StubResolver());
        var sb = new StringBuilder();
        var indent = new IndentWriter(sb) { CurrentTypeName = "IAsset" };

        emitter.WriteInterfaceChoiceDescriptors(indent, Interface("IAsset", choice), "IAsset");

        indent.RequiredUsings.Should().Contain("System");
    }

    [Fact]
    public void ChoiceEmitterInterfaceDescriptor_decodes_a_record_argument_through_its_FromRecord()
    {
        var choice = Choice("Transfer", new DamlTypeRef(LocalPackageId, "Main", "TransferArg"), new DamlPrimitiveType(DamlPrimitive.Unit));

        var output = EmitDescriptors(Interface("IAsset", choice), "IAsset", Package());

        output.Should().Contain("public static Choice<IAsset, Resolved, DamlUnit> ChoiceTransfer { get; } = new()");
        output.Should().Contain("ArgumentEncoder = arg => arg.ToRecord(),");
        output.Should().Contain("ArgumentDecoder = val => global::Test.Package.Main.Resolved.FromRecord(val.As<DamlRecord>()),");
    }

    [Fact]
    public void ChoiceEmitterInterfaceDescriptor_encodes_synthetic_stdlib_interface_archive_argument_as_empty_record()
    {
        var choice = Choice("Archive", new DamlTypeRef(StdlibPackageId, "DA.Internal.Template", "Archive"), new DamlPrimitiveType(DamlPrimitive.Unit));
        var resolver = new StubResolver(packages: new Dictionary<string, DamlPackage> { [StdlibPackageId] = StdlibPackage() });

        var output = EmitDescriptors(Interface("IArchivable", choice), "IArchivable", Package(), resolver);

        output.Should().Contain("public static Choice<IArchivable, DamlUnit, DamlUnit> ChoiceArchive { get; } = new()");
        output.Should().Contain("ArgumentEncoder = _ => DamlRecord.Create(),");
        output.Should().NotContain("ArgumentEncoder = _ => DamlUnit.Instance,");
        output.Should().Contain("ArgumentDecoder = val => val is DamlRecord { Fields.Count: 0 } ? DamlUnit.Instance : throw new global::System.InvalidOperationException(\"Choice 'Archive' argument must decode to an empty record.\"),");
        output.Should().Contain(
            "ArgumentJsonReader = (json, context) =>\n"
            + "    {\n"
            + "        global::Daml.Runtime.Serialization.DamlLfJsonDecoders.RequireObject(json, context);\n"
            + "        return DamlRecord.Create();\n"
            + "    },");
    }

    [Fact]
    public void ChoiceEmitterInterfaceDescriptor_emits_one_property_per_choice_in_declaration_order()
    {
        var accept = Choice("Accept", new DamlPrimitiveType(DamlPrimitive.Unit), new DamlPrimitiveType(DamlPrimitive.Unit));
        var reject = Choice("Reject", new DamlPrimitiveType(DamlPrimitive.Unit), new DamlPrimitiveType(DamlPrimitive.Unit));

        var output = EmitDescriptors(Interface("IAsset", accept, reject), "IAsset", Package());

        output.IndexOf("ChoiceAccept", StringComparison.Ordinal)
            .Should().BeLessThan(output.IndexOf("ChoiceReject", StringComparison.Ordinal));
    }

    [Fact]
    public void ChoiceEmitterInterfaceDescriptor_decodes_a_bare_primitive_argument_through_the_general_value_conversion()
    {
        var choice = Choice("Transfer", new DamlPrimitiveType(DamlPrimitive.Party), new DamlPrimitiveType(DamlPrimitive.Unit));

        var output = EmitDescriptors(Interface("IHolding", choice), "IHolding", Package());

        output.Should().Contain("public static Choice<IHolding, Party, DamlUnit> ChoiceTransfer { get; } = new()");
        output.Should().Contain("ArgumentEncoder = arg => arg.ToDamlValue(),");
        output.Should().Contain("ArgumentDecoder = val => Party.FromDamlValue(val.As<DamlParty>()),");
    }
}
