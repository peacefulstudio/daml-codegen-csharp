// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Text.Json;
using AwesomeAssertions;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Tests.TestHelpers;
using Daml.Codegen.Intermediate.Model;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Daml.Runtime.Stdlib;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class EmittedTemplateKeyJsonDecoderTests
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

    private static readonly IReadOnlyList<GeneratedFile> Files = Generate();

    private static readonly Assembly Emitted = EmitToAssembly(Files);

    private static DamlType TupleType(params DamlType[] componentTypes) =>
        new DamlTypeApp(
            new DamlTypeRef("daml-prim", "DA.Types", $"Tuple{componentTypes.Length}"),
            componentTypes);

    private static IReadOnlyList<GeneratedFile> Generate()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Membership",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Renew",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = ContractIdOf("Membership"),
                        },
                    ],
                    Key = TupleType(new DamlPrimitiveType(DamlPrimitive.Party), new DamlPrimitiveType(DamlPrimitive.Text)),
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Membership",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("plan", new DamlPrimitiveType(DamlPrimitive.Text)),
                    ]),
                },
            ],
            Interfaces = [],
        };

        var dar = new DamlModelBuilder().WithModule(module).WithDependency(DamlPrim).Build();
        var options = new CodeGenOptions { EnableNullableReferenceTypes = true, UseFileScopedNamespaces = true };
        return CreateGenerator(options).Generate(dar);
    }

    private static Type Membership => Emitted.GetTypes().Single(t => t.Name == "Membership");

    private static PropertyInfo KeyProperty =>
        Membership.GetProperty("Key", BindingFlags.Public | BindingFlags.Static)!;

    [Fact]
    public void Keyed_template_with_a_tuple_key_emits_only_KeyJsonReader_never_a_separate_KeyJsonDecoder_member()
    {
        var descriptorType = KeyProperty.PropertyType;
        descriptorType.GetProperty("KeyJsonReader").Should().NotBeNull();

        var membershipSource = Files.Single(f => f.RelativePath.EndsWith("Membership.cs", StringComparison.Ordinal)).Content;

        membershipSource.Should().Contain("KeyJsonReader");
        membershipSource.Should().NotContain("KeyJsonDecoder");
    }

    [Fact]
    public void Keyed_template_tuple_key_decodes_wire_json_through_the_descriptor_and_IKeyDescriptor()
    {
        var descriptor = KeyProperty.GetValue(null);
        descriptor.Should().BeAssignableTo<IKeyDescriptor>();

        using var document = JsonDocument.Parse("""{"_1":"Alice","_2":"member-1"}""");
        var value = ((IKeyDescriptor)descriptor!).ReadKeyJson(document.RootElement, DamlLfJsonDecodeContext.Root("Membership.Key"));

        var decoded = ((IKeyDescriptor)descriptor).DecodeKey(value);
        decoded.Should().Be(new Tuple2<Party, string>(new Party("Alice"), "member-1"));
    }
}
