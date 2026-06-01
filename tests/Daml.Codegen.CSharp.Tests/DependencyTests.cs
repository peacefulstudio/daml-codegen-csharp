using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using Daml.Codegen.DarParser;
using FluentAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class DependencyTests
{
    #region DamlPackageReference Tests

    [Fact]
    public void DamlPackageReference_should_store_package_id()
    {
        // Arrange & Act
        var reference = new DamlPackageReference
        {
            PackageId = "abc123def456"
        };

        // Assert
        reference.PackageId.Should().Be("abc123def456");
        reference.Name.Should().BeNull();
        reference.Version.Should().BeNull();
    }

    [Fact]
    public void DamlPackageReference_should_allow_setting_name_and_version()
    {
        // Arrange
        var reference = new DamlPackageReference
        {
            PackageId = "abc123def456"
        };

        // Act
        reference.Name = "my-dependency";
        reference.Version = new Version(1, 2, 3);

        // Assert
        reference.Name.Should().Be("my-dependency");
        reference.Version.Should().Be(new Version(1, 2, 3));
    }

    #endregion

    #region DamlPackage DependencyReferences Tests

    [Fact]
    public void DamlPackage_should_store_dependency_references()
    {
        // Arrange
        var dependencies = new List<DamlPackageReference>
        {
            new() { PackageId = "dep1" },
            new() { PackageId = "dep2" }
        };

        // Act
        var package = new DamlPackage
        {
            PackageId = "main-package",
            Name = "main",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = dependencies
        };

        // Assert
        package.DependencyReferences.Should().HaveCount(2);
        package.DependencyReferences[0].PackageId.Should().Be("dep1");
        package.DependencyReferences[1].PackageId.Should().Be("dep2");
    }

    #endregion

    #region DarArchive GetPackageById Tests

    [Fact]
    public void DarArchive_GetPackageById_should_find_main_package()
    {
        // Arrange
        var mainPackage = new DamlPackage
        {
            PackageId = "main-pkg-id",
            Name = "main",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var dar = new DarArchive
        {
            MainPackage = mainPackage,
            Dependencies = []
        };

        // Act
        var found = dar.GetPackageById("main-pkg-id");

        // Assert
        found.Should().Be(mainPackage);
    }

    [Fact]
    public void DarArchive_GetPackageById_should_find_dependency_package()
    {
        // Arrange
        var mainPackage = new DamlPackage
        {
            PackageId = "main-pkg-id",
            Name = "main",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var depPackage = new DamlPackage
        {
            PackageId = "dep-pkg-id",
            Name = "dependency",
            Version = new Version(2, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var dar = new DarArchive
        {
            MainPackage = mainPackage,
            Dependencies = [depPackage]
        };

        // Act
        var found = dar.GetPackageById("dep-pkg-id");

        // Assert
        found.Should().Be(depPackage);
    }

    [Fact]
    public void DarArchive_GetPackageById_should_return_null_for_unknown_id()
    {
        // Arrange
        var mainPackage = new DamlPackage
        {
            PackageId = "main-pkg-id",
            Name = "main",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var dar = new DarArchive
        {
            MainPackage = mainPackage,
            Dependencies = []
        };

        // Act
        var found = dar.GetPackageById("unknown-id");

        // Assert
        found.Should().BeNull();
    }

    #endregion

    #region DarArchive ResolveDependencyReferences Tests

    [Fact]
    public void DarArchive_ResolveDependencyReferences_should_populate_name_and_version()
    {
        // Arrange
        var depPackage = new DamlPackage
        {
            PackageId = "dep-pkg-id",
            Name = "my-dependency",
            Version = new Version(2, 5, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var mainPackage = new DamlPackage
        {
            PackageId = "main-pkg-id",
            Name = "main",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = [new DamlPackageReference { PackageId = "dep-pkg-id" }]
        };

        var dar = new DarArchive
        {
            MainPackage = mainPackage,
            Dependencies = [depPackage]
        };

        // Act
        dar.ResolveDependencyReferences(mainPackage);

        // Assert
        mainPackage.DependencyReferences[0].Name.Should().Be("my-dependency");
        mainPackage.DependencyReferences[0].Version.Should().Be(new Version(2, 5, 0));
    }

    [Fact]
    public void DarArchive_ResolveDependencyReferences_should_handle_missing_dependency()
    {
        // Arrange
        var mainPackage = new DamlPackage
        {
            PackageId = "main-pkg-id",
            Name = "main",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = [new DamlPackageReference { PackageId = "unknown-dep-id" }]
        };

        var dar = new DarArchive
        {
            MainPackage = mainPackage,
            Dependencies = []
        };

        // Act
        dar.ResolveDependencyReferences(mainPackage);

        // Assert - should remain null for unresolved dependencies
        mainPackage.DependencyReferences[0].Name.Should().BeNull();
        mainPackage.DependencyReferences[0].Version.Should().BeNull();
    }

    #endregion
}
