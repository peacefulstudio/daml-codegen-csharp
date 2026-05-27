using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using Daml.Codegen.DarParser;
using FluentAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class TemplateCodeGenTests
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
            UsePrimaryConstructors = true
        };
        var logger = new ConsoleLogger(0); // Silent
        return new CSharpCodeGenerator(options, logger);
    }

    private static DarArchive CreateTestDar(DamlModule module, string packageName = "test-package")
    {
        var package = new DamlPackage
        {
            PackageId = "test-package-id",
            Name = packageName,
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

    #region Template Basic Structure Tests

    [Fact]
    public void Generate_should_create_template_with_ITemplate_interface()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "SimpleTemplate",
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = []
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "SimpleTemplate",
                    Definition = new DamlRecordDefinition([new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var templateFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("SimpleTemplate.cs", StringComparison.Ordinal));

        // Assert
        templateFile.Should().NotBeNull();
        var code = templateFile!.Content;

        code.Should().Contain(": ITemplate");
        code.Should().Contain("public sealed partial record SimpleTemplate");
    }

    [Fact]
    public void Generate_should_include_template_metadata()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Asset",
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = []
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Asset",
                    Definition = new DamlRecordDefinition([new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))])
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

        code.Should().Contain("public static Identifier TemplateId { get; }");
        code.Should().Contain("\"test-package-id\"");
        code.Should().Contain("\"Test.Module\"");
        code.Should().Contain("\"Asset\"");
        code.Should().Contain("public static string PackageId => \"test-package-id\";");
        code.Should().Contain("public static string PackageName => \"test-package\";");
        code.Should().Contain("public static Version PackageVersion { get; }");
    }

    [Fact]
    public void Generate_should_create_nested_ContractId_class()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Token",
                    Fields = [new DamlField("issuer", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = []
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Token",
                    Definition = new DamlRecordDefinition([new DamlField("issuer", new DamlPrimitiveType(DamlPrimitive.Party))])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var tokenFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Token.cs", StringComparison.Ordinal));

        // Assert
        tokenFile.Should().NotBeNull();
        var code = tokenFile!.Content;

        code.Should().Contain("public sealed record ContractId(string Value)");
        code.Should().Contain(": ContractId<Token>(Value)");
        code.Should().Contain("IExercises<Token>");
    }

    [Fact]
    public void Generate_should_create_nested_Contract_class()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Holding",
                    Fields = [new DamlField("amount", new DamlPrimitiveType(DamlPrimitive.Numeric))],
                    Choices = []
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Holding",
                    Definition = new DamlRecordDefinition([new DamlField("amount", new DamlPrimitiveType(DamlPrimitive.Numeric))])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var holdingFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Holding.cs", StringComparison.Ordinal));

        // Assert
        holdingFile.Should().NotBeNull();
        var code = holdingFile!.Content;

        code.Should().Contain("public sealed record Contract(ContractId Id, Holding Data)");
        code.Should().Contain(": IContract<ContractId, Holding>");
        code.Should().Contain("public static Contract FromCreatedEvent(CreatedEvent @event)");
    }

    #endregion

    #region Template with Multiple Fields Tests

    [Fact]
    public void Generate_should_create_template_with_all_primitive_fields()
    {
        // Arrange
        var fields = new[]
        {
            new DamlField("textField", new DamlPrimitiveType(DamlPrimitive.Text)),
            new DamlField("intField", new DamlPrimitiveType(DamlPrimitive.Int64)),
            new DamlField("boolField", new DamlPrimitiveType(DamlPrimitive.Bool)),
            new DamlField("numericField", new DamlPrimitiveType(DamlPrimitive.Numeric)),
            new DamlField("partyField", new DamlPrimitiveType(DamlPrimitive.Party)),
            new DamlField("dateField", new DamlPrimitiveType(DamlPrimitive.Date)),
            new DamlField("timestampField", new DamlPrimitiveType(DamlPrimitive.Timestamp))
        };

        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "AllPrimitives",
                    Fields = fields,
                    Choices = []
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "AllPrimitives",
                    Definition = new DamlRecordDefinition(fields)
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var templateFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("AllPrimitives.cs", StringComparison.Ordinal));

        // Assert
        templateFile.Should().NotBeNull();
        var code = templateFile!.Content;

        code.Should().Contain("string TextField");
        code.Should().Contain("long IntField");
        code.Should().Contain("bool BoolField");
        code.Should().Contain("decimal NumericField");
        code.Should().Contain("Party PartyField");
        code.Should().Contain("DateOnly DateField");
        code.Should().Contain("DateTimeOffset TimestampField");
    }

    [Fact]
    public void Generate_should_create_template_with_complex_fields()
    {
        // Arrange
        var fields = new[]
        {
            new DamlField("items", new DamlTypeApp(
                new DamlPrimitiveType(DamlPrimitive.List),
                [new DamlPrimitiveType(DamlPrimitive.Text)])),
            new DamlField("maybeValue", new DamlTypeApp(
                new DamlPrimitiveType(DamlPrimitive.Optional),
                [new DamlPrimitiveType(DamlPrimitive.Int64)])),
            new DamlField("metadata", new DamlTypeApp(
                new DamlPrimitiveType(DamlPrimitive.TextMap),
                [new DamlPrimitiveType(DamlPrimitive.Text)]))
        };

        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "ComplexFields",
                    Fields = fields,
                    Choices = []
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "ComplexFields",
                    Definition = new DamlRecordDefinition(fields)
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var templateFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("ComplexFields.cs", StringComparison.Ordinal));

        // Assert
        templateFile.Should().NotBeNull();
        var code = templateFile!.Content;

        code.Should().Contain("IReadOnlyList<string> Items");
        code.Should().Contain("long? MaybeValue");
        code.Should().Contain("IReadOnlyDictionary<string, string> Metadata");
    }

    #endregion

    #region Template Serialization Tests

    [Fact]
    public void Generate_should_create_ToRecord_method_for_template()
    {
        // Arrange
        var fields = new[]
        {
            new DamlField("name", new DamlPrimitiveType(DamlPrimitive.Text)),
            new DamlField("count", new DamlPrimitiveType(DamlPrimitive.Int64))
        };

        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Item",
                    Fields = fields,
                    Choices = []
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Item",
                    Definition = new DamlRecordDefinition(fields)
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var itemFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Item.cs", StringComparison.Ordinal));

        // Assert
        itemFile.Should().NotBeNull();
        var code = itemFile!.Content;

        code.Should().Contain("public DamlRecord ToRecord()");
        code.Should().Contain("DamlField.Create(\"name\", new DamlText(Name))");
        code.Should().Contain("DamlField.Create(\"count\", new DamlInt64(Count))");
    }

    [Fact]
    public void Generate_should_create_FromRecord_method_for_template()
    {
        // Arrange
        var fields = new[]
        {
            new DamlField("isActive", new DamlPrimitiveType(DamlPrimitive.Bool)),
            new DamlField("amount", new DamlPrimitiveType(DamlPrimitive.Numeric))
        };

        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Status",
                    Fields = fields,
                    Choices = []
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Status",
                    Definition = new DamlRecordDefinition(fields)
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var statusFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Status.cs", StringComparison.Ordinal));

        // Assert
        statusFile.Should().NotBeNull();
        var code = statusFile!.Content;

        code.Should().Contain("public static Status FromRecord(DamlRecord record)");
        code.Should().Contain("IsActive: record.GetRequiredField(\"isActive\").As<DamlBool>().Value");
        code.Should().Contain("Amount: record.GetRequiredField(\"amount\").As<DamlNumeric>().Value");
    }

    [Fact]
    public void Generate_should_serialize_list_fields_correctly()
    {
        // Arrange
        var fields = new[]
        {
            new DamlField("tags", new DamlTypeApp(
                new DamlPrimitiveType(DamlPrimitive.List),
                [new DamlPrimitiveType(DamlPrimitive.Text)]))
        };

        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Tagged",
                    Fields = fields,
                    Choices = []
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Tagged",
                    Definition = new DamlRecordDefinition(fields)
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var taggedFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Tagged.cs", StringComparison.Ordinal));

        // Assert
        taggedFile.Should().NotBeNull();
        var code = taggedFile!.Content;

        // Generated code wraps the Select projection in `(DamlValue)` cast and materializes
        // it with `.ToList()` so the result is assignable to DamlList(IReadOnlyList<DamlValue>).
        code.Should().Contain("new DamlList(Tags.Select(x => (DamlValue)new DamlText(x)).ToList())");
    }

    [Fact]
    public void Generate_should_serialize_optional_fields_correctly()
    {
        // Arrange
        var fields = new[]
        {
            new DamlField("maybeText", new DamlTypeApp(
                new DamlPrimitiveType(DamlPrimitive.Optional),
                [new DamlPrimitiveType(DamlPrimitive.Text)]))
        };

        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "OptionalTemplate",
                    Fields = fields,
                    Choices = []
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "OptionalTemplate",
                    Definition = new DamlRecordDefinition(fields)
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var optionalFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("OptionalTemplate.cs", StringComparison.Ordinal));

        // Assert
        optionalFile.Should().NotBeNull();
        var code = optionalFile!.Content;

        code.Should().Contain("MaybeText is { } __MaybeText ? new DamlOptional(new DamlText(__MaybeText)) : DamlOptional.None");
    }

    #endregion

    #region Template Without Primary Constructor Tests

    [Fact]
    public void Generate_should_create_template_without_primary_constructor_when_disabled()
    {
        // Arrange
        var options = new CodeGenOptions
        {
            OutputDirectory = "/tmp/test",
            GenerateJsonSupport = true,
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            UseRecordTypes = true,
            UsePrimaryConstructors = false
        };

        var fields = new[]
        {
            new DamlField("value", new DamlPrimitiveType(DamlPrimitive.Text))
        };

        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "NoConstructor",
                    Fields = fields,
                    Choices = []
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "NoConstructor",
                    Definition = new DamlRecordDefinition(fields)
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator(options);

        // Act
        var files = generator.Generate(dar);
        var templateFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("NoConstructor.cs", StringComparison.Ordinal));

        // Assert
        templateFile.Should().NotBeNull();
        var code = templateFile!.Content;

        // Should have record without primary constructor
        code.Should().Contain("public sealed partial record NoConstructor : ITemplate");
        code.Should().Contain("public required string Value { get; init; }");
    }

    [Fact]
    public void Generate_should_use_class_when_record_types_disabled()
    {
        // Arrange
        var options = new CodeGenOptions
        {
            OutputDirectory = "/tmp/test",
            GenerateJsonSupport = true,
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            UseRecordTypes = false,
            UsePrimaryConstructors = false
        };

        var fields = new[]
        {
            new DamlField("value", new DamlPrimitiveType(DamlPrimitive.Text))
        };

        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "ClassTemplate",
                    Fields = fields,
                    Choices = []
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "ClassTemplate",
                    Definition = new DamlRecordDefinition(fields)
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator(options);

        // Act
        var files = generator.Generate(dar);
        var templateFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("ClassTemplate.cs", StringComparison.Ordinal));

        // Assert
        templateFile.Should().NotBeNull();
        var code = templateFile!.Content;

        code.Should().Contain("public sealed partial class ClassTemplate : ITemplate");
    }

    #endregion

    #region Empty Template Tests

    [Fact]
    public void Generate_should_handle_template_with_no_fields()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "EmptyTemplate",
                    Fields = [],
                    Choices = []
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "EmptyTemplate",
                    Definition = new DamlRecordDefinition([])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var emptyFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("EmptyTemplate.cs", StringComparison.Ordinal));

        // Assert
        emptyFile.Should().NotBeNull();
        var code = emptyFile!.Content;

        // Should still have record declaration but without primary constructor parameters
        code.Should().Contain("public sealed partial record EmptyTemplate : ITemplate");
        code.Should().Contain("public DamlRecord ToRecord()");
        code.Should().Contain("DamlRecord.Create(");
    }

    #endregion

    #region File Path Tests

    [Fact]
    public void Generate_should_create_correct_file_path()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Deeply.Nested.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "MyTemplate",
                    Fields = [new DamlField("value", new DamlPrimitiveType(DamlPrimitive.Text))],
                    Choices = []
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "MyTemplate",
                    Definition = new DamlRecordDefinition([new DamlField("value", new DamlPrimitiveType(DamlPrimitive.Text))])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var templateFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("MyTemplate.cs", StringComparison.Ordinal));

        // Assert
        templateFile.Should().NotBeNull();
        // All types go into the root namespace directory (derived from package name, not module)
        templateFile!.RelativePath.Should().Contain("Test/Package/");
    }

    #endregion

    #region Package Info Tests

    [Fact]
    public void Generate_should_use_package_version_in_metadata()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Versioned",
                    Fields = [new DamlField("value", new DamlPrimitiveType(DamlPrimitive.Text))],
                    Choices = []
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Versioned",
                    Definition = new DamlRecordDefinition([new DamlField("value", new DamlPrimitiveType(DamlPrimitive.Text))])
                }
            ],
            Interfaces = []
        };

        var package = new DamlPackage
        {
            PackageId = "versioned-package-id",
            Name = "versioned-package",
            Version = new Version(2, 3, 4),
            LfVersion = "2.1",
            Modules = [module],
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
        var versionedFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Versioned.cs", StringComparison.Ordinal));

        // Assert
        versionedFile.Should().NotBeNull();
        var code = versionedFile!.Content;

        code.Should().Contain("new(2, 3, 4)");
    }

    #endregion
}
