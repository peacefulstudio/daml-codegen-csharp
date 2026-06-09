// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using Daml.Codegen.DarParser;
using FluentAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class DataTypeCodeGenTests
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

    #region Record Data Type Tests

    [Fact]
    public void Generate_should_create_record_data_type_with_primitive_fields()
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
                    Name = "PersonInfo",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("name", new DamlPrimitiveType(DamlPrimitive.Text)),
                        new DamlField("age", new DamlPrimitiveType(DamlPrimitive.Int64)),
                        new DamlField("active", new DamlPrimitiveType(DamlPrimitive.Bool))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var personFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("PersonInfo.cs", StringComparison.Ordinal));

        // Assert
        personFile.Should().NotBeNull();
        var code = personFile!.Content;

        code.Should().Contain("public sealed record PersonInfo(");
        code.Should().Contain("string Name");
        code.Should().Contain("long Age");
        code.Should().Contain("bool Active");
        code.Should().Contain(": IDamlRecord");
    }

    [Fact]
    public void Generate_should_create_record_with_numeric_fields()
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
                    Name = "Amount",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("value", new DamlPrimitiveType(DamlPrimitive.Numeric)),
                        new DamlField("currency", new DamlPrimitiveType(DamlPrimitive.Text))
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

        code.Should().Contain("decimal Value");
        code.Should().Contain("string Currency");
    }

    [Fact]
    public void Generate_should_create_record_with_date_and_timestamp_fields()
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
                    Name = "Event",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("eventDate", new DamlPrimitiveType(DamlPrimitive.Date)),
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
        var eventFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Event.cs", StringComparison.Ordinal));

        // Assert
        eventFile.Should().NotBeNull();
        var code = eventFile!.Content;

        code.Should().Contain("DateOnly EventDate");
        code.Should().Contain("DateTimeOffset CreatedAt");
    }

    [Fact]
    public void Generate_should_create_record_with_party_field()
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
                    Name = "Ownership",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlField("receiver", new DamlPrimitiveType(DamlPrimitive.Party))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var ownershipFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Ownership.cs", StringComparison.Ordinal));

        // Assert
        ownershipFile.Should().NotBeNull();
        var code = ownershipFile!.Content;

        // Party maps to Party
        code.Should().Contain("Party Owner");
        code.Should().Contain("Party Receiver");
    }

    [Fact]
    public void Generate_should_create_record_with_optional_fields()
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
                    Name = "OptionalData",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("maybeText", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.Optional),
                            [new DamlPrimitiveType(DamlPrimitive.Text)])),
                        new DamlField("maybeNumber", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.Optional),
                            [new DamlPrimitiveType(DamlPrimitive.Int64)]))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var optionalFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("OptionalData.cs", StringComparison.Ordinal));

        // Assert
        optionalFile.Should().NotBeNull();
        var code = optionalFile!.Content;

        code.Should().Contain("string? MaybeText");
        code.Should().Contain("long? MaybeNumber");
    }

    [Fact]
    public void Generate_should_create_record_with_list_fields()
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
                    Name = "Collection",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("items", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.List),
                            [new DamlPrimitiveType(DamlPrimitive.Text)])),
                        new DamlField("counts", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.List),
                            [new DamlPrimitiveType(DamlPrimitive.Int64)]))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var collectionFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Collection.cs", StringComparison.Ordinal));

        // Assert
        collectionFile.Should().NotBeNull();
        var code = collectionFile!.Content;

        code.Should().Contain("IReadOnlyList<string> Items");
        code.Should().Contain("IReadOnlyList<long> Counts");
    }

    [Fact]
    public void Generate_should_create_record_with_textmap_field()
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
                    Name = "Metadata",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("attributes", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.TextMap),
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
        var metadataFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Metadata.cs", StringComparison.Ordinal));

        // Assert
        metadataFile.Should().NotBeNull();
        var code = metadataFile!.Content;

        code.Should().Contain("IReadOnlyDictionary<string, string> Attributes");
    }

    [Fact]
    public void Generate_should_create_record_with_contract_id_field()
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
                        new DamlField("assetRef", new DamlTypeApp(
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
        var referenceFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Reference.cs", StringComparison.Ordinal));

        // Assert
        referenceFile.Should().NotBeNull();
        var code = referenceFile!.Content;

        code.Should().Contain("ContractId<Asset> AssetRef");
    }

    [Fact]
    public void Generate_should_create_record_with_ToRecord_method()
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
                    Name = "Simple",
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
        var simpleFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Simple.cs", StringComparison.Ordinal));

        // Assert
        simpleFile.Should().NotBeNull();
        var code = simpleFile!.Content;

        code.Should().Contain("public DamlRecord ToRecord()");
        code.Should().Contain("DamlRecord.Create(");
        code.Should().Contain("DamlField.Create(\"value\", new DamlText(Value))");
    }

    [Fact]
    public void Generate_should_create_record_with_FromRecord_method()
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
                    Name = "Simple",
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
        var simpleFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Simple.cs", StringComparison.Ordinal));

        // Assert
        simpleFile.Should().NotBeNull();
        var code = simpleFile!.Content;

        code.Should().Contain("public static Simple FromRecord(DamlRecord record)");
        code.Should().Contain("record.GetRequiredField(\"value\")");
    }

    #endregion

    #region Variant Data Type Tests

    [Fact]
    public void Generate_should_create_variant_with_constructors()
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
                    Name = "PaymentMethod",
                    Definition = new DamlVariantDefinition(
                    [
                        new DamlVariantConstructor("Cash", null),
                        new DamlVariantConstructor("Card", new DamlPrimitiveType(DamlPrimitive.Text)),
                        new DamlVariantConstructor("BankTransfer", new DamlPrimitiveType(DamlPrimitive.Text))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var paymentFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("PaymentMethod.cs", StringComparison.Ordinal));

        // Assert
        paymentFile.Should().NotBeNull();
        var code = paymentFile!.Content;

        // Base abstract record
        code.Should().Contain("public abstract record PaymentMethod : IDamlVariant");
        code.Should().Contain("public abstract string Tag { get; }");

        // Derived types
        code.Should().Contain("public sealed record Cash() : PaymentMethod");
        code.Should().Contain("public sealed record Card(string Value) : PaymentMethod");
        code.Should().Contain("public sealed record BankTransfer(string Value) : PaymentMethod");

        // Tag implementations
        code.Should().Contain("public override string Tag => \"Cash\"");
        code.Should().Contain("public override string Tag => \"Card\"");
        code.Should().Contain("public override string Tag => \"BankTransfer\"");
    }

    [Fact]
    public void Generate_should_create_variant_with_numeric_argument()
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
                    Name = "Amount",
                    Definition = new DamlVariantDefinition(
                    [
                        new DamlVariantConstructor("Fixed", new DamlPrimitiveType(DamlPrimitive.Numeric)),
                        new DamlVariantConstructor("Percentage", new DamlPrimitiveType(DamlPrimitive.Numeric))
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

        code.Should().Contain("public sealed record Fixed(decimal Value) : Amount");
        code.Should().Contain("public sealed record Percentage(decimal Value) : Amount");
    }

    [Fact]
    public void Generate_should_emit_ToVariant_and_FromVariant_for_primitive_payload_case()
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
        var maybe = files.First(f => f.RelativePath.EndsWith("Maybe.cs", StringComparison.Ordinal));
        var code = maybe.Content;

        // Assert — base is a variant, not a record.
        code.Should().Contain("public abstract record Maybe : IDamlVariant");
        code.Should().Contain("public abstract DamlVariant ToVariant();");
        code.Should().NotContain("ToRecord");
        code.Should().NotContain("NotImplementedException");

        // Each case produces a DamlVariant carrying its tag and payload conversion.
        code.Should().Contain("public override DamlVariant ToVariant() => DamlVariant.Create(\"Nothing\", DamlUnit.Instance);");
        code.Should().Contain("public override DamlVariant ToVariant() => DamlVariant.Create(\"Just\", new DamlText(Value));");

        // FromVariant dispatches on the constructor tag back to the typed case.
        code.Should().Contain("public static Maybe FromVariant(DamlVariant variant) =>");
        code.Should().Contain("\"Nothing\" => new Nothing(),");
        code.Should().Contain("\"Just\" => new Just(variant.Value.As<DamlText>().Value),");
    }

    [Fact]
    public void Generate_should_route_variant_payload_through_ToVariant_and_FromVariant_when_payload_is_another_variant()
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
                    Name = "Inner",
                    Definition = new DamlVariantDefinition(
                    [
                        new DamlVariantConstructor("Lit", new DamlPrimitiveType(DamlPrimitive.Int64))
                    ])
                },
                new DamlDataType
                {
                    Name = "Outer",
                    Definition = new DamlVariantDefinition(
                    [
                        new DamlVariantConstructor("Wrap", new DamlTypeRef(string.Empty, "Test.Module", "Inner"))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var outer = files.First(f => f.RelativePath.EndsWith("Outer.cs", StringComparison.Ordinal));
        var code = outer.Content;

        // Assert — a variant payload that is itself a variant must round-trip through
        // the inner variant's ToVariant/FromVariant, never the (now non-existent)
        // ToRecord/FromRecord.
        code.Should().NotContain(".ToRecord()");
        code.Should().NotContain(".FromRecord(");
        code.Should().Contain("public override DamlVariant ToVariant() => DamlVariant.Create(\"Wrap\", Value.ToVariant());");
        code.Should().Contain("\"Wrap\" => new Wrap(Inner.FromVariant(variant.Value.As<DamlVariant>())),");
    }

    [Fact]
    public void Generate_should_route_record_field_through_ToVariant_and_FromVariant_when_field_type_is_a_variant()
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
                    Name = "Choice",
                    Definition = new DamlVariantDefinition(
                    [
                        new DamlVariantConstructor("Yes", new DamlPrimitiveType(DamlPrimitive.Int64))
                    ])
                },
                new DamlDataType
                {
                    Name = "Holder",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("pick", new DamlTypeRef(string.Empty, "Test.Module", "Choice"))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var holder = files.First(f => f.RelativePath.EndsWith("Holder.cs", StringComparison.Ordinal));
        var code = holder.Content;

        // Assert — a record field whose type is a variant serializes through the
        // variant's ToVariant/FromVariant.
        code.Should().Contain("DamlField.Create(\"pick\", Pick.ToVariant())");
        code.Should().Contain("Pick: Choice.FromVariant(record.GetRequiredField(\"pick\").As<DamlVariant>())");
    }

    #endregion

    #region Enum Data Type Tests

    [Fact]
    public void Generate_should_create_enum_with_constructors()
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
                    Name = "Status",
                    Definition = new DamlEnumDefinition(["Pending", "Active", "Completed", "Cancelled"])
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

        code.Should().Contain("public enum Status");
        code.Should().Contain("Pending,");
        code.Should().Contain("Active,");
        code.Should().Contain("Completed,");
        code.Should().Contain("Cancelled,");
    }

    [Fact]
    public void Generate_should_name_enum_extension_methods_after_their_DamlEnum_type()
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
                    Name = "Status",
                    Definition = new DamlEnumDefinition(["Pending", "Active"])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var statusFile = files.First(f => f.RelativePath.EndsWith("Status.cs", StringComparison.Ordinal));
        var code = statusFile.Content;

        // Assert — the extension method names must match the DamlEnum return/param type.
        code.Should().Contain("public static DamlEnum ToDamlEnum(this Status value)");
        code.Should().Contain("public static Status FromDamlEnum(DamlEnum value)");
        code.Should().NotContain("ToRecord");
        code.Should().NotContain("FromRecord");
    }

    [Fact]
    public void Generate_should_create_enum_with_xml_docs_when_enabled()
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
            GenerateXmlDocs = true
        };

        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Color",
                    Definition = new DamlEnumDefinition(["Red", "Green", "Blue"])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator(options);

        // Act
        var files = generator.Generate(dar);
        var colorFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Color.cs", StringComparison.Ordinal));

        // Assert
        colorFile.Should().NotBeNull();
        var code = colorFile!.Content;

        code.Should().Contain("/// <summary>");
        code.Should().Contain("/// Generated from Daml enum Color");
        code.Should().Contain("/// </summary>");
    }

    #endregion

    #region Code Generation Options Tests

    [Fact]
    public void Generate_should_use_block_scoped_namespace_when_configured()
    {
        // Arrange
        var options = new CodeGenOptions
        {
            OutputDirectory = "/tmp/test",
            GenerateJsonSupport = false,
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = false,
            UseRecordTypes = true,
            UsePrimaryConstructors = true
        };

        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Simple",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("value", new DamlPrimitiveType(DamlPrimitive.Text))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator(options);

        // Act
        var files = generator.Generate(dar);
        var simpleFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Simple.cs", StringComparison.Ordinal));

        // Assert
        simpleFile.Should().NotBeNull();
        var code = simpleFile!.Content;

        // Block-scoped namespace has { } braces (uses root namespace from package)
        code.Should().Contain("namespace Test.Package");
        code.Should().Contain("{");
    }

    [Fact]
    public void Generate_should_include_nullable_enable_when_configured()
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
                    Name = "Simple",
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
        var simpleFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Simple.cs", StringComparison.Ordinal));

        // Assert
        simpleFile.Should().NotBeNull();
        var code = simpleFile!.Content;

        code.Should().Contain("#nullable enable");
    }

    [Fact]
    public void Generate_should_not_emit_serialization_using_when_body_has_no_json_references()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Simple",
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

        var files = generator.Generate(dar);
        var simpleFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Simple.cs", StringComparison.Ordinal));

        simpleFile.Should().NotBeNull();
        simpleFile!.Content.Should().NotContain("using Daml.Runtime.Serialization;",
            "conditional using emission must not emit Daml.Runtime.Serialization when the body contains no JSON-serialization references");
    }

    [Fact]
    public void Generate_should_skip_json_serialization_using_when_disabled()
    {
        // Arrange
        var options = new CodeGenOptions
        {
            OutputDirectory = "/tmp/test",
            GenerateJsonSupport = false,
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            UseRecordTypes = true,
            UsePrimaryConstructors = true
        };

        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Simple",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("value", new DamlPrimitiveType(DamlPrimitive.Text))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator(options);

        // Act
        var files = generator.Generate(dar);
        var simpleFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Simple.cs", StringComparison.Ordinal));

        // Assert
        simpleFile.Should().NotBeNull();
        var code = simpleFile!.Content;

        code.Should().NotContain("using Daml.Runtime.Serialization;");
    }

    #endregion

    #region Namespace and Identifier Tests

    [Fact]
    public void Generate_should_use_custom_root_namespace_when_configured()
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
            RootNamespace = "Custom.Namespace"
        };

        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Simple",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("value", new DamlPrimitiveType(DamlPrimitive.Text))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator(options);

        // Act
        var files = generator.Generate(dar);
        var simpleFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Simple.cs", StringComparison.Ordinal));

        // Assert
        simpleFile.Should().NotBeNull();
        var code = simpleFile!.Content;

        // When custom root namespace is specified, all types go into that namespace directly
        code.Should().Contain("namespace Custom.Namespace;");
    }

    [Fact]
    public void Generate_should_sanitize_identifier_with_csharp_keyword()
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
                    Name = "class", // C# keyword
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

        // Assert - The generated file should have @ prefix for the keyword
        files.Should().NotBeEmpty();
        var classFile = files.FirstOrDefault(f => f.RelativePath.Contains("@class.cs"));
        classFile.Should().NotBeNull();
        var code = classFile!.Content;
        code.Should().Contain("public sealed record @class");
    }

    [Fact]
    public void Generate_should_sanitize_identifier_starting_with_digit()
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
                    Name = "123Type", // Starts with digit
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

        // Assert - Should have _ prefix
        files.Should().NotBeEmpty();
        var typeFile = files.FirstOrDefault(f => f.RelativePath.Contains("_123Type.cs"));
        typeFile.Should().NotBeNull();
        var code = typeFile!.Content;
        code.Should().Contain("public sealed record _123Type");
    }

    [Fact]
    public void Generate_should_convert_field_names_to_pascal_case()
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
                    Name = "CasingTest",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("lower_case_field", new DamlPrimitiveType(DamlPrimitive.Text)),
                        new DamlField("camelCaseField", new DamlPrimitiveType(DamlPrimitive.Text)),
                        new DamlField("kebab-case-field", new DamlPrimitiveType(DamlPrimitive.Text))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var casingFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("CasingTest.cs", StringComparison.Ordinal));

        // Assert
        casingFile.Should().NotBeNull();
        var code = casingFile!.Content;

        code.Should().Contain("LowerCaseField");
        code.Should().Contain("CamelCaseField");
        code.Should().Contain("KebabCaseField");
    }

    [Fact]
    public void Generate_should_handle_tuple_field_names()
    {
        // Arrange - tuple fields in Daml are named _1, _2, etc.
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "TupleType",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("_1", new DamlPrimitiveType(DamlPrimitive.Text)),
                        new DamlField("_2", new DamlPrimitiveType(DamlPrimitive.Int64)),
                        new DamlField("_3", new DamlPrimitiveType(DamlPrimitive.Bool))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var tupleFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("TupleType.cs", StringComparison.Ordinal));

        // Assert
        tupleFile.Should().NotBeNull();
        var code = tupleFile!.Content;

        // Tuple fields _1, _2, _3 should become valid C# identifiers _1, _2, _3
        code.Should().Contain("string _1");
        code.Should().Contain("long _2");
        code.Should().Contain("bool _3");

        // The ToRecord should reference the original field names
        code.Should().Contain("DamlField.Create(\"_1\"");
        code.Should().Contain("DamlField.Create(\"_2\"");
        code.Should().Contain("DamlField.Create(\"_3\"");
    }

    #endregion

    #region Template Filtering Tests

    [Fact]
    public void Generate_should_filter_templates_with_root_filter()
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
            Templates =
            [
                new DamlTemplate
                {
                    Name = "IncludeThis",
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = []
                },
                new DamlTemplate
                {
                    Name = "ExcludeThis",
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = []
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "IncludeThis",
                    Definition = new DamlRecordDefinition([new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))])
                },
                new DamlDataType
                {
                    Name = "ExcludeThis",
                    Definition = new DamlRecordDefinition([new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator(options);

        // Act
        var files = generator.Generate(dar);

        // Assert - Only IncludeThis template should be generated
        var templateFiles = files.Where(f => f.RelativePath.Contains("IncludeThis") || f.RelativePath.Contains("ExcludeThis")).ToList();
        templateFiles.Should().HaveCount(1);
        templateFiles[0].RelativePath.Should().Contain("IncludeThis");
    }

    #endregion

    #region Cross-Type Reference Tests

    [Fact]
    public void Generate_should_reference_other_data_types()
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
                    Name = "Address",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("street", new DamlPrimitiveType(DamlPrimitive.Text)),
                        new DamlField("city", new DamlPrimitiveType(DamlPrimitive.Text))
                    ])
                },
                new DamlDataType
                {
                    Name = "Person",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("name", new DamlPrimitiveType(DamlPrimitive.Text)),
                        new DamlField("homeAddress", new DamlTypeRef("", "Test.Module", "Address"))
                    ])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var personFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Person.cs", StringComparison.Ordinal));

        // Assert
        personFile.Should().NotBeNull();
        var code = personFile!.Content;

        code.Should().Contain("Address HomeAddress");
        code.Should().Contain("Address.FromRecord(");
    }

    #endregion
}
