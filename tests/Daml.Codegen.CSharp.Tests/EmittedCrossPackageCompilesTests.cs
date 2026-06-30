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

public class EmittedCrossPackageCompilesTests
{
    [Fact]
    public void Emitted_choice_with_cross_package_argument_compiles_against_both_packages()
    {
        var foreignModule = new DamlModule
        {
            Name = "Other.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "OrderRequest",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("payload", new DamlPrimitiveType(DamlPrimitive.Text))]),
                }
            ],
            Interfaces = [],
        };
        var foreignPackage = new DamlPackage
        {
            PackageId = "other-pkg-id",
            Name = "other-pkg",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [foreignModule],
            DependencyReferences = [],
        };

        var mainModule = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Trader",
                    Fields = [new DamlFieldDefinition("operator", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Submit",
                            Consuming = false,
                            ArgumentType = new DamlTypeRef("other-pkg-id", "Other.Module", "OrderRequest"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Numeric),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Trader",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("operator", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces = [],
        };
        var mainPackage = new DamlPackage
        {
            PackageId = "test-pkg",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [mainModule],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = mainPackage, Dependencies = [foreignPackage] };

        var options = new CodeGenOptions
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            UseRecordTypes = true,
            UsePrimaryConstructors = true,
            IncludeDependencies = true,
        };
        var generator = new CSharpCodeGenerator(options, new ConsoleLogger(0));
        var files = generator.Generate(dar);

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "cross-package choice arg wrappers must compile alongside the foreign package's emission, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_record_with_cross_package_enum_field_compiles_against_both_packages()
    {
        var foreignModule = new DamlModule
        {
            Name = "Other.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Severity",
                    Definition = new DamlEnumDefinition(["Low", "High"]),
                }
            ],
            Interfaces = [],
        };
        var foreignPackage = new DamlPackage
        {
            PackageId = "other-pkg-id",
            Name = "other-pkg",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [foreignModule],
            DependencyReferences = [],
        };

        var mainModule = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Alert",
                    Fields =
                    [
                        new DamlFieldDefinition("operator", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("severity", new DamlTypeRef("other-pkg-id", "Other.Module", "Severity")),
                    ],
                    Choices = [],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Alert",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("operator", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("severity", new DamlTypeRef("other-pkg-id", "Other.Module", "Severity")),
                    ]),
                },
            ],
            Interfaces = [],
        };
        var mainPackage = new DamlPackage
        {
            PackageId = "test-pkg",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [mainModule],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = mainPackage, Dependencies = [foreignPackage] };

        var options = new CodeGenOptions
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            UseRecordTypes = true,
            UsePrimaryConstructors = true,
            IncludeDependencies = true,
        };
        var generator = new CSharpCodeGenerator(options, new ConsoleLogger(0));
        var files = generator.Generate(dar);

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a record field whose type is an enum from a dependency package must round-trip via the foreign enum's *Extensions.ToDamlEnum/FromDamlEnum, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_cross_package_variant_constructor_referencing_nested_choice_arg_type_compiles()
    {
        var foreignModule = new DamlModule
        {
            Name = "Splice.Amulet",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "AmuletRules",
                    Fields = [new DamlFieldDefinition("operator", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "AmuletRules_MiningRound_Archive",
                            Consuming = false,
                            ArgumentType = new DamlTypeRef("foreign-pkg-id", "Splice.Amulet", "AmuletRules_MiningRound_Archive"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "AmuletRules",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("operator", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "AmuletRules_MiningRound_Archive",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("roundId", new DamlPrimitiveType(DamlPrimitive.Int64))]),
                },
            ],
            Interfaces = [],
        };

        var foreignPackage = new DamlPackage
        {
            PackageId = "foreign-pkg-id",
            Name = "splice-amulet",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [foreignModule],
            DependencyReferences = [],
        };

        var mainModule = new DamlModule
        {
            Name = "Test.Governance",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "AmuletRules_ActionRequiringConfirmation",
                    Definition = new DamlVariantDefinition(
                    [
                        new DamlVariantConstructor("CRARC_MiningRound_Archive",
                            new DamlTypeRef("foreign-pkg-id", "Splice.Amulet", "AmuletRules_MiningRound_Archive")),
                    ]),
                },
            ],
            Interfaces = [],
        };

        var mainPackage = new DamlPackage
        {
            PackageId = "main-pkg-id",
            Name = "test-governance",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [mainModule],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = mainPackage, Dependencies = [foreignPackage] };

        var options = new CodeGenOptions
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            UseRecordTypes = true,
            UsePrimaryConstructors = true,
            IncludeDependencies = true,
        };
        var generator = new CSharpCodeGenerator(options, new ConsoleLogger(0));
        var files = generator.Generate(dar);

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a cross-package variant constructor referencing a nested choice-arg type must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }
}
