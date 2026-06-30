// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using AwesomeAssertions;
using Xunit;
using static Daml.Codegen.CSharp.Tests.TestHelpers.DamlModelBuilder;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class DataTypeCodeGenTests
{
    #region Code Generation Options Tests

    [Fact]
    public void Generate_should_use_block_scoped_namespace_when_configured()
    {
        // Arrange
        var options = new CodeGenOptions
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = false,
            UseRecordTypes = true,
            UsePrimaryConstructors = true
        };

        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Simple",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("value", new DamlPrimitiveType(DamlPrimitive.Text))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator(options);

        // Act
        var files = generator.Generate(dar);
        var simpleFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Simple.cs", StringComparison.Ordinal));

        // Assert
        simpleFile.Should().NotBeNull();
        var code = simpleFile!.Content;

        // Block-scoped namespace has { } braces (uses root namespace from package)
        code.Should().Contain("namespace Test.Package");
        code.Should().Contain("{");
    }

    [Fact]
    public void Generate_should_include_nullable_enable_when_configured()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Simple",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("value", new DamlPrimitiveType(DamlPrimitive.Text))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var simpleFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Simple.cs", StringComparison.Ordinal));

        // Assert
        simpleFile.Should().NotBeNull();
        var code = simpleFile!.Content;

        code.Should().Contain("#nullable enable");
    }

    [Fact]
    public void Generate_should_not_emit_serialization_using_when_body_has_no_json_references()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Simple",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("value", new DamlPrimitiveType(DamlPrimitive.Text))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        var files = generator.Generate(dar);
        var simpleFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Simple.cs", StringComparison.Ordinal));

        simpleFile.Should().NotBeNull();
        simpleFile!.Content.Should().NotContain("using Daml.Runtime.Serialization;",
            "conditional using emission must not emit Daml.Runtime.Serialization when the body contains no JSON-serialization references");
    }

    [Fact]
    public void Generate_should_skip_json_serialization_using_when_disabled()
    {
        // Arrange
        var options = new CodeGenOptions
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            UseRecordTypes = true,
            UsePrimaryConstructors = true
        };

        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Simple",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("value", new DamlPrimitiveType(DamlPrimitive.Text))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator(options);

        // Act
        var files = generator.Generate(dar);
        var simpleFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Simple.cs", StringComparison.Ordinal));

        // Assert
        simpleFile.Should().NotBeNull();
        var code = simpleFile!.Content;

        code.Should().NotContain("using Daml.Runtime.Serialization;");
    }

    #endregion

    #region Namespace and Identifier Tests

    [Fact]
    public void Generate_should_use_custom_root_namespace_when_configured()
    {
        // Arrange
        var options = new CodeGenOptions
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            UseRecordTypes = true,
            UsePrimaryConstructors = true,
            RootNamespace = "Custom.Namespace"
        };

        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Simple",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("value", new DamlPrimitiveType(DamlPrimitive.Text))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator(options);

        // Act
        var files = generator.Generate(dar);
        var simpleFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Simple.cs", StringComparison.Ordinal));

        // Assert
        simpleFile.Should().NotBeNull();
        var code = simpleFile!.Content;

        // When custom root namespace is specified, all types go into that namespace directly
        code.Should().Contain("namespace Custom.Namespace;");
    }

    [Fact]
    public void Generate_should_sanitize_identifier_with_csharp_keyword()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "class", // C# keyword
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("value", new DamlPrimitiveType(DamlPrimitive.Text))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);

        // Assert - The generated file should have @ prefix for the keyword
        files.Should().NotBeEmpty();
        var classFile = files.FirstOrDefault(f => f.RelativePath.Contains("@class.cs"));
        classFile.Should().NotBeNull();
        var code = classFile!.Content;
        code.Should().Contain("public sealed record @class");
    }

    [Fact]
    public void Generate_should_sanitize_identifier_starting_with_digit()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "123Type", // Starts with digit
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("value", new DamlPrimitiveType(DamlPrimitive.Text))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);

        // Assert - Should have _ prefix
        files.Should().NotBeEmpty();
        var typeFile = files.FirstOrDefault(f => f.RelativePath.Contains("_123Type.cs"));
        typeFile.Should().NotBeNull();
        var code = typeFile!.Content;
        code.Should().Contain("public sealed record _123Type");
    }

    [Fact]
    public void Generate_should_convert_field_names_to_pascal_case()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "CasingTest",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("lower_case_field", new DamlPrimitiveType(DamlPrimitive.Text)),
                        new DamlFieldDefinition("camelCaseField", new DamlPrimitiveType(DamlPrimitive.Text)),
                        new DamlFieldDefinition("kebab-case-field", new DamlPrimitiveType(DamlPrimitive.Text))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var casingFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("CasingTest.cs", StringComparison.Ordinal));

        // Assert
        casingFile.Should().NotBeNull();
        var code = casingFile!.Content;

        code.Should().Contain("LowerCaseField");
        code.Should().Contain("CamelCaseField");
        code.Should().Contain("KebabCaseField");
    }

    [Fact]
    public void Generate_should_handle_tuple_field_names()
    {
        // Arrange - tuple fields in Daml are named _1, _2, etc.
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "TupleType",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("_1", new DamlPrimitiveType(DamlPrimitive.Text)),
                        new DamlFieldDefinition("_2", new DamlPrimitiveType(DamlPrimitive.Int64)),
                        new DamlFieldDefinition("_3", new DamlPrimitiveType(DamlPrimitive.Bool))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var tupleFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("TupleType.cs", StringComparison.Ordinal));

        // Assert
        tupleFile.Should().NotBeNull();
        var code = tupleFile!.Content;

        // Tuple fields _1, _2, _3 should become valid C# identifiers _1, _2, _3
        code.Should().Contain("string _1");
        code.Should().Contain("long _2");
        code.Should().Contain("bool _3");

        // The ToRecord should reference the original field names
        code.Should().Contain("DamlField.Create(\"_1\"");
        code.Should().Contain("DamlField.Create(\"_2\"");
        code.Should().Contain("DamlField.Create(\"_3\"");
    }

    #endregion

    #region Template Filtering Tests

    [Fact]
    public void Generate_should_filter_templates_with_root_filter()
    {
        // Arrange
        var options = new CodeGenOptions
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            UseRecordTypes = true,
            UsePrimaryConstructors = true,
            RootFilter = "Test\\.Module:Include.*"
        };

        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "IncludeThis",
                    Fields = [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = []
                },
                new DamlTemplate
                {
                    Name = "ExcludeThis",
                    Fields = [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = []
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "IncludeThis",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))])
                },
                new DamlDataType
                {
                    Name = "ExcludeThis",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator(options);

        // Act
        var files = generator.Generate(dar);

        // Assert - Only IncludeThis template should be generated
        var templateFiles = files.Where(f => f.RelativePath.Contains("IncludeThis") || f.RelativePath.Contains("ExcludeThis")).ToList();
        templateFiles.Should().HaveCount(1);
        templateFiles[0].RelativePath.Should().Contain("IncludeThis");
    }

    #endregion
}
