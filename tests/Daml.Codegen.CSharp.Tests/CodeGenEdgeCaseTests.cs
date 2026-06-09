// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using Daml.Codegen.DarParser;
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

    private static DamlPackage CreateTestPackage(string packageId, string packageName, params DamlModule[] modules) =>
        new()
        {
            PackageId = packageId,
            Name = packageName,
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = modules,
            DependencyReferences = []
        };

    private static DarArchive CreateMultiPackageDar(DamlPackage main, params DamlPackage[] dependencies) =>
        new()
        {
            MainPackage = main,
            Dependencies = dependencies
        };

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

        // Complex types decode element-by-element through the field-conversion helper.
        // The previous expectation `IReadOnlyList<string>.FromRecord(...)` was a known
        // codegen bug — IReadOnlyList<T> has no FromRecord, so it never compiled.
        code.Should().Contain("Choice<Contract, DamlUnit, IReadOnlyList<string>>");
        code.Should().Contain("ResultDecoder = val => (IReadOnlyList<string>)val.As<DamlList>().Values.Select(x => x.As<DamlText>().Value).ToList()");
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
        otherTypeFile!.Content.Should().Contain(": IDamlRecord");
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
        code.Should().Contain("As<DamlDate>().Value");
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
        code.Should().Contain("As<DamlTimestamp>().Value");
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
        code.Should().Contain("new ContractId<Asset>(");
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
        code.Should().Contain("public sealed record EmptyRecord : IDamlRecord");
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

    #region Interface Placeholder Tests

    // Daml-LF emits a same-named empty record for every `interface I where ...`
    // declaration. Those records are the phantom type parameter for `ContractId I`,
    // and the codegen emits them as `: ITemplate` with throwing static metadata so
    // `ContractId<I>` keeps satisfying the runtime's `where T : ITemplate` constraint
    // while loudly failing any caller that tries to read template metadata directly.
    // See WriteInterfacePlaceholderRecord in CSharpCodeGenerator for the rationale.

    [Fact]
    public void Generate_should_emit_interface_placeholder_record_as_ITemplate_with_throwing_stubs()
    {
        // Arrange — declare an interface and the LF placeholder record that always
        // accompanies it.
        var module = new DamlModule
        {
            Name = "Test.Holding",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Holding",
                    Definition = new DamlRecordDefinition([])
                }
            ],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = "Holding",
                    Choices = [],
                    ViewType = null
                }
            ]
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var holdingRecord = files.FirstOrDefault(f =>
            f.RelativePath.EndsWith("Holding.cs", StringComparison.Ordinal)
            && !f.RelativePath.Contains("IHolding", StringComparison.Ordinal));

        // Assert
        holdingRecord.Should().NotBeNull("the LF placeholder record should be emitted alongside the interface");
        var code = holdingRecord!.Content;

        // Sealed record implementing ITemplate (NOT just IDamlRecord)
        code.Should().Contain("public sealed record Holding : ITemplate");
        // Throwing static metadata — InvalidOperationException with the qualified Daml name in the message
        code.Should().Contain("public static Identifier TemplateId =>");
        code.Should().Contain("throw new InvalidOperationException(\"'Holding' is the C# placeholder for the Daml interface 'Test.Holding:Holding'");
        code.Should().Contain("public static string PackageId =>");
        code.Should().Contain("public static string PackageName =>");
        code.Should().Contain("public static Version PackageVersion =>");
        // Empty ToRecord/FromRecord — placeholders carry no data
        code.Should().Contain("public DamlRecord ToRecord() => DamlRecord.Create();");
        code.Should().Contain("public static Holding FromRecord(DamlRecord record) => new Holding();");
    }

    [Fact]
    public void Generate_should_emit_regular_record_when_no_matching_interface_in_same_module()
    {
        // Arrange — record name matches an interface in a DIFFERENT module; should
        // NOT be treated as a placeholder.
        var module = new DamlModule
        {
            Name = "Test.Records",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Holding",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("amount", new DamlPrimitiveType(DamlPrimitive.Numeric))
                    ])
                }
            ],
            Interfaces = []  // No interface in *this* module — the simple-name match in
                             // some other module must not leak in.
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var holdingFile = files.FirstOrDefault(f =>
            f.RelativePath.EndsWith("Holding.cs", StringComparison.Ordinal));

        // Assert
        holdingFile.Should().NotBeNull();
        var code = holdingFile!.Content;
        code.Should().Contain("public sealed record Holding(decimal Amount) : IDamlRecord");
        code.Should().NotContain(": ITemplate");
        code.Should().NotContain("InvalidOperationException");
    }

    [Fact]
    public void Generate_should_distinguish_interface_placeholders_across_modules_with_same_name()
    {
        // Arrange — module A has an interface `Token` (so the same-named record is a
        // placeholder); module B has an unrelated record `Token`. Each module must be
        // emitted with its own treatment.
        var modA = new DamlModule
        {
            Name = "App.A",
            Templates = [],
            DataTypes =
            [
                new DamlDataType { Name = "Token", Definition = new DamlRecordDefinition([]) }
            ],
            Interfaces =
            [
                new DamlInterface { Name = "Token", Choices = [], ViewType = null }
            ]
        };
        var modB = new DamlModule
        {
            Name = "App.B",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Token",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("symbol", new DamlPrimitiveType(DamlPrimitive.Text))
                    ])
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
            Modules = [modA, modB],
            DependencyReferences = []
        };
        var dar = new DarArchive { MainPackage = package, Dependencies = [] };
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar).ToList();
        // The codegen flattens all modules into a single namespace derived from the
        // package name, so file paths collide on simple names. Both `Token.cs` files
        // share a path; the latter overwrites the former. Verify by content instead:
        // the two Token records produce different bodies, and only one of them
        // becomes the placeholder in the emitted set.
        var tokenFiles = files.Where(f => f.RelativePath.EndsWith("Token.cs", StringComparison.Ordinal)).ToList();

        // Assert — both Token records exist (last-wins file path is acceptable here:
        // the regular record keeps its IDamlRecord shape, and the placeholder keeps
        // its ITemplate shape, with their qualifying logic running independently).
        tokenFiles.Should().NotBeEmpty();
        var hasPlaceholder = tokenFiles.Any(f => f.Content.Contains("public sealed record Token : ITemplate", StringComparison.Ordinal));
        var hasRegular = tokenFiles.Any(f => f.Content.Contains("public sealed record Token(string Symbol) : IDamlRecord", StringComparison.Ordinal));
        hasPlaceholder.Should().BeTrue("module A's Token must be emitted as the interface placeholder");
        hasRegular.Should().BeTrue("module B's Token must keep its IDamlRecord regular-record shape");
    }

    // -------------------------------------------------------------------
    // Interface choice extension method tests — for every Daml interface
    // choice, codegen now emits a typed `<Choice>Async`-style helper on
    // `ContractId<I>` so consumers can do `await cid.TransferAsync(arg)`
    // without naming the concrete template. The generated extension class
    // sits beside the interface declaration in the same file.
    // -------------------------------------------------------------------

    [Fact]
    public void Generate_should_emit_extension_class_for_interface_choices()
    {
        // Arrange — interface with one record-argument choice and one Unit choice.
        // Both shapes are common: Splice's IHolding has both styles.
        var module = new DamlModule
        {
            Name = "Test.Holding",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Holding",
                    Definition = new DamlRecordDefinition([])
                },
                new DamlDataType
                {
                    Name = "Transfer",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("amount", new DamlPrimitiveType(DamlPrimitive.Numeric))
                    ])
                },
                new DamlDataType
                {
                    Name = "Transfer_Result",
                    Definition = new DamlRecordDefinition([])
                }
            ],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = "Holding",
                    ViewType = null,
                    Choices =                     [
                        new DamlChoice
                        {
                            Name = "Transfer",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef("", "Test.Holding", "Transfer"),
                            ReturnType = new DamlTypeRef("", "Test.Holding", "Transfer_Result")
                        },
                        new DamlChoice
                        {
                            Name = "Lock",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit)
                        }
                    ]
                }
            ]
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var ifaceFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("IHolding.cs", StringComparison.Ordinal));

        // Assert — the file contains both the interface declaration AND a
        // sibling static extensions class with one method per choice.
        ifaceFile.Should().NotBeNull();
        var code = ifaceFile!.Content;

        // Marker-typed interface declaration is unchanged
        code.Should().Contain("public interface IHolding : IDamlInterface");

        // Sibling extensions class with one method per choice
        code.Should().Contain("public static class IHoldingExtensions");

        // Record-argument choice: async signature returning ExerciseOutcome<TransactionResult>
        // (mirrors the concrete-template <Choice>Async shape from #77). Interface choices
        // surface the raw ExerciseOutcome<TransactionResult> because the implementing
        // template — and therefore any typed <Choice>Result projection — is unknown at
        // the call site.
        code.Should().Contain("public static async Task<ExerciseOutcome<TransactionResult>> TransferAsync(");
        code.Should().Contain("this ContractId<IHolding> contractId,");
        code.Should().Contain("ILedgerClient client,");
        code.Should().Contain("Transfer argument,");
        code.Should().Contain("Party actAs,");
        // Internally builds the command via the runtime ForInterface helper — the
        // wire-level template_id slot carries IHolding.InterfaceId, and the choice
        // argument is serialised via argument.ToRecord().
        code.Should().Contain("ExerciseCommand.ForInterface<IHolding>(contractId, new ChoiceName(\"Transfer\"), argument.ToRecord())");
        // Submission is funnelled through ILedgerClient.TrySubmitAndWaitForTransactionAsync
        // — same submission path as concrete-template <Choice>Async.
        code.Should().Contain("await client.TrySubmitAndWaitForTransactionAsync(submission, cancellationToken)");

        // Unit-argument choice: no `argument` parameter, DamlUnit.Instance is passed
        code.Should().Contain("public static async Task<ExerciseOutcome<TransactionResult>> LockAsync(");
        code.Should().Contain("ExerciseCommand.ForInterface<IHolding>(contractId, new ChoiceName(\"Lock\"), DamlUnit.Instance)");
    }

    [Fact]
    public void Generate_should_skip_extension_class_when_interface_has_no_methods()
    {
        // Arrange — view-only interface with no choices. No exerciser methods to
        // emit, so the extension class is suppressed (avoids an empty static
        // class littering the namespace).
        var module = new DamlModule
        {
            Name = "Test.Marker",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Marker",
                    Definition = new DamlRecordDefinition([])
                }
            ],
            Interfaces =
            [
                new DamlInterface { Name = "Marker", Choices = [], ViewType = null }
            ]
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var ifaceFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("IMarker.cs", StringComparison.Ordinal));

        // Assert
        ifaceFile.Should().NotBeNull();
        ifaceFile!.Content.Should().Contain("public interface IMarker : IDamlInterface");
        ifaceFile.Content.Should().NotContain("IMarkerExtensions");
    }

    #endregion

    #region Cross-Package Type Reference Tests

    // Cross-DAR type refs are the headline of the spike: a generated record can
    // reference types defined in a different DAR shipped in the same archive (e.g.
    // splice-api-token-holding-v1's HoldingView references
    // splice-api-token-metadata-v1's Metadata). The codegen needs to (a) emit a
    // fully qualified C# name, (b) record the foreign package id so a
    // <PackageReference> ends up in the generated csproj. These tests pin both
    // halves down independent of any specific Splice version.

    [Fact]
    public void Generate_should_emit_fully_qualified_name_for_foreign_package_type_ref()
    {
        // Arrange — main package references a record defined in a foreign package.
        const string ForeignPackageId = "foreign-pkg-id";

        var foreignModule = new DamlModule
        {
            Name = "Foreign.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Meta",
                    Definition = new DamlRecordDefinition([new DamlField("note", new DamlPrimitiveType(DamlPrimitive.Text))])
                }
            ],
            Interfaces = []
        };
        var foreignPkg = CreateTestPackage(ForeignPackageId, "foreign-pkg", foreignModule);

        var mainModule = new DamlModule
        {
            Name = "Main.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Wrapper",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("meta", new DamlTypeRef(ForeignPackageId, "Foreign.Module", "Meta"))
                    ])
                }
            ],
            Interfaces = []
        };
        var mainPkg = CreateTestPackage("main-pkg-id", "main-pkg", mainModule);
        var dar = CreateMultiPackageDar(mainPkg, foreignPkg);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar).ToList();
        var wrapper = files.FirstOrDefault(f => f.RelativePath.EndsWith("Wrapper.cs", StringComparison.Ordinal));

        // Assert — generated field type uses the foreign package's namespace
        wrapper.Should().NotBeNull();
        wrapper!.Content.Should().Contain("Foreign.Pkg.Meta Meta");
        // FromRecord uses the same fully qualified name
        wrapper.Content.Should().Contain("Foreign.Pkg.Meta.FromRecord(record.GetRequiredField(\"meta\").As<DamlRecord>())");
    }

    [Fact]
    public void Generate_should_emit_PackageReference_for_each_foreign_package_referenced()
    {
        // Arrange — two foreign packages, both referenced by fields of the main type.
        var foreignA = CreateTestPackage(
            "foreign-a-id",
            "foreign-a",
            new DamlModule
            {
                Name = "A.Module",
                Templates = [],
                DataTypes = [new DamlDataType { Name = "AType", Definition = new DamlRecordDefinition([]) }],
                Interfaces = []
            });
        var foreignB = CreateTestPackage(
            "foreign-b-id",
            "foreign-b",
            new DamlModule
            {
                Name = "B.Module",
                Templates = [],
                DataTypes = [new DamlDataType { Name = "BType", Definition = new DamlRecordDefinition([]) }],
                Interfaces = []
            });

        var mainModule = new DamlModule
        {
            Name = "Main.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Owner",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("a", new DamlTypeRef("foreign-a-id", "A.Module", "AType")),
                        new DamlField("b", new DamlTypeRef("foreign-b-id", "B.Module", "BType"))
                    ])
                }
            ],
            Interfaces = []
        };
        var mainPkg = CreateTestPackage("main-pkg-id", "main-pkg", mainModule);
        var dar = CreateMultiPackageDar(mainPkg, foreignA, foreignB);

        var options = new CodeGenOptions
        {
            OutputDirectory = "/tmp/test",
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            UseRecordTypes = true,
            UsePrimaryConstructors = true,
            GenerateXmlDocs = true,
            GenerateProjectFile = true
        };
        var generator = CreateGenerator(options);

        // Act
        var files = generator.Generate(dar).ToList();
        var csproj = files.FirstOrDefault(f => f.RelativePath.EndsWith(".csproj", StringComparison.Ordinal));

        // Assert
        csproj.Should().NotBeNull();
        csproj!.Content.Should().Contain("<PackageReference Include=\"Foreign.A\" Version=\"1.0.0\" />");
        csproj.Content.Should().Contain("<PackageReference Include=\"Foreign.B\" Version=\"1.0.0\" />");
    }

    [Fact]
    public void Generate_should_route_stdlib_RelTime_through_Daml_Runtime_Stdlib_namespace()
    {
        // Arrange — daml-stdlib-DA-Time-Types is a stdlib package whose `RelTime`
        // type the codegen maps to the hand-coded Daml.Runtime.Stdlib.RelTime.
        // Stdlib refs must NOT produce a <PackageReference> on the generated csproj.
        const string StdlibTimePackageId = "stdlib-time-id";

        var stdlibModule = new DamlModule
        {
            Name = "DA.Time.Types",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "RelTime",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("microseconds", new DamlPrimitiveType(DamlPrimitive.Int64))
                    ])
                }
            ],
            Interfaces = []
        };
        var stdlibPkg = CreateTestPackage(StdlibTimePackageId, "daml-stdlib-DA-Time-Types", stdlibModule);

        var mainModule = new DamlModule
        {
            Name = "App.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Timer",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("d", new DamlTypeRef(StdlibTimePackageId, "DA.Time.Types", "RelTime"))
                    ])
                }
            ],
            Interfaces = []
        };
        var mainPkg = CreateTestPackage("main-pkg-id", "main-pkg", mainModule);
        var dar = CreateMultiPackageDar(mainPkg, stdlibPkg);

        var options = new CodeGenOptions
        {
            OutputDirectory = "/tmp/test",
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            UseRecordTypes = true,
            UsePrimaryConstructors = true,
            GenerateXmlDocs = true,
            GenerateProjectFile = true
        };
        var generator = CreateGenerator(options);

        // Act
        var files = generator.Generate(dar).ToList();
        var timer = files.FirstOrDefault(f => f.RelativePath.EndsWith("Timer.cs", StringComparison.Ordinal));
        var csproj = files.FirstOrDefault(f => f.RelativePath.EndsWith(".csproj", StringComparison.Ordinal));

        // Assert
        timer.Should().NotBeNull();
        timer!.Content.Should().Contain("using Daml.Runtime.Stdlib;");
        timer.Content.Should().Contain("RelTime D");
        timer.Content.Should().Contain("RelTime.FromRecord");
        // No <PackageReference> for stdlib packages — they're served by the runtime stub.
        csproj.Should().NotBeNull();
        csproj!.Content.Should().NotContain("Daml.Stdlib");
        csproj.Content.Should().NotContain("daml-stdlib");
    }

    [Fact]
    public void Generate_should_route_stdlib_Tuple2_through_Daml_Runtime_Stdlib_namespace()
    {
        // Arrange — Tuple2 lives in daml-prim's DA.Types module. The codegen must
        // route it to Daml.Runtime.Stdlib.Tuple2 with the parameterised arguments
        // preserved, and emit delegate-based ToRecord / FromRecord.
        const string DamlPrimPackageId = "daml-prim-id";

        var stdlibModule = new DamlModule
        {
            Name = "DA.Types",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Tuple2",
                    TypeParams = ["a", "b"],
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("_1", new DamlTypeVar("a")),
                        new DamlField("_2", new DamlTypeVar("b"))
                    ])
                }
            ],
            Interfaces = []
        };
        var stdlibPkg = CreateTestPackage(DamlPrimPackageId, "daml-prim-DA-Types", stdlibModule);

        var mainModule = new DamlModule
        {
            Name = "App.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Pair",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("p", new DamlTypeApp(
                            new DamlTypeRef(DamlPrimPackageId, "DA.Types", "Tuple2"),
                            [
                                new DamlPrimitiveType(DamlPrimitive.Int64),
                                new DamlPrimitiveType(DamlPrimitive.Text)
                            ]))
                    ])
                }
            ],
            Interfaces = []
        };
        var mainPkg = CreateTestPackage("main-pkg-id", "main-pkg", mainModule);
        var dar = CreateMultiPackageDar(mainPkg, stdlibPkg);

        var options = new CodeGenOptions
        {
            OutputDirectory = "/tmp/test",
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            UseRecordTypes = true,
            UsePrimaryConstructors = true,
            GenerateXmlDocs = true,
            GenerateProjectFile = false
        };
        var generator = CreateGenerator(options);

        // Act
        var files = generator.Generate(dar).ToList();
        var pair = files.FirstOrDefault(f => f.RelativePath.EndsWith("Pair.cs", StringComparison.Ordinal));

        // Assert — type uses the stdlib name, FromRecord uses delegate-based decoder.
        pair.Should().NotBeNull();
        pair!.Content.Should().Contain("using Daml.Runtime.Stdlib;");
        pair.Content.Should().Contain("Tuple2<long, string>");
        pair.Content.Should().Contain("Tuple2<long, string>.FromRecord(");
    }

    [Fact]
    public void Generate_should_route_stdlib_Set_through_Daml_Runtime_Stdlib_namespace()
    {
        // Arrange — Set lives in daml-stdlib's DA.Set.Types module. Wire shape is
        // a record wrapping `Map k ()`, exposed as Daml.Runtime.Stdlib.Set<k>.
        const string SetPackageId = "stdlib-set-types";

        var stdlibModule = new DamlModule
        {
            Name = "DA.Set.Types",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Set",
                    TypeParams = ["k"],
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("map", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.GenMap),
                            [new DamlTypeVar("k"), new DamlPrimitiveType(DamlPrimitive.Unit)]))
                    ])
                }
            ],
            Interfaces = []
        };
        var stdlibPkg = CreateTestPackage(SetPackageId, "daml-stdlib-DA-Set-Types", stdlibModule);

        var mainModule = new DamlModule
        {
            Name = "App.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Roster",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("members", new DamlTypeApp(
                            new DamlTypeRef(SetPackageId, "DA.Set.Types", "Set"),
                            [new DamlPrimitiveType(DamlPrimitive.Party)]))
                    ])
                }
            ],
            Interfaces = []
        };
        var mainPkg = CreateTestPackage("main-pkg-id", "main-pkg", mainModule);
        var dar = CreateMultiPackageDar(mainPkg, stdlibPkg);

        var options = new CodeGenOptions
        {
            OutputDirectory = "/tmp/test",
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            UseRecordTypes = true,
            UsePrimaryConstructors = true,
            GenerateXmlDocs = true,
            GenerateProjectFile = false
        };
        var generator = CreateGenerator(options);

        // Act
        var files = generator.Generate(dar).ToList();
        var roster = files.FirstOrDefault(f => f.RelativePath.EndsWith("Roster.cs", StringComparison.Ordinal));

        // Assert
        roster.Should().NotBeNull();
        roster!.Content.Should().Contain("using Daml.Runtime.Stdlib;");
        roster.Content.Should().Contain("Set<Party>");
        roster.Content.Should().Contain("Set<Party>.FromRecord(");
    }

    [Fact]
    public void Generate_should_not_route_user_defined_DA_Types_Tuple2_through_Daml_Runtime_Stdlib()
    {
        const string UserPackageId = "user-pkg-id";

        var userTuplesModule = new DamlModule
        {
            Name = "DA.Types",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Tuple2",
                    TypeParams = ["a", "b"],
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("_1", new DamlTypeVar("a")),
                        new DamlField("_2", new DamlTypeVar("b"))
                    ])
                }
            ],
            Interfaces = []
        };
        var userTuplesPkg = CreateTestPackage(UserPackageId, "my-cheeky-package", userTuplesModule);

        var mainModule = new DamlModule
        {
            Name = "App.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Pair",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("p", new DamlTypeApp(
                            new DamlTypeRef(UserPackageId, "DA.Types", "Tuple2"),
                            [
                                new DamlPrimitiveType(DamlPrimitive.Int64),
                                new DamlPrimitiveType(DamlPrimitive.Text)
                            ]))
                    ])
                }
            ],
            Interfaces = []
        };
        var mainPkg = CreateTestPackage("main-pkg-id", "main-pkg", mainModule);
        var dar = CreateMultiPackageDar(mainPkg, userTuplesPkg);

        var options = new CodeGenOptions
        {
            OutputDirectory = "/tmp/test",
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            UseRecordTypes = true,
            UsePrimaryConstructors = true,
            GenerateXmlDocs = true,
            GenerateProjectFile = false
        };
        var generator = CreateGenerator(options);

        var files = generator.Generate(dar).ToList();
        var pair = files.FirstOrDefault(f => f.RelativePath.EndsWith("Pair.cs", StringComparison.Ordinal));

        pair.Should().NotBeNull();
        pair!.Content.Should().NotContain("Daml.Runtime.Stdlib.Tuple2");
        pair.Content.Should().NotContain("using Daml.Runtime.Stdlib;");
    }

    #endregion

    #region Module-Qualified Enum Dispatch Tests

    // Daml lets the same simple type name appear in multiple modules within one
    // package. The codegen used to look up enum-ness by simple name only; that
    // misrouted any time a record and an enum shared a name across modules. Below
    // is the exact regression case from splice-amulet (record `Amulet` in
    // `Splice.Amulet`, enum `Amulet` in `Splice.AmuletConfig`). The same dispatch
    // applies to choice ResultDecoders, so we cover both code paths.

    [Fact]
    public void Generate_should_dispatch_field_decoder_to_record_FromRecord_when_same_named_enum_lives_in_other_module()
    {
        // Arrange
        var recordModule = new DamlModule
        {
            Name = "App.Records",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Token",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("symbol", new DamlPrimitiveType(DamlPrimitive.Text))
                    ])
                }
            ],
            Interfaces = []
        };
        var enumModule = new DamlModule
        {
            Name = "App.Config",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Token",
                    Definition = new DamlEnumDefinition(["Active", "Frozen"])
                }
            ],
            Interfaces = []
        };
        var holderModule = new DamlModule
        {
            Name = "App.Holder",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Holder",
                    Definition = new DamlRecordDefinition(
                    [
                        // Refers to the record-shaped Token in App.Records, NOT the enum
                        // of the same name in App.Config.
                        new DamlField("t", new DamlTypeRef("test-package-id", "App.Records", "Token"))
                    ])
                }
            ],
            Interfaces = []
        };
        var pkg = CreateTestPackage("test-package-id", "test-package", recordModule, enumModule, holderModule);
        var dar = new DarArchive { MainPackage = pkg, Dependencies = [] };
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar).ToList();
        var holder = files.FirstOrDefault(f => f.RelativePath.EndsWith("Holder.cs", StringComparison.Ordinal));

        // Assert — must dispatch through `Token.FromRecord(...As<DamlRecord>())`,
        // NOT through `TokenExtensions.FromDamlEnum(...As<DamlEnum>())`.
        holder.Should().NotBeNull();
        holder!.Content.Should().Contain("Token.FromRecord(record.GetRequiredField(\"t\").As<DamlRecord>())");
        holder.Content.Should().NotContain("TokenExtensions.FromDamlEnum");
    }

    [Fact]
    public void Generate_should_dispatch_field_decoder_to_enum_extensions_when_module_qualifier_matches()
    {
        // Same package layout, but the holder field references the enum in App.Config.
        var recordModule = new DamlModule
        {
            Name = "App.Records",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Token",
                    Definition = new DamlRecordDefinition([new DamlField("symbol", new DamlPrimitiveType(DamlPrimitive.Text))])
                }
            ],
            Interfaces = []
        };
        var enumModule = new DamlModule
        {
            Name = "App.Config",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Token",
                    Definition = new DamlEnumDefinition(["Active", "Frozen"])
                }
            ],
            Interfaces = []
        };
        var holderModule = new DamlModule
        {
            Name = "App.Holder",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Holder",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("t", new DamlTypeRef("test-package-id", "App.Config", "Token"))
                    ])
                }
            ],
            Interfaces = []
        };
        var pkg = CreateTestPackage("test-package-id", "test-package", recordModule, enumModule, holderModule);
        var dar = new DarArchive { MainPackage = pkg, Dependencies = [] };
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar).ToList();
        var holder = files.FirstOrDefault(f => f.RelativePath.EndsWith("Holder.cs", StringComparison.Ordinal));

        // Assert — enum module qualifier matches, so dispatch through the *Extensions helper.
        holder.Should().NotBeNull();
        holder!.Content.Should().Contain("TokenExtensions.FromDamlEnum(record.GetRequiredField(\"t\").As<DamlEnum>())");
    }

    [Fact]
    public void Generate_should_encode_enum_field_through_the_ToDamlEnum_extension()
    {
        var enumModule = new DamlModule
        {
            Name = "App.Config",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Token",
                    Definition = new DamlEnumDefinition(["Active", "Frozen"])
                }
            ],
            Interfaces = []
        };
        var holderModule = new DamlModule
        {
            Name = "App.Holder",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Holder",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("t", new DamlTypeRef("test-package-id", "App.Config", "Token"))
                    ])
                }
            ],
            Interfaces = []
        };
        var pkg = CreateTestPackage("test-package-id", "test-package", enumModule, holderModule);
        var dar = new DarArchive { MainPackage = pkg, Dependencies = [] };
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar).ToList();
        var holder = files.FirstOrDefault(f => f.RelativePath.EndsWith("Holder.cs", StringComparison.Ordinal));

        // Assert — the to-value side must route the enum field through the renamed
        // extension method, not the misnamed `.ToRecord()`.
        holder.Should().NotBeNull();
        holder!.Content.Should().Contain(".ToDamlEnum()");
        holder.Content.Should().NotContain(".ToRecord()");
    }

    #endregion

    #region Optional Field With C-Sharp Keyword Name

    [Fact]
    public void Generate_should_strip_at_prefix_in_Optional_temporary_when_field_name_is_csharp_keyword()
    {
        // Arrange — `lock` is a C# keyword. The sanitizer escapes the field name to
        // `@lock`, but the local-variable binding `__@lock` is invalid; the codegen
        // must trim the `@` for the local name only.
        var module = new DamlModule
        {
            Name = "App.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Locked",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("lock", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.Optional),
                            [new DamlPrimitiveType(DamlPrimitive.Text)]))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var locked = files.FirstOrDefault(f => f.RelativePath.EndsWith("Locked.cs", StringComparison.Ordinal));

        // Assert — local variable in the `is { } __X` pattern is `__lock`, not `__@lock`.
        locked.Should().NotBeNull();
        locked!.Content.Should().Contain("@lock is { } __lock ?");
        locked.Content.Should().NotContain("__@lock");
    }

    #endregion

    #region Variant Record-Payload Round-Trip

    [Fact]
    public void Generate_should_round_trip_variant_case_with_record_payload_through_ToRecord_and_FromRecord()
    {
        // A Daml-LF variant constructor carries exactly one type argument; when that
        // argument is a (synthesized) record, the case payload round-trips through the
        // record's ToRecord/FromRecord — no per-field flattening. This is the path the
        // primitive-payload spec does not exercise.
        var module = new DamlModule
        {
            Name = "App.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Shape",
                    Definition = new DamlVariantDefinition(
                    [
                        new DamlVariantConstructor("Point", null),
                        new DamlVariantConstructor("Rect", new DamlTypeRef(string.Empty, "App.Module", "Rectangle"))
                    ])
                },
                new DamlDataType
                {
                    Name = "Rectangle",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("width", new DamlPrimitiveType(DamlPrimitive.Int64)),
                        new DamlField("height", new DamlPrimitiveType(DamlPrimitive.Int64))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var shape = files.FirstOrDefault(f => f.RelativePath.EndsWith("Shape.cs", StringComparison.Ordinal));

        // Assert — no throwing path survives.
        shape.Should().NotBeNull();
        shape!.Content.Should().NotContain("NotImplementedException");
        shape.Content.Should().NotContain("issues/57");

        // The record-payload case carries the record under the variant tag.
        shape.Content.Should().Contain("public sealed record Rect(Rectangle Value) : Shape");
        shape.Content.Should().Contain("public override DamlVariant ToVariant() => DamlVariant.Create(\"Rect\", Value.ToRecord());");

        // FromVariant decodes the payload back through the record's FromRecord.
        shape.Content.Should().Contain("\"Rect\" => new Rect(Rectangle.FromRecord(variant.Value.As<DamlRecord>())),");
        shape.Content.Should().Contain("\"Point\" => new Point(),");
    }

    #endregion

    #region GenMap Conversion

    [Fact]
    public void Generate_should_emit_DamlGenMap_for_GenMap_field_to_value_path()
    {
        // GenMap is the catch-all map for non-string keys. ToValue must materialise a
        // DamlGenMap from the dictionary; FromValue must reverse it via Entries.
        var module = new DamlModule
        {
            Name = "App.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Bag",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("counts", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.GenMap),
                            [
                                new DamlPrimitiveType(DamlPrimitive.Text),
                                new DamlPrimitiveType(DamlPrimitive.Int64)
                            ]))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var bag = files.FirstOrDefault(f => f.RelativePath.EndsWith("Bag.cs", StringComparison.Ordinal));

        // Assert
        bag.Should().NotBeNull();
        bag!.Content.Should().Contain("IReadOnlyDictionary<string, long> Counts");
        bag.Content.Should().Contain("new DamlGenMap(Counts.Select(kv => ((DamlValue)new DamlText(kv.Key), (DamlValue)new DamlInt64(kv.Value))).ToList())");
        bag.Content.Should().Contain("record.GetRequiredField(\"counts\").As<DamlGenMap>().Entries.ToDictionary(kv => kv.Key.As<DamlText>().Value, kv => kv.Value.As<DamlInt64>().Value)");
    }

    #endregion

    #region TextMap-of-List and GenMap-of-List ReadOnly Emission (#110)

    [Fact]
    public void Generate_should_emit_IReadOnlyList_cast_in_FromRecord_for_TextMap_of_List_field()
    {
        var module = new DamlModule
        {
            Name = "App.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Buckets",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("items", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.TextMap),
                            [
                                new DamlTypeApp(
                                    new DamlPrimitiveType(DamlPrimitive.List),
                                    [new DamlPrimitiveType(DamlPrimitive.Text)])
                            ]))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        var files = generator.Generate(dar);
        var bucketsFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Buckets.cs", StringComparison.Ordinal));

        bucketsFile.Should().NotBeNull();
        var code = bucketsFile!.Content;

        code.Should().Contain("IReadOnlyDictionary<string, IReadOnlyList<string>> Items");
        code.Should().Contain("(IReadOnlyList<string>)");
        code.Should().NotContain("ToDictionary(kv => kv.Key, kv => kv.Value.As<DamlList>().Values.Select(x => x.As<DamlText>().Value).ToList())");
    }

    [Fact]
    public void Generate_should_emit_IReadOnlyList_cast_in_FromRecord_for_GenMap_of_List_field()
    {
        var module = new DamlModule
        {
            Name = "App.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Ledger",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("entries", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.GenMap),
                            [
                                new DamlPrimitiveType(DamlPrimitive.Text),
                                new DamlTypeApp(
                                    new DamlPrimitiveType(DamlPrimitive.List),
                                    [new DamlPrimitiveType(DamlPrimitive.Int64)])
                            ]))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        var files = generator.Generate(dar);
        var ledgerFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Ledger.cs", StringComparison.Ordinal));

        ledgerFile.Should().NotBeNull();
        var code = ledgerFile!.Content;

        code.Should().Contain("IReadOnlyDictionary<string, IReadOnlyList<long>> Entries");
        code.Should().Contain("(IReadOnlyList<long>)");
        code.Should().NotContain("Entries.ToDictionary(kv => kv.Key.As<DamlText>().Value, kv => kv.Value.As<DamlList>().Values.Select(x => x.As<DamlInt64>().Value).ToList())");
    }

    #endregion
}
