// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.DarReader;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Smoke test that pipes the codegen-emitted source through Roslyn against the
/// real <c>Daml.Runtime</c> + <c>Daml.Ledger.Abstractions</c> assemblies. Pins
/// "the emitted shape compiles" against quiet drift: string-shape tests in
/// <see cref="ChoiceResultStructTests"/> and <see cref="ChoiceAsyncExerciserTests"/>
/// can pass while the surrounding template body introduces Roslyn errors — this
/// test fails on any such error-severity diagnostic. Warnings are not asserted
/// against; consumer projects choose their own warning policy.
/// </summary>
public class EmittedCodeCompilesTests
{
    private static CSharpCodeGenerator CreateGenerator(bool useRecordTypes = true)
    {
        var options = new CodeGenOptions
        {
            OutputDirectory = "/tmp/test",
            GenerateJsonSupport = true,
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            UseRecordTypes = useRecordTypes,
            UsePrimaryConstructors = useRecordTypes,
        };
        var logger = new ConsoleLogger(0);
        return new CSharpCodeGenerator(options, logger);
    }

    private static DamlType ContractIdOf(string templateName) =>
        new DamlTypeApp(
            new DamlPrimitiveType(DamlPrimitive.ContractId),
            [new DamlTypeRef("", "Test.Module", templateName)]);

    private static DamlType TupleType(params DamlType[] componentTypes) =>
        new DamlTypeApp(
            new DamlTypeRef("daml-prim", "DA.Types", $"Tuple{componentTypes.Length}"),
            componentTypes);

    private static DamlType OptionalOf(DamlType inner) =>
        new DamlTypeApp(new DamlPrimitiveType(DamlPrimitive.Optional), [inner]);

