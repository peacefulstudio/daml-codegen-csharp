using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.DarReader;
using FluentAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class CodeGenOptionsTests
{
    #region CodeGenOptions Defaults Tests
    [Fact]
    public void CodeGenOptions_should_have_correct_defaults()
    {
        // Arrange & Act
        var options = new CodeGenOptions
        {
            OutputDirectory = "/tmp/output"
        };

        // Assert
        options.GenerateJsonSupport.Should().BeTrue();
        options.EnableNullableReferenceTypes.Should().BeTrue();
        options.GenerateXmlDocs.Should().BeTrue();
        options.UseFileScopedNamespaces.Should().BeTrue();
        options.UseRecordTypes.Should().BeTrue();
        options.UsePrimaryConstructors.Should().BeTrue();
        options.Verbosity.Should().Be(1);
    }

    [Fact]
    public void CodeGenOptions_should_allow_customization()
    {
        // Arrange & Act
        var options = new CodeGenOptions
        {
            OutputDirectory = "/output",
            RootNamespace = "MyCompany.Contracts",
            RootFilter = ".*Iou.*",
            GenerateJsonSupport = false,
            EnableNullableReferenceTypes = false,
            Verbosity = 3
        };

        // Assert
        options.RootNamespace.Should().Be("MyCompany.Contracts");
        options.RootFilter.Should().Be(".*Iou.*");
        options.GenerateJsonSupport.Should().BeFalse();
        options.EnableNullableReferenceTypes.Should().BeFalse();
        options.Verbosity.Should().Be(3);
    }

    [Fact]
    public void CodeGenOptions_should_have_correct_new_option_defaults()
    {
        // Arrange & Act
        var options = new CodeGenOptions
        {
            OutputDirectory = "/tmp/output"
        };

        // Assert - new options should have correct defaults
        options.GenerateProjectFile.Should().BeFalse();
        options.IncludeDependencies.Should().BeFalse();
        options.TargetFramework.Should().Be("net10.0");
        options.RuntimePackageVersion.Should().BeNull();
    }

    #endregion

    #region CSharpCodeGenerator Integration Tests

    private static CSharpCodeGenerator CreateGenerator(CodeGenOptions options)
    {
        var logger = new ConsoleLogger(0); // Silent
        return new CSharpCodeGenerator(options, logger);
    }

    private static DamlModule CreateSimpleModule(string moduleName = "Test.Module", string templateName = "SimpleTemplate")
    {
        return new DamlModule
        {
            Name = moduleName,
            Templates =
            [
                new DamlTemplate
                {
                    Name = templateName,
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = []
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = templateName,
                    Definition = new DamlRecordDefinition([new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))])
                }
            ],
            Interfaces = []
        };
    }

    private static DarArchive CreateTestDar(DamlModule mainModule, List<DamlPackage>? dependencies = null)
    {
        var mainPackage = new DamlPackage
        {
            PackageId = "main-package-id",
            Name = "main-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [mainModule],
            DependencyReferences = []
        };

        return new DarArchive
        {
            MainPackage = mainPackage,
            Dependencies = dependencies ?? []
        };
    }

    #endregion

    #region GenerateProjectFile Option Tests

    [Fact]
    public void Generate_should_include_project_file_when_GenerateProjectFile_is_true()
    {
        // Arrange
        var options = new CodeGenOptions
        {
            OutputDirectory = "/tmp/test",
            GenerateProjectFile = true,
            TargetFramework = "net10.0"
        };
        var generator = CreateGenerator(options);
        var dar = CreateTestDar(CreateSimpleModule());

        // Act
        var files = generator.Generate(dar);

        // Assert
        var projectFile = files.FirstOrDefault(f => f.RelativePath.EndsWith(".csproj", StringComparison.Ordinal));
        projectFile.Should().NotBeNull();
        projectFile!.RelativePath.Should().Be("Main.Package.csproj");
        projectFile.Content.Should().Contain("<TargetFramework>net10.0</TargetFramework>");
    }

    [Fact]
    public void Generate_should_not_include_project_file_when_GenerateProjectFile_is_false()
    {
        // Arrange
        var options = new CodeGenOptions
        {
            OutputDirectory = "/tmp/test",
            GenerateProjectFile = false
        };
        var generator = CreateGenerator(options);
        var dar = CreateTestDar(CreateSimpleModule());

        // Act
        var files = generator.Generate(dar);

        // Assert
        var projectFile = files.FirstOrDefault(f => f.RelativePath.EndsWith(".csproj", StringComparison.Ordinal));
        projectFile.Should().BeNull();
    }

    #endregion

    #region IncludeDependencies Option Tests

    [Fact]
    public void Generate_should_include_dependency_code_when_IncludeDependencies_is_true()
    {
        // Arrange
        var depModule = CreateSimpleModule("Dep.Module", "DepTemplate");
        var depPackage = new DamlPackage
        {
            PackageId = "dep-package-id",
            Name = "dep-package",
            Version = new Version(2, 0, 0),
            LfVersion = "2.1",
            Modules = [depModule],
            DependencyReferences = []
        };

        var options = new CodeGenOptions
        {
            OutputDirectory = "/tmp/test",
            IncludeDependencies = true
        };
        var generator = CreateGenerator(options);
        var dar = CreateTestDar(CreateSimpleModule(), [depPackage]);

        // Act
        var files = generator.Generate(dar);

        // Assert
        var mainFile = files.FirstOrDefault(f => f.RelativePath.Contains("SimpleTemplate.cs", StringComparison.Ordinal));
        var depFile = files.FirstOrDefault(f => f.RelativePath.Contains("DepTemplate.cs", StringComparison.Ordinal));

        mainFile.Should().NotBeNull();
        depFile.Should().NotBeNull();
    }

    [Fact]
    public void Generate_should_not_include_dependency_code_when_IncludeDependencies_is_false()
    {
        // Arrange
        var depModule = CreateSimpleModule("Dep.Module", "DepTemplate");
        var depPackage = new DamlPackage
        {
            PackageId = "dep-package-id",
            Name = "dep-package",
            Version = new Version(2, 0, 0),
            LfVersion = "2.1",
            Modules = [depModule],
            DependencyReferences = []
        };

        var options = new CodeGenOptions
        {
            OutputDirectory = "/tmp/test",
            IncludeDependencies = false
        };
        var generator = CreateGenerator(options);
        var dar = CreateTestDar(CreateSimpleModule(), [depPackage]);

        // Act
        var files = generator.Generate(dar);

        // Assert
        var mainFile = files.FirstOrDefault(f => f.RelativePath.Contains("SimpleTemplate.cs", StringComparison.Ordinal));
        var depFile = files.FirstOrDefault(f => f.RelativePath.Contains("DepTemplate.cs", StringComparison.Ordinal));

        mainFile.Should().NotBeNull();
        depFile.Should().BeNull("dependency code should not be generated when IncludeDependencies is false");
    }

    [Fact]
    public void Generate_should_include_multiple_dependencies_when_IncludeDependencies_is_true()
    {
        // Arrange
        var dep1Module = CreateSimpleModule("Dep1.Module", "Dep1Template");
        var dep1Package = new DamlPackage
        {
            PackageId = "dep1-package-id",
            Name = "dep1-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [dep1Module],
            DependencyReferences = []
        };

        var dep2Module = CreateSimpleModule("Dep2.Module", "Dep2Template");
        var dep2Package = new DamlPackage
        {
            PackageId = "dep2-package-id",
            Name = "dep2-package",
            Version = new Version(2, 0, 0),
            LfVersion = "2.1",
            Modules = [dep2Module],
            DependencyReferences = []
        };

        var options = new CodeGenOptions
        {
            OutputDirectory = "/tmp/test",
            IncludeDependencies = true
        };
        var generator = CreateGenerator(options);
        var dar = CreateTestDar(CreateSimpleModule(), [dep1Package, dep2Package]);

        // Act
        var files = generator.Generate(dar);

        // Assert
        var mainFile = files.FirstOrDefault(f => f.RelativePath.Contains("SimpleTemplate.cs", StringComparison.Ordinal));
        var dep1File = files.FirstOrDefault(f => f.RelativePath.Contains("Dep1Template.cs", StringComparison.Ordinal));
        var dep2File = files.FirstOrDefault(f => f.RelativePath.Contains("Dep2Template.cs", StringComparison.Ordinal));

        mainFile.Should().NotBeNull();
        dep1File.Should().NotBeNull();
        dep2File.Should().NotBeNull();
    }

    #endregion

    #region Combined Options Tests

    [Fact]
    public void Generate_should_include_both_project_file_and_dependencies()
    {
        // Arrange
        var depModule = CreateSimpleModule("Dep.Module", "DepTemplate");
        var depPackage = new DamlPackage
        {
            PackageId = "dep-package-id",
            Name = "dep-package",
            Version = new Version(2, 0, 0),
            LfVersion = "2.1",
            Modules = [depModule],
            DependencyReferences = []
        };

        var options = new CodeGenOptions
        {
            OutputDirectory = "/tmp/test",
            GenerateProjectFile = true,
            IncludeDependencies = true,
            TargetFramework = "net10.0"
        };
        var generator = CreateGenerator(options);
        var dar = CreateTestDar(CreateSimpleModule(), [depPackage]);

        // Act
        var files = generator.Generate(dar);

        // Assert
        var projectFile = files.FirstOrDefault(f => f.RelativePath.EndsWith(".csproj", StringComparison.Ordinal));
        var mainFile = files.FirstOrDefault(f => f.RelativePath.Contains("SimpleTemplate.cs", StringComparison.Ordinal));
        var depFile = files.FirstOrDefault(f => f.RelativePath.Contains("DepTemplate.cs", StringComparison.Ordinal));

        projectFile.Should().NotBeNull();
        mainFile.Should().NotBeNull();
        depFile.Should().NotBeNull();
    }

    [Fact]
    public void Generate_should_use_runtime_version_in_project_file()
    {
        // Arrange
        var options = new CodeGenOptions
        {
            OutputDirectory = "/tmp/test",
            GenerateProjectFile = true,
            RuntimePackageVersion = "1.2.3"
        };
        var generator = CreateGenerator(options);
        var dar = CreateTestDar(CreateSimpleModule());

        // Act
        var files = generator.Generate(dar);

        // Assert
        var projectFile = files.FirstOrDefault(f => f.RelativePath.EndsWith(".csproj", StringComparison.Ordinal));
        projectFile.Should().NotBeNull();
        projectFile!.Content.Should().Contain("Version=\"1.2.3\"");
    }

    #endregion
}
