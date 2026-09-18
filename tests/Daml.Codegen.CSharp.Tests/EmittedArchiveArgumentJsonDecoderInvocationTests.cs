// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Text.Json;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using AwesomeAssertions;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class EmittedArchiveArgumentJsonDecoderInvocationTests
{
    private static readonly DamlPackage StdlibStub = new()
    {
        PackageId = "daml-prim-pkg-id",
        Name = "daml-prim",
        Version = new Version(1, 0, 0),
        LfVersion = "2.1",
        Modules = [],
        DependencyReferences = [],
    };

    private static readonly Assembly Emitted = EmitToAssembly(Generate());

    private static IReadOnlyList<GeneratedFile> Generate()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Asset",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Archive",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef(StdlibStub.PackageId, "DA.Internal.Template", "Archive"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        },
                        new DamlChoice
                        {
                            Name = "Ping",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        },
                    ],
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Asset",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces = [],
        };

        var dar = new DarModel
        {
            MainPackage = new DamlPackage
            {
                PackageId = "test-pkg",
                Name = "test-package",
                Version = new Version(1, 0, 0),
                LfVersion = "2.1",
                Modules = [module],
                DependencyReferences = [],
            },
            Dependencies = [StdlibStub],
        };

        return CreateGenerator(new CodeGenOptions { EnableNullableReferenceTypes = true, UseFileScopedNamespaces = true }).Generate(dar);
    }

    private static IChoice ArchiveChoice =>
        (IChoice)Emitted.GetTypes().Single(t => t.Name == "Asset")
            .GetProperty("ChoiceArchive", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;

    private static IChoice PingChoice =>
        (IChoice)Emitted.GetTypes().Single(t => t.Name == "Asset")
            .GetProperty("ChoicePing", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;

    [Fact]
    public void ArchiveArgumentJsonDecoder_decodes_the_canonical_empty_object_to_Unit()
    {
        using var document = JsonDocument.Parse("{}");

        var result = ArchiveChoice.DecodeArgumentJson(document.RootElement, DamlLfJsonDecodeContext.Root("Archive"));

        result.Should().Be(DamlUnit.Instance);
    }

    [Fact]
    public void ArchiveArgumentJsonReader_reads_the_canonical_empty_object_as_an_empty_record()
    {
        using var document = JsonDocument.Parse("{}");

        var read = ArchiveChoice.ReadArgumentJson(document.RootElement, DamlLfJsonDecodeContext.Root("Archive"));

        read.Should().Be(DamlRecord.Create());
    }

    [Fact]
    public void ArchiveArgumentJsonReader_reads_an_object_with_an_unexpected_field_as_an_empty_record()
    {
        using var document = JsonDocument.Parse("""{"foo":1}""");

        var read = ArchiveChoice.ReadArgumentJson(document.RootElement, DamlLfJsonDecodeContext.Root("Archive"));

        read.Should().Be(DamlRecord.Create());
    }

    [Fact]
    public void ArchiveArgumentJsonDecoder_decodes_an_object_with_an_unexpected_field_to_Unit()
    {
        using var document = JsonDocument.Parse("""{"foo":1}""");

        var result = ArchiveChoice.DecodeArgumentJson(document.RootElement, DamlLfJsonDecodeContext.Root("Archive"));

        result.Should().Be(DamlUnit.Instance);
    }

    [Theory]
    [InlineData("[]", "Array")]
    [InlineData("null", "Null")]
    [InlineData("\"x\"", "String")]
    public void ArchiveArgumentJsonReader_rejects_a_non_object_wire_value(string wireJson, string foundKind)
    {
        using var document = JsonDocument.Parse(wireJson);

        var read = () => ArchiveChoice.ReadArgumentJson(document.RootElement, DamlLfJsonDecodeContext.Root("Archive"));

        read.Should().Throw<JsonException>()
            .WithMessage($"Expected JSON Object at 'Archive' but found {foundKind}");
    }

    [Theory]
    [InlineData("[]", "Array")]
    [InlineData("null", "Null")]
    [InlineData("\"x\"", "String")]
    public void ArchiveArgumentJsonDecoder_rejects_a_non_object_wire_value(string wireJson, string foundKind)
    {
        using var document = JsonDocument.Parse(wireJson);

        var decode = () => ArchiveChoice.DecodeArgumentJson(document.RootElement, DamlLfJsonDecodeContext.Root("Archive"));

        decode.Should().Throw<JsonException>()
            .WithMessage($"Expected JSON Object at 'Archive' but found {foundKind}");
    }

    [Fact]
    public void UnitArgumentJsonReaderAndDecoder_read_the_canonical_empty_object_as_DamlUnit()
    {
        using var document = JsonDocument.Parse("{}");

        var read = PingChoice.ReadArgumentJson(document.RootElement, DamlLfJsonDecodeContext.Root("Ping"));
        var decoded = PingChoice.DecodeArgumentJson(document.RootElement, DamlLfJsonDecodeContext.Root("Ping"));

        read.Should().Be(DamlUnit.Instance);
        decoded.Should().Be(DamlUnit.Instance);
    }

    [Fact]
    public void UnitArgumentJsonReader_rejects_an_object_with_an_unexpected_field()
    {
        using var document = JsonDocument.Parse("""{"foo":1}""");

        var read = () => PingChoice.ReadArgumentJson(document.RootElement, DamlLfJsonDecodeContext.Root("Ping"));

        read.Should().Throw<JsonException>();
    }

    [Fact]
    public void UnitArgumentJsonDecoder_rejects_an_object_with_an_unexpected_field()
    {
        using var document = JsonDocument.Parse("""{"foo":1}""");

        var decode = () => PingChoice.DecodeArgumentJson(document.RootElement, DamlLfJsonDecodeContext.Root("Ping"));

        decode.Should().Throw<JsonException>();
    }
}
