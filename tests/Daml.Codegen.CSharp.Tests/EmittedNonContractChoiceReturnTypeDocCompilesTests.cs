// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.CSharp.Tests.TestHelpers;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class EmittedNonContractChoiceReturnTypeDocCompilesTests
{
    private static readonly DamlPackage DamlPrim = new()
    {
        PackageId = "daml-prim",
        Name = "daml-prim",
        Version = new Version(0, 0, 0),
        LfVersion = "2.1",
        Modules = [],
        DependencyReferences = [],
    };

    [Fact]
    public void Emitted_non_contract_choices_returning_constructed_generics_have_no_doc_diagnostics()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "ResultVault",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "LabelCounts",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.TextMap),
                                [new DamlPrimitiveType(DamlPrimitive.Int64)]),
                        },
                        new DamlChoice
                        {
                            Name = "OwnerAndCount",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = TupleType(
                                new DamlPrimitiveType(DamlPrimitive.Party),
                                new DamlPrimitiveType(DamlPrimitive.Int64)),
                        },
                        new DamlChoice
                        {
                            Name = "RankByOwner",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.List),
                                [new DamlTypeApp(
                                    new DamlPrimitiveType(DamlPrimitive.TextMap),
                                    [new DamlPrimitiveType(DamlPrimitive.Int64)])]),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "ResultVault",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("operator", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces = [],
        };

        var dar = new DamlModelBuilder().WithModule(module).WithDependency(DamlPrim).Build();
        var files = CreateGenerator().Generate(dar);

        var docDiagnostics = CompileEmittedFilesWithDocDiagnostics(files)
            .Where(d => d.Id is "CS1570" or "CS1572" or "CS1573" or "CS1574" or "CS1580" or "CS1584" or "CS1658")
            .ToList();

        docDiagnostics.Should().BeEmpty(
            "a non-contract choice returning a constructed generic type (dictionary, tuple, or a generic nested inside another) "
            + "embeds its C# type name inside a <c> doc tag; unescaped angle brackets make the XML doc comment malformed under "
            + "GenerateDocumentationFile, but got: {0}",
            string.Join("\n", docDiagnostics.Select(e => e.Id + " " + e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));

        var errors = CompileEmittedFiles(files).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "the emitted non-contract choice wrappers for dictionary/tuple/nested-generic returns must compile end-to-end, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }
}
