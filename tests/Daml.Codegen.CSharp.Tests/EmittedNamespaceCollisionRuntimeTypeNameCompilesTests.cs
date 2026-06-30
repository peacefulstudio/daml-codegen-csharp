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

public class EmittedNamespaceCollisionRuntimeTypeNameCompilesTests
{
    [Fact]
    public void Emitted_code_compiles_when_package_namespace_ends_in_party()
    {
        // Regression for B3: the runtime type Daml.Runtime.Data.Party was emitted
        // as the bare identifier `Party`. When the package name derives a C# namespace
        // whose tail segment is `Party` (real DAR: canton-party-replication-alpha),
        // that namespace shadows the type and Roslyn reports CS0118. Every emitted
        // Party TYPE site must be global::-qualified. This exercises the field type,
        // the contract-key type + IHasKey<> argument, the choice actAs/controller
        // params, the signatory-derived CreateAsync, and the Observers(payload) helper.
        var module = new DamlModule
        {
            Name = "Replication",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Holding",
                    Fields =
                    [
                        new DamlFieldDefinition("issuer", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("amount", new DamlPrimitiveType(DamlPrimitive.Int64)),
                    ],
                    Key = new DamlPrimitiveType(DamlPrimitive.Party),
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("issuer")]),
                    Observers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Transfer",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = ContractIdOf("Holding"),
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
                    Name = "Holding",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("issuer", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("amount", new DamlPrimitiveType(DamlPrimitive.Int64)),
                    ]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "canton-party-id",
            Name = "canton-party",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(
            f => f.Content.Contains("namespace Canton.Party", StringComparison.Ordinal),
            "the test only guards the shadowing bug if the derived namespace actually ends in .Party");

        // The key-bearing template emits a throwing `public ... Key =>` stub (ADR 0013)
        // whose key type must be global::-qualified so it doesn't resolve against the
        // shadowing `Canton.Party` namespace. The generated files compile standalone.
        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code whose namespace ends in .Party must global::-qualify the runtime Party type, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_code_compiles_when_package_namespace_ends_in_itemplate()
    {
        var module = new DamlModule
        {
            Name = "Templates",
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
            PackageId = "acme-itemplate-id",
            Name = "acme-ITemplate",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(
            f => f.Content.Contains("namespace Acme.ITemplate", StringComparison.Ordinal),
            "the test only guards the shadowing bug if the derived namespace actually ends in .ITemplate");

        var asset = files.First(f => f.RelativePath.EndsWith("Asset.cs", StringComparison.Ordinal));
        asset.Content.Should().Contain(
            "global::Daml.Runtime.Contracts.ITemplate",
            "the ITemplate interface head must be global::-qualified when the surrounding namespace tail is `ITemplate`, otherwise it is ambiguous with the enclosing namespace (CS0118)");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code whose namespace ends in .ITemplate must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_code_compiles_when_package_namespace_ends_in_idamlrecord()
    {
        var module = new DamlModule
        {
            Name = "Values",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Payload",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("amount", new DamlPrimitiveType(DamlPrimitive.Int64))]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "acme-idamlrecord-id",
            Name = "acme-IDamlRecord",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(
            f => f.Content.Contains("namespace Acme.IDamlRecord", StringComparison.Ordinal),
            "the test only guards the shadowing bug if the derived namespace actually ends in .IDamlRecord");

        var payload = files.First(f => f.RelativePath.EndsWith("Payload.cs", StringComparison.Ordinal));
        payload.Content.Should().Contain(
            "global::Daml.Runtime.Data.IDamlRecord",
            "the IDamlRecord interface head must be global::-qualified when the surrounding namespace tail is `IDamlRecord`, otherwise it is ambiguous with the enclosing namespace (CS0118)");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code whose namespace ends in .IDamlRecord must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_code_compiles_when_package_namespace_ends_in_idamlvariant()
    {
        var module = new DamlModule
        {
            Name = "Values",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Choice",
                    Definition = new DamlVariantDefinition(
                        [
                            new DamlVariantConstructor("Yes", new DamlPrimitiveType(DamlPrimitive.Int64)),
                            new DamlVariantConstructor("No", null),
                        ]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "acme-idamlvariant-id",
            Name = "acme-IDamlVariant",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(
            f => f.Content.Contains("namespace Acme.IDamlVariant", StringComparison.Ordinal),
            "the test only guards the shadowing bug if the derived namespace actually ends in .IDamlVariant");

        var choice = files.First(f => f.RelativePath.EndsWith("Choice.cs", StringComparison.Ordinal));
        choice.Content.Should().Contain(
            "global::Daml.Runtime.Data.IDamlVariant",
            "the variant abstract base record head must be global::-qualified when the surrounding namespace tail is `IDamlVariant`, otherwise it is ambiguous with the enclosing namespace (CS0118)");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code whose namespace ends in .IDamlValue must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_code_compiles_when_package_namespace_ends_in_submitterinfo()
    {
        var module = new DamlModule
        {
            Name = "Submissions",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Asset",
                    Fields =
                    [
                        new DamlFieldDefinition("issuer", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                    ],
                    Signatories = DamlPartyAnalysis.Static(
                    [
                        new DamlPartyPayloadField("issuer"),
                        new DamlPartyPayloadField("owner"),
                    ]),
                    Observers = DamlPartyAnalysis.Static([]),
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Transfer",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = ContractIdOf("Asset"),
                            Controllers = DamlPartyAnalysis.Dynamic,
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
                    [
                        new DamlFieldDefinition("issuer", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                    ]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "acme-submitterinfo-id",
            Name = "acme-SubmitterInfo",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(
            f => f.Content.Contains("namespace Acme.SubmitterInfo", StringComparison.Ordinal),
            "the test only guards the shadowing bug if the derived namespace actually ends in .SubmitterInfo");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code whose namespace ends in .SubmitterInfo must global::-qualify the runtime SubmitterInfo type in parameter and constructor positions, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_code_compiles_when_package_namespace_ends_in_identifier()
    {
        var module = new DamlModule
        {
            Name = "Ids",
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
            PackageId = "acme-identifier-id",
            Name = "acme-Identifier",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(
            f => f.Content.Contains("namespace Acme.Identifier", StringComparison.Ordinal),
            "the test only guards the shadowing bug if the derived namespace actually ends in .Identifier");

        var asset = files.First(f => f.RelativePath.EndsWith("Asset.cs", StringComparison.Ordinal));
        asset.Content.Should().Contain(
            "global::Daml.Runtime.Data.Identifier TemplateId",
            "the Identifier TemplateId type must be global::-qualified when the surrounding namespace tail is `Identifier`");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code whose namespace ends in .Identifier must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_code_compiles_when_package_namespace_ends_in_damlparty()
    {
        var module = new DamlModule
        {
            Name = "Values",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Payload",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("amount", new DamlPrimitiveType(DamlPrimitive.Int64)),
                    ]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "acme-damlparty-id",
            Name = "acme-DamlParty",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(
            f => f.Content.Contains("namespace Acme.DamlParty", StringComparison.Ordinal),
            "the test only guards the shadowing bug if the derived namespace actually ends in .DamlParty");

        var payload = files.First(f => f.RelativePath.EndsWith("Payload.cs", StringComparison.Ordinal));
        payload.Content.Should().Contain(
            ".As<global::Daml.Runtime.Data.DamlParty>()",
            "the DamlParty cast must be global::-qualified when the surrounding namespace tail is `DamlParty`, otherwise the leading simple name resolves to the enclosing namespace (CS0118)");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code whose namespace ends in .DamlParty must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }
}
