// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Microsoft.CodeAnalysis;
using AwesomeAssertions;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Compiles emitted code carrying the <c>IDamlRecord&lt;TSelf&gt;</c> facet in the
/// declaration shape the conformance corpus does not cover: the zero-field
/// <c>FromRecord</c> short-circuit, which emits a memberless record rather than a
/// positional one. The facet turns <c>FromRecord</c>'s shape from convention into
/// a compile-time contract, so the branch is compile-verified, not string-matched.
/// </summary>
public class EmittedFacetDeclarationCompilesTests
{
    [Fact]
    public void Emitted_facet_compiles_on_zero_field_records()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "PlainAsset",
                    Choices = [],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "PlainAsset",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "EmptyMarker",
                    Definition = new DamlRecordDefinition([]),
                },
            ],
            Interfaces = [],
        };
        var package = new DamlPackage
        {
            PackageId = "facet-shapes-id",
            Name = "facet-shapes",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };
        var dar = new DarModel { MainPackage = package, Dependencies = [] };

        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(f => f.Content.Contains("IDamlRecord<PlainAsset>", StringComparison.Ordinal));
        files.Should().Contain(f => f.Content.Contains("IDamlRecord<EmptyMarker>", StringComparison.Ordinal));
        var errors = CompileEmittedFiles(files).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "the facet must hold in the zero-field declaration shape, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }
}
