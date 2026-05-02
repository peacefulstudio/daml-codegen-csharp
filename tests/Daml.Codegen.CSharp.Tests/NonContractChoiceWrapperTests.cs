// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.DarReader;
using FluentAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Codegen-shape tests for the non-CID choice wrappers introduced by issue #63.
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

    private static DarArchive CreateDar(DamlModule module) =>
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
            Dependencies = [],
        };

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

        // Unit returns must surface Daml.Runtime.Stdlib.Unit at the call site —
        // not the wire-level DamlUnit. Both the type and the singleton accessor
        // are fully qualified so a user-defined Daml type named `Unit` in the
        // same namespace can't shadow the runtime marker.
        sink.Content.Should().Contain("public static async Task<ExerciseOutcome<Daml.Runtime.Stdlib.Unit>> DoNothingAsync(");
        sink.Content.Should().Contain("new ExerciseOutcome<Daml.Runtime.Stdlib.Unit>.One(Daml.Runtime.Stdlib.Unit.Value)");
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
        // (issue #60); no entry should appear in the non-contract extensions class.
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
        // (Mint) returns a bare ContractId T and is routed through #77's
        // FactoryExtensions / FromCreatedContracts path instead.
        factory.Content.Should().NotContain("FactoryNonContractExtensions");
        // Mint still gets a <Choice>Async method via #77's emission, in the
        // FactoryExtensions class. That path projects via FromCreatedContracts,
        // not the ExercisedEvents projector — so verify by structural hint.
        factory.Content.Should().Contain("public static class FactoryExtensions");
        factory.Content.Should().Contain("MintResult.FromCreatedContracts");
        // The non-CID projector helper specific to #63 must not appear.
        factory.Content.Should().NotContain("ProjectMintResult");
    }

    [Fact]
    public void Generate_should_not_emit_async_wrapper_for_optional_contract_id_return()
    {
        // Regression: returns containing a CID slot via Optional/List/Tuple
        // (here: `Optional (ContractId Coin)`) must flow only through #60's
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
                            ArgumentType = new DamlTypeRef("daml-prim", "DA.Internal.Template", "Archive"),
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
    public void Generate_should_skip_choice_with_non_archive_external_ref_argument()
    {
        // Regression: a choice whose argument type is a non-Archive external
        // DamlTypeRef (e.g. an imported record from another package) currently
        // resolves through GetChoiceArgumentInfo as ("DamlUnit", isExternalRef=true)
        // because the helper lacks the archive context to fully qualify the
        // cross-package name. Emitting a wrapper anyway would silently drop the
        // choice's actual argument and submit an empty unit payload. The filter
        // skips these choices until proper cross-package arg qualification
        // lands; tracked in a separate follow-up issue.
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
                            // Cross-package import — not Archive.
                            ArgumentType = new DamlTypeRef("other-pkg", "Other.Module", "OrderRequest"),
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

        // No wrapper for Submit — better to skip than to emit a wrapper that
        // silently drops the OrderRequest argument and submits empty unit.
        trader.Content.Should().NotContain("TraderNonContractExtensions");
        trader.Content.Should().NotContain("SubmitAsync(");
    }

    [Fact]
    public void Generate_csproj_should_not_reference_Canton_Ledger_Grpc_Client()
    {
        // After the lift (#73/#74) the codegen-emitted wrappers reference
        // `ILedgerClient`, `TransactionResult`, and `ExerciseOutcome<>` from
        // the transport-agnostic `Daml.Ledger.Abstractions` package, not from
        // `Canton.Ledger.Grpc.Client`. The csproj generator must not pull
        // pure-projector consumers into a gRPC dep just to compile the
        // non-CID exerciser wrappers from #63.
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
