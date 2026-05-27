using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using Daml.Codegen.DarParser;
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
        // Package name is converted to PascalCase namespace (my-package -> My.Package)
        file.RelativePath.Should().Be("My.Package.csproj");
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
        file.Content.Should().Contain("<PackageReference Include=\"Daml.Runtime\" Version=\"1.2.3\" />");
        file.Content.Should().NotContain("Daml.Codegen.CSharp.Runtime");
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
        file.Content.Should().Contain("<PackageReference Include=\"Daml.Runtime\" Version=\"*\" />");
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
            DependencyReferences = []
        };
        var externalReferences = new List<DamlPackage>
        {
            new()
            {
                PackageId = "dep-id-1",
                Name = "my-dependency",
                Version = new Version(2, 0, 0),
                LfVersion = "2.1",
                Modules = [],
                DependencyReferences = []
            }
        };

        // Act
        var file = generator.GenerateProjectFile(package, externalReferences);

        // Assert
        file.Content.Should().Contain("<PackageReference Include=\"My.Dependency\" Version=\"2.0.0\" />");
    }

    // Removed test "should_handle_dependency_without_name": no longer applicable.
    // ProjectFileGenerator now receives a list of resolved DamlPackage instances; the
    // codegen filters out unknown/unresolved package ids before calling it. The case
    // a comment-only fallback used to handle simply cannot reach this layer anymore.

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

    // Removed test "should_handle_dependency_without_version": no longer applicable.
    // External references are passed as concrete DamlPackage instances whose Version is
    // non-nullable, so the version-missing case cannot arise.

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

    // Removed test "should_truncate_long_package_id_in_comment": no longer applicable.
    // The new generator does not emit per-dependency comments; it only emits the
    // PackageReference itself. Package id truncation/preview lived only in those comments.

    [Fact]
    public void GenerateProjectFile_should_include_ledger_abstractions_package_reference()
    {
        // Generated code's <Choice>Async extensions reference
        // Daml.Ledger.Abstractions.ILedgerClient. The csproj must declare that
        // package as a NuGet reference alongside Daml.Runtime so consumer
        // builds resolve the type. Emitted unconditionally — pure-projector
        // consumers absorb the reference at zero transitive cost (interface-
        // only package).
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

        file.Content.Should().Contain("<PackageReference Include=\"Daml.Ledger.Abstractions\"");
    }

    [Fact]
    public void GenerateProjectFile_should_emit_one_PackageReference_per_external_dependency()
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
        var externalReferences = new List<DamlPackage>
        {
            new()
            {
                PackageId = "dep1-id",
                Name = "known-dep",
                Version = new Version(2, 0, 0),
                LfVersion = "2.1",
                Modules = [],
                DependencyReferences = []
            },
            new()
            {
                PackageId = "dep3-id",
                Name = "another-known-dep",
                Version = new Version(3, 0, 0),
                LfVersion = "2.1",
                Modules = [],
                DependencyReferences = []
            }
        };

        // Act
        var file = generator.GenerateProjectFile(package, externalReferences);

        // Assert
        file.Content.Should().Contain("<PackageReference Include=\"Another.Known.Dep\" Version=\"3.0.0\" />");
        file.Content.Should().Contain("<PackageReference Include=\"Known.Dep\" Version=\"2.0.0\" />");
    }

    [Fact]
    public void GenerateProjectFile_should_pin_LangVersion_13_when_emitted_files_contain_partial_property()
    {
        // Pinning LangVersion=13 is load-bearing for `--generate-project` builds
        // whose emission contains the partial-property syntax: the codegen's
        // partial-property `Key` accessor requires C# 13 to parse. Without this
        // pin, `--target-framework net8.0` builds would fail on a syntax error
        // before reaching the intentional CS9248 missing-implementation
        // diagnostic.
        //
        // The decision is anchored to the EMITTED file set rather than the
        // package's templates, so a key-bearing template added via
        // `IncludeDependencies` still pins LangVersion correctly — see
        // daml-codegen-csharp#65 round-5 review.
        var options = CreateOptions();
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "any-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = [],
        };
        var emittedFiles = new[]
        {
            new GeneratedFile(
                RelativePath: "Foo.cs",
                Content: "namespace Test; public sealed partial record Foo { public partial string Key { get; } }"),
        };

        var file = generator.GenerateProjectFile(package, externalReferences: null, emittedFiles: emittedFiles);

        file.Content.Should().Contain("<LangVersion>13</LangVersion>");
    }

    [Fact]
    public void GenerateProjectFile_should_not_pin_LangVersion_when_emitted_files_contain_no_partial_property()
    {
        // The pin is opt-in based on actually needing C# 13 syntax. Key-less
        // emissions don't contain partial-property syntax and so shouldn't have
        // their SDK floor raised — they continue to build with whatever
        // LangVersion the consumer's project / SDK defaults supply. This also
        // covers the `RootFilter` case: a keyed template that's filtered out of
        // emission must not force the pin.
        var options = CreateOptions();
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "any-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = [],
        };
        var emittedFiles = new[]
        {
            new GeneratedFile(
                RelativePath: "Foo.cs",
                Content: "namespace Test; public sealed partial record Foo(Party Owner) : ITemplate;"),
        };

        var file = generator.GenerateProjectFile(package, externalReferences: null, emittedFiles: emittedFiles);

        file.Content.Should().NotContain("<LangVersion>");
    }

    [Fact]
    public void GenerateProjectFile_should_pin_LangVersion_when_emittedFiles_omitted_but_package_has_key_bearing_template()
    {
        // Back-compat for older callers that pass only `package` (no emission
        // set). Without this fallback, an old caller with a key-bearing package
        // would silently produce a `.csproj` lacking <LangVersion>13</> and the
        // build would fail on a syntax error in the emitted partial property.
        // The fallback is less precise than the emission-set scan (it can't see
        // IncludeDependencies or RootFilter) but is functionally safe for the
        // simple case.
        var options = CreateOptions();
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "keyed-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules =
            [
                new DamlModule
                {
                    Name = "Test.Module",
                    Templates =
                    [
                        new DamlTemplate
                        {
                            Name = "KeyedTemplate",
                            Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                            Choices = [],
                            Key = new DamlPrimitiveType(DamlPrimitive.Text),
                        },
                    ],
                    DataTypes = [],
                    Interfaces = [],
                },
            ],
            DependencyReferences = [],
        };

        var file = generator.GenerateProjectFile(package);

        file.Content.Should().Contain("<LangVersion>13</LangVersion>");
    }

    [Fact]
    public void GenerateProjectFile_should_not_pin_LangVersion_when_emittedFiles_omitted_and_package_has_no_key_bearing_template()
    {
        // The back-compat fallback only triggers when the package itself has
        // key-bearing templates. A key-less package with no emission set
        // continues to build without a LangVersion pin (consumer SDK defaults).
        var options = CreateOptions();
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "keyless-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = [],
        };

        var file = generator.GenerateProjectFile(package);

        file.Content.Should().NotContain("<LangVersion>");
    }
}
