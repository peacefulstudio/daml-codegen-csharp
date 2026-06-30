// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using AwesomeAssertions;
using Xunit;
using static Daml.Codegen.CSharp.Tests.TestHelpers.DamlModelBuilder;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class DamlFieldAttributeEmitterTests
{
    [Fact]
    public void Positional_record_property_carries_daml_field_attribute()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Holding",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("count", new DamlPrimitiveType(DamlPrimitive.Int64))
                    ])
                }
            ],
            Interfaces = []
        };

        var files = CreateGenerator().Generate(CreateTestDar(module));
        var code = files.First(f => f.RelativePath.EndsWith("Holding.cs", StringComparison.Ordinal)).Content;

        code.Should().Contain("[property: DamlFieldAttribute(\"owner\")] Party Owner");
        code.Should().Contain("[property: DamlFieldAttribute(\"count\")] long Count");
    }

    [Fact]
    public void Required_property_carries_daml_field_attribute_on_its_own_line()
    {
        var options = new CodeGenOptions
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            UseRecordTypes = true,
            UsePrimaryConstructors = false
        };

        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Vault",
                    Fields = [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = []
                }
            ],
            DataTypes = [],
            Interfaces = []
        };

        var files = CreateGenerator(options).Generate(CreateTestDar(module));
        var code = files.First(f => f.RelativePath.EndsWith("Vault.cs", StringComparison.Ordinal)).Content;

        code.Should().Contain("[DamlFieldAttribute(\"owner\")]");
        code.Should().Contain("public required Party Owner { get; init; }");
    }
}