    [Fact]
    public void Emitted_template_with_fallback_argument_choice_compiles()
    {
        // Regression for #78: WriteChoiceMethod previously emitted
        //   `ArgumentEncoder = arg => arg.ToRecord()`
        // for any non-Unit, non-external choice argument type. When the type
        // hits the codegen fallback path (here: a non-Unit DamlPrimitiveType),
        // WriteChoiceArgumentType emits a stub `<Choice>Arg` record with no
        // ToRecord() method — so the static `Choice<T,A,R>` field site no
        // longer compiles in consumer output. The B3 gate already extended to
        // <Choice>Async emission in #77; this test pins the same gate on
        // WriteChoiceMethod.
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Agreement",
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
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
                    Definition = new DamlRecordDefinition([new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
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

        var dar = new DarArchive { MainPackage = package, Dependencies = [] };
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
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
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
                    Definition = new DamlRecordDefinition([new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
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

        var dar = new DarArchive { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code should compile against Daml.Runtime + Daml.Ledger.Abstractions, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_template_with_key_fails_to_compile_without_implementing_partial()
    {
        // PR #65 contract: a template with a key emits `public partial T Key { get; }`
        // — a body-less defining partial property. By design, the consumer MUST supply
        // an implementing partial declaration. If they don't, Roslyn reports CS9248
        // "Partial property '...Key' must have an implementation part." This test pins
        // that contract: a regression that emits a body (e.g. a throwing fallback or an
        // auto-property setter) would let the emitted source compile standalone, hiding
        // the deferred-work gap from consumers who would only discover it at runtime.
        var files = GenerateKeyBearingTemplate();

        var diagnostics = CompileEmittedFiles(files);

        diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().Contain(d => d.Id == "CS9248",
                "the body-less partial Key declaration must surface at compile time so consumers can't ship without supplying an implementing partial");
    }

    [Fact]
    public void Emitted_template_with_key_compiles_when_consumer_supplies_implementing_partial()
    {
        // The other half of the PR #65 contract: when the consumer DOES supply an
        // implementing partial declaration in the same compilation, the emitted code
        // compiles cleanly. Locks in the "extension point" half of the partial-property
        // shape — a regression that rejected valid implementing partials would block
        // every consumer of key-bearing templates.
        var files = GenerateKeyBearingTemplate();

        // Synthesise the implementing partial that a consuming project would write
        // by hand alongside the generated AssetWithKey.cs.
        // Namespace must match the codegen-emitted partial — the codegen uses the
        // package name (`Test.Package`), not the module name. Mismatched namespaces
        // are silently treated as different types, so neither partial finds its
        // counterpart and Roslyn emits CS9248 + CS9249 simultaneously.
        var consumerPartial = new GeneratedFile(
            RelativePath: "AssetWithKey.Consumer.cs",
            Content: """
                // Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.
                using Daml.Runtime.Data;
                namespace Test.Package;
                public sealed partial record AssetWithKey
                {
                    public partial string Key => Owner.Id;
                }
                """);

        var diagnostics = CompileEmittedFiles([.. files, consumerPartial]);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

        errors.Should().BeEmpty(
            "emitted code with a consumer-supplied implementing partial should compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_choice_with_cross_package_argument_compiles_against_both_packages()
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
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Trader",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("operator", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
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

        var dar = new DarArchive { MainPackage = mainPackage, Dependencies = [foreignPackage] };

        var options = new CodeGenOptions
        {
            OutputDirectory = "/tmp/test",
            GenerateJsonSupport = true,
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            UseRecordTypes = true,
            UsePrimaryConstructors = true,
            IncludeDependencies = true,
        };
        var generator = new CSharpCodeGenerator(options, new ConsoleLogger(0));
        var files = generator.Generate(dar);

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "cross-package choice arg wrappers must compile alongside the foreign package's emission, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_non_contract_wrapper_with_nested_unit_return_compiles()
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
                            Name = "MaybeNothing",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.Optional),
                                [new DamlPrimitiveType(DamlPrimitive.Unit)]),
                        },
                        new DamlChoice
                        {
                            Name = "ListOfUnits",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.List),
                                [new DamlPrimitiveType(DamlPrimitive.Unit)]),
                        },
                        new DamlChoice
                        {
                            Name = "MapOfUnits",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.TextMap),
                                [new DamlPrimitiveType(DamlPrimitive.Unit)]),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Sink",
                    Definition = new DamlRecordDefinition([new DamlField("operator", new DamlPrimitiveType(DamlPrimitive.Party))]),
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

        var dar = new DarArchive { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "non-CID wrappers with nested Unit returns must compile end-to-end, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Generate_should_emit_langversion_marker_with_value_13_when_output_contains_partial_property()
    {
        var files = GenerateKeyBearingTemplate();

        var marker = files.Should().ContainSingle(f => f.RelativePath == ".daml-langversion",
            "the MSBuild integration reads this state file to bump LangVersion").Subject;
        marker.Content.Trim().Should().Be("13",
            "key-bearing output requires C# 13 partial-property syntax");
    }

    [Fact]
    public void Generate_should_emit_empty_langversion_marker_for_keyless_output()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "NoKey",
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = [],
                    Key = null,
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "NoKey",
                    Definition = new DamlRecordDefinition([new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
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
        var dar = new DarArchive { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        var marker = files.Should().ContainSingle(f => f.RelativePath == ".daml-langversion").Subject;
        marker.Content.Should().BeEmpty(
            "keyless output records no required version, so the MSBuild bump is skipped");
    }

    [Fact]
    public void Emitted_class_template_with_key_compiles_when_consumer_supplies_implementing_partial_class()
    {
        // CHANGELOG documents `UseRecordTypes=false` as a supported configuration:
        // the generator emits a `partial class` (instead of `partial record`) and
        // the consumer's implementing partial must match the type kind. This test
        // pins that combination end-to-end through Roslyn so a class-mode
        // regression doesn't slip through CI while only the record case is
        // covered above.
        var files = GenerateKeyBearingTemplate(useRecordTypes: false);

        // Implementing partial declared as `partial class` to match the
        // generator's class-mode output. `Owner` is a regular property on the
        // generated class body (not a primary-constructor parameter).
        var consumerPartial = new GeneratedFile(
            RelativePath: "AssetWithKey.Consumer.cs",
            Content: """
                // Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.
                using Daml.Runtime.Data;
                namespace Test.Package;
                public sealed partial class AssetWithKey
                {
                    public partial string Key => Owner.Id;
                }
                """);

        var diagnostics = CompileEmittedFiles([.. files, consumerPartial]);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

        errors.Should().BeEmpty(
            "emitted class-mode code with a consumer-supplied `partial class` implementing partial should compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_non_contract_choice_wrapper_compiles_for_optional_unit_return()
    {
        // Regression: GetFromValueConversion previously had no DamlPrimitive.Unit
        // branch, so non-top-level () shapes — Optional (), [()], tuples
        // containing () — fell through to `default!` in the emitted decoder,
        // breaking typed projection at runtime. The Unit arm now decodes via
        // .As<DamlUnit>(), so the optional-of-unit return must compile and
        // produce a working DamlUnit?-typed decoder.
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Probe",
                    Fields = [new DamlField("operator", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "MaybeAck",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.Optional),
                                [new DamlPrimitiveType(DamlPrimitive.Unit)]),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Probe",
                    Definition = new DamlRecordDefinition([new DamlField("operator", new DamlPrimitiveType(DamlPrimitive.Party))]),
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

        var dar = new DarArchive { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted Optional () non-CID wrapper should compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));

        var probe = files.First(f => f.RelativePath.EndsWith("Probe.cs", StringComparison.Ordinal));
        // The decoder reuses GetFromValueConversion; the new Unit arm must
        // produce a .As<DamlUnit>() cast inside the optional decoder.
        probe.Content.Should().Contain(".As<DamlUnit>()");
        probe.Content.Should().NotContain("default! /* TODO: Implement deserialization for unit");
    }

    [Fact]
    public void Emitted_non_contract_choice_wrappers_compile_for_decimal_record_and_unit_returns()
    {
        // Pin the new <Choice>Async non-CID wrapper path against quiet drift —
        // string-shape tests in NonContractChoiceWrapperTests assert the
        // expected substrings, but only Roslyn catches missing qualifications,
        // shadowed type names, or missing imports. The shapes here mirror the
        // three return-type buckets Copilot called out: a primitive (Decimal),
        // a record, and Unit (the singleton-via-Daml.Runtime.Stdlib.Unit path).
        var module = new DamlModule
        {
            Name = "Test.Module",
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
                        },
                        new DamlChoice
                        {
                            Name = "ComputeReport",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlTypeRef("", "Test.Module", "Report"),
                        },
                        new DamlChoice
                        {
                            Name = "Tick",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Oracle",
                    Definition = new DamlRecordDefinition([new DamlField("operator", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "Report",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("twap", new DamlPrimitiveType(DamlPrimitive.Numeric)),
                        new DamlField("samples", new DamlPrimitiveType(DamlPrimitive.Int64)),
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

        var dar = new DarArchive { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted non-CID wrapper code (Decimal / record / Unit returns) should compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    private static IReadOnlyList<GeneratedFile> GenerateKeyBearingTemplate(bool useRecordTypes = true)
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "AssetWithKey",
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = [],
                    Key = new DamlPrimitiveType(DamlPrimitive.Text),
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "AssetWithKey",
                    Definition = new DamlRecordDefinition([new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
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

        var dar = new DarArchive { MainPackage = package, Dependencies = [] };
        return CreateGenerator(useRecordTypes).Generate(dar);
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
                        new DamlField("platform", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlField("holder", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlField("issuer", new DamlPrimitiveType(DamlPrimitive.Party)),
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
                        new DamlField("platform", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlField("holder", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlField("issuer", new DamlPrimitiveType(DamlPrimitive.Party)),
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

        var dar = new DarArchive { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "observer-aware emitted code should compile cleanly, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    private static IReadOnlyList<Diagnostic> CompileEmittedFiles(IReadOnlyList<GeneratedFile> files)
    {
        var trees = files
            .Where(f => f.RelativePath.EndsWith(".cs", StringComparison.Ordinal))
            .Select(f => CSharpSyntaxTree.ParseText(f.Content, path: f.RelativePath))
            .ToArray();

        // The TFM is net10.0 — pull system assemblies via reflection on a known type
        // (object lives in System.Private.CoreLib, GetReferenceAssemblies pattern
        // would require a separate package). We grab everything Daml.Runtime and
        // Daml.Ledger.Abstractions transitively reference, which covers the surface
        // emitted code touches.
        var runtimeAssemblies = new[]
        {
            typeof(object).Assembly,
            typeof(System.Linq.Enumerable).Assembly,
            typeof(System.Collections.Generic.IEnumerable<>).Assembly,
            typeof(System.Threading.Tasks.Task).Assembly,
            typeof(System.Console).Assembly,
        };

        var runtimeRefs = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        // Add explicit refs that may not be loaded yet.
        foreach (var asm in runtimeAssemblies)
        {
            if (!runtimeRefs.Any(r => r is PortableExecutableReference per && per.FilePath == asm.Location))
            {
                runtimeRefs.Add(MetadataReference.CreateFromFile(asm.Location));
            }
        }

        // Daml.Runtime + Daml.Ledger.Abstractions — referenced via project-ref.
        var damlRuntime = typeof(Daml.Runtime.Contracts.ITemplate).Assembly;
        var damlAbstractions = typeof(Daml.Ledger.Abstractions.ILedgerClient).Assembly;
        if (!runtimeRefs.Any(r => r is PortableExecutableReference per && per.FilePath == damlRuntime.Location))
        {
            runtimeRefs.Add(MetadataReference.CreateFromFile(damlRuntime.Location));
        }
        if (!runtimeRefs.Any(r => r is PortableExecutableReference per && per.FilePath == damlAbstractions.Location))
        {
            runtimeRefs.Add(MetadataReference.CreateFromFile(damlAbstractions.Location));
        }

        var compilation = CSharpCompilation.Create(
            assemblyName: "EmittedCodeCompilesTests-emit",
            syntaxTrees: trees,
            references: runtimeRefs,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        return compilation.GetDiagnostics();
    }
}
