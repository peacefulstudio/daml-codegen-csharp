// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Pins that Daml names colliding with C# keywords survive every emission grammar.
/// A keyword-named identifier reaches three sites with different escaping rules: a
/// parameter declaration (needs <c>@operator</c>), an XML doc <c>name=</c> attribute
/// (needs bare <c>operator</c>), and a type-parameter name (needs no escape once
/// prefixed). Sibling compile gates parse with <see cref="DocumentationMode.Parse"/>,
/// which suppresses doc diagnostics outright — these compile with
/// <see cref="DocumentationMode.Diagnose"/> so CS1572/CS1573 surface.
/// </summary>
public class EmittedKeywordNamedIdentifierCompilesTests
{
    private static DarModel KeywordControllerDar(string partyFieldName)
    {
        var fields = new[]
        {
            new DamlFieldDefinition(partyFieldName, new DamlPrimitiveType(DamlPrimitive.Party)),
            new DamlFieldDefinition("platform", new DamlPrimitiveType(DamlPrimitive.Party)),
        };

        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Market",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Resume",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = ContractIdOf("Market"),
                            Controllers = DamlPartyAnalysis.Static([new DamlPartyPayloadField(partyFieldName)]),
                        },
                    ],
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField(partyFieldName)]),
                    Observers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Market",
                    Definition = new DamlRecordDefinition(fields),
                },
            ],
            Interfaces = [],
        };

        return new DarModel
        {
            MainPackage = new DamlPackage
            {
                PackageId = "test-package-id",
                Name = "test-package",
                Version = new Version(1, 0, 0),
                LfVersion = "2.1",
                Modules = [module],
                DependencyReferences = [],
            },
            Dependencies = [],
        };
    }

    [Fact]
    public void Emitted_choice_with_keyword_named_controller_field_has_matching_param_docs()
    {
        var files = CreateGenerator().Generate(KeywordControllerDar("operator"));

        var docDiagnostics = CompileEmittedFilesWithDocDiagnostics(files)
            .Where(d => d.Id is "CS1572" or "CS1573")
            .ToList();

        docDiagnostics.Should().BeEmpty(
            "a keyword-named controller field must escape as @operator in the parameter "
            + "declaration but appear bare in the <param name=\"...\"> doc tag; "
            + $"got: {string.Join("; ", docDiagnostics.Select(d => d.ToString()))}");
    }

    [Fact]
    public void Emitted_choice_with_keyword_cased_controller_field_compiles()
    {
        var files = CreateGenerator().Generate(KeywordControllerDar("Operator"));

        var errors = CompileEmittedFilesWithDocDiagnostics(files)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        errors.Should().BeEmpty(
            "a Daml field named Operator camelCases to the C# keyword operator, so the "
            + $"escape must run after the casing; got: {string.Join("; ", errors.Select(d => d.ToString()))}");
    }

    [Fact]
    public void Emitted_record_with_keyword_named_type_parameter_compiles()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Box",
                    TypeParams = ["event"],
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("value", new DamlTypeVar("event"))]),
                },
            ],
            Interfaces = [],
        };

        var dar = new DarModel
        {
            MainPackage = new DamlPackage
            {
                PackageId = "test-package-id",
                Name = "test-package",
                Version = new Version(1, 0, 0),
                LfVersion = "2.1",
                Modules = [module],
                DependencyReferences = [],
            },
            Dependencies = [],
        };

        var files = CreateGenerator().Generate(dar);

        var errors = CompileEmittedFilesWithDocDiagnostics(files)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        errors.Should().BeEmpty(
            "a Daml type variable named event is prefixed with T, so the keyword escape "
            + $"must not survive into TEvent; got: {string.Join("; ", errors.Select(d => d.ToString()))}");
    }

    [Fact]
    public void Emitted_choice_with_keyword_named_observer_field_has_matching_param_docs()
    {
        var fields = new[]
        {
            new DamlFieldDefinition("platform", new DamlPrimitiveType(DamlPrimitive.Party)),
            new DamlFieldDefinition("operator", new DamlPrimitiveType(DamlPrimitive.Party)),
        };

        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Market",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Resume",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = ContractIdOf("Market"),
                            Controllers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
                        },
                    ],
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
                    Observers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("operator")]),
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Market",
                    Definition = new DamlRecordDefinition(fields),
                },
            ],
            Interfaces = [],
        };

        var dar = new DarModel
        {
            MainPackage = new DamlPackage
            {
                PackageId = "test-package-id",
                Name = "test-package",
                Version = new Version(1, 0, 0),
                LfVersion = "2.1",
                Modules = [module],
                DependencyReferences = [],
            },
            Dependencies = [],
        };

        var files = CreateGenerator().Generate(dar);

        var docDiagnostics = CompileEmittedFilesWithDocDiagnostics(files)
            .Where(d => d.Id is "CS1572" or "CS1573")
            .ToList();

        docDiagnostics.Should().BeEmpty(
            "a keyword-named observer field must escape as @operator in the readAs parameter "
            + "declaration but appear bare in the <param name=\"...\"> doc tag; "
            + $"got: {string.Join("; ", docDiagnostics.Select(d => d.ToString()))}");
    }
}
