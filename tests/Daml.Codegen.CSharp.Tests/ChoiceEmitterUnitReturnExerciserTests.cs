// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class ChoiceEmitterUnitReturnExerciserTests
{
    private const string LocalPackageId = "pkg-id";
    private const string StdlibNamespace = "Daml.Runtime.Stdlib";

    private sealed class StubResolver : ICrossPackageResolver
    {
        public string Resolve(DamlTypeRef typeRef, PackageEmitContext context) => Identifiers.Sanitize(typeRef.Name);

        public IReadOnlySet<string> DiscoveredExternalPackageIds => new HashSet<string>();

        public DamlPackage? LookupPackage(string packageId) => null;
    }

    private static DamlPackage Package(DamlTemplate template) =>
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
                    Templates = [template],
                    DataTypes = [],
                    Interfaces = [],
                },
            ],
            DependencyReferences = [],
        };

    private static DamlChoice Choice(string name, DamlType returnType) =>
        new()
        {
            Name = name,
            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
            ReturnType = returnType,
            Consuming = false,
        };

    private static DamlTemplate Template(params DamlChoice[] choices) =>
        new()
        {
            Name = "Sink",
            Fields = [],
            Choices = choices,
        };

    private static (string Code, IReadOnlyCollection<string> Usings) EmitNonContract(DamlTemplate template)
    {
        var package = Package(template);
        var context = PackageEmitContext.ForPackage(package, new CodeGenOptions { RootNamespace = "Test.Package" });
        var resolver = new StubResolver();
        var emitter = new ChoiceEmitter(context, resolver, new CodeGenOptions { RootNamespace = "Test.Package" }, new DamlTypeMapper(context, resolver), new PartyAnalysis());
        var sb = new StringBuilder();
        var indent = new IndentWriter(sb) { CurrentTypeName = template.Name };
        emitter.TryWriteNonContractChoiceExtensions(indent, template, context.DataTypes);
        return (sb.ToString(), indent.RequiredUsings);
    }

    [Fact]
    public void non_contract_exerciser_emits_stdlib_unit_wrapper_for_unit_returning_choice()
    {
        var (code, usings) = EmitNonContract(Template(Choice("DoNothing", new DamlPrimitiveType(DamlPrimitive.Unit))));

        code.Should().Contain("public static async Task<ExerciseOutcome<Unit>> DoNothingAsync(");
        code.Should().Contain("new ExerciseOutcome<Unit>.One(Unit.Value)");
        usings.Should().Contain(StdlibNamespace);
    }

    [Fact]
    public void non_contract_exerciser_emits_stdlib_unit_wrapper_for_optional_unit_in_signature_and_decoder()
    {
        var optionalUnit = new DamlTypeApp(
            new DamlPrimitiveType(DamlPrimitive.Optional),
            [new DamlPrimitiveType(DamlPrimitive.Unit)]);

        var (code, usings) = EmitNonContract(Template(Choice("MaybeNothing", optionalUnit)));

        code.Should().Contain("public static async Task<ExerciseOutcome<Unit?>> MaybeNothingAsync(");
        code.Should().Contain("new ExerciseOutcome<Unit?>.One(");
        code.Should().Contain(".AsOptional().HasValue ? Unit.Value : null");
        usings.Should().Contain(StdlibNamespace);
        code.Should().NotContain("DamlUnit?");
    }

    [Fact]
    public void non_contract_exerciser_emits_stdlib_unit_wrapper_for_list_of_unit()
    {
        var listOfUnit = new DamlTypeApp(
            new DamlPrimitiveType(DamlPrimitive.List),
            [new DamlPrimitiveType(DamlPrimitive.Unit)]);

        var (code, usings) = EmitNonContract(Template(Choice("ListOfUnits", listOfUnit)));

        code.Should().Contain("public static async Task<ExerciseOutcome<IReadOnlyList<Unit>>> ListOfUnitsAsync(");
        code.Should().Contain(".As<DamlList>().Values.Select(x => Unit.Value).ToList()");
        usings.Should().Contain(StdlibNamespace);
        code.Should().NotContain("IReadOnlyList<DamlUnit>");
    }

    [Fact]
    public void non_contract_exerciser_emits_stdlib_unit_wrapper_for_textmap_of_unit()
    {
        var mapOfUnit = new DamlTypeApp(
            new DamlPrimitiveType(DamlPrimitive.TextMap),
            [new DamlPrimitiveType(DamlPrimitive.Unit)]);

        var (code, usings) = EmitNonContract(Template(Choice("MapOfUnits", mapOfUnit)));

        code.Should().Contain("public static async Task<ExerciseOutcome<IReadOnlyDictionary<string, Unit>>> MapOfUnitsAsync(");
        code.Should().Contain(".As<DamlTextMap>().Values.ToDictionary(kv => kv.Key, kv => Unit.Value)");
        usings.Should().Contain(StdlibNamespace);
    }
}
