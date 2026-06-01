using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using Daml.Codegen.DarParser;
using FluentAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class ContractIdentifiersTests
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
            GenerateContractIdentifiers = true
        };
        var logger = new ConsoleLogger(0); // Silent
        return new CSharpCodeGenerator(options, logger);
    }

    private static DarArchive CreateTestDar(DamlModule[] modules, string packageName = "test-package")
    {
        var package = new DamlPackage
        {
            PackageId = "test-package-id",
            Name = packageName,
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = modules,
            DependencyReferences = []
        };

        return new DarArchive
        {
            MainPackage = package,
            Dependencies = []
        };
    }

    #region Basic Generation Tests

    [Fact]
    public void generate_should_create_contract_identifiers_file()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "MyTemplate",
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = []
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "MyTemplate",
                    Definition = new DamlRecordDefinition([new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar([module]);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var identifiersFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("ContractIdentifiers.cs", StringComparison.Ordinal));

        // Assert
        identifiersFile.Should().NotBeNull();
    }

    [Fact]
    public void generate_should_create_file_in_correct_path()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "MyTemplate",
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = []
                }
            ],
            DataTypes = [],
            Interfaces = []
        };

        var dar = CreateTestDar([module]);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var identifiersFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("ContractIdentifiers.cs", StringComparison.Ordinal));

        // Assert
        identifiersFile.Should().NotBeNull();
        // File should be placed one level above the package folder (beside it, not inside)
        identifiersFile!.RelativePath.Should().Be("Test/ContractIdentifiers.cs");
    }

    [Fact]
    public void generate_should_include_all_templates_as_properties()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "FirstTemplate",
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = []
                },
                new DamlTemplate
                {
                    Name = "SecondTemplate",
                    Fields = [new DamlField("value", new DamlPrimitiveType(DamlPrimitive.Text))],
                    Choices = []
                }
            ],
            DataTypes = [],
            Interfaces = []
        };

        var dar = CreateTestDar([module]);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var identifiersFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("ContractIdentifiers.cs", StringComparison.Ordinal));

        // Assert
        identifiersFile.Should().NotBeNull();
        var code = identifiersFile!.Content;

        code.Should().Contain("public static string FirstTemplate { get; } = GetTemplateId<FirstTemplate>();");
        code.Should().Contain("public static string SecondTemplate { get; } = GetTemplateId<SecondTemplate>();");
    }

    [Fact]
    public void generate_should_include_templates_from_multiple_modules()
    {
        // Arrange
        var module1 = new DamlModule
        {
            Name = "Module.One",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "TemplateA",
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = []
                }
            ],
            DataTypes = [],
            Interfaces = []
        };

        var module2 = new DamlModule
        {
            Name = "Module.Two",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "TemplateB",
                    Fields = [new DamlField("value", new DamlPrimitiveType(DamlPrimitive.Text))],
                    Choices = []
                }
            ],
            DataTypes = [],
            Interfaces = []
        };

        var dar = CreateTestDar([module1, module2]);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var identifiersFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("ContractIdentifiers.cs", StringComparison.Ordinal));

        // Assert
        identifiersFile.Should().NotBeNull();
        var code = identifiersFile!.Content;

        code.Should().Contain("public static string TemplateA { get; } = GetTemplateId<TemplateA>();");
        code.Should().Contain("public static string TemplateB { get; } = GetTemplateId<TemplateB>();");
    }

    #endregion

    #region Helper Method Tests

    [Fact]
    public void generate_should_use_template_extensions_get_template_id()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "MyTemplate",
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = []
                }
            ],
            DataTypes = [],
            Interfaces = []
        };

        var dar = CreateTestDar([module]);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var identifiersFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("ContractIdentifiers.cs", StringComparison.Ordinal));

        // Assert
        identifiersFile.Should().NotBeNull();
        var code = identifiersFile!.Content;

        // Should use static import from TemplateExtensions
        code.Should().Contain("using static Daml.Runtime.Contracts.TemplateExtensions;");
        code.Should().Contain("GetTemplateId<MyTemplate>()");
        // Should NOT contain the private helper method anymore
        code.Should().NotContain("private static string GetTemplateId<T>()");
    }

    #endregion

    #region Code Structure Tests

    [Fact]
    public void generate_should_include_auto_generated_header()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "MyTemplate",
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = []
                }
            ],
            DataTypes = [],
            Interfaces = []
        };

        var dar = CreateTestDar([module]);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var identifiersFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("ContractIdentifiers.cs", StringComparison.Ordinal));

        // Assert
        identifiersFile.Should().NotBeNull();
        var code = identifiersFile!.Content;

        code.Should().Contain("// <auto-generated>");
        code.Should().Contain("// This code was generated by daml-codegen-csharp.");
    }

    [Fact]
    public void generate_should_include_nullable_enable_directive()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "MyTemplate",
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = []
                }
            ],
            DataTypes = [],
            Interfaces = []
        };

        var dar = CreateTestDar([module]);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var identifiersFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("ContractIdentifiers.cs", StringComparison.Ordinal));

        // Assert
        identifiersFile.Should().NotBeNull();
        var code = identifiersFile!.Content;

        code.Should().Contain("#nullable enable");
    }

    [Fact]
    public void generate_should_include_required_usings()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "MyTemplate",
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = []
                }
            ],
            DataTypes = [],
            Interfaces = []
        };

        var dar = CreateTestDar([module]);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var identifiersFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("ContractIdentifiers.cs", StringComparison.Ordinal));

        // Assert
        identifiersFile.Should().NotBeNull();
        var code = identifiersFile!.Content;

        code.Should().Contain("using Daml.Runtime.Contracts;");
        code.Should().Contain("using static Daml.Runtime.Contracts.TemplateExtensions;");
    }

    [Fact]
    public void generate_should_use_file_scoped_namespace()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "MyTemplate",
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = []
                }
            ],
            DataTypes = [],
            Interfaces = []
        };

        var dar = CreateTestDar([module]);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var identifiersFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("ContractIdentifiers.cs", StringComparison.Ordinal));

        // Assert
        identifiersFile.Should().NotBeNull();
        var code = identifiersFile!.Content;

        code.Should().Contain("namespace Test.Package;");
    }

    [Fact]
    public void generate_should_include_xml_docs()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "MyTemplate",
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = []
                }
            ],
            DataTypes = [],
            Interfaces = []
        };

        var dar = CreateTestDar([module]);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var identifiersFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("ContractIdentifiers.cs", StringComparison.Ordinal));

        // Assert
        identifiersFile.Should().NotBeNull();
        var code = identifiersFile!.Content;

        code.Should().Contain("/// <summary>");
        code.Should().Contain("/// Provides fully qualified contract identifiers for all templates in this package.");
        code.Should().Contain("/// These identifiers can be used for PQS queries.");
    }

    [Fact]
    public void generate_should_create_static_class()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "MyTemplate",
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = []
                }
            ],
            DataTypes = [],
            Interfaces = []
        };

        var dar = CreateTestDar([module]);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var identifiersFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("ContractIdentifiers.cs", StringComparison.Ordinal));

        // Assert
        identifiersFile.Should().NotBeNull();
        var code = identifiersFile!.Content;

        code.Should().Contain("public static class ContractIdentifiers");
    }

    #endregion

    #region Option Tests

    [Fact]
    public void generate_should_not_create_file_when_option_disabled()
    {
        // Arrange
        var options = new CodeGenOptions
        {
            OutputDirectory = "/tmp/test",
            GenerateContractIdentifiers = false
        };

        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "MyTemplate",
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = []
                }
            ],
            DataTypes = [],
            Interfaces = []
        };

        var dar = CreateTestDar([module]);
        var generator = CreateGenerator(options);

        // Act
        var files = generator.Generate(dar);
        var identifiersFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("ContractIdentifiers.cs", StringComparison.Ordinal));

        // Assert
        identifiersFile.Should().BeNull();
    }

    [Fact]
    public void generate_should_not_create_file_when_no_templates()
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
                    Name = "SomeRecord",
                    Definition = new DamlRecordDefinition([new DamlField("value", new DamlPrimitiveType(DamlPrimitive.Text))])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar([module]);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var identifiersFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("ContractIdentifiers.cs", StringComparison.Ordinal));

        // Assert
        identifiersFile.Should().BeNull();
    }

    [Fact]
    public void generate_should_use_block_scoped_namespace_when_option_disabled()
    {
        // Arrange
        var options = new CodeGenOptions
        {
            OutputDirectory = "/tmp/test",
            UseFileScopedNamespaces = false,
            GenerateContractIdentifiers = true
        };

        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "MyTemplate",
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = []
                }
            ],
            DataTypes = [],
            Interfaces = []
        };

        var dar = CreateTestDar([module]);
        var generator = CreateGenerator(options);

        // Act
        var files = generator.Generate(dar);
        var identifiersFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("ContractIdentifiers.cs", StringComparison.Ordinal));

        // Assert
        identifiersFile.Should().NotBeNull();
        var code = identifiersFile!.Content;

        code.Should().Contain("namespace Test.Package\n{");
    }

    #endregion

    #region Filter Tests

    [Fact]
    public void generate_should_respect_root_filter()
    {
        // Arrange
        var options = new CodeGenOptions
        {
            OutputDirectory = "/tmp/test",
            RootFilter = ".*:IncludedTemplate",
            GenerateContractIdentifiers = true
        };

        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "IncludedTemplate",
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = []
                },
                new DamlTemplate
                {
                    Name = "ExcludedTemplate",
                    Fields = [new DamlField("value", new DamlPrimitiveType(DamlPrimitive.Text))],
                    Choices = []
                }
            ],
            DataTypes = [],
            Interfaces = []
        };

        var dar = CreateTestDar([module]);
        var generator = CreateGenerator(options);

        // Act
        var files = generator.Generate(dar);
        var identifiersFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("ContractIdentifiers.cs", StringComparison.Ordinal));

        // Assert
        identifiersFile.Should().NotBeNull();
        var code = identifiersFile!.Content;

        code.Should().Contain("IncludedTemplate");
        code.Should().NotContain("ExcludedTemplate");
    }

    #endregion
}
