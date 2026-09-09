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

/// <summary>
/// Compiles the emitted output of every <see cref="CodeGenOptions"/> switch whose
/// non-default value the CLI never selects, across the three declaration paths
/// that switch shapes: a plain record, a template, and a template choice whose
/// argument is a record in the same package. A switch whose <c>false</c> path is
/// only string-matched is a shape nobody has ever compiled.
/// </summary>
public class EmittedOptionFalsePathCompilesTests
{
    [Fact]
    public void Emitted_declarations_compile_with_block_scoped_namespaces()
    {
        AssertDeclarationsCompile(new CodeGenOptions { UseFileScopedNamespaces = false });
    }

    [Fact]
    public void Emitted_declarations_compile_without_xml_docs()
    {
        AssertDeclarationsCompile(new CodeGenOptions { GenerateXmlDocs = false });
    }

    private static void AssertDeclarationsCompile(CodeGenOptions options)
    {
        var files = CreateGenerator(options).Generate(DeclarationShapes());

        files.Should().Contain(f => f.Content.Contains("ITemplate", StringComparison.Ordinal));
        files.Should().Contain(f => f.Content.Contains("public sealed record AssetHolding_Transfer", StringComparison.Ordinal));
        files.Should().Contain(f => f.Content.Contains("IDamlRecord<TransferSummary>", StringComparison.Ordinal));
        var errors = CompileEmittedFiles(files).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "every declaration path must compile under this option, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    private static DarModel DeclarationShapes()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "AssetHolding",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "AssetHolding_Transfer",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef("", "Test.Module", "AssetHolding_Transfer"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "AssetHolding",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "AssetHolding_Transfer",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("newOwner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "TransferSummary",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("quantity", new DamlPrimitiveType(DamlPrimitive.Numeric))]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "option-false-path-id",
            Name = "option-false-path",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        return new DarModel { MainPackage = package, Dependencies = [] };
    }
}
