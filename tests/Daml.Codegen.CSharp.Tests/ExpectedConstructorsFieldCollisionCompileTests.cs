// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.DamlModelBuilder;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Generated variants compile when decoder metadata names overlap legal Daml declarations.
/// </summary>
public class ExpectedConstructorsFieldCollisionCompileTests
{
    private static IReadOnlyList<GeneratedFile> EmitVariant(
        string typeName,
        DamlVariantConstructor[] constructors,
        IReadOnlyList<string>? typeParams = null) =>
        EmitTypes(
        [
            new DamlDataType
            {
                Name = typeName,
                TypeParams = typeParams ?? [],
                Definition = new DamlVariantDefinition(constructors),
            },
        ]);

    private static IReadOnlyList<GeneratedFile> EmitTypes(DamlDataType[] dataTypes)
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes = dataTypes,
            Interfaces = [],
        };

        return CreateGenerator(
            new CodeGenOptions
            {
                EnableNullableReferenceTypes = true,
                UseFileScopedNamespaces = true,
            })
            .Generate(CreateTestDar(module));
    }

    [Fact]
    public void Emitted_variant_with_constructor_named_ExpectedConstructors_compiles()
    {
        var files = EmitVariant(
            "Syntax",
            [
                new DamlVariantConstructor("ExpectedConstructors", new DamlPrimitiveType(DamlPrimitive.Text)),
                new DamlVariantConstructor("Other", null),
            ]);

        var errors = CompileEmittedFiles(files).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a variant constructor named ExpectedConstructors must not duplicate-declare the emitter's own "
            + "private ExpectedConstructors helper field (CS0102), but got: {0}",
            string.Join("; ", errors.Select(d => d.ToString())));
    }

    [Fact]
    public void Emitted_variant_named_ExpectedConstructors_compiles()
    {
        var files = EmitVariant(
            "ExpectedConstructors",
            [
                new DamlVariantConstructor("Found", new DamlPrimitiveType(DamlPrimitive.Text)),
                new DamlVariantConstructor("Other", null),
            ]);

        var errors = CompileEmittedFiles(files).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a variant type named ExpectedConstructors must not have its own private ExpectedConstructors "
            + "helper field share its enclosing type's name (CS0542), but got: {0}",
            string.Join("; ", errors.Select(d => d.ToString())));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Emitted_variant_helper_suffix_avoids_enclosing_type(bool generic)
    {
        var files = EmitVariant(
            "ExpectedConstructors_",
            [
                new DamlVariantConstructor(
                    "ExpectedConstructors",
                    generic ? new DamlTypeVar("a") : new DamlPrimitiveType(DamlPrimitive.Text)),
                new DamlVariantConstructor("Other", null),
            ],
            generic ? ["a"] : []);

        var errors = CompileEmittedFiles(files).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "suffixing a helper past a constructor must also avoid the enclosing type: {0}",
            string.Join("; ", errors));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Emitted_variant_helper_avoids_sibling_payload_type(bool suffixCollisions, bool generic)
    {
        var payloadName = suffixCollisions ? "ExpectedConstructors__" : "ExpectedConstructors";
        var constructors = new List<DamlVariantConstructor>
        {
            new("Found", new DamlTypeRef("test-package-id", "Test.Module", payloadName)),
            new("Other", null),
        };
        if (suffixCollisions)
        {
            constructors.Add(new DamlVariantConstructor(
                "ExpectedConstructors",
                generic ? new DamlTypeVar("a") : new DamlPrimitiveType(DamlPrimitive.Text)));
            constructors.Add(new DamlVariantConstructor("ExpectedConstructors_", null));
        }

        var files = EmitTypes(
        [
            new DamlDataType
            {
                Name = payloadName,
                Definition = new DamlRecordDefinition(
                [
                    new DamlFieldDefinition("text", new DamlPrimitiveType(DamlPrimitive.Text)),
                ]),
            },
            new DamlDataType
            {
                Name = suffixCollisions ? "ExpectedConstructors_" : "Syntax",
                TypeParams = generic ? ["a"] : [],
                Definition = new DamlVariantDefinition(constructors),
            },
        ]);

        var errors = CompileEmittedFiles(files).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "variant readers must bind to the payload type even after skipping enclosing-type and constructor names: {0}",
            string.Join("; ", errors));
    }
}
