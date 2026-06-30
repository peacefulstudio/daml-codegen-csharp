// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class ChoiceEmitterContractIdFilteringTests
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
            Name = "Factory",
            Fields = [],
            Choices = choices,
        };

    private static DamlTypeApp ContractIdOf(string templateName) =>
        new(new DamlPrimitiveType(DamlPrimitive.ContractId), [new DamlTypeRef(LocalPackageId, "Main", templateName)]);

    private static (string NonContract, string Exercisers) Emit(DamlTemplate template)
    {
        var package = Package(template);
        var context = PackageEmitContext.ForPackage(package, new CodeGenOptions { RootNamespace = "Test.Package" });
        var resolver = new StubResolver();
        var emitter = new ChoiceEmitter(context, resolver, new CodeGenOptions { RootNamespace = "Test.Package" }, new DamlTypeMapper(context, resolver), new PartyAnalysis());

        var nonContractSb = new StringBuilder();
        var nonContractIndent = new IndentWriter(nonContractSb) { CurrentTypeName = template.Name };
        emitter.TryWriteNonContractChoiceExtensions(nonContractIndent, template, context.DataTypes);

        var exercisersSb = new StringBuilder();
        var exercisersIndent = new IndentWriter(exercisersSb) { CurrentTypeName = template.Name };
        emitter.WriteChoiceAsyncExercisersClass(exercisersIndent, template, template.Name, template.Fields, context.DataTypes);

        return (nonContractSb.ToString(), exercisersSb.ToString());
    }

    [Fact]
    public void contract_id_return_routes_bare_contract_id_choice_to_slot_path_not_non_contract_path()
    {
        var (nonContract, exercisers) = Emit(Template(Choice("Mint", ContractIdOf("Coin"))));

        nonContract.Should().NotContain("FactoryNonContractExtensions");
        nonContract.Should().NotContain("ProjectMintResult");
        exercisers.Should().Contain("public static class FactoryExtensions");
        exercisers.Should().Contain("MintResult.FromCreatedContracts");
    }

    [Fact]
    public void contract_id_return_routes_optional_contract_id_choice_to_slot_path_not_non_contract_path()
    {
        var optionalCid = new DamlTypeApp(new DamlPrimitiveType(DamlPrimitive.Optional), [ContractIdOf("Coin")]);

        var (nonContract, exercisers) = Emit(Template(Choice("MaybeMint", optionalCid)));

        nonContract.Should().NotContain("FactoryNonContractExtensions");
        nonContract.Should().NotContain("ProjectMaybeMintResult(");
        exercisers.Should().Contain("MaybeMintResult.FromCreatedContracts");
    }
}
