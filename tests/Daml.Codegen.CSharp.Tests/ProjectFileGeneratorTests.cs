// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using System.Reflection;
using FluentAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class ProjectFileGeneratorTests
{
    private static CodeGenOptions CreateOptions(
        string targetFramework = "net10.0",
        string? runtimeVersion = null,
        bool enableNullable = true,
        string? repositoryUrl = null,
        string? versionSuffix = null)
    {
        return new CodeGenOptions
        {
            TargetFramework = targetFramework,
            RuntimePackageVersion = runtimeVersion,
            EnableNullableReferenceTypes = enableNullable,
            GenerateProjectFile = true,
            RepositoryUrl = repositoryUrl,
            VersionSuffix = versionSuffix
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
    public void GenerateProjectFile_should_emit_4_part_version_with_r0_by_default()
    {
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

        var file = generator.GenerateProjectFile(package);

        file.Content.Should().Contain("<Version>2.3.4.0</Version>");
    }

    [Fact]
    public void GenerateProjectFile_should_throw_on_negative_EmitterCounter()
    {
        var options = new CodeGenOptions
        {
            TargetFramework = "net10.0",
            GenerateProjectFile = true,
            EmitterCounter = -1,
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

        var act = () => generator.GenerateProjectFile(package);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*EmitterCounter*M.m.p.r*");
    }

    [Fact]
    public void GenerateProjectFile_should_throw_on_two_part_dar_version()
    {
        var options = CreateOptions();
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(1, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var act = () => generator.GenerateProjectFile(package);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*3-part*M.m.p.r*");
    }

    [Fact]
    public void GenerateProjectFile_should_use_EmitterCounter_as_4th_version_segment()
    {
        var options = new CodeGenOptions
        {
            TargetFramework = "net10.0",
            GenerateProjectFile = true,
            EmitterCounter = 7,
        };
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(0, 1, 17),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var file = generator.GenerateProjectFile(package);

        file.Content.Should().Contain("<Version>0.1.17.7</Version>");
    }

    [Fact]
    public void GenerateProjectFile_should_append_version_suffix_to_package_version_only()
    {
        var options = new CodeGenOptions
        {
            TargetFramework = "net10.0",
            GenerateProjectFile = true,
            RuntimePackageVersion = "1.2.3",
            EmitterCounter = 5,
            VersionSuffix = "preview.2",
        };
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "my-package",
            Version = new Version(0, 1, 6),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var file = generator.GenerateProjectFile(package);

        file.Content.Should().Contain("<Version>0.1.6.5-preview.2</Version>");
        file.Content.Should().Contain("<PackageReference Include=\"Daml.Runtime\" Version=\"1.2.3\" />");
        file.Content.Should().NotContain("Version=\"1.2.3-preview.2\"");
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
    public void GenerateProjectFile_should_default_runtime_reference_to_the_emitter_lockstep_version()
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
        var lockstepVersion = typeof(ProjectFileGenerator).Assembly
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()!
            .InformationalVersion.Split('+')[0];
        file.Content.Should().Contain(
            $"<PackageReference Include=\"Daml.Runtime\" Version=\"{lockstepVersion}\" />",
            "with --runtime-version omitted, the generated project must pin the runtime that is lockstep-versioned with this emitter");
        lockstepVersion.Should().NotContain("+",
            "NuGet version ranges reject SemVer build metadata, so the source-revision suffix must be stripped");
        lockstepVersion.Should().MatchRegex(
            @"^\d+\.\d+\.\d+(\.\d+)?(-[0-9A-Za-z][0-9A-Za-z.-]*)?$",
            "the emitted reference must parse as a NuGet package version");
        file.Content.Should().NotContain("Version=\"*\"",
            "a floating * matches stable versions only and fails restore when the runtime ships as a prerelease");
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

    [Fact]
    public void GenerateProjectFile_should_not_emit_leading_dot_package_id_for_leading_hyphen_dependency_name()
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
        var externalReferences = new List<DamlPackage>
        {
            new()
            {
                PackageId = "lf1x-prim-id",
                Name = "-no-package-metadata",
                Version = new Version(0, 0, 0),
                LfVersion = "1.6",
                Modules = [],
                DependencyReferences = []
            }
        };

        var file = generator.GenerateProjectFile(package, externalReferences);

        file.Content.Should().Contain("<PackageReference Include=\"No.Package.Metadata\" Version=\"0.0.0\" />");
        file.Content.Should().NotContain("Include=\".No.Package.Metadata");
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
        // `IncludeDependencies` still pins LangVersion correctly.
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
            GeneratedFile.Text(
                "Foo.cs",
                "namespace Test; public sealed partial record Foo { public partial string Key { get; } }"),
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
            GeneratedFile.Text(
                "Foo.cs",
                "namespace Test; public sealed partial record Foo(Party Owner) : ITemplate;"),
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
                            Fields = [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
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

    [Fact]
    public void GenerateProjectFile_should_xml_escape_target_framework_in_element_text()
    {
        var options = new CodeGenOptions
        {
            TargetFramework = "net10.0 & <evil>",
            GenerateProjectFile = true,
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
            "<TargetFramework>net10.0 &amp; &lt;evil&gt;</TargetFramework>",
            "user-supplied target framework flows into csproj element text and must be XML-escaped");
    }

    [Fact]
    public void GenerateProjectFile_should_xml_escape_runtime_version_in_attribute_value()
    {
        var options = new CodeGenOptions
        {
            TargetFramework = "net10.0",
            RuntimePackageVersion = "1.0.0\"injected",
            GenerateProjectFile = true,
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
            "Version=\"1.0.0&quot;injected\"",
            "user-supplied runtime version flows into a csproj attribute and embedded quotes must be escaped");
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

    [Fact]
    public void GenerateReadme_should_be_emitted_at_the_project_root()
    {
        var options = CreateOptions();
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "splice-amulet",
            Version = new Version(1, 2, 3),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var file = generator.GenerateReadme(package);

        file.RelativePath.Should().Be("README.md");
    }

    [Fact]
    public void GenerateIcon_should_return_the_png_icon_bytes_at_the_project_root()
    {
        var options = CreateOptions();
        var generator = new ProjectFileGenerator(options);

        var file = generator.GenerateIcon();

        file.RelativePath.Should().Be("icon.png");
        file.BinaryContent.Should().NotBeNullOrEmpty();
        file.BinaryContent!.Take(4).Should().Equal(
            new byte[] { 0x89, 0x50, 0x4E, 0x47 },
            "the emitted icon must be a real PNG (magic bytes 0x89 'P' 'N' 'G')");
    }

    [Fact]
    public void GenerateReadme_should_contain_the_package_id_daml_name_and_license()
    {
        var options = new CodeGenOptions
        {
            TargetFramework = "net10.0",
            GenerateProjectFile = true,
            RuntimePackageVersion = "1.2.3",
            PackageLicenseExpression = "MIT",
        };
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "splice-amulet",
            Version = new Version(1, 2, 3),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var file = generator.GenerateReadme(package);

        file.Content.Should().Contain("# Splice.Amulet");
        file.Content.Should().Contain("`splice-amulet`");
        file.Content.Should().Contain("1.2.3");
        file.Content.Should().Contain("MIT");
        file.Content.Should().Contain("https://github.com/peacefulstudio/daml-codegen-csharp");
    }

    [Fact]
    public void GenerateReadme_should_show_the_runtime_row_with_the_lockstep_fallback_when_runtime_version_is_null()
    {
        var options = CreateOptions(runtimeVersion: null);
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "splice-amulet",
            Version = new Version(1, 2, 3),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var file = generator.GenerateReadme(package);

        var lockstepVersion = typeof(ProjectFileGenerator).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion.Split('+')[0];
        file.Content.Should().Contain(
            $"| Runtime dependency  | `Daml.Runtime` `{lockstepVersion}` |",
            "with --runtime-version omitted the README must document the same lockstep runtime the csproj pins");
    }

    [Fact]
    public void GenerateReadme_should_add_prerelease_flag_to_install_hint_for_a_prerelease_package()
    {
        var options = CreateOptions(versionSuffix: "preview.2");
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "splice-amulet",
            Version = new Version(1, 2, 3),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var file = generator.GenerateReadme(package);

        file.Content.Should().Contain("dotnet add package Splice.Amulet --prerelease");
    }

    [Fact]
    public void GenerateReadme_should_not_add_prerelease_flag_to_install_hint_for_a_stable_package()
    {
        var options = CreateOptions(versionSuffix: null);
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "splice-amulet",
            Version = new Version(1, 2, 3),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var file = generator.GenerateReadme(package);

        file.Content.Should().Contain("dotnet add package Splice.Amulet");
        file.Content.Should().NotContain("--prerelease");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://github.com/acme/widgets")]
    public void GenerateReadme_should_keep_the_generator_attribution_link_regardless_of_repository_url(string? repositoryUrl)
    {
        var options = CreateOptions(repositoryUrl: repositoryUrl);
        var generator = new ProjectFileGenerator(options);
        var package = new DamlPackage
        {
            PackageId = "test-id",
            Name = "splice-amulet",
            Version = new Version(1, 2, 3),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        var file = generator.GenerateReadme(package);

        file.Content.Should().Contain(
            "[Daml.Codegen.CSharp]: https://github.com/peacefulstudio/daml-codegen-csharp",
            "the attribution link identifies the generator tool, not the consumer's repository");
    }
}
