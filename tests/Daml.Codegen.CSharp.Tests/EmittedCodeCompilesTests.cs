// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
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
    public void Emitted_record_with_field_that_pascalcases_to_the_type_name_compiles()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Period",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("period", new DamlPrimitiveType(DamlPrimitive.Text)),
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
        var files = CreateGenerator().Generate(dar).ToList();

        var periodSource = files.Single(f => f.RelativePath.EndsWith("Period.cs", StringComparison.Ordinal)).Content;
        periodSource.Should().NotMatchRegex(
            @"\b(string|required)\s+Period\b\s*\{",
            "a property whose name equals the enclosing type Period would be CS0542; the field must be disambiguated");

        periodSource.Should().Contain("Period_",
            "the colliding member must be disambiguated to Period_");
        periodSource.Should().Contain("record.GetRequiredField(\"period\")",
            "deserialization must read the original Daml wire field name \"period\", unchanged by the C# member rename");
        periodSource.Should().Contain("Create(\"period\", ",
            "serialization must emit the original Daml wire field name \"period\", unchanged by the C# member rename");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a record whose field PascalCases to its own type name must still compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_variant_with_constructor_that_matches_the_type_name_compiles()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Wrapper",
                    Definition = new DamlVariantDefinition(
                    [
                        new DamlVariantConstructor("Wrapper", new DamlPrimitiveType(DamlPrimitive.Int64)),
                        new DamlVariantConstructor("Empty", null),
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
        var files = CreateGenerator().Generate(dar).ToList();

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a variant whose constructor name equals its own type name would be CS0542 unless disambiguated, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_template_with_key_compiles_standalone_with_throwing_key_stub()
    {
        // ADR 0013: the body-less `partial Key` (CS9248 until a consumer supplied
        // an implementing partial) blocked the automated DAR publish pipeline, which
        // has no human to write that partial. The accessor is now a non-partial
        // throwing stub, so a key-bearing package compiles standalone with no consumer
        // contribution. A regression that re-introduced the partial declaration would
        // surface here as CS9248.
        var files = GenerateKeyBearingTemplate();

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

        errors.Should().BeEmpty(
            "a key-bearing template must compile standalone now that Key is a throwing stub (ADR 0013), but got: {0}",
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
                        [new DamlFieldDefinition("payload", new DamlPrimitiveType(DamlPrimitive.Text))]),
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
                    Fields = [new DamlFieldDefinition("operator", new DamlPrimitiveType(DamlPrimitive.Party))],
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
                        [new DamlFieldDefinition("operator", new DamlPrimitiveType(DamlPrimitive.Party))]),
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

        var dar = new DarModel { MainPackage = mainPackage, Dependencies = [foreignPackage] };

        var options = new CodeGenOptions
        {
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
    public void Emitted_record_with_cross_package_enum_field_compiles_against_both_packages()
    {
        var foreignModule = new DamlModule
        {
            Name = "Other.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Severity",
                    Definition = new DamlEnumDefinition(["Low", "High"]),
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
                    Name = "Alert",
                    Fields =
                    [
                        new DamlFieldDefinition("operator", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("severity", new DamlTypeRef("other-pkg-id", "Other.Module", "Severity")),
                    ],
                    Choices = [],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Alert",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("operator", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("severity", new DamlTypeRef("other-pkg-id", "Other.Module", "Severity")),
                    ]),
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

        var dar = new DarModel { MainPackage = mainPackage, Dependencies = [foreignPackage] };

        var options = new CodeGenOptions
        {
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
            "a record field whose type is an enum from a dependency package must round-trip via the foreign enum's *Extensions.ToDamlEnum/FromDamlEnum, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_record_with_stdlib_DayOfWeek_enum_field_compiles()
    {
        var stdlibModule = new DamlModule
        {
            Name = "DA.Date.Types",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "DayOfWeek",
                    Definition = new DamlEnumDefinition(
                        ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"]),
                }
            ],
            Interfaces = [],
        };
        var stdlibPackage = new DamlPackage
        {
            PackageId = "daml-stdlib-id",
            Name = "daml-stdlib",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [stdlibModule],
            DependencyReferences = [],
        };

        var mainModule = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Schedule",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("day", new DamlTypeRef("daml-stdlib-id", "DA.Date.Types", "DayOfWeek")),
                    ]),
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

        var dar = new DarModel { MainPackage = mainPackage, Dependencies = [stdlibPackage] };

        var options = new CodeGenOptions
        {
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
            "a record field whose type is the stdlib DayOfWeek enum must round-trip via the runtime-provided Daml.Runtime.Stdlib.DayOfWeekExtensions, but got: {0}",
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
                    Fields = [new DamlFieldDefinition("operator", new DamlPrimitiveType(DamlPrimitive.Party))],
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
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("operator", new DamlPrimitiveType(DamlPrimitive.Party))]),
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
                    Fields = [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
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
                        new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                    ])
                },
                new DamlDataType
                {
                    Name = "InstrumentId",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("id", new DamlPrimitiveType(DamlPrimitive.Text)),
                    ])
                },
                new DamlDataType
                {
                    Name = "BatchResult",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("senderChangeMap", new DamlTypeApp(
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

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "GenMap-of-List FromRecord must compile without CS1503 against IReadOnlyDictionary<K,IReadOnlyList<V>>, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Generate_should_emit_empty_langversion_marker_for_key_bearing_output()
    {
        // ADR 0013: with the Key accessor reverted from a `partial` to a plain
        // throwing stub, key-bearing output no longer contains the C# 13
        // partial-property syntax, so the LangVersion marker stays empty just like
        // keyless output — the build no longer raises the SDK floor for keys.
        var files = GenerateKeyBearingTemplate();

        var marker = files.Should().ContainSingle(f => f.RelativePath == ".daml-langversion",
            "build-integration tooling reads this state file to bump LangVersion").Subject;
        marker.Content.Should().BeEmpty(
            "key-bearing output no longer requires C# 13 after the partial-property revert (ADR 0013)");
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
                    Fields = [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = [],
                    Key = null,
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "NoKey",
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

        var marker = files.Should().ContainSingle(f => f.RelativePath == ".daml-langversion").Subject;
        marker.Content.Should().BeEmpty(
            "keyless output records no required version, so the LangVersion bump is skipped");
    }

    [Fact]
    public void Emitted_class_template_with_key_compiles_standalone_with_throwing_key_stub()
    {
        // Class-mode (`UseRecordTypes=false`) counterpart of the record-mode standalone
        // test: a key-bearing `partial class` template must also compile with no
        // consumer contribution now that Key is a throwing stub (ADR 0013).
        var files = GenerateKeyBearingTemplate(useRecordTypes: false);

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

        errors.Should().BeEmpty(
            "a class-mode key-bearing template must compile standalone now that Key is a throwing stub (ADR 0013), but got: {0}",
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
                    Fields = [new DamlFieldDefinition("operator", new DamlPrimitiveType(DamlPrimitive.Party))],
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
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("operator", new DamlPrimitiveType(DamlPrimitive.Party))]),
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
                    Fields = [new DamlFieldDefinition("operator", new DamlPrimitiveType(DamlPrimitive.Party))],
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
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("operator", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "Report",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("twap", new DamlPrimitiveType(DamlPrimitive.Numeric)),
                        new DamlFieldDefinition("samples", new DamlPrimitiveType(DamlPrimitive.Int64)),
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
                    Fields = [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = [],
                    Key = new DamlPrimitiveType(DamlPrimitive.Text),
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "AssetWithKey",
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
                    Fields = [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
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
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "MergeDelegation_Merge",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("quantity", new DamlPrimitiveType(DamlPrimitive.Numeric))]),
                },
                new DamlDataType
                {
                    Name = "MergeDelegationCall",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("delegationCid", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.ContractId),
                            [new DamlTypeRef("", "Test.Module", "MergeDelegation")])),
                        new DamlFieldDefinition("choiceArg", new DamlTypeRef("", "Test.Module", "MergeDelegation_Merge")),
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

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a sibling record referencing a same-package nested choice-arg type must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_choice_argument_record_with_field_that_pascalcases_to_the_choice_name_compiles()
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Holding",
                    Fields = [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Quantity",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef("", "Test.Module", "Quantity"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
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
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "Quantity",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("quantity", new DamlPrimitiveType(DamlPrimitive.Numeric))]),
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

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar).ToList();

        var nestedSource = files
            .Single(f => f.RelativePath.EndsWith("Holding.Quantity.cs", StringComparison.Ordinal))
            .Content;
        nestedSource.Should().Contain("Quantity_",
            "the choice-arg-record field colliding with the nested choice type name must be disambiguated to Quantity_");
        nestedSource.Should().Contain("record.GetRequiredField(\"quantity\")",
            "the Daml wire field name \"quantity\" must be unchanged by the C# member rename");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a nested choice-arg record whose field PascalCases to the choice (and thus the nested record) name would be CS0542 unless disambiguated, but got: {0}",
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
                    Fields = [new DamlFieldDefinition("dso", new DamlPrimitiveType(DamlPrimitive.Party))],
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
                        [new DamlFieldDefinition("dso", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "DsoRules_AddSv",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("svParty", new DamlPrimitiveType(DamlPrimitive.Party))]),
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

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
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
                    Fields = [new DamlFieldDefinition("operator", new DamlPrimitiveType(DamlPrimitive.Party))],
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
                        [new DamlFieldDefinition("operator", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "AmuletRules_MiningRound_Archive",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("roundId", new DamlPrimitiveType(DamlPrimitive.Int64))]),
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

        var dar = new DarModel { MainPackage = mainPackage, Dependencies = [foreignPackage] };

        var options = new CodeGenOptions
        {
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
                    Fields = [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = [],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Asset",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
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

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a variant constructor whose argument type is ContractId<T> must compile (the file needs `using Daml.Runtime.Contracts;`), but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_variant_constructor_whose_payload_is_another_variant_compiles()
    {
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
                        new DamlVariantConstructor("Lit", new DamlPrimitiveType(DamlPrimitive.Int64)),
                        new DamlVariantConstructor("None", null),
                    ]),
                },
                new DamlDataType
                {
                    Name = "Outer",
                    Definition = new DamlVariantDefinition(
                    [
                        new DamlVariantConstructor("Wrap", new DamlTypeRef("", "Test.Module", "Inner")),
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

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a variant constructor whose payload is another variant must round-trip through that variant's ToVariant/FromVariant, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_record_with_a_variant_typed_field_compiles()
    {
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
                        new DamlVariantConstructor("Yes", new DamlPrimitiveType(DamlPrimitive.Int64)),
                        new DamlVariantConstructor("No", null),
                    ]),
                },
                new DamlDataType
                {
                    Name = "Holder",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("pick", new DamlTypeRef("", "Test.Module", "Choice"))]),
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

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a record field whose type is a variant must serialize via the variant's ToVariant/FromVariant, but got: {0}",
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

        var package = new DamlPackage
        {
            PackageId = "test-pkg",
            Name = "test-pkg",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = [],
        };
        var resolver = new DarCrossPackageResolver(
            new DarModel { MainPackage = package, Dependencies = [] }, new ConsoleLogger(0));

        StdlibPackages.RequireForFieldType(resolver, indent, nestedAppCarryingContractId);

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
                    Fields = [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = [],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Asset",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "Holder",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("pair", contractIdTimesInt)]),
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

        var dar = new DarModel { MainPackage = package, Dependencies = [stdlibPackage] };
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
                        new DamlFieldDefinition("issuer", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("amount", new DamlPrimitiveType(DamlPrimitive.Int64)),
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
                        new DamlFieldDefinition("issuer", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("amount", new DamlPrimitiveType(DamlPrimitive.Int64)),
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

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(
            f => f.Content.Contains("namespace Canton.Party", StringComparison.Ordinal),
            "the test only guards the shadowing bug if the derived namespace actually ends in .Party");

        // The key-bearing template emits a throwing `public ... Key =>` stub (ADR 0013)
        // whose key type must be global::-qualified so it doesn't resolve against the
        // shadowing `Canton.Party` namespace. The generated files compile standalone.
        var diagnostics = CompileEmittedFiles(files);
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
                    Fields = [new DamlFieldDefinition("custodian", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices = [],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Vault",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("custodian", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                new DamlDataType
                {
                    Name = "VaultRef",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("vaultCid", ContractIdOf("Vault"))]),
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

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
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
                        new DamlFieldDefinition("items", new DamlTypeApp(
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

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
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
                    Fields = [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
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
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
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

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
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
    public void Emitted_code_compiles_when_package_namespace_ends_in_idamlrecord()
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
                        [new DamlFieldDefinition("amount", new DamlPrimitiveType(DamlPrimitive.Int64))]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "acme-idamlrecord-id",
            Name = "acme-IDamlRecord",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(
            f => f.Content.Contains("namespace Acme.IDamlRecord", StringComparison.Ordinal),
            "the test only guards the shadowing bug if the derived namespace actually ends in .IDamlRecord");

        var payload = files.First(f => f.RelativePath.EndsWith("Payload.cs", StringComparison.Ordinal));
        payload.Content.Should().Contain(
            "global::Daml.Runtime.Data.IDamlRecord",
            "the IDamlRecord interface head must be global::-qualified when the surrounding namespace tail is `IDamlRecord`, otherwise it is ambiguous with the enclosing namespace (CS0118)");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted code whose namespace ends in .IDamlRecord must compile, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_code_compiles_when_package_namespace_ends_in_idamlvariant()
    {
        var module = new DamlModule
        {
            Name = "Values",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Choice",
                    Definition = new DamlVariantDefinition(
                        [
                            new DamlVariantConstructor("Yes", new DamlPrimitiveType(DamlPrimitive.Int64)),
                            new DamlVariantConstructor("No", null),
                        ]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "acme-idamlvariant-id",
            Name = "acme-IDamlVariant",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar);

        files.Should().Contain(
            f => f.Content.Contains("namespace Acme.IDamlVariant", StringComparison.Ordinal),
            "the test only guards the shadowing bug if the derived namespace actually ends in .IDamlVariant");

        var choice = files.First(f => f.RelativePath.EndsWith("Choice.cs", StringComparison.Ordinal));
        choice.Content.Should().Contain(
            "global::Daml.Runtime.Data.IDamlVariant",
            "the variant abstract base record head must be global::-qualified when the surrounding namespace tail is `IDamlVariant`, otherwise it is ambiguous with the enclosing namespace (CS0118)");

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
    [InlineData("daml", "Daml")]
    public void Emitted_interface_code_compiles_when_package_namespace_shadows_a_runtime_type(
        string packageName,
        string expectedNamespace)
    {
        var module = new DamlModule
        {
            Name = "Holdings",
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
                            Name = "Reissue",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Party),
                            ReturnType = ContractIdOf("Asset"),
                        },
                    ],
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "HoldingView",
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
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

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
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
        iface.Content.Should().NotContain(
            "cref=\"Daml.Runtime.",
            "interface doc crefs must use global:: so they resolve correctly when the generated namespace shadows the Daml root (e.g. package 'daml' -> namespace Daml.*)");
        iface.Content.Should().NotContain(
            "cref=\"Daml.Ledger.",
            "interface doc crefs must use global:: so they resolve correctly when the generated namespace shadows the Daml root (e.g. package 'daml' -> namespace Daml.*)");

        var asset = files.First(f => f.RelativePath.EndsWith("Asset.cs", StringComparison.Ordinal));
        asset.Content.Should().NotContain(
            "cref=\"Daml.Ledger.",
            "template extension doc crefs must use global:: so they resolve correctly when the generated namespace shadows the Daml root");

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
                    Fields = [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
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
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
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

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
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
                    Fields = [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
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
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
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

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
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
                        [new DamlFieldDefinition("outcome", eitherTextInt)]),
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

        var dar = new DarModel { MainPackage = package, Dependencies = [stdlibPackage] };
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
                        new DamlFieldDefinition("issuer", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
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
                        new DamlFieldDefinition("issuer", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
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

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
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
                    Fields = [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
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
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
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

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
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
                        new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("amount", new DamlPrimitiveType(DamlPrimitive.Int64)),
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

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
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
                    Fields = [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
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
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
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

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
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
                    Fields = [new DamlFieldDefinition("issuer", new DamlPrimitiveType(DamlPrimitive.Party))],
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
                        [new DamlFieldDefinition("issuer", new DamlPrimitiveType(DamlPrimitive.Party))]),
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

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
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
                    Fields = [new DamlFieldDefinition("issuer", new DamlPrimitiveType(DamlPrimitive.Party))],
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
                        [new DamlFieldDefinition("issuer", new DamlPrimitiveType(DamlPrimitive.Party))]),
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

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
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

    [Theory]
    [InlineData("acme-Daml")]
    [InlineData("acme-Stdlib-V1")]
    [InlineData("acme-Either-V1")]
    [InlineData("acme-RelTime-V1")]
    [InlineData("acme-Unit-V1")]
    [InlineData("acme-Set-V1")]
    [InlineData("acme-Map-V1")]
    [InlineData("acme-Tuple2-V1")]
    [InlineData("acme-Tuple3-V1")]
    [InlineData("acme-NonEmpty-V1")]
    public void Emitted_stdlib_types_compile_when_package_namespace_shadows_a_stdlib_type(string packageName)
    {
        // Phase 2 of routing Daml.Runtime.Stdlib.* through the central qualifier:
        // a record field typed as RelTime plus parametric stdlib types
        // (Either/Tuple2/Set/Map/NonEmpty) and a Unit-returning choice, all emitted
        // into a namespace whose tail segment shadows a stdlib simple name. The
        // qualifier must global::-qualify the shadowed names and the file must carry
        // `using Daml.Runtime.Stdlib;`, so the emitted code compiles with no CS0118.
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
                    Name = "DA.Time.Types",
                    Templates = [],
                    DataTypes =
                    [
                        new DamlDataType { Name = "RelTime", Definition = new DamlRecordDefinition([]) },
                    ],
                    Interfaces = [],
                },
                new DamlModule
                {
                    Name = "DA.Types",
                    Templates = [],
                    DataTypes =
                    [
                        new DamlDataType { Name = "Tuple2", TypeParams = ["a", "b"], Definition = new DamlRecordDefinition([]) },
                        new DamlDataType { Name = "Tuple3", TypeParams = ["a", "b", "c"], Definition = new DamlRecordDefinition([]) },
                        new DamlDataType { Name = "Either", TypeParams = ["a", "b"], Definition = new DamlRecordDefinition([]) },
                    ],
                    Interfaces = [],
                },
                new DamlModule
                {
                    Name = "DA.Set.Types",
                    Templates = [],
                    DataTypes =
                    [
                        new DamlDataType { Name = "Set", TypeParams = ["a"], Definition = new DamlRecordDefinition([]) },
                    ],
                    Interfaces = [],
                },
                new DamlModule
                {
                    Name = "DA.Map.Types",
                    Templates = [],
                    DataTypes =
                    [
                        new DamlDataType { Name = "Map", TypeParams = ["a", "b"], Definition = new DamlRecordDefinition([]) },
                    ],
                    Interfaces = [],
                },
                new DamlModule
                {
                    Name = "DA.Internal.Map",
                    Templates = [],
                    DataTypes =
                    [
                        new DamlDataType { Name = "Map", TypeParams = ["a", "b"], Definition = new DamlRecordDefinition([]) },
                    ],
                    Interfaces = [],
                },
                new DamlModule
                {
                    Name = "DA.NonEmpty.Types",
                    Templates = [],
                    DataTypes =
                    [
                        new DamlDataType { Name = "NonEmpty", TypeParams = ["a"], Definition = new DamlRecordDefinition([]) },
                    ],
                    Interfaces = [],
                },
            ],
            DependencyReferences = [],
        };

        var relTime = new DamlTypeRef("daml-prim-id", "DA.Time.Types", "RelTime");
        var eitherTextInt = new DamlTypeApp(
            new DamlTypeRef("daml-prim-id", "DA.Types", "Either"),
            [new DamlPrimitiveType(DamlPrimitive.Text), new DamlPrimitiveType(DamlPrimitive.Int64)]);
        var tuple2TextInt = new DamlTypeApp(
            new DamlTypeRef("daml-prim-id", "DA.Types", "Tuple2"),
            [new DamlPrimitiveType(DamlPrimitive.Text), new DamlPrimitiveType(DamlPrimitive.Int64)]);
        var setText = new DamlTypeApp(
            new DamlTypeRef("daml-prim-id", "DA.Set.Types", "Set"),
            [new DamlPrimitiveType(DamlPrimitive.Text)]);
        var mapTextInt = new DamlTypeApp(
            new DamlTypeRef("daml-prim-id", "DA.Map.Types", "Map"),
            [new DamlPrimitiveType(DamlPrimitive.Text), new DamlPrimitiveType(DamlPrimitive.Int64)]);
        var nonEmptyText = new DamlTypeApp(
            new DamlTypeRef("daml-prim-id", "DA.NonEmpty.Types", "NonEmpty"),
            [new DamlPrimitiveType(DamlPrimitive.Text)]);
        var tuple3TextIntText = new DamlTypeApp(
            new DamlTypeRef("daml-prim-id", "DA.Types", "Tuple3"),
            [
                new DamlPrimitiveType(DamlPrimitive.Text),
                new DamlPrimitiveType(DamlPrimitive.Int64),
                new DamlPrimitiveType(DamlPrimitive.Text),
            ]);
        var internalMapTextInt = new DamlTypeApp(
            new DamlTypeRef("daml-prim-id", "DA.Internal.Map", "Map"),
            [new DamlPrimitiveType(DamlPrimitive.Text), new DamlPrimitiveType(DamlPrimitive.Int64)]);
        var listOfEither = new DamlTypeApp(
            new DamlPrimitiveType(DamlPrimitive.List),
            [eitherTextInt]);
        var setOfTuple2 = new DamlTypeApp(
            new DamlTypeRef("daml-prim-id", "DA.Set.Types", "Set"),
            [tuple2TextInt]);

        var module = new DamlModule
        {
            Name = "Holdings",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Lock",
                    Fields =
                    [
                        new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("duration", relTime),
                    ],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Release",
                            Consuming = true,
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
                    Name = "Lock",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlFieldDefinition("duration", relTime),
                    ]),
                },
                new DamlDataType
                {
                    Name = "Bag",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("outcome", eitherTextInt),
                        new DamlFieldDefinition("pair", tuple2TextInt),
                        new DamlFieldDefinition("tags", setText),
                        new DamlFieldDefinition("scores", mapTextInt),
                        new DamlFieldDefinition("required", nonEmptyText),
                        new DamlFieldDefinition("triple", tuple3TextIntText),
                        new DamlFieldDefinition("internalScores", internalMapTextInt),
                        new DamlFieldDefinition("outcomes", listOfEither),
                        new DamlFieldDefinition("pairSet", setOfTuple2),
                    ]),
                },
            ],
            Interfaces = [],
        };

        var package = new DamlPackage
        {
            PackageId = "main-pkg-id",
            Name = packageName,
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };

        var dar = new DarModel { MainPackage = package, Dependencies = [stdlibPackage] };
        var files = CreateGenerator().Generate(dar);

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "emitted stdlib types must global::-qualify (no CS0118) and import Daml.Runtime.Stdlib when the namespace shadows a stdlib simple name, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_record_with_unmappable_object_field_compiles()
    {
        // Regression (#397): ToValue's `_` arm unconditionally emitted
        // `<value>.ToRecord()` for any field the type mapper falls back to
        // `object` for. `object` has no ToRecord(), so the emitted body was CS1061.
        // The fallback-producing shape is a higher-kinded application (`f a` where
        // the base is a type var), which no other MapType arm names. (Arrow types
        // like DA.Monoid.Types.Endo's `appEndo : a -> a` do not reach here: the
        // DarParser path maps BUILTIN_TYPE_ARROW to Unit, and the proto path throws
        // — per AGENTS.md §Gotchas.) `f a` is the parser-independent reproduction.
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Endo",
                    TypeParams = ["a"],
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("appEndo", new DamlTypeApp(
                            new DamlTypeVar("f"),
                            [new DamlTypeVar("a")])),
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

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar).ToList();

        var endoSource = files.Single(f => f.RelativePath.EndsWith("Endo.cs", StringComparison.Ordinal)).Content;
        endoSource.Should().NotContain("AppEndo.ToRecord()",
            "an object-typed (unmappable) field must not emit `.ToRecord()` — object has no such method");
        endoSource.Should().Contain("NotImplemented<DamlValue>(\"AppEndo\")",
            "an object-typed (unmappable) field must serialize via the GenericStub.NotImplemented stub");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a record with an unmappable object-typed field must compile (no .ToRecord() on object), but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_recursive_variant_with_direct_nested_variant_payload_compiles()
    {
        // Regression (#397): a recursive VARIANT like DA.Logic.Types.Formula<a>
        // has constructors (Negation/Conjunction/Disjunction) whose payloads are
        // themselves Formula<a> — a DamlTypeApp over the variant's own TypeRef.
        // ToValue's `_` arm wrongly emitted `Value.ToRecord()` for that payload
        // (Formula is IDamlVariant, exposing ToVariant() not ToRecord()), so the
        // ToVariant() body was CS1061.
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Formula",
                    TypeParams = ["a"],
                    Definition = new DamlVariantDefinition(
                    [
                        new DamlVariantConstructor("Proposition", new DamlTypeVar("a")),
                        new DamlVariantConstructor("Negation", new DamlTypeApp(
                            new DamlTypeRef("", "Test.Module", "Formula"),
                            [new DamlTypeVar("a")])),
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

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar).ToList();

        var formulaSource = files.Single(f => f.RelativePath.EndsWith("Formula.cs", StringComparison.Ordinal)).Content;
        formulaSource.Should().Contain("Value.ToVariant()",
            "a direct nested-variant payload must serialize via ToVariant(), not ToRecord()");
        formulaSource.Should().Contain(".FromVariant(",
            "a direct nested-variant payload must deserialize via FromVariant(), not FromRecord()");
        formulaSource.Should().NotContain(".FromRecord(",
            "a nested-variant payload has no FromRecord() — deserialization must use FromVariant()");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a recursive variant whose payload is the same variant must round-trip via ToVariant/FromVariant, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
    }

    [Fact]
    public void Emitted_variant_with_list_of_nested_variant_payload_compiles()
    {
        // Regression (#397): Formula<a>'s Conjunction/Disjunction carry
        // [Formula a] — a List of nested-variant payloads. The list element
        // serializer inside the `.Select(x => ...)` previously emitted
        // `x.ToRecord()`; it must emit `x.ToVariant()`.
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Formula",
                    TypeParams = ["a"],
                    Definition = new DamlVariantDefinition(
                    [
                        new DamlVariantConstructor("Proposition", new DamlTypeVar("a")),
                        new DamlVariantConstructor("Conjunction", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.List),
                            [new DamlTypeApp(
                                new DamlTypeRef("", "Test.Module", "Formula"),
                                [new DamlTypeVar("a")])])),
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

        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var files = CreateGenerator().Generate(dar).ToList();

        var formulaSource = files.Single(f => f.RelativePath.EndsWith("Formula.cs", StringComparison.Ordinal)).Content;
        formulaSource.Should().Contain("x.ToVariant()",
            "the list element serializer must convert nested-variant payloads via ToVariant(), not ToRecord()");

        var diagnostics = CompileEmittedFiles(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Should().BeEmpty(
            "a variant carrying a list of nested-variant payloads must round-trip via ToVariant/FromVariant, but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage() + " @ " + e.Location)));
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
