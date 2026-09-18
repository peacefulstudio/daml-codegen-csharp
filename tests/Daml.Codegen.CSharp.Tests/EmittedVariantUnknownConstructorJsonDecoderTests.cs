// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Text.Json;
using AwesomeAssertions;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Runtime.Serialization;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class EmittedVariantUnknownConstructorJsonDecoderTests
{
    private static readonly Assembly Emitted = EmitToAssembly(Generate());

    private static IReadOnlyList<GeneratedFile> Generate()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "PaymentMethod",
                    Definition = new DamlVariantDefinition(
                    [
                        new DamlVariantConstructor("Cash", null),
                        new DamlVariantConstructor("Card", new DamlPrimitiveType(DamlPrimitive.Text)),
                    ]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "test-package-id",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var options = new CodeGenOptions { EnableNullableReferenceTypes = true, UseFileScopedNamespaces = true };
        return CreateGenerator(options).Generate(dar);
    }

    private static Type PaymentMethod => Emitted.GetTypes().Single(t => t.Name == "PaymentMethod");

    private static object Decode(string json)
    {
        using var document = JsonDocument.Parse(json);
        var method = PaymentMethod.GetMethod("__ReadDamlLfJson", BindingFlags.Public | BindingFlags.Static)!;
        try
        {
            return method.Invoke(null, [document.RootElement, DamlLfJsonDecodeContext.Root("PaymentMethod")])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    [Fact]
    public void VariantJsonDecoder_reports_UnknownConstructor_for_an_unrecognized_tag_with_no_value_member()
    {
        var act = () => Decode("""{"tag":"Bogus"}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Unknown Daml variant constructor 'Bogus'*")
            .Where(ex => ex.Message.Contains("Cash") && ex.Message.Contains("Card"));
    }

    [Fact]
    public void VariantJsonDecoder_decodes_a_recognized_tag_with_a_payload()
    {
        var result = Decode("""{"tag":"Card","value":"visa"}""");

        result.Should().NotBeNull();
    }
}
