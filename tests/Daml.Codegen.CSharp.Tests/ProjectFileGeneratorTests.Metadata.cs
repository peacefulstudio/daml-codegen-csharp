// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public partial class ProjectFileGeneratorTests
{
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
        // Package name is converted to PascalCase namespace (my-package -> My.Package)
        file.RelativePath.Should().Be("My.Package.csproj");
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
        file.RelativePath.Should().Be("My.Cool.Package.Name.csproj");
        file.Content.Should().Contain("<PackageId>My.Cool.Package.Name</PackageId>");
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
        file.RelativePath.Should().Be("_123.Numeric.Package.csproj");
        file.Content.Should().Contain("<PackageId>_123.Numeric.Package</PackageId>");
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
    public void GenerateProjectFile_should_xml_escape_the_package_name_in_the_description()
    {
        var options = CreateOptions();
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my<package>&co",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var file = generator.GenerateProjectFile(package);

        file.Content.Should().Contain(
            "<Description>C# bindings for Daml package my&lt;package&gt;&amp;co</Description>",
            "a package name is foreign input and must be XML-escaped like the sibling fields");
    }

    [Fact]
    public void GenerateProjectFile_should_emit_apache_2_0_license_expression_by_default()
    {
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

        var file = generator.GenerateProjectFile(package);

        file.Content.Should().Contain("<PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>");
    }

    [Fact]
    public void GenerateProjectFile_should_use_configured_license_expression()
    {
        var options = new CodeGenOptions
        {
            TargetFramework = "net10.0",
            GenerateProjectFile = true,
            PackageLicenseExpression = "MIT",
        };
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

        var file = generator.GenerateProjectFile(package);

        file.Content.Should().Contain("<PackageLicenseExpression>MIT</PackageLicenseExpression>");
    }

    [Fact]
    public void GenerateProjectFile_should_xml_escape_license_expression()
    {
        var options = new CodeGenOptions
        {
            TargetFramework = "net10.0",
            GenerateProjectFile = true,
            PackageLicenseExpression = "MIT & Apache-2.0 <or> later",
        };
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

        var file = generator.GenerateProjectFile(package);

        file.Content.Should().Contain(
            "<PackageLicenseExpression>MIT &amp; Apache-2.0 &lt;or&gt; later</PackageLicenseExpression>",
            "user-supplied SPDX values flow into csproj XML and must be escaped to keep the generated project parseable");
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
    public void GenerateProjectFile_should_declare_the_packed_readme_file()
    {
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

        var file = generator.GenerateProjectFile(package);

        file.Content.Should().Contain("<PackageReadmeFile>README.md</PackageReadmeFile>");
        file.Content.Should().Contain("<None Include=\"README.md\" Pack=\"true\" PackagePath=\"\\\" />");
    }

    [Fact]
    public void GenerateProjectFile_should_declare_the_packed_icon_file()
    {
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

        var file = generator.GenerateProjectFile(package);

        file.Content.Should().Contain("<PackageIcon>icon.png</PackageIcon>");
        file.Content.Should().Contain("<None Include=\"icon.png\" Pack=\"true\" PackagePath=\"\\\" />");
    }

    [Fact]
    public void GenerateProjectFile_should_emit_the_configured_repository_urls()
    {
        var options = CreateOptions(repositoryUrl: "https://github.com/acme/widgets");
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

        var file = generator.GenerateProjectFile(package);

        file.Content.Should().Contain(
            "<PackageProjectUrl>https://github.com/acme/widgets</PackageProjectUrl>");
        file.Content.Should().Contain(
            "<RepositoryUrl>https://github.com/acme/widgets</RepositoryUrl>");
        file.Content.Should().Contain("<RepositoryType>git</RepositoryType>");
    }

    [Fact]
    public void GenerateProjectFile_should_xml_escape_the_repository_url()
    {
        var options = CreateOptions(repositoryUrl: "https://example.com/repo?a=1&b=<2>");
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

        var file = generator.GenerateProjectFile(package);

        file.Content.Should().Contain(
            "<RepositoryUrl>https://example.com/repo?a=1&amp;b=&lt;2&gt;</RepositoryUrl>",
            "a user-supplied repository URL flows into csproj element text and must be XML-escaped");
    }

    [Fact]
    public void GenerateProjectFile_should_omit_repository_urls_when_unset()
    {
        var options = CreateOptions(repositoryUrl: null);
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

        var file = generator.GenerateProjectFile(package);

        file.Content.Should().NotContain("<PackageProjectUrl>");
        file.Content.Should().NotContain("<RepositoryUrl>");
        file.Content.Should().NotContain("<RepositoryType>");
    }

    [Fact]
    public void GenerateProjectFile_should_include_package_tags_with_the_daml_package_name()
    {
        var options = CreateOptions();
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "splice-amulet",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var file = generator.GenerateProjectFile(package);

        file.Content.Should().Contain(
            "<PackageTags>daml;canton;codegen;generated;splice-amulet</PackageTags>");
    }
}
