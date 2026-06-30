// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.Model;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class DamlTypeTests
{
    [Fact]
    public void DamlPrimitiveType_should_identify_primitives()
    {
        // Arrange
        var int64Type = new DamlPrimitiveType(DamlPrimitive.Int64);
        var textType = new DamlPrimitiveType(DamlPrimitive.Text);

        // Assert
        int64Type.Primitive.Should().Be(DamlPrimitive.Int64);
        textType.Primitive.Should().Be(DamlPrimitive.Text);
    }

    [Fact]
    public void DamlTypeApp_should_identify_Optional()
    {
        // Arrange
        var optionalType = new DamlTypeApp(
            new DamlPrimitiveType(DamlPrimitive.Optional),
            [new DamlPrimitiveType(DamlPrimitive.Text)]);

        // Assert
        optionalType.IsOptional.Should().BeTrue();
    }

    [Fact]
    public void DamlTypeApp_List_should_not_be_Optional()
    {
        // Arrange
        var listType = new DamlTypeApp(
            new DamlPrimitiveType(DamlPrimitive.List),
            [new DamlPrimitiveType(DamlPrimitive.Int64)]);

        // Assert
        listType.IsOptional.Should().BeFalse();
    }

    [Fact]
    public void DamlTypeRef_should_store_type_reference()
    {
        // Arrange
        var typeRef = new DamlTypeRef("pkg123", "Module.Name", "MyType");

        // Assert
        typeRef.PackageId.Should().Be("pkg123");
        typeRef.Module.Should().Be("Module.Name");
        typeRef.Name.Should().Be("MyType");
    }
}

public class DamlDataTypeTests
{
    [Fact]
    public void DamlRecordDefinition_should_store_fields()
    {
        // Arrange
        var fields = new[]
        {
            new DamlFieldDefinition("name", new DamlPrimitiveType(DamlPrimitive.Text)),
            new DamlFieldDefinition("age", new DamlPrimitiveType(DamlPrimitive.Int64))
        };
        var record = new DamlRecordDefinition(fields);

        // Assert
        record.Fields.Should().HaveCount(2);
        record.Fields[0].Name.Should().Be("name");
        record.Fields[1].Name.Should().Be("age");
    }

    [Fact]
    public void DamlVariantDefinition_should_store_constructors()
    {
        // Arrange
        var constructors = new[]
        {
            new DamlVariantConstructor("None", null),
            new DamlVariantConstructor("Some", new DamlPrimitiveType(DamlPrimitive.Text))
        };
        var variant = new DamlVariantDefinition(constructors);

        // Assert
        variant.Constructors.Should().HaveCount(2);
        variant.Constructors[0].Name.Should().Be("None");
        variant.Constructors[0].ArgumentType.Should().BeNull();
        variant.Constructors[1].Name.Should().Be("Some");
        variant.Constructors[1].ArgumentType.Should().NotBeNull();
    }

    [Fact]
    public void DamlEnumDefinition_should_store_constructors()
    {
        // Arrange
        var enumDef = new DamlEnumDefinition(["Red", "Green", "Blue"]);

        // Assert
        enumDef.Constructors.Should().BeEquivalentTo(["Red", "Green", "Blue"]);
    }
}
