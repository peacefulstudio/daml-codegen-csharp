using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.DarReader;
using FluentAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Tests for edge cases and special scenarios in code generation.
/// </summary>
public class CodeGenEdgeCaseTests
{
    private static CSharpCodeGenerator CreateGenerator(CodeGenOptions? options = null)
    {
        options ??= new CodeGenOptions
        {
            OutputDirectory = "/tmp/test",
            GenerateJsonSupport = true,
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            UseRecordTypes = true,
            UsePrimaryConstructors = true,
            GenerateXmlDocs = true
        };
        var logger = new ConsoleLogger(0); // Silent
        return new CSharpCodeGenerator(options, logger);
    }

    private static DarArchive CreateTestDar(DamlModule module)
    {
        var package = new DamlPackage
        {
            PackageId = "test-package-id",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = []
        };

        return new DarArchive
        {
            MainPackage = package,
            Dependencies = []
        };
    }

    #region Numeric Type Scale Tests

    [Fact]
    public void Generate_should_handle_numeric_with_scale_argument()
    {
        // Arrange - Numeric 10 is represented as DamlTypeApp(Numeric, [10])
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Amount",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("value", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.Numeric),
                            [new DamlTypeVar("10")])) // Numeric 10 scale
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var amountFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Amount.cs", StringComparison.Ordinal));

        // Assert
        amountFile.Should().NotBeNull();
        var code = amountFile!.Content;

        // Numeric with scale still maps to decimal
        code.Should().Contain("decimal Value");
        code.Should().Contain("new DamlNumeric(Value)");
    }

    #endregion

    #region Type Variable Tests

    [Fact]
    public void Generate_should_handle_type_variable()
    {
        // Arrange - Generic types have type variables
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Container",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("item", new DamlTypeVar("a"))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var containerFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Container.cs", StringComparison.Ordinal));

        // Assert
        containerFile.Should().NotBeNull();
        var code = containerFile!.Content;

        // Type variables are mapped to generic type parameters (a -> TA)
        code.Should().Contain("TA Item");
    }

    #endregion

    #region Choice with Various Argument Types

    [Fact]
    public void Generate_should_handle_choice_with_nested_argument_fallback()
    {
        // Arrange - A choice with an argument type that's not found in data types
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Contract",
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Process",
                            Consuming = false,
                            // Primitive type that's not Unit - fallback to nested type
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Text),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit)
                        }
                    ]
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Contract",
                    Definition = new DamlRecordDefinition([new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var contractFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Contract.cs", StringComparison.Ordinal));

        // Assert
        contractFile.Should().NotBeNull();
        var code = contractFile!.Content;

        // Should generate a fallback nested argument type
        code.Should().Contain("ProcessArg");
    }

    [Fact]
    public void Generate_should_handle_choice_with_party_return_type()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Contract",
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "GetOwner",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Party)
                        }
                    ]
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Contract",
                    Definition = new DamlRecordDefinition([new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var contractFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Contract.cs", StringComparison.Ordinal));

        // Assert
        contractFile.Should().NotBeNull();
        var code = contractFile!.Content;

        code.Should().Contain("Choice<Contract, DamlUnit, Party> ChoiceGetOwner");
        code.Should().Contain("ResultDecoder = val => Party.FromDamlValue(val.As<DamlParty>())");
    }

    [Fact]
    public void Generate_should_handle_choice_with_complex_return_type()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Contract",
                    Fields = [new DamlField("data", new DamlPrimitiveType(DamlPrimitive.Text))],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "GetData",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            // Complex return type that falls back to FromRecord
                            ReturnType = new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.List),
                                [new DamlPrimitiveType(DamlPrimitive.Text)])
                        }
                    ]
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Contract",
                    Definition = new DamlRecordDefinition([new DamlField("data", new DamlPrimitiveType(DamlPrimitive.Text))])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var contractFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Contract.cs", StringComparison.Ordinal));

        // Assert
        contractFile.Should().NotBeNull();
        var code = contractFile!.Content;

        // Complex types fall back to FromRecord
        code.Should().Contain("Choice<Contract, DamlUnit, IReadOnlyList<string>>");
        code.Should().Contain("ResultDecoder = val => IReadOnlyList<string>.FromRecord(val.As<DamlRecord>())");
    }

    #endregion

    #region Template Gets Fields from Data Type

    [Fact]
    public void Generate_should_get_fields_from_data_type_when_template_has_none()
    {
        // Arrange - Template with no fields, fields come from corresponding data type
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Asset",
                    Fields = [], // Empty fields
                    Choices = []
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Asset",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlField("value", new DamlPrimitiveType(DamlPrimitive.Numeric))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var assetFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Asset.cs", StringComparison.Ordinal));

        // Assert
        assetFile.Should().NotBeNull();
        var code = assetFile!.Content;

        // Should have fields from the data type
        code.Should().Contain("Party Owner");
        code.Should().Contain("decimal Value");
    }

    #endregion

    #region Data Type Not Generated for Templates

    [Fact]
    public void Generate_should_not_create_separate_data_type_for_template()
    {
        // Arrange - Data type with same name as template should only appear as template
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Token",
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = []
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Token", // Same name as template
                    Definition = new DamlRecordDefinition([new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))])
                },
                new DamlDataType
                {
                    Name = "OtherType", // Different name
                    Definition = new DamlRecordDefinition([new DamlField("value", new DamlPrimitiveType(DamlPrimitive.Text))])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);

        // Assert - Should have Token.cs (template) and OtherType.cs (data type), not two Token.cs
        var tokenFiles = files.Where(f => f.RelativePath.EndsWith("Token.cs", StringComparison.Ordinal)).ToList();
        tokenFiles.Should().HaveCount(1);

        // The Token should be a template (has ITemplate)
        tokenFiles[0].Content.Should().Contain(": ITemplate");

        // OtherType should exist as a data type
        var otherTypeFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("OtherType.cs", StringComparison.Ordinal));
        otherTypeFile.Should().NotBeNull();
        otherTypeFile!.Content.Should().Contain(": IDamlValue");
    }

    #endregion

    #region Serialization Edge Cases

    [Fact]
    public void Generate_should_serialize_date_fields_correctly()
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
                    Name = "DateRecord",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("startDate", new DamlPrimitiveType(DamlPrimitive.Date)),
                        new DamlField("endDate", new DamlPrimitiveType(DamlPrimitive.Date))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var dateFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("DateRecord.cs", StringComparison.Ordinal));

        // Assert
        dateFile.Should().NotBeNull();
        var code = dateFile!.Content;

        code.Should().Contain("new DamlDate(StartDate)");
        code.Should().Contain("As<DamlDate>()).Value");
    }

    [Fact]
    public void Generate_should_serialize_timestamp_fields_correctly()
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
                    Name = "TimestampRecord",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("createdAt", new DamlPrimitiveType(DamlPrimitive.Timestamp))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var timestampFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("TimestampRecord.cs", StringComparison.Ordinal));

        // Assert
        timestampFile.Should().NotBeNull();
        var code = timestampFile!.Content;

        code.Should().Contain("new DamlTimestamp(CreatedAt)");
        code.Should().Contain("As<DamlTimestamp>()).Value");
    }

    [Fact]
    public void Generate_should_serialize_contract_id_fields_correctly()
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
                    Name = "Reference",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("target", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.ContractId),
                            [new DamlTypeRef("", "Test.Module", "Asset")]))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var refFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Reference.cs", StringComparison.Ordinal));

        // Assert
        refFile.Should().NotBeNull();
        var code = refFile!.Content;

        code.Should().Contain("Target.ToDamlValue()");
        code.Should().Contain("new ContractId<Asset>((");
    }

    [Fact]
    public void Generate_should_serialize_type_ref_fields_correctly()
    {
        // Arrange - A record referencing another record type
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Inner",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("value", new DamlPrimitiveType(DamlPrimitive.Text))
                    ])
                },
                new DamlDataType
                {
                    Name = "Outer",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("inner", new DamlTypeRef("", "Test.Module", "Inner"))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var outerFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Outer.cs", StringComparison.Ordinal));

        // Assert
        outerFile.Should().NotBeNull();
        var code = outerFile!.Content;

        code.Should().Contain("Inner.ToRecord()");
        code.Should().Contain("Inner.FromRecord(");
    }

    #endregion

    #region Empty Field Lists

    [Fact]
    public void Generate_should_handle_empty_record()
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
                    Name = "EmptyRecord",
                    Definition = new DamlRecordDefinition([])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var emptyFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("EmptyRecord.cs", StringComparison.Ordinal));

        // Assert
        emptyFile.Should().NotBeNull();
        var code = emptyFile!.Content;

        // Record with no primary constructor parameters
        code.Should().Contain("public sealed record EmptyRecord : IDamlValue");
        code.Should().Contain("DamlRecord.Create(");
    }

    #endregion

    #region Multiple Modules

    [Fact]
    public void Generate_should_handle_multiple_modules()
    {
        // Arrange
        var module1 = new DamlModule
        {
            Name = "Module.One",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Type1",
                    Definition = new DamlRecordDefinition([new DamlField("v", new DamlPrimitiveType(DamlPrimitive.Text))])
                }
            ],
            Interfaces = []
        };

        var module2 = new DamlModule
        {
            Name = "Module.Two",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Type2",
                    Definition = new DamlRecordDefinition([new DamlField("v", new DamlPrimitiveType(DamlPrimitive.Int64))])
                }
            ],
            Interfaces = []
        };

        var package = new DamlPackage
        {
            PackageId = "test-package-id",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module1, module2],
            DependencyReferences = []
        };

        var dar = new DarArchive
        {
            MainPackage = package,
            Dependencies = []
        };

        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);

        // Assert
        files.Count.Should().BeGreaterThanOrEqualTo(2);

        var type1File = files.FirstOrDefault(f => f.RelativePath.EndsWith("Type1.cs", StringComparison.Ordinal));
        var type2File = files.FirstOrDefault(f => f.RelativePath.EndsWith("Type2.cs", StringComparison.Ordinal));

        type1File.Should().NotBeNull();
        type2File.Should().NotBeNull();

        // All types from all modules go into the root namespace (derived from package name)
        type1File!.Content.Should().Contain("namespace Test.Package;");
        type2File!.Content.Should().Contain("namespace Test.Package;");
    }

    #endregion

    #region Variant Without Argument

    [Fact]
    public void Generate_should_handle_variant_constructor_without_argument()
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
                    Name = "Maybe",
                    Definition = new DamlVariantDefinition(
                    [
                        new DamlVariantConstructor("Nothing", null),
                        new DamlVariantConstructor("Just", new DamlPrimitiveType(DamlPrimitive.Text))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var maybeFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Maybe.cs", StringComparison.Ordinal));

        // Assert
        maybeFile.Should().NotBeNull();
        var code = maybeFile!.Content;

        // Nothing has no argument
        code.Should().Contain("public sealed record Nothing() : Maybe");
        // Just has an argument
        code.Should().Contain("public sealed record Just(string Value) : Maybe");
    }

    #endregion
}
