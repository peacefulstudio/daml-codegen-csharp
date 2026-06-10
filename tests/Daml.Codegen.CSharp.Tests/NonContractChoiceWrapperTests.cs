// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using FluentAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Codegen-shape tests for the non-CID choice wrappers.
/// Each fact arranges a single template with one choice of a particular return-
/// type shape (Decimal, record, list, Unit), runs the generator, and asserts on
/// the emitted source. The assertions are deliberately narrow — they pin the
/// public surface (method signature, return-type shape, error pass-through) but
/// not the internal projection helper's exact wording, which can be polished
/// later without breaking consumers.
/// </summary>
public class NonContractChoiceWrapperTests
{
    private static CSharpCodeGenerator CreateGenerator()
    {
        var options = new CodeGenOptions
        {
            OutputDirectory = "/tmp/test",
            GenerateJsonSupport = true,
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            UseRecordTypes = true,
            UsePrimaryConstructors = true,
        };
        var logger = new ConsoleLogger(0);
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

    private static DarModel CreateDar(DamlModule module) =>
        new()
        {
            MainPackage = new DamlPackage
            {
                PackageId = "test-pkg",
                Name = "test-package",
                Version = new Version(1, 0, 0),
                LfVersion = "2.1",
                Modules = [module],
                DependencyReferences = [],
            },
            Dependencies = [StdlibStub],
        };

    private static string ExtractNonContractExtensionsClass(string fileContent)
    {
        var marker = "NonContractExtensions";
        var classKeywordIndex = fileContent.IndexOf("public static class ", StringComparison.Ordinal);
        while (classKeywordIndex >= 0)
        {
            var lineEnd = fileContent.IndexOf('\n', classKeywordIndex);
            var declarationLine = lineEnd >= 0
                ? fileContent[classKeywordIndex..lineEnd]
                : fileContent[classKeywordIndex..];
            if (declarationLine.Contains(marker, StringComparison.Ordinal))
            {
                return fileContent[classKeywordIndex..];
            }
            classKeywordIndex = fileContent.IndexOf("public static class ", classKeywordIndex + 1, StringComparison.Ordinal);
        }
        throw new InvalidOperationException("No NonContractExtensions class found in generated file content");
    }

    [Fact]
    public void Generate_emits_async_wrapper_for_decimal_returning_choice()
    {
        var module = new DamlModule
        {
            Name = "Test.Oracle",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Oracle",
                    Fields = [new DamlField("operator", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "GetTrailingTwap",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Numeric),
                        }
                    ]
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Oracle",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("operator", new DamlPrimitiveType(DamlPrimitive.Party))]),
                }
            ],
            Interfaces = [],
        };

        var files = CreateGenerator().Generate(CreateDar(module));
        var oracle = files.First(f => f.RelativePath.EndsWith("Oracle.cs", StringComparison.Ordinal));

        // Public surface: an extensions class hung off the namespace, an async
        // method with the choice's name, and an outcome over the choice's
        // declared return type.
        oracle.Content.Should().Contain("public static class OracleNonContractExtensions");
        oracle.Content.Should().Contain("public static async Task<ExerciseOutcome<decimal>> GetTrailingTwapAsync(");
        oracle.Content.Should().Contain("this ContractId<Oracle> contractId,");
        oracle.Content.Should().Contain("ILedgerClient client,");
        oracle.Content.Should().Contain(".TrySubmitAndWaitForTransactionAsync(submission, cancellationToken)");
        // Error pass-through: each variant reconstructed with the typed return.
        oracle.Content.Should().Contain("ExerciseOutcome<TransactionResult>.DamlError damlError => new ExerciseOutcome<decimal>.DamlError(");
        oracle.Content.Should().Contain("ExerciseOutcome<TransactionResult>.InfraError infraError => new ExerciseOutcome<decimal>.InfraError(");
        // The projector must filter on (ContractId, TemplateId by ModuleName+EntityName,
        // ChoiceName) so (a) a nested exercise of the same choice name on a different
        // contract within the same transaction can't be returned by mistake, and (b)
        // package-id drift from upgrades doesn't break projection.
        oracle.Content.Should().Contain("ProjectGetTrailingTwapResult(success.Result, contractId.Value)");
        oracle.Content.Should().Contain("string.Equals(exercised.ContractId, contractId, StringComparison.Ordinal)");
        oracle.Content.Should().Contain("string.Equals(exercised.TemplateId.ModuleName, Oracle.TemplateId.ModuleName, StringComparison.Ordinal)");
        oracle.Content.Should().Contain("string.Equals(exercised.TemplateId.EntityName, Oracle.TemplateId.EntityName, StringComparison.Ordinal)");
        oracle.Content.Should().NotContain("exercised.TemplateId.Equals(");
    }

    [Fact]
    public void Generate_emits_async_wrapper_for_record_returning_choice()
    {
        var module = new DamlModule
        {
            Name = "Test.Reports",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Reporter",
                    Fields = [new DamlField("operator", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "ComputeReport",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlTypeRef("", "Test.Reports", "Report"),
                        }
                    ]
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Reporter",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("operator", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "Report",
                    Definition = new DamlRecordDefinition(
                        [
                            new DamlField("twap", new DamlPrimitiveType(DamlPrimitive.Numeric)),
                            new DamlField("samples", new DamlPrimitiveType(DamlPrimitive.Int64)),
                        ]),
                }
            ],
            Interfaces = [],
        };

        var files = CreateGenerator().Generate(CreateDar(module));
        var reporter = files.First(f => f.RelativePath.EndsWith("Reporter.cs", StringComparison.Ordinal));

        reporter.Content.Should().Contain("public static class ReporterNonContractExtensions");
        reporter.Content.Should().Contain("public static async Task<ExerciseOutcome<Report>> ComputeReportAsync(");
    }

    [Fact]
    public void Generate_emits_async_wrapper_for_unit_returning_choice_using_Stdlib_Unit()
    {
        var module = new DamlModule
        {
            Name = "Test.Sink",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Sink",
                    Fields = [new DamlField("operator", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices =
                    [
                        // A non-consuming `()`-returning choice that *isn't* the synthetic Archive.
                        // Codegen treats Archive specially (see Generate_should_skip_Archive_for_async_wrapper);
                        // a hand-declared `choice DoNothing : ()` should produce an async wrapper.
                        new DamlChoice
                        {
                            Name = "DoNothing",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        }
                    ]
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Sink",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("operator", new DamlPrimitiveType(DamlPrimitive.Party))]),
                }
            ],
            Interfaces = [],
        };

        var files = CreateGenerator().Generate(CreateDar(module));
        var sink = files.First(f => f.RelativePath.EndsWith("Sink.cs", StringComparison.Ordinal));

        // Unit returns must surface the runtime Unit marker at the call site —
        // not the wire-level DamlUnit. The type and the singleton accessor are
        // routed through the central qualifier (bare Unit + `using
        // Daml.Runtime.Stdlib;`), or global::-prefixed when the surrounding
        // namespace shadows the name.
        sink.Content.Should().Contain("public static async Task<ExerciseOutcome<Unit>> DoNothingAsync(");
        sink.Content.Should().Contain("new ExerciseOutcome<Unit>.One(Unit.Value)");
        sink.Content.Should().Contain("using Daml.Runtime.Stdlib;");
    }

    [Fact]
    public void Generate_emits_async_wrapper_for_optional_unit_using_Stdlib_Unit_in_signature_and_decoder()
    {
        var module = new DamlModule
        {
            Name = "Test.Sink",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Sink",
                    Fields = [new DamlField("operator", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "MaybeNothing",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.Optional),
                                [new DamlPrimitiveType(DamlPrimitive.Unit)]),
                        }
                    ]
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Sink",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("operator", new DamlPrimitiveType(DamlPrimitive.Party))]),
                }
            ],
            Interfaces = [],
        };

        var files = CreateGenerator().Generate(CreateDar(module));
        var sink = files.First(f => f.RelativePath.EndsWith("Sink.cs", StringComparison.Ordinal));

        sink.Content.Should().Contain("public static async Task<ExerciseOutcome<Unit?>> MaybeNothingAsync(");
        sink.Content.Should().Contain("new ExerciseOutcome<Unit?>.One(");
        sink.Content.Should().Contain(".AsOptional().HasValue ? Unit.Value : null");
        sink.Content.Should().Contain("using Daml.Runtime.Stdlib;");
        var nonContractSection = ExtractNonContractExtensionsClass(sink.Content);
        nonContractSection.Should().NotContain("DamlUnit?",
            "the public-surface NonContractExtensions class must not leak the wire-level DamlUnit type for nested Unit shapes");
    }

    [Fact]
    public void Generate_emits_async_wrapper_for_list_of_unit_using_Stdlib_Unit()
    {
        var module = new DamlModule
        {
            Name = "Test.Sink",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Sink",
                    Fields = [new DamlField("operator", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "ListOfUnits",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.List),
                                [new DamlPrimitiveType(DamlPrimitive.Unit)]),
                        }
                    ]
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Sink",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("operator", new DamlPrimitiveType(DamlPrimitive.Party))]),
                }
            ],
            Interfaces = [],
        };

        var files = CreateGenerator().Generate(CreateDar(module));
        var sink = files.First(f => f.RelativePath.EndsWith("Sink.cs", StringComparison.Ordinal));

        sink.Content.Should().Contain("public static async Task<ExerciseOutcome<IReadOnlyList<Unit>>> ListOfUnitsAsync(");
        sink.Content.Should().Contain(".As<DamlList>().Values.Select(x => Unit.Value).ToList()");
        sink.Content.Should().Contain("using Daml.Runtime.Stdlib;");
        var nonContractSection = ExtractNonContractExtensionsClass(sink.Content);
        nonContractSection.Should().NotContain("IReadOnlyList<DamlUnit>",
            "the public-surface NonContractExtensions class must not leak the wire-level DamlUnit type for list-of-Unit");
    }

    [Fact]
    public void Generate_emits_async_wrapper_for_textmap_of_unit_using_Stdlib_Unit()
    {
        var module = new DamlModule
        {
            Name = "Test.Sink",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Sink",
                    Fields = [new DamlField("operator", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "MapOfUnits",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.TextMap),
                                [new DamlPrimitiveType(DamlPrimitive.Unit)]),
                        }
                    ]
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Sink",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("operator", new DamlPrimitiveType(DamlPrimitive.Party))]),
                }
            ],
            Interfaces = [],
        };

        var files = CreateGenerator().Generate(CreateDar(module));
        var sink = files.First(f => f.RelativePath.EndsWith("Sink.cs", StringComparison.Ordinal));

        sink.Content.Should().Contain("public static async Task<ExerciseOutcome<IReadOnlyDictionary<string, Unit>>> MapOfUnitsAsync(");
        sink.Content.Should().Contain(".As<DamlTextMap>().Values.ToDictionary(kv => kv.Key, kv => Unit.Value)");
        sink.Content.Should().Contain("using Daml.Runtime.Stdlib;");
    }

    [Fact]
    public void Generate_emits_async_wrapper_for_list_returning_choice()
    {
        var module = new DamlModule
        {
            Name = "Test.Oracle",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Oracle",
                    Fields = [new DamlField("operator", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "RecentTwaps",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.List),
                                [new DamlPrimitiveType(DamlPrimitive.Numeric)]),
                        }
                    ]
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Oracle",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("operator", new DamlPrimitiveType(DamlPrimitive.Party))]),
                }
            ],
            Interfaces = [],
        };

        var files = CreateGenerator().Generate(CreateDar(module));
        var oracle = files.First(f => f.RelativePath.EndsWith("Oracle.cs", StringComparison.Ordinal));

        oracle.Content.Should().Contain("public static async Task<ExerciseOutcome<IReadOnlyList<decimal>>> RecentTwapsAsync(");
    }

    [Fact]
    public void Generate_should_not_emit_async_wrapper_for_bare_contract_id_returning_choice()
    {
        // A bare `ContractId T` return goes through the existing slot-based projector
        // no entry should appear in the non-contract extensions class.
        var module = new DamlModule
        {
            Name = "Test.Factory",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Factory",
                    Fields = [new DamlField("operator", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Mint",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.ContractId),
                                [new DamlTypeRef("", "Test.Factory", "Coin")]),
                        }
                    ]
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Factory",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("operator", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "Coin",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                }
            ],
            Interfaces = [],
        };

        var files = CreateGenerator().Generate(CreateDar(module));
        var factory = files.First(f => f.RelativePath.EndsWith("Factory.cs", StringComparison.Ordinal));

        // The non-CID extensions class isn't emitted because the only choice
        // (Mint) returns a bare ContractId T and is routed through the create-bearing
        // FactoryExtensions / FromCreatedContracts path instead.
        factory.Content.Should().NotContain("FactoryNonContractExtensions");
        // Mint still gets a <Choice>Async method via the create-bearing emission, in the
        // FactoryExtensions class. That path projects via FromCreatedContracts,
        // not the ExercisedEvents projector — so verify by structural hint.
        factory.Content.Should().Contain("public static class FactoryExtensions");
        factory.Content.Should().Contain("MintResult.FromCreatedContracts");
        // The non-CID projector helper must not appear.
        factory.Content.Should().NotContain("ProjectMintResult");
    }

    [Fact]
    public void Generate_should_not_emit_async_wrapper_for_optional_contract_id_return()
    {
        // Regression: returns containing a CID slot via Optional/List/Tuple
        // (here: `Optional (ContractId Coin)`) must flow only through the slot-based projector's
        // slot-based projector emitted into <Tpl>Extensions. The previous
        // filter (`!IsBareContractIdReturn`) emitted them into both
        // <Tpl>Extensions AND <Tpl>NonContractExtensions, producing an
        // ambiguous-call error at the consumer. The split now keys on
        // ExtractCreatedSlots so any CID-slot-bearing return (bare, Optional,
        // List, tuple) skips this path. Records-via-typeref containing CID
        // fields stay on this path because ExtractCreatedSlots intentionally
        // doesn't unfold record types.
        var module = new DamlModule
        {
            Name = "Test.Factory",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Factory",
                    Fields = [new DamlField("operator", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "MaybeMint",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.Optional),
                                [new DamlTypeApp(
                                    new DamlPrimitiveType(DamlPrimitive.ContractId),
                                    [new DamlTypeRef("", "Test.Factory", "Coin")])]),
                        }
                    ]
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Factory",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("operator", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "Coin",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                }
            ],
            Interfaces = [],
        };

        var files = CreateGenerator().Generate(CreateDar(module));
        var factory = files.First(f => f.RelativePath.EndsWith("Factory.cs", StringComparison.Ordinal));

        factory.Content.Should().NotContain("FactoryNonContractExtensions");
        factory.Content.Should().NotContain("ProjectMaybeMintResult(");
        // The slot-based path still emits a typed result struct + projector.
        factory.Content.Should().Contain("MaybeMintResult.FromCreatedContracts");
    }

    [Fact]
    public void Generate_should_skip_synthetic_Archive_choice_in_non_contract_wrappers()
    {
        // Archive is imported from DA.Internal.Template and has a Unit return.
        // The non-CID emitter must skip it — the existing Choice<>.Archive
        // already exposes the choice without a typed wrapper.
        var module = new DamlModule
        {
            Name = "Test.Archive",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Item",
                    Fields = [new DamlField("data", new DamlPrimitiveType(DamlPrimitive.Text))],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Archive",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef(StdlibStub.PackageId, "DA.Internal.Template", "Archive"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        }
                    ]
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Item",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("data", new DamlPrimitiveType(DamlPrimitive.Text))]),
                }
            ],
            Interfaces = [],
        };

        var files = CreateGenerator().Generate(CreateDar(module));
        var item = files.First(f => f.RelativePath.EndsWith("Item.cs", StringComparison.Ordinal));

        // No extensions class because the only choice (Archive) is filtered out.
        item.Content.Should().NotContain("ItemNonContractExtensions");
        item.Content.Should().NotContain("ArchiveAsync(");
    }

    [Fact]
    public void Generate_should_not_filter_user_Archive_choice_from_non_stdlib_package()
    {
        var userPkg = new DamlPackage
        {
            PackageId = "user-pkg-id",
            Name = "user-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = [],
        };

        var module = new DamlModule
        {
            Name = "Test.Archive",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Item",
                    Fields = [new DamlField("data", new DamlPrimitiveType(DamlPrimitive.Text))],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Archive",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef(userPkg.PackageId, "DA.Internal.Template", "Archive"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        }
                    ]
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Item",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("data", new DamlPrimitiveType(DamlPrimitive.Text))]),
                }
            ],
            Interfaces = [],
        };

        var dar = new DarModel
        {
            MainPackage = new DamlPackage
            {
                PackageId = "test-pkg",
                Name = "test-package",
                Version = new Version(1, 0, 0),
                LfVersion = "2.1",
                Modules = [module],
                DependencyReferences = [],
            },
            Dependencies = [StdlibStub, userPkg],
        };

        var files = CreateGenerator().Generate(dar);
        var item = files.First(f => f.RelativePath.EndsWith("Item.cs", StringComparison.Ordinal));

        item.Content.Should().Contain("ItemNonContractExtensions");
        item.Content.Should().Contain("ArchiveAsync(");
    }

    [Fact]
    public void Generate_user_Archive_choice_gets_actual_argument_type_not_DamlUnit()
    {
        var userPkg = new DamlPackage
        {
            PackageId = "user-pkg-id",
            Name = "user-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules =
            [
                new DamlModule
                {
                    Name = "DA.Internal.Template",
                    Templates = [],
                    DataTypes =
                    [
                        new DamlDataType
                        {
                            Name = "Archive",
                            Definition = new DamlRecordDefinition(
                                [new DamlField("reason", new DamlPrimitiveType(DamlPrimitive.Text))]),
                        }
                    ],
                    Interfaces = [],
                }
            ],
            DependencyReferences = [],
        };

        var module = new DamlModule
        {
            Name = "Test.Archive",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Item",
                    Fields = [new DamlField("data", new DamlPrimitiveType(DamlPrimitive.Text))],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Archive",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef(userPkg.PackageId, "DA.Internal.Template", "Archive"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        }
                    ]
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Item",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("data", new DamlPrimitiveType(DamlPrimitive.Text))]),
                }
            ],
            Interfaces = [],
        };

        var dar = new DarModel
        {
            MainPackage = new DamlPackage
            {
                PackageId = "test-pkg",
                Name = "test-package",
                Version = new Version(1, 0, 0),
                LfVersion = "2.1",
                Modules = [module],
                DependencyReferences = [],
            },
            Dependencies = [StdlibStub, userPkg],
        };

        var files = CreateGenerator().Generate(dar);
        var item = files.First(f => f.RelativePath.EndsWith("Item.cs", StringComparison.Ordinal));

        item.Content.Should().Contain("ItemNonContractExtensions");
        item.Content.Should().Contain("ArchiveAsync(");
        item.Content.Should().Contain("User.Package.Archive argument,",
            "a user-defined Archive choice must use its actual argument type, not be silently mapped to DamlUnit");
        item.Content.Should().Contain("argument.ToRecord()",
            "the argument encoder must call ToRecord() on the user's actual Archive type, not submit DamlUnit.Instance");
        item.Content.Should().NotContain("ArgumentEncoder = _ => DamlUnit.Instance",
            "the Choice property must not use DamlUnit for the argument encoder when the Archive argument is a user type");
    }

    [Fact]
    public void Generate_user_Archive_interface_choice_gets_actual_argument_type_not_DamlUnit()
    {
        var userPkg = new DamlPackage
        {
            PackageId = "user-pkg-id",
            Name = "user-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules =
            [
                new DamlModule
                {
                    Name = "DA.Internal.Template",
                    Templates = [],
                    DataTypes =
                    [
                        new DamlDataType
                        {
                            Name = "Archive",
                            Definition = new DamlRecordDefinition(
                                [new DamlField("reason", new DamlPrimitiveType(DamlPrimitive.Text))]),
                        }
                    ],
                    Interfaces = [],
                }
            ],
            DependencyReferences = [],
        };

        var mainModule = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes = [],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = "Archivable",
                    Choices =                    [
                        new DamlChoice
                        {
                            Name = "Archive",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef(userPkg.PackageId, "DA.Internal.Template", "Archive"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        },
                    ],
                    ViewType = null,
                },
            ],
        };

        var dar = new DarModel
        {
            MainPackage = new DamlPackage
            {
                PackageId = "test-pkg",
                Name = "test-package",
                Version = new Version(1, 0, 0),
                LfVersion = "2.1",
                Modules = [mainModule],
                DependencyReferences = [],
            },
            Dependencies = [StdlibStub, userPkg],
        };

        var files = CreateGenerator().Generate(dar);
        var iface = files.First(f => f.RelativePath.EndsWith("IArchivable.cs", StringComparison.Ordinal));

        iface.Content.Should().Contain("IArchivableExtensions");
        iface.Content.Should().Contain("ArchiveAsync(");
        iface.Content.Should().Contain("User.Package.Archive argument,",
            "a user-defined Archive interface choice must use its actual argument type, not be silently mapped to DamlUnit");
        iface.Content.Should().Contain("argument.ToRecord()",
            "the argument encoder must call ToRecord() on the user's actual Archive type, not submit DamlUnit.Instance");
    }

    [Fact]
    public void Generate_should_emit_wrapper_with_resolved_type_for_non_archive_cross_package_argument()
    {
        var foreignModule = new DamlModule
        {
            Name = "Other.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "OrderRequest",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("payload", new DamlPrimitiveType(DamlPrimitive.Text))]),
                }
            ],
            Interfaces = [],
        };
        var foreignPackage = new DamlPackage
        {
            PackageId = "other-pkg-id",
            Name = "other-pkg",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [foreignModule],
            DependencyReferences = [],
        };

        var mainModule = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Trader",
                    Fields = [new DamlField("operator", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Submit",
                            Consuming = false,
                            ArgumentType = new DamlTypeRef("other-pkg-id", "Other.Module", "OrderRequest"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Numeric),
                        }
                    ]
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Trader",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("operator", new DamlPrimitiveType(DamlPrimitive.Party))]),
                }
            ],
            Interfaces = [],
        };
        var mainPackage = new DamlPackage
        {
            PackageId = "test-pkg",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [mainModule],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = mainPackage, Dependencies = [foreignPackage] };
        var files = CreateGenerator().Generate(dar);
        var trader = files.First(f => f.RelativePath.EndsWith("Trader.cs", StringComparison.Ordinal));

        trader.Content.Should().Contain("TraderNonContractExtensions");
        trader.Content.Should().Contain("SubmitAsync(");
        trader.Content.Should().Contain("Other.Pkg.OrderRequest argument,");
        trader.Content.Should().Contain("argument.ToRecord()");
    }

    [Fact]
    public void Generate_should_emit_interface_choice_extension_with_resolved_type_for_cross_package_argument()
    {
        var foreignModule = new DamlModule
        {
            Name = "Other.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "TransferRequest",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("amount", new DamlPrimitiveType(DamlPrimitive.Numeric))]),
                }
            ],
            Interfaces = [],
        };
        var foreignPackage = new DamlPackage
        {
            PackageId = "other-pkg-id",
            Name = "other-pkg",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [foreignModule],
            DependencyReferences = [],
        };

        var mainModule = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes = [],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = "Transferable",
                    Choices =                    [
                        new DamlChoice
                        {
                            Name = "Transfer",
                            Consuming = false,
                            ArgumentType = new DamlTypeRef("other-pkg-id", "Other.Module", "TransferRequest"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        },
                    ],
                    ViewType = null,
                },
            ],
        };
        var mainPackage = new DamlPackage
        {
            PackageId = "test-pkg",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [mainModule],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = mainPackage, Dependencies = [foreignPackage] };
        var files = CreateGenerator().Generate(dar);
        var iface = files.First(f => f.RelativePath.EndsWith("ITransferable.cs", StringComparison.Ordinal));

        iface.Content.Should().Contain("public static class ITransferableExtensions");
        iface.Content.Should().Contain("public static async Task<ExerciseOutcome<TransactionResult>> TransferAsync(");
        iface.Content.Should().Contain("Other.Pkg.TransferRequest argument,");
        iface.Content.Should().Contain("argument.ToRecord()");
    }

    [Fact]
    public void Generate_should_emit_interface_choice_extension_with_primitive_argument()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes = [],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = "Quotable",
                    Choices =                    [
                        new DamlChoice
                        {
                            Name = "Quote",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Text),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        },
                    ],
                    ViewType = null,
                },
            ],
        };

        var files = CreateGenerator().Generate(CreateDar(module));
        var iface = files.First(f => f.RelativePath.EndsWith("IQuotable.cs", StringComparison.Ordinal));

        iface.Content.Should().Contain("public static class IQuotableExtensions");
        iface.Content.Should().Contain("public static async Task<ExerciseOutcome<TransactionResult>> QuoteAsync(");
        iface.Content.Should().Contain("string argument,");
        iface.Content.Should().NotContain("QuoteArg argument,",
            "primitive args must not route through the GetChoiceArgumentInfo fallback that emits a non-existent <Choice>Arg type at namespace scope");
    }

    [Fact]
    public void Generate_should_resolve_cross_package_arg_when_simple_name_collides_with_local_record()
    {
        var foreignModule = new DamlModule
        {
            Name = "Other.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Quote",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("text", new DamlPrimitiveType(DamlPrimitive.Text))]),
                }
            ],
            Interfaces = [],
        };
        var foreignPackage = new DamlPackage
        {
            PackageId = "other-pkg-id",
            Name = "other-pkg",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [foreignModule],
            DependencyReferences = [],
        };

        var mainModule = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Trader",
                    Fields = [new DamlField("operator", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Submit",
                            Consuming = false,
                            ArgumentType = new DamlTypeRef("other-pkg-id", "Other.Module", "Quote"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Numeric),
                        }
                    ]
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Trader",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("operator", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "Quote",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("local", new DamlPrimitiveType(DamlPrimitive.Text))]),
                }
            ],
            Interfaces = [],
        };
        var mainPackage = new DamlPackage
        {
            PackageId = "test-pkg",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [mainModule],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = mainPackage, Dependencies = [foreignPackage] };
        var files = CreateGenerator().Generate(dar);
        var trader = files.First(f => f.RelativePath.EndsWith("Trader.cs", StringComparison.Ordinal));

        trader.Content.Should().Contain("Other.Pkg.Quote argument,",
            "the cross-package Quote ref must be resolved to its foreign namespace, not silently classified as the locally-named Quote record");
        trader.Content.Should().NotContain("Trader.Submit argument,",
            "a simple-name collision with a local record must not cause the cross-package ref to be qualified as a nested template arg");
    }

    [Fact]
    public void Generate_should_throw_for_nested_optional_return_type()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Sink",
                    Fields = [new DamlField("operator", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "MaybeMaybe",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.Optional),
                                [new DamlTypeApp(
                                    new DamlPrimitiveType(DamlPrimitive.Optional),
                                    [new DamlPrimitiveType(DamlPrimitive.Text)])]),
                        }
                    ]
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Sink",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("operator", new DamlPrimitiveType(DamlPrimitive.Party))]),
                }
            ],
            Interfaces = [],
        };

        var generator = CreateGenerator();
        var act = () => generator.Generate(CreateDar(module));

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*nested Optional*Optional (Optional t)*");
    }

    [Fact]
    public void Generate_should_throw_at_codegen_time_for_unresolvable_cross_package_ref()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Trader",
                    Fields = [new DamlField("operator", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Submit",
                            Consuming = false,
                            ArgumentType = new DamlTypeRef("missing-pkg-id", "Other.Module", "OrderRequest"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Numeric),
                        }
                    ]
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Trader",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("operator", new DamlPrimitiveType(DamlPrimitive.Party))]),
                }
            ],
            Interfaces = [],
        };

        var generator = CreateGenerator();
        var act = () => generator.Generate(CreateDar(module));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Other.Module:OrderRequest*missing-pkg-id*not present in the DAR*");
    }

    [Fact]
    public void Generate_should_skip_non_contract_wrapper_for_choice_with_fallback_argument_shape()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Trader",
                    Fields = [new DamlField("operator", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Quote",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Text),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Numeric),
                        }
                    ]
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Trader",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("operator", new DamlPrimitiveType(DamlPrimitive.Party))]),
                }
            ],
            Interfaces = [],
        };

        var files = CreateGenerator().Generate(CreateDar(module));
        var trader = files.First(f => f.RelativePath.EndsWith("Trader.cs", StringComparison.Ordinal));

        trader.Content.Should().NotContain("TraderNonContractExtensions");
        trader.Content.Should().NotContain("QuoteAsync(");
    }

    [Fact]
    public void Generate_csproj_should_not_reference_Canton_Ledger_Grpc_Client()
    {
        // After the lift the codegen-emitted wrappers reference
        // `ILedgerClient`, `TransactionResult`, and `ExerciseOutcome<>` from
        // the transport-agnostic `Daml.Ledger.Abstractions` package, not from
        // `Canton.Ledger.Grpc.Client`. The csproj generator must not pull
        // pure-projector consumers into a gRPC dep just to compile the
        // non-CID exerciser wrappers.
        var options = new CodeGenOptions
        {
            OutputDirectory = "/tmp/test",
            TargetFramework = "net10.0",
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
            DependencyReferences = [],
        };

        var file = generator.GenerateProjectFile(package);

        file.Content.Should().NotContain("Canton.Ledger.Grpc.Client");
        file.Content.Should().Contain("<PackageReference Include=\"Daml.Ledger.Abstractions\"");
    }
}
