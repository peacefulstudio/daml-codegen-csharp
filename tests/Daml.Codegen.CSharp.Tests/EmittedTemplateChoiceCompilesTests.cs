// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class EmittedTemplateChoiceCompilesTests
{
    [Fact]
    public void Emitted_template_with_fallback_argument_choice_compiles()
    {
        // Regression: WriteChoiceMethod previously emitted
        //   `ArgumentEncoder = arg => arg.ToRecord()`
        // for any non-Unit, non-external choice argument type. When the type
        // hits the codegen fallback path (here: a non-Unit DamlPrimitiveType),
        // WriteChoiceArgumentType emits a stub `<Choice>Arg` record with no
        // ToRecord() method — so the static `Choice<T,A,R>` field site no
        // longer compiles in consumer output. The B3 gate already extended to
        // <Choice>Async emission; this test pins the same gate on
        // WriteChoiceMethod.
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Agreement",
                    Fields = [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Process",
                            Consuming = false,
                            // Non-Unit primitive — routes through the GetChoiceArgumentInfo
                            // fallback branch (IsFallback=true) and emits a stub ProcessArg
                            // record with no ToRecord().
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Text),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Agreement",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "test-package-id",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code for a fallback-argument-type choice should compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_template_with_create_bearing_choice_compiles()
    {
        // Bare ContractId<T> return is the simplest path that exercises:
        //   - the <Choice>Result record (one slot named after the template)
        //   - the FromCreatedContracts projector (cardinality + global:: qualifier)
        //   - the <Choice>Async extension (ILedgerClient + ExerciseOutcome plumbing)
        // Tuple returns hit the unrelated DA.Types:TupleN result-decoder mapping
        // path, which is orthogonal to this PR — covered by string-shape tests
        // upstream.
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Agreement",
                    Fields = [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Renew",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = ContractIdOf("Agreement"),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Agreement",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "test-package-id",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code should compile against Daml.Runtime + Daml.Ledger.Abstractions, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_create_bearing_choice_with_static_controllers_compiles_both_contractid_and_contract_overloads()
    {
        var fields = new[]
        {
            new DamlFieldDefinition("counterparty", new DamlPrimitiveType(DamlPrimitive.Party)),
            new DamlFieldDefinition("platform", new DamlPrimitiveType(DamlPrimitive.Party)),
        };

        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Agreement",
                    Fields = fields,
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Renew",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = ContractIdOf("Agreement"),
                            Controllers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("counterparty")]),
                        },
                    ],
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("counterparty")]),
                    Observers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Agreement",
                    Definition = new DamlRecordDefinition(fields),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "test-package-id",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        var content = files.First(f => f.RelativePath.EndsWith("Agreement.cs", StringComparison.Ordinal)).Content;
        content.Should().Contain("this Agreement.Contract contract,");
        content.Should().Contain("return contract.Id.RenewAsync(");

        var consumerHoldingFromCreatedEventResult = GeneratedFile.Text(
            "ReachabilityProbe.cs",
            """
            namespace Test.Package
            {
                internal static class ReachabilityProbe
                {
                    internal static System.Threading.Tasks.Task Use(
                        Agreement.Contract contract,
                        global::Daml.Ledger.Abstractions.ILedgerClient client) =>
                        contract.RenewAsync(client);
                }
            }
            """);

        var diagnostics = CompileEmittedFiles([.. files, consumerHoldingFromCreatedEventResult]);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "the payload-bearing overload must be reachable from a nested Agreement.Contract (the type FromCreatedEvent returns), but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_contract_overload_with_record_argument_and_multiple_controllers_compiles()
    {
        var fields = new[]
        {
            new DamlFieldDefinition("buyer", new DamlPrimitiveType(DamlPrimitive.Party)),
            new DamlFieldDefinition("seller", new DamlPrimitiveType(DamlPrimitive.Party)),
            new DamlFieldDefinition("broker", new DamlPrimitiveType(DamlPrimitive.Party)),
        };

        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Agreement",
                    Fields = fields,
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Settle",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef("", "Test.Module", "SettleArgs"),
                            ReturnType = ContractIdOf("Agreement"),
                            Controllers = DamlPartyAnalysis.Static(
                                [new DamlPartyPayloadField("buyer"), new DamlPartyPayloadField("seller")]),
                        },
                    ],
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("buyer")]),
                    Observers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("broker")]),
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Agreement",
                    Definition = new DamlRecordDefinition(fields),
                },
                new DamlDataType
                {
                    Name = "SettleArgs",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("memo", new DamlPrimitiveType(DamlPrimitive.Text))]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "test-package-id",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        var content = files.First(f => f.RelativePath.EndsWith("Agreement.cs", StringComparison.Ordinal)).Content;
        content.Should().Contain("this Agreement.Contract contract,");
        content.Should().Contain("return contract.Id.SettleAsync(");
        var idxArg = content.IndexOf("return contract.Id.SettleAsync(", StringComparison.Ordinal);
        var delegateBody = content[idxArg..];
        var idxArgument = delegateBody.IndexOf("argument,", StringComparison.Ordinal);
        var idxBuyer = delegateBody.IndexOf("contract.Data.Buyer,", StringComparison.Ordinal);
        var idxSeller = delegateBody.IndexOf("contract.Data.Seller,", StringComparison.Ordinal);
        idxArgument.Should().BeGreaterThan(0);
        idxArgument.Should().BeLessThan(idxBuyer);
        idxBuyer.Should().BeLessThan(idxSeller);

        var consumerHoldingFromCreatedEventResult = GeneratedFile.Text(
            "ReachabilityProbe.cs",
            """
            namespace Test.Package
            {
                internal static class ReachabilityProbe
                {
                    internal static System.Threading.Tasks.Task Use(
                        Agreement.Contract contract,
                        global::Daml.Ledger.Abstractions.ILedgerClient client) =>
                        contract.SettleAsync(client, default!);
                }
            }
            """);

        var diagnostics = CompileEmittedFiles([.. files, consumerHoldingFromCreatedEventResult]);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "the nested-Contract overload forwarding a record argument and multiple controllers should compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_template_with_payload_derived_observers_compiles()
    {
        // End-to-end: template with payload-derived signatories, controllers,
        // AND observers. The codegen should emit:
        //   - SubmissionExtensions.CreateAsync (payload-only)
        //   - SubmissionExtensions.Observers(payload) doc helper
        //   - <Template>Extensions.<Choice>Async with Party params for both
        //     controllers (actAs) and non-controller observers (readAs)
        //   - SubmitterInfo built locally with both actAs and readAs
        //   - submission.WithSubmitter(submitter) projection
        // All three concerns compile cleanly against the real
        // Daml.Runtime + Daml.Ledger.Abstractions surface.
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Agreement",
                    Fields =
                    [
                        new DamlFieldDefinition("platform", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("holder", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("issuer", new DamlPrimitiveType(DamlPrimitive.Party)),
                    ],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Renew",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = ContractIdOf("Agreement"),
                            Controllers = DamlPartyAnalysis.Static(
                                [new DamlPartyPayloadField("platform")]),
                            Observers = DamlPartyAnalysis.Static([]),
                        },
                    ],
                    Signatories = DamlPartyAnalysis.Static(
                        [new DamlPartyPayloadField("platform")]),
                    Observers = DamlPartyAnalysis.Static(
                        [new DamlPartyPayloadField("holder"), new DamlPartyPayloadField("issuer")]),
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Agreement",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("platform", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("holder", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("issuer", new DamlPrimitiveType(DamlPrimitive.Party)),
                    ]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "test-package-id",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "observer-aware emitted code should compile cleanly, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }
}
