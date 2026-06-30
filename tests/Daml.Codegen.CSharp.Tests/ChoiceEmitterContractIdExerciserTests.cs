// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class ChoiceEmitterContractIdExerciserTests
{
    private const string LocalPackageId = "pkg-id";

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

    private static DamlPartyAnalysis StaticParties(params string[] fieldNames) =>
        DamlPartyAnalysis.Static(fieldNames.Select(n => (DamlPartyReference)new DamlPartyPayloadField(n)).ToList());

    private static DamlChoice Choice(string name, DamlType returnType, DamlPartyAnalysis? controllers = null) =>
        new()
        {
            Name = name,
            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
            ReturnType = returnType,
            Consuming = false,
            Controllers = controllers ?? DamlPartyAnalysis.Dynamic,
            Observers = DamlPartyAnalysis.Dynamic,
        };

    private static DamlTemplate Template(IReadOnlyList<DamlFieldDefinition> fields, params DamlChoice[] choices) =>
        new()
        {
            Name = "Vault",
            Fields = fields,
            Choices = choices,
            Signatories = DamlPartyAnalysis.Dynamic,
            Observers = DamlPartyAnalysis.Dynamic,
        };

    private static DamlTypeApp ContractIdOf(string templateName) =>
        new(new DamlPrimitiveType(DamlPrimitive.ContractId), [new DamlTypeRef(LocalPackageId, "Main", templateName)]);

    private static (string ResultStructs, string Exercisers) Emit(DamlTemplate template)
    {
        var package = Package(template);
        var context = PackageEmitContext.ForPackage(package, new CodeGenOptions { RootNamespace = "Test.Package" });
        var resolver = new StubResolver();
        var emitter = new ChoiceEmitter(context, resolver, new CodeGenOptions { RootNamespace = "Test.Package" }, new DamlTypeMapper(context, resolver), new PartyAnalysis());

        var structsSb = new StringBuilder();
        var structsIndent = new IndentWriter(structsSb) { CurrentTypeName = template.Name };
        emitter.WriteChoiceResultStructs(structsIndent, template, "Test.Package");

        var exerciserSb = new StringBuilder();
        var exerciserIndent = new IndentWriter(exerciserSb) { CurrentTypeName = template.Name };
        emitter.WriteChoiceAsyncExercisersClass(exerciserIndent, template, template.Name, template.Fields, context.DataTypes);

        return (structsSb.ToString(), exerciserSb.ToString());
    }

    [Fact]
    public void single_contract_id_choice_emits_a_single_cardinality_slot_property()
    {
        var template = Template([], Choice("Spawn", ContractIdOf("Token")));

        var (structs, _) = Emit(template);

        structs.Should().Contain("public sealed record SpawnResult(ContractId<Token> Token)");
        structs.Should().Contain("public static ExerciseOutcome<SpawnResult> FromCreatedContracts");
    }

    [Fact]
    public void optional_contract_id_choice_emits_a_nullable_slot_property()
    {
        var template = Template([], Choice("Spawn", new DamlTypeApp(new DamlPrimitiveType(DamlPrimitive.Optional), [ContractIdOf("Token")])));

        var (structs, _) = Emit(template);

        structs.Should().Contain("public sealed record SpawnResult(ContractId<Token>? Token)");
    }

    [Fact]
    public void list_contract_id_choice_emits_a_list_slot_property()
    {
        var template = Template([], Choice("Spawn", new DamlTypeApp(new DamlPrimitiveType(DamlPrimitive.List), [ContractIdOf("Token")])));

        var (structs, _) = Emit(template);

        structs.Should().Contain("public sealed record SpawnResult(IReadOnlyList<ContractId<Token>> Token)");
    }

    [Fact]
    public void create_bearing_choice_emits_a_typed_async_exerciser()
    {
        var template = Template(
            [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
            Choice("Spawn", ContractIdOf("Token"), controllers: StaticParties("owner")));

        var (_, exercisers) = Emit(template);

        exercisers.Should().Contain("public static class VaultExtensions");
        exercisers.Should().Contain("public static async Task<ExerciseOutcome<SpawnResult>> SpawnAsync(");
        exercisers.Should().Contain("this ContractId<Vault> contractId,");
    }

    [Fact]
    public void non_creating_choice_emits_no_exerciser_class()
    {
        var template = Template([], Choice("Touch", new DamlPrimitiveType(DamlPrimitive.Unit)));

        var (structs, exercisers) = Emit(template);

        structs.Should().BeEmpty();
        exercisers.Should().BeEmpty();
    }
}
