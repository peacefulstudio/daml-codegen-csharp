// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using Daml.Codegen.DarParser;
using FluentAssertions;
using System.IO;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class ChoiceCodeGenTests
{
    private static CSharpCodeGenerator CreateGenerator()
    {
        var options = new CodeGenOptions
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            UseRecordTypes = true,
            UsePrimaryConstructors = true
        };
        var logger = new ConsoleLogger(0); // Silent
        return new CSharpCodeGenerator(options, logger);
    }

    private static readonly DamlPackage StdlibStub = new()
    {
        PackageId = "daml-prim-pkg-id",
        Name = "daml-prim",
        Version = new Version(1, 0, 0),
        LfVersion = "2.1",
        Modules = [],
        DependencyReferences = [],
    };

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
            Dependencies = [StdlibStub]
        };
    }

    [Fact]
    public void Generate_should_reference_existing_type_for_local_data_type_argument()
    {
        // Arrange
        var transferArgFields = new[]
        {
            new DamlFieldDefinition("newOwner", new DamlPrimitiveType(DamlPrimitive.Party)),
            new DamlFieldDefinition("amount", new DamlPrimitiveType(DamlPrimitive.Numeric))
        };

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
                        new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("value", new DamlPrimitiveType(DamlPrimitive.Int64))
                    ],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Transfer",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef("", "Test.Module", "Transfer"),
                            ReturnType = new DamlTypeRef("", "Test.Module", "Transfer_Result")
                        }
                    ]
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Asset",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("value", new DamlPrimitiveType(DamlPrimitive.Int64))
                    ])
                },
                new DamlDataType
                {
                    Name = "Transfer",
                    Definition = new DamlRecordDefinition(transferArgFields)
                },
                new DamlDataType
                {
                    Name = "Transfer_Result",
                    Definition = new DamlRecordDefinition([])
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

        // Should reference the Transfer data type, not generate a nested TransferArgument
        code.Should().Contain("Choice<Asset, Transfer, Transfer_Result>");
        code.Should().Contain("ArgumentEncoder = arg => arg.ToRecord()");
        code.Should().Contain("ResultDecoder = val => Transfer_Result.FromRecord(val.As<DamlRecord>())");

        // Should NOT contain a nested argument class
        code.Should().NotContain("public sealed record TransferArgument");
        code.Should().NotContain("public sealed record TransferArg");
    }

    [Fact]
    public void GenerateNestedChoiceArgumentType_emits_indented_signature_without_stray_spaces()
    {
        var module = NestedTransferArgumentModule();
        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        var files = generator.Generate(dar);
        var nestedFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Asset.Transfer.cs", StringComparison.Ordinal));

        nestedFile.Should().NotBeNull("the choice argument record is emitted as a nested type in a partial template file");
        nestedFile!.Content.Should().Contain(
            "\n    public sealed record Transfer(Party NewOwner, decimal Amount) : IDamlRecord",
            "the nested record signature must be indented one level and the parameter list must close without trailing whitespace");
    }

    private static DamlModule NestedTransferArgumentModule()
    {
        DamlFieldDefinition[] transferArgFields =
        [
            new DamlFieldDefinition("newOwner", new DamlPrimitiveType(DamlPrimitive.Party)),
            new DamlFieldDefinition("amount", new DamlPrimitiveType(DamlPrimitive.Numeric)),
        ];

        return new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Asset",
                    Fields = [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Transfer",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef("", "Test.Module", "Transfer"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        }
                    ]
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Asset",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))])
                },
                new DamlDataType
                {
                    Name = "Transfer",
                    Definition = new DamlRecordDefinition(transferArgFields)
                },
            ],
            Interfaces = []
        };
    }

    [Fact]
    public void Generate_should_use_DamlUnit_for_Unit_argument()
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
                    Fields = [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Close",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
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
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))])
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

        code.Should().Contain("Choice<Contract, DamlUnit, DamlUnit>");
        code.Should().Contain("ArgumentEncoder = _ => DamlUnit.Instance");
        code.Should().Contain("ResultDecoder = _ => DamlUnit.Instance");
    }

    [Fact]
    public void Generate_should_use_DamlUnit_for_Archive_choice()
    {
        // Arrange - Archive is a special external type reference
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "MyTemplate",
                    Fields = [new DamlFieldDefinition("data", new DamlPrimitiveType(DamlPrimitive.Text))],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Archive",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef(StdlibStub.PackageId, "DA.Internal.Template", "Archive"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit)
                        }
                    ]
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "MyTemplate",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("data", new DamlPrimitiveType(DamlPrimitive.Text))])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var templateFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("MyTemplate.cs", StringComparison.Ordinal));

        // Assert
        templateFile.Should().NotBeNull();
        var code = templateFile!.Content;

        // Archive should use DamlUnit as it's an external type
        code.Should().Contain("Choice<MyTemplate, DamlUnit, DamlUnit>");
        code.Should().Contain("ArgumentEncoder = _ => DamlUnit.Instance");
    }

    [Fact]
    public void Generate_should_decode_ContractId_result_correctly()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Factory",
                    Fields = [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Create",
                            Consuming = false,
                            ArgumentType = new DamlTypeRef("", "Test.Module", "CreateArgs"),
                            ReturnType = new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.ContractId),
                                [new DamlTypeRef("", "Test.Module", "Product")])
                        }
                    ]
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Factory",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))])
                },
                new DamlDataType
                {
                    Name = "CreateArgs",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("name", new DamlPrimitiveType(DamlPrimitive.Text))])
                },
                new DamlDataType
                {
                    Name = "Product",
                    Definition = new DamlRecordDefinition([])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var factoryFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Factory.cs", StringComparison.Ordinal));

        // Assert
        factoryFile.Should().NotBeNull();
        var code = factoryFile!.Content;

        code.Should().Contain("Choice<Factory, Create, ContractId<Product>>");
        code.Should().Contain("ResultDecoder = val => new ContractId<Product>(val.As<DamlContractId>().Value)");
    }

    [Fact]
    public void Generate_should_decode_primitive_results_correctly()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Counter",
                    Fields = [new DamlFieldDefinition("count", new DamlPrimitiveType(DamlPrimitive.Int64))],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "GetCount",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Int64)
                        },
                        new DamlChoice
                        {
                            Name = "GetName",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Text)
                        },
                        new DamlChoice
                        {
                            Name = "IsValid",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Bool)
                        }
                    ]
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Counter",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("count", new DamlPrimitiveType(DamlPrimitive.Int64))])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var counterFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Counter.cs", StringComparison.Ordinal));

        // Assert
        counterFile.Should().NotBeNull();
        var code = counterFile!.Content;

        // Check primitive result decoders
        code.Should().Contain("Choice<Counter, DamlUnit, long> ChoiceGetCount");
        code.Should().Contain("ResultDecoder = val => val.As<DamlInt64>().Value");

        code.Should().Contain("Choice<Counter, DamlUnit, string> ChoiceGetName");
        code.Should().Contain("ResultDecoder = val => val.As<DamlText>().Value");

        code.Should().Contain("Choice<Counter, DamlUnit, bool> ChoiceIsValid");
        code.Should().Contain("ResultDecoder = val => val.As<DamlBool>().Value");
    }

    [Fact]
    public void Generate_should_handle_multiple_choices_with_different_arg_types()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Holding",
                    Fields = [new DamlFieldDefinition("amount", new DamlPrimitiveType(DamlPrimitive.Numeric))],
                    Choices =
                    [
                        // Local data type argument
                        new DamlChoice
                        {
                            Name = "Split",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef("", "Test.Module", "Split"),
                            ReturnType = new DamlTypeRef("", "Test.Module", "Split_Result")
                        },
                        // Unit argument
                        new DamlChoice
                        {
                            Name = "GetBalance",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Numeric)
                        },
                        // External Archive argument
                        new DamlChoice
                        {
                            Name = "Archive",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef(StdlibStub.PackageId, "DA.Internal.Template", "Archive"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit)
                        }
                    ]
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Holding",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("amount", new DamlPrimitiveType(DamlPrimitive.Numeric))])
                },
                new DamlDataType
                {
                    Name = "Split",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("splitAmount", new DamlPrimitiveType(DamlPrimitive.Numeric))])
                },
                new DamlDataType
                {
                    Name = "Split_Result",
                    Definition = new DamlRecordDefinition([])
                }
            ],
            Interfaces = []
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var holdingFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Holding.cs", StringComparison.Ordinal));

        // Assert
        holdingFile.Should().NotBeNull();
        var code = holdingFile!.Content;

        // Split - uses local data type
        code.Should().Contain("Choice<Holding, Split, Split_Result> ChoiceSplit");
        code.Should().Contain("ArgumentEncoder = arg => arg.ToRecord(),");

        // GetBalance - uses Unit argument
        code.Should().Contain("Choice<Holding, DamlUnit, decimal> ChoiceGetBalance");

        // Archive - uses DamlUnit for external type
        code.Should().Contain("Choice<Holding, DamlUnit, DamlUnit> ChoiceArchive");
    }
}
