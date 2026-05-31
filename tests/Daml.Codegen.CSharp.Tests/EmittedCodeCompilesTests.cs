// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using Daml.Codegen.DarParser;
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
    public void Emitted_record_with_genmap_of_list_field_compiles()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Holding",
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = [],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Holding",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                    ])
                },
                new DamlDataType
                {
                    Name = "InstrumentId",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("id", new DamlPrimitiveType(DamlPrimitive.Text)),
                    ])
                },
                new DamlDataType
                {
                    Name = "BatchResult",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("senderChangeMap", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.GenMap),
                            [
                                new DamlTypeRef("", "Test.Module", "InstrumentId"),
                                new DamlTypeApp(
                                    new DamlPrimitiveType(DamlPrimitive.List),
                                    [new DamlTypeApp(
                                        new DamlPrimitiveType(DamlPrimitive.ContractId),
                                        [new DamlTypeRef("", "Test.Module", "Holding")])])
                            ])),
                    ])
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
            "GenMap-of-List FromRecord must compile without CS1503 against IReadOnlyDictionary<K,IReadOnlyList<V>>, but got: {0}",
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

    [Fact]
    public void Emitted_sibling_record_referencing_nested_choice_arg_type_compiles()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "MergeDelegation",
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "MergeDelegation_Merge",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef("", "Test.Module", "MergeDelegation_Merge"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "MergeDelegation",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "MergeDelegation_Merge",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("quantity", new DamlPrimitiveType(DamlPrimitive.Numeric))]),
                },
                new DamlDataType
                {
                    Name = "MergeDelegationCall",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("delegationCid", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.ContractId),
                            [new DamlTypeRef("", "Test.Module", "MergeDelegation")])),
                        new DamlField("choiceArg", new DamlTypeRef("", "Test.Module", "MergeDelegation_Merge")),
                    ]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "test-pkg",
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
            "a sibling record referencing a same-package nested choice-arg type must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_variant_constructor_referencing_nested_choice_arg_type_compiles()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "DsoRules",
                    Fields = [new DamlField("dso", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "DsoRules_AddSv",
                            Consuming = false,
                            ArgumentType = new DamlTypeRef("", "Test.Module", "DsoRules_AddSv"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "DsoRules",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("dso", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "DsoRules_AddSv",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("svParty", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "DsoRules_ActionRequiringConfirmation",
                    Definition = new DamlVariantDefinition(
                    [
                        new DamlVariantConstructor("SRARC_AddSv",
                            new DamlTypeRef("", "Test.Module", "DsoRules_AddSv")),
                    ]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "test-pkg",
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
            "a variant constructor referencing a same-package nested choice-arg type must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_cross_package_variant_constructor_referencing_nested_choice_arg_type_compiles()
    {
        var foreignModule = new DamlModule
        {
            Name = "Splice.Amulet",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "AmuletRules",
                    Fields = [new DamlField("operator", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "AmuletRules_MiningRound_Archive",
                            Consuming = false,
                            ArgumentType = new DamlTypeRef("foreign-pkg-id", "Splice.Amulet", "AmuletRules_MiningRound_Archive"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "AmuletRules",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("operator", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "AmuletRules_MiningRound_Archive",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("roundId", new DamlPrimitiveType(DamlPrimitive.Int64))]),
                },
            ],
            Interfaces = [],
        };

        var foreignPackage = new DamlPackage
        {
            PackageId = "foreign-pkg-id",
            Name = "splice-amulet",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [foreignModule],
            DependencyReferences = [],
        };

        var mainModule = new DamlModule
        {
            Name = "Test.Governance",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "AmuletRules_ActionRequiringConfirmation",
                    Definition = new DamlVariantDefinition(
                    [
                        new DamlVariantConstructor("CRARC_MiningRound_Archive",
                            new DamlTypeRef("foreign-pkg-id", "Splice.Amulet", "AmuletRules_MiningRound_Archive")),
                    ]),
                },
            ],
            Interfaces = [],
        };

        var mainPackage = new DamlPackage
        {
            PackageId = "main-pkg-id",
            Name = "test-governance",
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
            "a cross-package variant constructor referencing a nested choice-arg type must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_variant_constructor_with_contract_id_argument_compiles()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Asset",
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = [],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Asset",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "AnyValue",
                    Definition = new DamlVariantDefinition(
                    [
                        new DamlVariantConstructor("AnyContractId", ContractIdOf("Asset")),
                    ]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "test-pkg",
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
            "a variant constructor whose argument type is ContractId<T> must compile (the file needs `using Daml.Runtime.Contracts;`), but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void RequireForFieldType_recurses_into_arguments_when_base_is_a_nested_type_app()
    {
        var sb = new System.Text.StringBuilder();
        var indent = new IndentWriter(sb);

        var nestedBase = new DamlTypeApp(
            new DamlTypeRef("test-pkg", "Test.Module", "Wrapper"),
            [new DamlPrimitiveType(DamlPrimitive.Int64)]);
        var nestedAppCarryingContractId = new DamlTypeApp(
            nestedBase,
            [ContractIdOf("Asset")]);

        var method = typeof(CSharpCodeGenerator).GetMethod(
            "RequireForFieldType",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("RequireForFieldType not found");
        method.Invoke(null, [indent, nestedAppCarryingContractId]);

        indent.RequiredUsings.Should().Contain(
            "Daml.Runtime.Contracts",
            "any DamlTypeApp shape that transitively wraps a ContractId must add the runtime-contracts using, regardless of what the head's Base is (TypeRef, nested TypeApp, TypeVar) — otherwise a future MapDamlTypeToCSharp arm that learns to render the head type would emit `ContractId<...>` without the import.");
    }

    [Fact]
    public void Emitted_record_with_tuple_field_wrapping_contract_id_compiles()
    {
        var stdlibPackage = new DamlPackage
        {
            PackageId = "daml-prim-id",
            Name = "daml-prim",
            Version = new Version(0, 0, 0),
            LfVersion = "2.1",
            Modules =
            [
                new DamlModule
                {
                    Name = "DA.Types",
                    Templates = [],
                    DataTypes =
                    [
                        new DamlDataType
                        {
                            Name = "Tuple2",
                            TypeParams = ["a", "b"],
                            Definition = new DamlRecordDefinition([]),
                        },
                    ],
                    Interfaces = [],
                },
            ],
            DependencyReferences = [],
        };

        var contractIdTimesInt = new DamlTypeApp(
            new DamlTypeRef("daml-prim-id", "DA.Types", "Tuple2"),
            [ContractIdOf("Asset"), new DamlPrimitiveType(DamlPrimitive.Int64)]);

        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Asset",
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = [],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Asset",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "Holder",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("pair", contractIdTimesInt)]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "test-pkg",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarArchive { MainPackage = package, Dependencies = [stdlibPackage] };
        var files = CreateGenerator().Generate(dar);

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a record field typed as a parametric stdlib type wrapping ContractId<T> must compile (the file needs `using Daml.Runtime.Contracts;`), but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_code_compiles_when_package_namespace_ends_in_party()
    {
        // Regression for B3: the runtime type Daml.Runtime.Data.Party was emitted
        // as the bare identifier `Party`. When the package name derives a C# namespace
        // whose tail segment is `Party` (real DAR: canton-party-replication-alpha),
        // that namespace shadows the type and Roslyn reports CS0118. Every emitted
        // Party TYPE site must be global::-qualified. This exercises the field type,
        // the contract-key type + IHasKey<> argument, the choice actAs/controller
        // params, the signatory-derived CreateAsync, and the Observers(payload) helper.
        var module = new DamlModule
        {
            Name = "Replication",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Holding",
                    Fields =
                    [
                        new DamlField("issuer", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlField("amount", new DamlPrimitiveType(DamlPrimitive.Int64)),
                    ],
                    Key = new DamlPrimitiveType(DamlPrimitive.Party),
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("issuer")]),
                    Observers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Transfer",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = ContractIdOf("Holding"),
                            Controllers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                            Observers = DamlPartyAnalysis.Static([]),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Holding",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("issuer", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlField("amount", new DamlPrimitiveType(DamlPrimitive.Int64)),
                    ]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "canton-party-id",
            Name = "canton-party",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarArchive { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(
            f => f.Content.Contains("namespace Canton.Party", StringComparison.Ordinal),
            "the test only guards the shadowing bug if the derived namespace actually ends in .Party");

        // The key-bearing template emits a body-less `public partial ... Key { get; }`;
        // the consumer must supply the implementing partial. Declaring it in the
        // shadowing namespace also exercises the key type's global:: qualification.
        var consumerPartial = new GeneratedFile(
            RelativePath: "Holding.Consumer.cs",
            Content: """
                // Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.
                namespace Canton.Party;
                public sealed partial record Holding
                {
                    public partial global::Daml.Runtime.Data.Party Key => Issuer;
                }
                """);

        var diagnostics = CompileEmittedFiles([.. files, consumerPartial]);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code whose namespace ends in .Party must global::-qualify the runtime Party type, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_contract_id_head_is_global_qualified_when_namespace_collides()
    {
        var module = new DamlModule
        {
            Name = "Holdings",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Vault",
                    Fields = [new DamlField("custodian", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = [],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Vault",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("custodian", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "VaultRef",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("vaultCid", ContractIdOf("Vault"))]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "acme-contractid-id",
            Name = "acme-ContractId",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarArchive { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(
            f => f.Content.Contains("namespace Acme.ContractId", StringComparison.Ordinal),
            "the test only guards the shadowing bug if the derived namespace actually ends in .ContractId");

        var vaultRef = files.First(f => f.RelativePath.EndsWith("VaultRef.cs", StringComparison.Ordinal));
        vaultRef.Content.Should().Contain(
            "global::Daml.Runtime.Contracts.ContractId<",
            "the ContractId head must be global::-qualified when the surrounding namespace tail is `ContractId`, otherwise an unqualified `ContractId` is ambiguous with the enclosing namespace");
        vaultRef.Content.Should().NotContain(
            "(ContractId<",
            "no bare ContractId head should survive in the shadowing namespace");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code whose namespace ends in .ContractId must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_read_only_list_head_is_global_qualified_when_namespace_collides()
    {
        var module = new DamlModule
        {
            Name = "Generic",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Bag",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("items", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.List),
                            [new DamlPrimitiveType(DamlPrimitive.Text)])),
                    ]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "acme-collections-generic-id",
            Name = "acme-collections-generic-IReadOnlyList",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarArchive { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(
            f => f.Content.Contains("namespace Acme.Collections.Generic.IReadOnlyList", StringComparison.Ordinal),
            "the test only guards the shadowing bug if the derived namespace actually ends in .IReadOnlyList");

        var bag = files.First(f => f.RelativePath.EndsWith("Bag.cs", StringComparison.Ordinal));
        bag.Content.Should().Contain(
            "global::System.Collections.Generic.IReadOnlyList<",
            "the IReadOnlyList head must be global::-qualified when the surrounding namespace tail is `IReadOnlyList`");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code whose namespace ends in .IReadOnlyList must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_code_compiles_when_package_namespace_ends_in_itemplate()
    {
        var module = new DamlModule
        {
            Name = "Templates",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Asset",
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                    Choices = [],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Asset",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "acme-itemplate-id",
            Name = "acme-ITemplate",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarArchive { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(
            f => f.Content.Contains("namespace Acme.ITemplate", StringComparison.Ordinal),
            "the test only guards the shadowing bug if the derived namespace actually ends in .ITemplate");

        var asset = files.First(f => f.RelativePath.EndsWith("Asset.cs", StringComparison.Ordinal));
        asset.Content.Should().Contain(
            "global::Daml.Runtime.Contracts.ITemplate",
            "the ITemplate interface head must be global::-qualified when the surrounding namespace tail is `ITemplate`, otherwise it is ambiguous with the enclosing namespace (CS0118)");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code whose namespace ends in .ITemplate must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_code_compiles_when_package_namespace_ends_in_idamlvalue()
    {
        var module = new DamlModule
        {
            Name = "Values",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Payload",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("amount", new DamlPrimitiveType(DamlPrimitive.Int64))]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "acme-idamlvalue-id",
            Name = "acme-IDamlValue",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarArchive { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(
            f => f.Content.Contains("namespace Acme.IDamlValue", StringComparison.Ordinal),
            "the test only guards the shadowing bug if the derived namespace actually ends in .IDamlValue");

        var payload = files.First(f => f.RelativePath.EndsWith("Payload.cs", StringComparison.Ordinal));
        payload.Content.Should().Contain(
            "global::Daml.Runtime.Data.IDamlValue",
            "the IDamlValue interface head must be global::-qualified when the surrounding namespace tail is `IDamlValue`, otherwise it is ambiguous with the enclosing namespace (CS0118)");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code whose namespace ends in .IDamlValue must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Theory]
    [InlineData("acme-IHasView", "Acme.IHasView")]
    [InlineData("acme-IDamlInterface", "Acme.IDamlInterface")]
    [InlineData("acme-ExerciseCommand", "Acme.ExerciseCommand")]
    public void Emitted_interface_code_compiles_when_package_namespace_shadows_a_runtime_type(
        string packageName,
        string expectedNamespace)
    {
        // The interface-emission sites — the interface header
        // `: IDamlInterface, IHasView<view>`, the explicit-interface static
        // members, and the interface-choice `ExerciseCommand.ForInterface<...>`
        // extension — all route their runtime type names through the central
        // qualifier. The splice snapshot only covers the BARE direction; this
        // pins the SHADOWING direction (CS0118) for the three runtime types
        // those sites name: IHasView, IDamlInterface, and ExerciseCommand.
        var module = new DamlModule
        {
            Name = "Holdings",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "HoldingView",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = "Holding",
                    ViewType = new DamlTypeRef("", "Holdings", "HoldingView"),
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Transfer",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Party),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        },
                    ],
                },
            ],
        };

        var package = new DamlPackage
        {
            PackageId = "acme-iface-shadow-id",
            Name = packageName,
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarArchive { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(
            f => f.Content.Contains($"namespace {expectedNamespace}", StringComparison.Ordinal),
            "the test only guards the shadowing bug if the derived namespace actually ends in the runtime type name");

        var iface = files.First(f => f.RelativePath.EndsWith("IHolding.cs", StringComparison.Ordinal));
        iface.Content.Should().NotContain(
            "  Daml.Runtime.Commands.ExerciseCommand.ForInterface<",
            "the interface-choice ExerciseCommand head must route through the qualifier, never the hard-coded non-global fully-qualified form");
        iface.Content.Should().Contain(
            "ExerciseCommand.ForInterface<",
            "the interface-choice site still calls ExerciseCommand.ForInterface, qualified by the central qualifier (bare or global::-prefixed depending on the surrounding namespace)");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted interface code whose namespace shadows a runtime type must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_arity_protected_generic_heads_are_global_qualified_when_namespace_collides()
    {
        var module = new DamlModule
        {
            Name = "Choice",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Asset",
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Touch",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            Controllers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                            Observers = DamlPartyAnalysis.Static([]),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Asset",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "acme-choice-id",
            Name = "acme-Choice",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarArchive { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(
            f => f.Content.Contains("namespace Acme.Choice", StringComparison.Ordinal),
            "the test only guards the shadowing path if the derived namespace actually ends in .Choice");

        var asset = files.First(f => f.RelativePath.EndsWith("Asset.cs", StringComparison.Ordinal));
        asset.Content.Should().Contain(
            "global::Daml.Runtime.Commands.Choice<",
            "the Choice<> head must be global::-qualified when the surrounding namespace tail is `Choice`");
        asset.Content.Should().NotContain(
            " Choice<",
            "no bare Choice<> head should survive in the shadowing namespace");
        asset.Content.Should().Contain(
            "IExercises<Asset>",
            "IExercises<> is routed through the qualifier; with no .IExercises namespace collision it stays bare (collision-aware no-op)");
        asset.Content.Should().Contain(
            "IContract<ContractId, Asset>",
            "IContract<> is routed through the qualifier; with no .IContract namespace collision it stays bare (collision-aware no-op)");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code whose namespace ends in .Choice must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_contract_interface_heads_are_global_qualified_when_namespace_ends_in_icontract()
    {
        var module = new DamlModule
        {
            Name = "IContract",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Asset",
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                    Choices = [],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Asset",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "acme-icontract-id",
            Name = "acme-IContract",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarArchive { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(
            f => f.Content.Contains("namespace Acme.IContract", StringComparison.Ordinal),
            "the test only guards the shadowing path if the derived namespace actually ends in .IContract");

        var asset = files.First(f => f.RelativePath.EndsWith("Asset.cs", StringComparison.Ordinal));
        asset.Content.Should().Contain(
            "global::Daml.Runtime.Contracts.IContract<",
            "the IContract<> head must be global::-qualified when the surrounding namespace tail is `IContract`");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code whose namespace ends in .IContract must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_record_with_either_field_compiles()
    {
        var stdlibPackage = new DamlPackage
        {
            PackageId = "daml-prim-id",
            Name = "daml-prim",
            Version = new Version(0, 0, 0),
            LfVersion = "2.1",
            Modules =
            [
                new DamlModule
                {
                    Name = "DA.Types",
                    Templates = [],
                    DataTypes =
                    [
                        new DamlDataType
                        {
                            Name = "Either",
                            TypeParams = ["a", "b"],
                            Definition = new DamlVariantDefinition([]),
                        },
                    ],
                    Interfaces = [],
                },
            ],
            DependencyReferences = [],
        };

        var eitherTextInt = new DamlTypeApp(
            new DamlTypeRef("daml-prim-id", "DA.Types", "Either"),
            [new DamlPrimitiveType(DamlPrimitive.Text), new DamlPrimitiveType(DamlPrimitive.Int64)]);

        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Decision",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("outcome", eitherTextInt)]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "test-pkg",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarArchive { MainPackage = package, Dependencies = [stdlibPackage] };
        var files = CreateGenerator().Generate(dar);

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a record field typed as DA.Types:Either must map to Daml.Runtime.Stdlib.Either and compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));

        var emitted = string.Join("\n", files.Select(f => f.Content));
        emitted.Should().Contain(
            ".ToValue(",
            "the emitted Decision.ToValue must wire DA.Types:Either through Either.ToValue (EmitParametricStdlibToValue)");
        emitted.Should().Contain(
            "Either<string, long>.FromValue(",
            "the emitted decoder must wire DA.Types:Either through Either.FromValue (EmitParametricStdlibFromValue)");
    }

    [Fact]
    public void Emitted_code_compiles_when_package_namespace_ends_in_submitterinfo()
    {
        var module = new DamlModule
        {
            Name = "Submissions",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Asset",
                    Fields =
                    [
                        new DamlField("issuer", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                    ],
                    Signatories = DamlPartyAnalysis.Static(
                    [
                        new DamlPartyPayloadField("issuer"),
                        new DamlPartyPayloadField("owner"),
                    ]),
                    Observers = DamlPartyAnalysis.Static([]),
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Transfer",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = ContractIdOf("Asset"),
                            Controllers = DamlPartyAnalysis.Dynamic,
                            Observers = DamlPartyAnalysis.Static([]),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Asset",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("issuer", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                    ]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "acme-submitterinfo-id",
            Name = "acme-SubmitterInfo",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarArchive { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(
            f => f.Content.Contains("namespace Acme.SubmitterInfo", StringComparison.Ordinal),
            "the test only guards the shadowing bug if the derived namespace actually ends in .SubmitterInfo");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code whose namespace ends in .SubmitterInfo must global::-qualify the runtime SubmitterInfo type in parameter and constructor positions, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_code_compiles_when_package_namespace_ends_in_identifier()
    {
        var module = new DamlModule
        {
            Name = "Ids",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Asset",
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                    Choices = [],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Asset",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "acme-identifier-id",
            Name = "acme-Identifier",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarArchive { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(
            f => f.Content.Contains("namespace Acme.Identifier", StringComparison.Ordinal),
            "the test only guards the shadowing bug if the derived namespace actually ends in .Identifier");

        var asset = files.First(f => f.RelativePath.EndsWith("Asset.cs", StringComparison.Ordinal));
        asset.Content.Should().Contain(
            "global::Daml.Runtime.Data.Identifier TemplateId",
            "the Identifier TemplateId type must be global::-qualified when the surrounding namespace tail is `Identifier`");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code whose namespace ends in .Identifier must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_code_compiles_when_package_namespace_ends_in_damlparty()
    {
        var module = new DamlModule
        {
            Name = "Values",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Payload",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlField("amount", new DamlPrimitiveType(DamlPrimitive.Int64)),
                    ]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "acme-damlparty-id",
            Name = "acme-DamlParty",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarArchive { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(
            f => f.Content.Contains("namespace Acme.DamlParty", StringComparison.Ordinal),
            "the test only guards the shadowing bug if the derived namespace actually ends in .DamlParty");

        var payload = files.First(f => f.RelativePath.EndsWith("Payload.cs", StringComparison.Ordinal));
        payload.Content.Should().Contain(
            ".As<global::Daml.Runtime.Data.DamlParty>()",
            "the DamlParty cast must be global::-qualified when the surrounding namespace tail is `DamlParty`, otherwise the leading simple name resolves to the enclosing namespace (CS0118)");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code whose namespace ends in .DamlParty must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_exercise_outcome_head_is_global_qualified_when_namespace_collides()
    {
        var module = new DamlModule
        {
            Name = "Outcomes",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Asset",
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Touch",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Int64),
                            Controllers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("owner")]),
                            Observers = DamlPartyAnalysis.Static([]),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Asset",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "acme-exerciseoutcome-id",
            Name = "acme-ExerciseOutcome",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarArchive { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(
            f => f.Content.Contains("namespace Acme.ExerciseOutcome", StringComparison.Ordinal),
            "the test only guards the shadowing path if the derived namespace actually ends in .ExerciseOutcome");

        var emitted = string.Join("\n", files.Select(f => f.Content));
        emitted.Should().Contain(
            "global::Daml.Runtime.Outcomes.ExerciseOutcome<",
            "the ExerciseOutcome<> head must be global::-qualified when the surrounding namespace tail is `ExerciseOutcome` (arity protects the type lookup, but the qualifier must still emit the global:: form for shape consistency)");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code whose namespace ends in .ExerciseOutcome must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_key_bearing_template_in_party_colliding_namespace_has_no_cref_diagnostics()
    {
        // Regression for CS1584/CS1658: WriteKeyProperty embedded the rendered key
        // type inside the IHasKey cref braces. In a Party-shadowing namespace
        // MapDamlTypeToCSharp returns `global::Daml.Runtime.Data.Party`, so the cref
        // became IHasKey{global::Daml.Runtime.Data.Party} — a constructed type in
        // cref braces, which Roslyn rejects under DocumentationMode.Diagnose.
        var module = new DamlModule
        {
            Name = "Replication",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Holding",
                    Fields = [new DamlField("issuer", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Key = new DamlPrimitiveType(DamlPrimitive.Party),
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("issuer")]),
                    Choices = [],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Holding",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("issuer", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "canton-party-id",
            Name = "canton-party",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarArchive { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(
            f => f.Content.Contains("namespace Canton.Party", StringComparison.Ordinal),
            "the test only guards the cref-shadowing path if the derived namespace actually ends in .Party");

        var diagnostics = CompileEmittedFilesWithDocDiagnostics(files);
        var crefDiagnostics = diagnostics
            .Where(d => d.Id is "CS1574" or "CS1580" or "CS1584" or "CS1658")
            .ToList();
        crefDiagnostics.Should().BeEmpty(
            "emitted XML-doc crefs must be single-global::, well-formed names so no malformed-cref diagnostic surfaces under DocumentationMode.Diagnose, but got: {0}",
            string.Join("\n", crefDiagnostics.Select(e => e.Id + " " + e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_key_bearing_template_with_nested_generic_party_key_has_no_cref_diagnostics()
    {
        // Regression for CS1584/CS1658 on a key whose mapped C# form is a NESTED
        // generic wrapping an imported type: `List Party` renders as
        // IReadOnlyList<global::Daml.Runtime.Data.Party> in a Party-shadowing
        // namespace. ToCrefTypeArgument must strip the inner global:: AND escape
        // the nested <>, yielding the prose form IReadOnlyList{Daml.Runtime.Data.Party}.
        var module = new DamlModule
        {
            Name = "Replication",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Holding",
                    Fields = [new DamlField("issuer", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Key = new DamlTypeApp(
                        new DamlPrimitiveType(DamlPrimitive.List),
                        [new DamlPrimitiveType(DamlPrimitive.Party)]),
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("issuer")]),
                    Choices = [],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Holding",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("issuer", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "canton-party-id",
            Name = "canton-party",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarArchive { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        var holding = files.First(f => f.RelativePath.EndsWith("Holding.cs", StringComparison.Ordinal));
        holding.Content.Should().Contain(
            "of type <c>IReadOnlyList{Daml.Runtime.Data.Party}</c>",
            "the nested-generic key type appears in cref-escaped prose with every global:: stripped and the inner angle brackets rendered as braces");

        var diagnostics = CompileEmittedFilesWithDocDiagnostics(files);
        var crefDiagnostics = diagnostics
            .Where(d => d.Id is "CS1574" or "CS1580" or "CS1584" or "CS1658")
            .ToList();
        crefDiagnostics.Should().BeEmpty(
            "a nested-generic key over an imported type must produce a well-formed cref under DocumentationMode.Diagnose, but got: {0}",
            string.Join("\n", crefDiagnostics.Select(e => e.Id + " " + e.GetMessage() + " @ " + e.Location)));
    }

    private static IReadOnlyList<Diagnostic> CompileEmittedFilesWithDocDiagnostics(IReadOnlyList<GeneratedFile> files) =>
        CompileEmittedFiles(files, DocumentationMode.Diagnose);

    private static IReadOnlyList<Diagnostic> CompileEmittedFiles(IReadOnlyList<GeneratedFile> files) =>
        CompileEmittedFiles(files, DocumentationMode.Parse);

    private static IReadOnlyList<Diagnostic> CompileEmittedFiles(
        IReadOnlyList<GeneratedFile> files,
        DocumentationMode documentationMode)
    {
        var parseOptions = new CSharpParseOptions(documentationMode: documentationMode);
        var trees = files
            .Where(f => f.RelativePath.EndsWith(".cs", StringComparison.Ordinal))
            .Select(f => CSharpSyntaxTree.ParseText(f.Content, parseOptions, path: f.RelativePath))
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
