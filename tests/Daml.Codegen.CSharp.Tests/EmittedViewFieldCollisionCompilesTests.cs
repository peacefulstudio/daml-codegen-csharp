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
/// Pins the field names that make a view record ineligible for the marker enrichment.
/// The record emitter and the interface emitter derive a field's C# member name
/// independently, each disambiguating only against its own enclosing type, and the marker
/// declares two members of its own — so four legal Daml view fields would otherwise emit a
/// marker the stamped record cannot satisfy: a field PascalCasing to the record's name is
/// renamed on the record but not on the marker (CS0535), a field PascalCasing to the
/// marker's name is renamed on the marker but not on the record (CS0535), and a field
/// PascalCasing to <c>View</c> or <c>InterfaceId</c> redeclares a member the marker already
/// owns (CS0102). Each must degrade to an un-stamped record beside an un-enriched marker
/// that still carries its <c>View</c> witness, so the interface stays subscribable.
/// </summary>
public class EmittedViewFieldCollisionCompilesTests
{
    private const string InterfaceName = "Asset";
    private const string MarkerFileName = "IAsset.cs";
    private const string ViewRecordName = "AssetView";
    private const string ViewRecordFileName = "AssetView.cs";

    private static IReadOnlyList<GeneratedFile> EmitViewedInterface(params string[] viewFieldNames)
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = ViewRecordName,
                    Definition = new DamlRecordDefinition(
                        [.. viewFieldNames.Select(name =>
                            new DamlFieldDefinition(name, new DamlPrimitiveType(DamlPrimitive.Party)))]),
                },
            ],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = InterfaceName,
                    Choices = [],
                    ViewType = new DamlTypeRef("", "Test.Module", ViewRecordName),
                },
            ],
        };

        return CreateGenerator(
            new CodeGenOptions
            {
                EnableNullableReferenceTypes = true,
                UseFileScopedNamespaces = true,
                UseRecordTypes = true,
                UsePrimaryConstructors = true,
            })
            .Generate(CreateTestDar(module));
    }

    private static string SourceOf(IReadOnlyList<GeneratedFile> files, string fileName) =>
        files.Single(f => f.RelativePath.EndsWith(fileName, StringComparison.Ordinal)).Content;

    private static void ShouldCompileCleanly(IReadOnlyList<GeneratedFile> files, string because)
    {
        var errors = CompileEmittedFiles(files).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

        errors.Should().BeEmpty(
            because + "; got: {0}",
            string.Join("; ", errors.Select(d => d.ToString())));
    }

    [Fact]
    public void Emitted_view_record_with_no_colliding_field_is_stamped_and_mirrored()
    {
        var files = EmitViewedInterface("owner");

        SourceOf(files, ViewRecordFileName).Should().Contain(
            $"Party Owner\n) : I{InterfaceName}, IDamlRecord<{ViewRecordName}>",
            "the collision cases below only prove something if the same fixture enriches a clean view record");
        SourceOf(files, MarkerFileName).Should().Contain("Party Owner { get; }");
        ShouldCompileCleanly(files, "the enriched shape is the baseline every degraded case falls back from");
    }

    [Theory]
    [InlineData("view", "the mirror would redeclare the marker's own View witness (CS0102)")]
    [InlineData("interfaceId", "the mirror would redeclare the marker's own InterfaceId (CS0102)")]
    [InlineData("assetView", "the record renames the property to AssetView_ but the marker declares AssetView (CS0535)")]
    [InlineData("iAsset", "the marker renames the property to IAsset_ but the record declares IAsset (CS0535)")]
    public void Emitted_view_record_with_a_colliding_field_is_left_un_stamped_and_compiles(
        string viewFieldName,
        string collision)
    {
        var files = EmitViewedInterface(viewFieldName);

        ShouldCompileCleanly(files, $"the emitter must degrade rather than emit uncompilable C#: {collision}");
        SourceOf(files, ViewRecordFileName).Should().NotContain(
            $": I{InterfaceName},",
            $"the view record must not be stamped with a marker it cannot satisfy: {collision}");
        SourceOf(files, MarkerFileName).Should().NotContain(
            "Party ",
            $"the marker must not mirror a field the stamped record would not implement: {collision}");
        SourceOf(files, MarkerFileName).Should().Contain(
            $"ViewDescriptor<I{InterfaceName}, {ViewRecordName}> View {{ get; }} = new();",
            "degrading the enrichment must still leave a subscribable marker paired with its view record");
    }

    [Fact]
    public void Emitted_marker_mirrors_a_collection_typed_and_a_keyword_named_view_field()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = ViewRecordName,
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition(
                            "holders",
                            new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.List),
                                [new DamlPrimitiveType(DamlPrimitive.Party)])),
                        new DamlFieldDefinition(
                            "quantities",
                            new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.TextMap),
                                [new DamlPrimitiveType(DamlPrimitive.Numeric)])),
                        new DamlFieldDefinition("interface", new DamlPrimitiveType(DamlPrimitive.Text)),
                    ]),
                },
            ],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = InterfaceName,
                    Choices = [],
                    ViewType = new DamlTypeRef("", "Test.Module", ViewRecordName),
                },
            ],
        };

        var files = CreateGenerator(
            new CodeGenOptions
            {
                EnableNullableReferenceTypes = true,
                UseFileScopedNamespaces = true,
                UseRecordTypes = true,
                UsePrimaryConstructors = true,
            })
            .Generate(CreateTestDar(module));

        var marker = SourceOf(files, MarkerFileName);
        marker.Should().Contain(
            "using System.Collections.Generic;",
            "a mirrored collection field needs the same namespace the record emitter requires for it");
        marker.Should().Contain("Interface { get; }", "a keyword-named field PascalCases on the marker exactly as it does on the view record");
        marker.Should().NotContain("@interface", "the escape runs after PascalCasing, which already lifts the name clear of the keyword");
        ShouldCompileCleanly(files, "a mirrored field's type and name must resolve on the marker as on the record");
    }
}
