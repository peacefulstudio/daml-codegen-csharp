// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class EmittedNamespaceCollisionGlobalQualificationCompilesTests
{
    [Fact]
    public void Emitted_contract_id_head_is_global_qualified_when_namespace_collides()
    {
        var module = new DamlModule
        {
            Name = "Holdings",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Vault",
                    Fields = [new DamlFieldDefinition("custodian", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = [],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Vault",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("custodian", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "VaultRef",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("vaultCid", ContractIdOf("Vault"))]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "acme-contractid-id",
            Name = "acme-ContractId",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(
            f => f.Content.Contains("namespace Acme.ContractId", StringComparison.Ordinal),
            "the test only guards the shadowing bug if the derived namespace actually ends in .ContractId");

        var vaultRef = files.First(f => f.RelativePath.EndsWith("VaultRef.cs", StringComparison.Ordinal));
        vaultRef.Content.Should().Contain(
            "global::Daml.Runtime.Contracts.ContractId<",
            "the ContractId head must be global::-qualified when the surrounding namespace tail is `ContractId`, otherwise an unqualified `ContractId` is ambiguous with the enclosing namespace");
        vaultRef.Content.Should().NotContain(
            "(ContractId<",
            "no bare ContractId head should survive in the shadowing namespace");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code whose namespace ends in .ContractId must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_read_only_list_head_is_global_qualified_when_namespace_collides()
    {
        var module = new DamlModule
        {
            Name = "Generic",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Bag",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("items", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.List),
                            [new DamlPrimitiveType(DamlPrimitive.Text)])),
                    ]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "acme-collections-generic-id",
            Name = "acme-collections-generic-IReadOnlyList",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(
            f => f.Content.Contains("namespace Acme.Collections.Generic.IReadOnlyList", StringComparison.Ordinal),
            "the test only guards the shadowing bug if the derived namespace actually ends in .IReadOnlyList");

        var bag = files.First(f => f.RelativePath.EndsWith("Bag.cs", StringComparison.Ordinal));
        bag.Content.Should().Contain(
            "global::System.Collections.Generic.IReadOnlyList<",
            "the IReadOnlyList head must be global::-qualified when the surrounding namespace tail is `IReadOnlyList`");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code whose namespace ends in .IReadOnlyList must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_arity_protected_generic_heads_are_global_qualified_when_namespace_collides()
    {
        var module = new DamlModule
        {
            Name = "Choice",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Asset",
                    Fields = [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Touch",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            Controllers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                            Observers = DamlPartyAnalysis.Static([]),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Asset",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "acme-choice-id",
            Name = "acme-Choice",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(
            f => f.Content.Contains("namespace Acme.Choice", StringComparison.Ordinal),
            "the test only guards the shadowing path if the derived namespace actually ends in .Choice");

        var asset = files.First(f => f.RelativePath.EndsWith("Asset.cs", StringComparison.Ordinal));
        asset.Content.Should().Contain(
            "global::Daml.Runtime.Commands.Choice<",
            "the Choice<> head must be global::-qualified when the surrounding namespace tail is `Choice`");
        asset.Content.Should().NotContain(
            " Choice<",
            "no bare Choice<> head should survive in the shadowing namespace");
        asset.Content.Should().Contain(
            "IExercises<Asset>",
            "IExercises<> is routed through the qualifier; with no .IExercises namespace collision it stays bare (collision-aware no-op)");
        asset.Content.Should().Contain(
            "IContract<ContractId, Asset>",
            "IContract<> is routed through the qualifier; with no .IContract namespace collision it stays bare (collision-aware no-op)");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code whose namespace ends in .Choice must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_contract_interface_heads_are_global_qualified_when_namespace_ends_in_icontract()
    {
        var module = new DamlModule
        {
            Name = "IContract",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Asset",
                    Fields = [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                    Choices = [],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Asset",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "acme-icontract-id",
            Name = "acme-IContract",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(
            f => f.Content.Contains("namespace Acme.IContract", StringComparison.Ordinal),
            "the test only guards the shadowing path if the derived namespace actually ends in .IContract");

        var asset = files.First(f => f.RelativePath.EndsWith("Asset.cs", StringComparison.Ordinal));
        asset.Content.Should().Contain(
            "global::Daml.Runtime.Contracts.IContract<",
            "the IContract<> head must be global::-qualified when the surrounding namespace tail is `IContract`");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code whose namespace ends in .IContract must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_exercise_outcome_head_is_global_qualified_when_namespace_collides()
    {
        var module = new DamlModule
        {
            Name = "Outcomes",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Asset",
                    Fields = [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Touch",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Int64),
                            Controllers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                            Observers = DamlPartyAnalysis.Static([]),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Asset",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "acme-exerciseoutcome-id",
            Name = "acme-ExerciseOutcome",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(
            f => f.Content.Contains("namespace Acme.ExerciseOutcome", StringComparison.Ordinal),
            "the test only guards the shadowing path if the derived namespace actually ends in .ExerciseOutcome");

        var emitted = string.Join("\n", files.Select(f => f.Content));
        emitted.Should().Contain(
            "global::Daml.Runtime.Outcomes.ExerciseOutcome<",
            "the ExerciseOutcome<> head must be global::-qualified when the surrounding namespace tail is `ExerciseOutcome` (arity protects the type lookup, but the qualifier must still emit the global:: form for shape consistency)");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code whose namespace ends in .ExerciseOutcome must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }
}
