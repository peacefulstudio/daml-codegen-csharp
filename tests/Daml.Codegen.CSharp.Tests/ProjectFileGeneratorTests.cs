using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.DarReader;
using FluentAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class ProjectFileGeneratorTests
{
    private static CodeGenOptions CreateOptions(
        string targetFramework = "net10.0",
        string? runtimeVersion = null,
        bool enableNullable = true)
    {
        return new CodeGenOptions
        {
            OutputDirectory = "/tmp/test",
            TargetFramework = targetFramework,
            RuntimePackageVersion = runtimeVersion,
            EnableNullableReferenceTypes = enableNullable,
            GenerateProjectFile = true
        };
    }

    [Fact]
    public void GenerateProjectFile_should_create_file_with_package_name()
    {
        // Arrange
        var options = CreateOptions();
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        // Act
        var file = generator.GenerateProjectFile(package);

        // Assert
        file.RelativePath.Should().Be("my.package.csproj");
    }

    [Fact]
    public void GenerateProjectFile_should_include_target_framework()
    {
        // Arrange
        var options = CreateOptions(targetFramework: "net9.0");
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        // Act
        var file = generator.GenerateProjectFile(package);

        // Assert
        file.Content.Should().Contain("<TargetFramework>net9.0</TargetFramework>");
    }

    [Fact]
    public void GenerateProjectFile_should_include_nullable_when_enabled()
    {
        // Arrange
        var options = CreateOptions(enableNullable: true);
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        // Act
        var file = generator.GenerateProjectFile(package);

        // Assert
        file.Content.Should().Contain("<Nullable>enable</Nullable>");
    }

    [Fact]
    public void GenerateProjectFile_should_not_include_nullable_when_disabled()
    {
        // Arrange
        var options = CreateOptions(enableNullable: false);
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        // Act
        var file = generator.GenerateProjectFile(package);

        // Assert
        file.Content.Should().NotContain("<Nullable>");
    }

    [Fact]
    public void GenerateProjectFile_should_include_package_version()
    {
        // Arrange
        var options = CreateOptions();
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(2, 3, 4),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        // Act
        var file = generator.GenerateProjectFile(package);

        // Assert
        file.Content.Should().Contain("<Version>2.3.4</Version>");
    }

    [Fact]
    public void GenerateProjectFile_should_include_runtime_package_reference()
    {
        // Arrange
        var options = CreateOptions(runtimeVersion: "1.2.3");
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        // Act
        var file = generator.GenerateProjectFile(package);

        // Assert
        file.Content.Should().Contain("<PackageReference Include=\"Daml.Codegen.CSharp.Runtime\" Version=\"1.2.3\" />");
    }

    [Fact]
    public void GenerateProjectFile_should_use_wildcard_version_when_runtime_version_not_specified()
    {
        // Arrange
        var options = CreateOptions(runtimeVersion: null);
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        // Act
        var file = generator.GenerateProjectFile(package);

        // Assert
        file.Content.Should().Contain("<PackageReference Include=\"Daml.Codegen.CSharp.Runtime\" Version=\"*\" />");
    }

    [Fact]
    public void GenerateProjectFile_should_include_dependency_references()
    {
        // Arrange
        var options = CreateOptions();
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences =
            [
                new DamlPackageReference
                {
                    PackageId = "dep-id-1",
                    Name = "my-dependency",
                    Version = new Version(2, 0, 0)
                }
            ]
        };

        // Act
        var file = generator.GenerateProjectFile(package);

        // Assert
        file.Content.Should().Contain("<PackageReference Include=\"my.dependency\" Version=\"2.0.0\" />");
    }

    [Fact]
    public void GenerateProjectFile_should_handle_dependency_without_name()
    {
        // Arrange
        var options = CreateOptions();
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences =
            [
                new DamlPackageReference
                {
                    PackageId = "abc123456789abcdef"
                }
            ]
        };

        // Act
        var file = generator.GenerateProjectFile(package);

        // Assert
        // Should include a comment with the unknown package ID
        file.Content.Should().Contain("<!-- Unknown dependency: abc123456789abcdef -->");
    }

    [Fact]
    public void GenerateProjectFile_should_sanitize_package_name_with_hyphens()
    {
        // Arrange
        var options = CreateOptions();
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-cool-package-name",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        // Act
        var file = generator.GenerateProjectFile(package);

        // Assert
        file.RelativePath.Should().Be("my.cool.package.name.csproj");
        file.Content.Should().Contain("<PackageId>my.cool.package.name</PackageId>");
    }

    [Fact]
    public void GenerateProjectFile_should_prefix_package_name_starting_with_digit()
    {
        // Arrange
        var options = CreateOptions();
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "123-numeric-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        // Act
        var file = generator.GenerateProjectFile(package);

        // Assert
        file.RelativePath.Should().Be("_123.numeric.package.csproj");
        file.Content.Should().Contain("<PackageId>_123.numeric.package</PackageId>");
    }

    [Fact]
    public void GenerateProjectFile_should_handle_dependency_without_version()
    {
        // Arrange
        var options = CreateOptions();
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences =
            [
                new DamlPackageReference
                {
                    PackageId = "dep-id",
                    Name = "some-dependency",
                    Version = null // No version specified
                }
            ]
        };

        // Act
        var file = generator.GenerateProjectFile(package);

        // Assert
        file.Content.Should().Contain("<PackageReference Include=\"some.dependency\" Version=\"*\" />");
    }

    [Fact]
    public void GenerateProjectFile_should_include_package_description()
    {
        // Arrange
        var options = CreateOptions();
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        // Act
        var file = generator.GenerateProjectFile(package);

        // Assert
        file.Content.Should().Contain("<Description>C# bindings for Daml package my-package</Description>");
    }

    [Fact]
    public void GenerateProjectFile_should_include_authors()
    {
        // Arrange
        var options = CreateOptions();
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        // Act
        var file = generator.GenerateProjectFile(package);

        // Assert
        file.Content.Should().Contain("<Authors>Generated by Daml.Codegen.CSharp</Authors>");
    }

    [Fact]
    public void GenerateProjectFile_should_include_implicit_usings()
    {
        // Arrange
        var options = CreateOptions();
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        // Act
        var file = generator.GenerateProjectFile(package);

        // Assert
        file.Content.Should().Contain("<ImplicitUsings>enable</ImplicitUsings>");
    }

    [Fact]
    public void GenerateProjectFile_should_truncate_long_package_id_in_comment()
    {
        // Arrange
        var options = CreateOptions();
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences =
            [
                new DamlPackageReference
                {
                    PackageId = "abc123def456ghi789jkl012mno345pqr678stu901vwx234yz",
                    Name = "long-id-dependency",
                    Version = new Version(1, 0, 0)
                }
            ]
        };

        // Act
        var file = generator.GenerateProjectFile(package);

        // Assert
        // Package ID should be truncated to first 16 characters in the comment
        file.Content.Should().Contain("<!-- Package: long-id-dependency (abc123def456ghi7) -->");
    }

    [Fact]
    public void GenerateProjectFile_should_handle_multiple_dependencies_with_mixed_resolution()
    {
        // Arrange
        var options = CreateOptions();
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences =
            [
                new DamlPackageReference
                {
                    PackageId = "dep1-id",
                    Name = "known-dep",
                    Version = new Version(2, 0, 0)
                },
                new DamlPackageReference
                {
                    PackageId = "unknown123456789"
                    // No name or version - unresolved
                },
                new DamlPackageReference
                {
                    PackageId = "dep3-id",
                    Name = "another-known-dep",
                    Version = new Version(3, 0, 0)
                }
            ]
        };

        // Act
        var file = generator.GenerateProjectFile(package);

        // Assert
        file.Content.Should().Contain("<PackageReference Include=\"known.dep\" Version=\"2.0.0\" />");
        file.Content.Should().Contain("<!-- Unknown dependency: unknown123456789 -->");
        file.Content.Should().Contain("<PackageReference Include=\"another.known.dep\" Version=\"3.0.0\" />");
    }
}
