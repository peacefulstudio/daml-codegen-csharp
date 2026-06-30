// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.Model;
using AwesomeAssertions;
using Xunit;
using static Daml.Codegen.CSharp.Tests.TestHelpers.DamlModelBuilder;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public partial class CodeGenEdgeCaseTests
{
    #region Module-Qualified Enum Dispatch Tests

    /// <summary>
    /// Daml lets one simple type name appear in several modules of a single
    /// package, so the codegen must dispatch field decoders and encoders by the
    /// module qualifier rather than the simple name. The splice-amulet regression
    /// paired a record <c>Amulet</c> in <c>Splice.Amulet</c> with an enum
    /// <c>Amulet</c> in <c>Splice.AmuletConfig</c>; each row references one
    /// same-named <c>Token</c> and pins the conversion the codegen must emit.
    /// </summary>
    [Theory]
    [InlineData(
        "App.Records",
        true,
        """Token.FromRecord(record.GetRequiredField("t").As<DamlRecord>())""",
        "TokenExtensions.FromDamlEnum")]
    [InlineData(
        "App.Config",
        true,
        """TokenExtensions.FromDamlEnum(record.GetRequiredField("t").As<DamlEnum>())""",
        null)]
    [InlineData(
        "App.Config",
        false,
        ".ToDamlEnum()",
        ".ToRecord()")]
    public void generate_dispatches_same_named_token_field_by_module_qualifier(
        string referencedModule,
        bool includeRecordToken,
        string expectedFragment,
        string? forbiddenFragment)
    {
        var recordModule = new DamlModule
        {
            Name = "App.Records",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Token",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("symbol", new DamlPrimitiveType(DamlPrimitive.Text))])
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
                        new DamlFieldDefinition("t", new DamlTypeRef("test-package-id", referencedModule, "Token"))
                    ])
                }
            ],
            Interfaces = []
        };

        DamlModule[] modules = includeRecordToken
            ? [recordModule, enumModule, holderModule]
            : [enumModule, holderModule];

        var pkg = CreateTestPackage("test-package-id", "test-package", modules);
        var dar = new DarModel { MainPackage = pkg, Dependencies = [] };
        var generator = CreateGenerator();

        var files = generator.Generate(dar).ToList();
        var holder = files.FirstOrDefault(f => f.RelativePath.EndsWith("Holder.cs", StringComparison.Ordinal));

        holder.Should().NotBeNull();
        holder!.Content.Should().Contain(expectedFragment);
        if (forbiddenFragment is not null)
        {
            holder.Content.Should().NotContain(forbiddenFragment);
        }
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
                        new DamlFieldDefinition("lock", new DamlTypeApp(
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
                        new DamlFieldDefinition("width", new DamlPrimitiveType(DamlPrimitive.Int64)),
                        new DamlFieldDefinition("height", new DamlPrimitiveType(DamlPrimitive.Int64))
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
                        new DamlFieldDefinition("counts", new DamlTypeApp(
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

    #region TextMap-of-List and GenMap-of-List ReadOnly Emission

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
                        new DamlFieldDefinition("items", new DamlTypeApp(
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
                        new DamlFieldDefinition("entries", new DamlTypeApp(
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
