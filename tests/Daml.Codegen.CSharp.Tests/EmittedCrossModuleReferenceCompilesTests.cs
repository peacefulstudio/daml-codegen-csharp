// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class EmittedCrossModuleReferenceCompilesTests
{
    private const string PackageId = "two-module-package-id";

    private static DamlModule TypesModule() =>
        new()
        {
            Name = "Acme.Types",
            Templates = [],
            DataTypes =
            [
                new DamlDataType { Name = "Currency", Definition = new DamlEnumDefinition(["USD", "EUR"]) },
                new DamlDataType
                {
                    Name = "Money",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("amount", new DamlPrimitiveType(DamlPrimitive.Numeric)),
                        new DamlFieldDefinition("currency", new DamlTypeRef(PackageId, "Acme.Types", "Currency")),
                    ]),
                },
            ],
            Interfaces = [],
        };

    private static DamlModule TradingModule() =>
        new()
        {
            Name = "Acme.Trading",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Quote",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Reprice",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef(PackageId, "Acme.Trading", "Reprice"),
                            ReturnType = ContractIdOf("Acme.Trading", "Quote"),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Quote",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("issuer", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("currency", new DamlTypeRef(PackageId, "Acme.Types", "Currency")),
                        new DamlFieldDefinition("price", new DamlTypeRef(PackageId, "Acme.Types", "Money")),
                        new DamlFieldDefinition("optionalCurrency", OptionalOf(new DamlTypeRef(PackageId, "Acme.Types", "Currency"))),
                    ]),
                },
                new DamlDataType
                {
                    Name = "Reprice",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("newPrice", new DamlTypeRef(PackageId, "Acme.Types", "Money")),
                        new DamlFieldDefinition("newCurrency", new DamlTypeRef(PackageId, "Acme.Types", "Currency")),
                    ]),
                },
            ],
            Interfaces = [],
        };

    private static IReadOnlyList<GeneratedFile> Generate(CodeGenOptions options) =>
        CreateGenerator(options).Generate(new DarModel
        {
            MainPackage = new DamlPackage
            {
                PackageId = PackageId,
                Name = "two-module-package",
                Version = new Version(1, 0, 0),
                LfVersion = "2.1",
                Modules = [TypesModule(), TradingModule()],
                DependencyReferences = [],
            },
            Dependencies = [],
        });

    [Theory]
    [InlineData(null)]
    [InlineData("Consumer.Bindings")]
    public void Emitted_types_referencing_an_enum_and_a_record_declared_in_another_module_compile(string? namespacePrefix)
    {
        var files = Generate(new CodeGenOptions { NamespacePrefix = namespacePrefix });

        var prefix = namespacePrefix is null ? string.Empty : namespacePrefix + ".";
        var quote = files.Single(f => f.RelativePath.EndsWith("/Quote.cs", StringComparison.Ordinal)).Content;
        quote.Should().Contain($"namespace {prefix}Acme.Trading;");
        quote.Should().Contain($"global::{prefix}Acme.Types.Currency Currency");
        quote.Should().Contain($"global::{prefix}Acme.Types.Money Price");
        var reprice = files.Single(f => f.RelativePath.EndsWith("Quote.Reprice.cs", StringComparison.Ordinal)).Content;
        reprice.Should().Contain($"global::{prefix}Acme.Types.Money NewPrice");

        var errors = CompileEmittedFiles(files).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a module referencing another module's enum and record must compile against that module's emission, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }
}
