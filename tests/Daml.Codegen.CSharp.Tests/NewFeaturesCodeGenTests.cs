using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using Daml.Codegen.DarParser;
using FluentAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Tests for new code generation features: Contract Keys, Interfaces, Generic Types, and Package Upgrades.
/// </summary>
public class NewFeaturesCodeGenTests
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

    private static DarArchive CreateTestDar(
        DamlModule module,
        string packageName = "test-package",
        string? upgradedPackageId = null)
    {
        var package = new DamlPackage
        {
            PackageId = "test-package-id",
            Name = packageName,
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
            UpgradedPackageId = upgradedPackageId
        };

        return new DarArchive
        {
            MainPackage = package,
            Dependencies = []
        };
    }

    #region Contract Keys Tests

    [Fact]
    public void Generate_should_implement_IHasKey_when_template_has_primitive_key()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "AssetWithKey",
                    Fields =
                    [
                        new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlField("assetId", new DamlPrimitiveType(DamlPrimitive.Text))
                    ],
                    Choices = [],
                    Key = new DamlPrimitiveType(DamlPrimitive.Text)
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "AssetWithKey",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlField("assetId", new DamlPrimitiveType(DamlPrimitive.Text))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var assetFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("AssetWithKey.cs", StringComparison.Ordinal));

        // Assert
        assetFile.Should().NotBeNull();
        var code = assetFile!.Content;

        code.Should().Contain("IHasKey<string>");
        // Codegen emits the Key property as a body-less `partial` declaration so
        // consuming projects fill in the body until DALF key-expression analysis
        // lands (daml-codegen-csharp#64). The literal `{ get; }` shape (no setter,
        // no body) is load-bearing — consumers' hand-rolled implementing partial
        // mates against this exact signature. See WriteKeyProperty in
        // CSharpCodeGenerator.
        code.Should().Contain("public partial string Key { get; }");
        // The whole point of the PR: no throwing body. A regression that emits
        // both a partial and a throwing fallback (or removes `partial` and
        // restores the old throw) must fail this assertion.
        code.Should().NotContain("NotImplementedException");
        code.Should().NotContain("throw new ");
    }

    [Fact]
    public void Generate_should_implement_IHasKey_when_template_has_party_key()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "UserProfile",
                    Fields =
                    [
                        new DamlField("user", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlField("name", new DamlPrimitiveType(DamlPrimitive.Text))
                    ],
                    Choices = [],
                    Key = new DamlPrimitiveType(DamlPrimitive.Party)
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "UserProfile",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("user", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlField("name", new DamlPrimitiveType(DamlPrimitive.Text))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var profileFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("UserProfile.cs", StringComparison.Ordinal));

        // Assert
        profileFile.Should().NotBeNull();
        var code = profileFile!.Content;

        // Party maps to Party, so key should be Party
        code.Should().Contain("IHasKey<global::Daml.Runtime.Data.Party>");
        code.Should().Contain("public partial global::Daml.Runtime.Data.Party Key { get; }");
        code.Should().NotContain("NotImplementedException");
    }

    [Fact]
    public void Generate_should_implement_IHasKey_when_template_has_record_type_key()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "CompositeKeyTemplate",
                    Fields =
                    [
                        new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlField("assetId", new DamlPrimitiveType(DamlPrimitive.Text))
                    ],
                    Choices = [],
                    Key = new DamlTypeRef("", "Test.Module", "AssetKey")
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "CompositeKeyTemplate",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlField("assetId", new DamlPrimitiveType(DamlPrimitive.Text))
                    ])
                },
                new DamlDataType
                {
                    Name = "AssetKey",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlField("assetId", new DamlPrimitiveType(DamlPrimitive.Text))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var templateFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("CompositeKeyTemplate.cs", StringComparison.Ordinal));

        // Assert
        templateFile.Should().NotBeNull();
        var code = templateFile!.Content;

        code.Should().Contain("IHasKey<AssetKey>");
        code.Should().Contain("public partial AssetKey Key { get; }");
        code.Should().NotContain("NotImplementedException");
    }

    [Fact]
    public void Generate_should_emit_valid_cref_when_key_type_contains_angle_brackets()
    {
        // Pins the cref-escape transform on a key type whose mapped C# form
        // contains generic angle brackets. `[Text]` (List Text) maps to
        // `IReadOnlyList<string>`, and cref-attribute syntax requires the angle
        // brackets be rendered as `{ }` — without the escape, the emitted XML
        // doc reads `cref="...IHasKey<IReadOnlyList<string>>"/>` which is
        // malformed XML and breaks consumer builds with
        // <GenerateDocumentationFile>true</> + TreatWarningsAsErrors.
        // The string-key test above doesn't exercise this path because `string`
        // contains no angle brackets to escape.
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "ListKeyTemplate",
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = [],
                    Key = new DamlTypeApp(
                        new DamlPrimitiveType(DamlPrimitive.List),
                        [new DamlPrimitiveType(DamlPrimitive.Text)]),
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "ListKeyTemplate",
                    Definition = new DamlRecordDefinition([new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces = [],
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        var files = generator.Generate(dar);
        var templateFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("ListKeyTemplate.cs", StringComparison.Ordinal));

        templateFile.Should().NotBeNull();
        var code = templateFile!.Content;

        // Property signature uses real C# generic syntax.
        code.Should().Contain("public partial IReadOnlyList<string> Key { get; }");
        // cref must use cref-escape `{ }` instead of `< >` — both nested levels.
        code.Should().Contain("/// Gets the contract key, satisfying <see cref=\"global::Daml.Runtime.Contracts.IHasKey{IReadOnlyList{string}}\"/>");
        // Belt-and-braces: the malformed unescaped form must not appear in the
        // doc. Using a regex anchored to the `cref="` prefix so the structural
        // angle brackets in the property signature don't trip the assertion.
        code.Should().NotMatchRegex(@"cref=""[^""]*<[^""]*""");
    }

    [Fact]
    public void Generate_should_not_implement_IHasKey_when_template_has_no_key()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "NoKeyTemplate",
                    Fields =
                    [
                        new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))
                    ],
                    Choices = [],
                    Key = null
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "NoKeyTemplate",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var templateFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("NoKeyTemplate.cs", StringComparison.Ordinal));

        // Assert
        templateFile.Should().NotBeNull();
        var code = templateFile!.Content;

        code.Should().NotContain("IHasKey");
        // Belt-and-braces: no Key emission of any shape for a key-less template.
        // The `IHasKey` check is the load-bearing one; this catches a Key
        // emission of ANY type form — generic (`IReadOnlyList<string>`),
        // nullable (`string?`), qualified (`Foo.Bar`), or bare identifier —
        // followed by the standalone `Key` token (so `IHasKey<...>` in the
        // type declaration doesn't trip a false positive). The character class
        // excludes `{`, `;`, `=` so the match can't span across the type
        // declaration's own opening brace and into the property body.
        code.Should().NotMatchRegex(@"\bpartial\s+[^;{=]+?\s+Key\b");
    }

    [Fact]
    public void Generate_should_include_key_documentation()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "DocumentedKey",
                    Fields =
                    [
                        new DamlField("id", new DamlPrimitiveType(DamlPrimitive.Text))
                    ],
                    Choices = [],
                    Key = new DamlPrimitiveType(DamlPrimitive.Text)
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "DocumentedKey",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("id", new DamlPrimitiveType(DamlPrimitive.Text))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var templateFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("DocumentedKey.cs", StringComparison.Ordinal));

        // Assert
        templateFile.Should().NotBeNull();
        var code = templateFile!.Content;

        // Multi-line XML doc emitted by the new partial-Key shape. Assert on the
        // doc-comment line itself (with the `///` prefix and the `<see cref>` to
        // the closed-generic IHasKey<string>): a bare `Contain("IHasKey")` would
        // pass from the type declaration `: IHasKey<string>` even if the doc
        // breadcrumb were dropped. The cref uses cref-attribute syntax (`{string}`
        // instead of `<string>`) so it survives <GenerateDocumentationFile> on
        // consumer projects without a CS1574 warning.
        code.Should().Contain("/// Gets the contract key, satisfying <see cref=\"global::Daml.Runtime.Contracts.IHasKey{string}\"/>");
        // Doc must point at the tracking issue so the deferred-work pointer
        // doesn't silently rot — this is the only in-source signal explaining
        // why the property has no body.
        code.Should().Contain("daml-codegen-csharp#64");
    }

    #endregion

    #region Interface Code Generation Tests

    [Fact]
    public void Generate_should_create_interface_file_with_I_prefix()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes = [],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = "Transferable",
                    Choices = [],
                    ViewType = null
                }
            ]
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var interfaceFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("ITransferable.cs", StringComparison.Ordinal));

        // Assert
        interfaceFile.Should().NotBeNull();
        var code = interfaceFile!.Content;

        code.Should().Contain("public interface ITransferable");
    }

    [Fact]
    public void Generate_should_implement_IDamlInterface()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes = [],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = "Lockable",
                    Choices = [],
                    ViewType = null
                }
            ]
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var interfaceFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("ILockable.cs", StringComparison.Ordinal));

        // Assert
        interfaceFile.Should().NotBeNull();
        var code = interfaceFile!.Content;

        code.Should().Contain(": IDamlInterface");
    }

    [Fact]
    public void Generate_should_include_interface_metadata()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes = [],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = "Holdable",
                    Choices = [],
                    ViewType = null
                }
            ]
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var interfaceFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("IHoldable.cs", StringComparison.Ordinal));

        // Assert
        interfaceFile.Should().NotBeNull();
        var code = interfaceFile!.Content;

        code.Should().Contain("static Identifier IDamlInterface.InterfaceId =>");
        code.Should().Contain("\"test-package-id\"");
        code.Should().Contain("\"Test.Module\"");
        code.Should().Contain("\"Holdable\"");
        code.Should().Contain("static string IDamlInterface.PackageId =>");
        code.Should().Contain("static string IDamlInterface.PackageName =>");
        code.Should().Contain("static Version IDamlInterface.PackageVersion =>");
    }

    [Fact]
    public void Generate_should_implement_IHasView_when_interface_has_view_type()
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
                    Name = "AssetView",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlField("amount", new DamlPrimitiveType(DamlPrimitive.Numeric))
                    ])
                }
            ],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = "Asset",
                    Choices = [],
                    ViewType = new DamlTypeRef("", "Test.Module", "AssetView")
                }
            ]
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var interfaceFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("IAsset.cs", StringComparison.Ordinal));

        // Assert
        interfaceFile.Should().NotBeNull();
        var code = interfaceFile!.Content;

        code.Should().Contain("IHasView<AssetView>");
    }

    [Fact]
    public void Generate_should_include_interface_methods_as_comments()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes = [],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = "Transferable",
                    Choices =                     [
                        new DamlChoice
                        {
                            Name = "Transfer",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Party),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit)
                        }
                    ],
                    ViewType = null
                }
            ]
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var interfaceFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("ITransferable.cs", StringComparison.Ordinal));

        // Assert
        interfaceFile.Should().NotBeNull();
        var code = interfaceFile!.Content;

        code.Should().Contain("// Choice Transfer");
    }

    [Fact]
    public void Generate_should_include_interface_xml_docs()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes = [],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = "Documented",
                    Choices = [],
                    ViewType = null
                }
            ]
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var interfaceFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("IDocumented.cs", StringComparison.Ordinal));

        // Assert
        interfaceFile.Should().NotBeNull();
        var code = interfaceFile!.Content;

        code.Should().Contain("/// <summary>");
        code.Should().Contain("/// Generated from Daml interface Test.Module:Documented");
        code.Should().Contain("/// </summary>");
    }

    [Fact]
    public void Generate_should_filter_interfaces_with_root_filter()
    {
        // Arrange
        var options = new CodeGenOptions
        {
            OutputDirectory = "/tmp/test",
            GenerateJsonSupport = true,
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            UseRecordTypes = true,
            UsePrimaryConstructors = true,
            RootFilter = "Test\\.Module:Include.*"
        };

        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes = [],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = "IncludeMe",
                    Choices = [],
                    ViewType = null
                },
                new DamlInterface
                {
                    Name = "ExcludeMe",
                    Choices = [],
                    ViewType = null
                }
            ]
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator(options);

        // Act
        var files = generator.Generate(dar);

        // Assert
        var interfaceFiles = files.Where(f => f.RelativePath.Contains("IIncludeMe") || f.RelativePath.Contains("IExcludeMe")).ToList();
        interfaceFiles.Should().HaveCount(1);
        interfaceFiles[0].RelativePath.Should().Contain("IIncludeMe");
    }

    #endregion

    #region Generic Types Tests

    [Fact]
    public void Generate_should_create_generic_record_with_type_parameters()
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
                    Name = "Container",
                    TypeParams = ["a"],
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("value", new DamlTypeVar("a"))
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

        code.Should().Contain("public sealed record Container<TA>");
        code.Should().Contain("TA Value");
    }

    [Fact]
    public void Generate_should_create_generic_record_with_multiple_type_parameters()
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
                    Name = "Pair",
                    TypeParams = ["a", "b"],
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("first", new DamlTypeVar("a")),
                        new DamlField("second", new DamlTypeVar("b"))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var pairFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Pair.cs", StringComparison.Ordinal));

        // Assert
        pairFile.Should().NotBeNull();
        var code = pairFile!.Content;

        code.Should().Contain("public sealed record Pair<TA, TB>");
        code.Should().Contain("TA First");
        code.Should().Contain("TB Second");
    }

    [Fact]
    public void Generate_should_create_generic_variant_with_type_parameters()
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
                    Name = "Either",
                    TypeParams = ["a", "b"],
                    Definition = new DamlVariantDefinition(
                    [
                        new DamlVariantConstructor("Left", new DamlTypeVar("a")),
                        new DamlVariantConstructor("Right", new DamlTypeVar("b"))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var eitherFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Either.cs", StringComparison.Ordinal));

        // Assert
        eitherFile.Should().NotBeNull();
        var code = eitherFile!.Content;

        code.Should().Contain("public abstract record Either<TA, TB>");
        code.Should().Contain("public sealed record Left(TA Value) : Either<TA, TB>");
        code.Should().Contain("public sealed record Right(TB Value) : Either<TA, TB>");
    }

    [Fact]
    public void Generate_should_include_typeparam_documentation_for_generic_types()
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
                    Name = "Box",
                    TypeParams = ["t"],
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("contents", new DamlTypeVar("t"))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var boxFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Box.cs", StringComparison.Ordinal));

        // Assert
        boxFile.Should().NotBeNull();
        var code = boxFile!.Content;

        code.Should().Contain("/// <typeparam name=\"TT\">Type parameter t</typeparam>");
    }

    [Fact]
    public void Generate_should_handle_record_without_type_parameters()
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
                    Name = "NonGeneric",
                    TypeParams = [], // Empty type params
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("value", new DamlPrimitiveType(DamlPrimitive.Text))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var nonGenericFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("NonGeneric.cs", StringComparison.Ordinal));

        // Assert
        nonGenericFile.Should().NotBeNull();
        var code = nonGenericFile!.Content;

        code.Should().Contain("public sealed record NonGeneric(");
        code.Should().NotContain("NonGeneric<");
    }

    [Fact]
    public void Generate_should_sanitize_type_parameter_names()
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
                    Name = "Wrapper",
                    TypeParams = ["some_type", "another-type"],
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("val1", new DamlTypeVar("some_type")),
                        new DamlField("val2", new DamlTypeVar("another-type"))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var wrapperFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Wrapper.cs", StringComparison.Ordinal));

        // Assert
        wrapperFile.Should().NotBeNull();
        var code = wrapperFile!.Content;

        code.Should().Contain("TSomeType");
        code.Should().Contain("TAnotherType");
    }

    #endregion

    #region Package Upgrades Tests

    [Fact]
    public void Generate_should_implement_IUpgradeable_when_package_has_upgraded_package_id()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "UpgradedTemplate",
                    Fields =
                    [
                        new DamlField("value", new DamlPrimitiveType(DamlPrimitive.Text))
                    ],
                    Choices = []
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "UpgradedTemplate",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("value", new DamlPrimitiveType(DamlPrimitive.Text))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module, upgradedPackageId: "previous-package-id-12345");
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var templateFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("UpgradedTemplate.cs", StringComparison.Ordinal));

        // Assert
        templateFile.Should().NotBeNull();
        var code = templateFile!.Content;

        code.Should().Contain("IUpgradeable");
        code.Should().Contain("public static string? UpgradedPackageId => \"previous-package-id-12345\";");
    }

    [Fact]
    public void Generate_should_not_implement_IUpgradeable_when_no_upgraded_package()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "NormalTemplate",
                    Fields =
                    [
                        new DamlField("value", new DamlPrimitiveType(DamlPrimitive.Text))
                    ],
                    Choices = []
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "NormalTemplate",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("value", new DamlPrimitiveType(DamlPrimitive.Text))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module, upgradedPackageId: null);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var templateFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("NormalTemplate.cs", StringComparison.Ordinal));

        // Assert
        templateFile.Should().NotBeNull();
        var code = templateFile!.Content;

        code.Should().NotContain("IUpgradeable");
        code.Should().NotContain("UpgradedPackageId");
    }

    [Fact]
    public void Generate_should_include_upgraded_package_id_documentation()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "DocumentedUpgrade",
                    Fields =
                    [
                        new DamlField("value", new DamlPrimitiveType(DamlPrimitive.Text))
                    ],
                    Choices = []
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "DocumentedUpgrade",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("value", new DamlPrimitiveType(DamlPrimitive.Text))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module, upgradedPackageId: "old-package-id");
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var templateFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("DocumentedUpgrade.cs", StringComparison.Ordinal));

        // Assert
        templateFile.Should().NotBeNull();
        var code = templateFile!.Content;

        code.Should().Contain("/// <summary>Gets the package ID that this package upgrades.</summary>");
    }

    [Fact]
    public void Generate_should_implement_both_IHasKey_and_IUpgradeable_when_applicable()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "FullFeaturedTemplate",
                    Fields =
                    [
                        new DamlField("id", new DamlPrimitiveType(DamlPrimitive.Text)),
                        new DamlField("value", new DamlPrimitiveType(DamlPrimitive.Numeric))
                    ],
                    Choices = [],
                    Key = new DamlPrimitiveType(DamlPrimitive.Text)
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "FullFeaturedTemplate",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("id", new DamlPrimitiveType(DamlPrimitive.Text)),
                        new DamlField("value", new DamlPrimitiveType(DamlPrimitive.Numeric))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module, upgradedPackageId: "previous-version-id");
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var templateFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("FullFeaturedTemplate.cs", StringComparison.Ordinal));

        // Assert
        templateFile.Should().NotBeNull();
        var code = templateFile!.Content;

        // Should have all three interfaces
        code.Should().Contain("ITemplate");
        code.Should().Contain("IHasKey<string>");
        code.Should().Contain("IUpgradeable");
    }

    #endregion

    #region Combined Features Tests

    [Fact]
    public void Generate_should_handle_module_with_templates_interfaces_and_data_types()
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
                    Fields =
                    [
                        new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlField("amount", new DamlPrimitiveType(DamlPrimitive.Numeric))
                    ],
                    Choices = [],
                    Key = new DamlPrimitiveType(DamlPrimitive.Party)
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
                        new DamlField("amount", new DamlPrimitiveType(DamlPrimitive.Numeric))
                    ])
                },
                new DamlDataType
                {
                    Name = "Wrapper",
                    TypeParams = ["t"],
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("value", new DamlTypeVar("t"))
                    ])
                }
            ],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = "Transferable",
                    Choices = [],
                    ViewType = null
                }
            ]
        };

        var dar = CreateTestDar(module, upgradedPackageId: "old-pkg-id");
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);

        // Assert
        files.Should().HaveCountGreaterThan(2);

        // Template should exist with key and upgrade support
        var assetFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Asset.cs", StringComparison.Ordinal));
        assetFile.Should().NotBeNull();
        assetFile!.Content.Should().Contain("IHasKey<global::Daml.Runtime.Data.Party>");
        assetFile.Content.Should().Contain("IUpgradeable");

        // Generic data type should exist
        var wrapperFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Wrapper.cs", StringComparison.Ordinal));
        wrapperFile.Should().NotBeNull();
        wrapperFile!.Content.Should().Contain("Wrapper<TT>");

        // Interface should exist
        var interfaceFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("ITransferable.cs", StringComparison.Ordinal));
        interfaceFile.Should().NotBeNull();
        interfaceFile!.Content.Should().Contain("public interface ITransferable");
    }

    #endregion
}
