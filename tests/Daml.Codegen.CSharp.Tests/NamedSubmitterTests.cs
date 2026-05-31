// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using Daml.Codegen.DarParser;
using FluentAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Codegen-shape tests for issue #68 (typed CreateAsync / &lt;Choice&gt;Async with one
/// parameter per signatory / controller). The static analyzer in the
/// <c>DarReader</c> namespace walks the Daml-LF expression tree; in unit
/// tests we pre-build the analysis directly on the model classes (bypassing
/// the proto layer) and assert on the emitted source.
///
/// <para>
/// The assertions focus on the public surface (signatures, parameter names,
/// presence/absence of payload-derived <c>actAs</c>) rather than the internal
/// command-construction wording, which can be polished without breaking
/// consumers.
/// </para>
/// </summary>
public class NamedSubmitterTests
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
            Dependencies = [StdlibStub],
        };

    /// <summary>
    /// Helper for the common shape: a template with three Party signatories,
    /// all referenced as payload fields. Mirrors the canonical Sample
    /// <c>Agreement</c> template (<c>signatory platform, initiator, counterparty</c>).
    /// </summary>
    private static DamlModule MakeAgreementModule(DamlPartyAnalysis signatories, DamlPartyAnalysis? archiveControllers = null)
    {
        var fields = new[]
        {
            new DamlField("platform", new DamlPrimitiveType(DamlPrimitive.Party)),
            new DamlField("initiator", new DamlPrimitiveType(DamlPrimitive.Party)),
            new DamlField("counterparty", new DamlPrimitiveType(DamlPrimitive.Party)),
            new DamlField("totalAmount", new DamlPrimitiveType(DamlPrimitive.Numeric)),
        };

        return new DamlModule
        {
            Name = "Sample.Agreements",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Agreement",
                    Fields = fields,
                    Choices = [],
                    Signatories = signatories,
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Agreement",
                    Definition = new DamlRecordDefinition(fields),
                }
            ],
            Interfaces = [],
        };
    }

    #region CreateAsync — payload-derived signatories

    [Fact]
    public void CreateAsync_with_payload_derived_signatories_omits_actAs_parameter()
    {
        var module = MakeAgreementModule(DamlPartyAnalysis.Static(
        [
            new DamlPartyPayloadField("platform"),
            new DamlPartyPayloadField("initiator"),
            new DamlPartyPayloadField("counterparty"),
        ]));

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Agreement.cs", StringComparison.Ordinal)).Content;

        // Public surface: extension class, payload-only CreateAsync.
        content.Should().Contain("public static class AgreementSubmissionExtensions");
        content.Should().Contain("public static Task<ExerciseOutcome<ContractId<Agreement>>> CreateAsync(");
        content.Should().Contain("this ILedgerClient client,");
        content.Should().Contain("Agreement payload,");
        // No explicit actAs parameter — the payload is sufficient.
        content.Should().NotContain("string actAs,");
    }

    [Fact]
    public void CreateAsync_with_payload_derived_signatories_unions_payload_party_fields_into_submitter()
    {
        var module = MakeAgreementModule(DamlPartyAnalysis.Static(
        [
            new DamlPartyPayloadField("platform"),
            new DamlPartyPayloadField("initiator"),
            new DamlPartyPayloadField("counterparty"),
        ]));

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Agreement.cs", StringComparison.Ordinal)).Content;

        // Each payload-derived party becomes a payload-property reference inside
        // the SubmitterInfo's HashSet<Party>. We assert on the property names
        // (PascalCased) rather than the surrounding HashSet boilerplate, which
        // could be polished later without breaking consumers.
        content.Should().Contain("new SubmitterInfo(new HashSet<Party>");
        content.Should().Contain("payload.Platform");
        content.Should().Contain("payload.Initiator");
        content.Should().Contain("payload.Counterparty");
        content.Should().Contain("client.TryCreateAsync<Agreement>(payload, submitter");
    }

    [Fact]
    public void CreateAsync_with_single_payload_derived_signatory_passes_party_directly()
    {
        // Single-signatory templates don't allocate a HashSet — the wrapper
        // passes the Party value, relying on the implicit conversion to
        // SubmitterInfo.
        var module = MakeAgreementModule(DamlPartyAnalysis.Static(
        [
            new DamlPartyPayloadField("platform"),
        ]));

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Agreement.cs", StringComparison.Ordinal)).Content;

        // Single-party fast-path: SubmitterInfo submitter = payload.Platform;
        content.Should().Contain("SubmitterInfo submitter = payload.Platform;");
        content.Should().NotContain("new HashSet<Party>");
    }

    [Fact]
    public void CreateAsync_with_dynamic_signatories_keeps_explicit_submitter_parameter()
    {
        // Dynamic = the analyzer couldn't resolve the signatory expression to
        // payload-field references. Codegen falls back to an explicit
        // SubmitterInfo parameter (which preserves single-party ergonomics
        // via implicit conversion from string/Party).
        var module = MakeAgreementModule(DamlPartyAnalysis.Dynamic);

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Agreement.cs", StringComparison.Ordinal)).Content;

        content.Should().Contain("public static Task<ExerciseOutcome<ContractId<Agreement>>> CreateAsync(");
        content.Should().Contain("SubmitterInfo submitter,");
        // No payload-derived `var submitter = ...` line.
        content.Should().NotContain("payload.Platform,");
    }

    [Fact]
    public void CreateAsync_with_unresolvable_payload_field_falls_back_to_dynamic()
    {
        // Analyzer claims `payload.unknownField` but no such field exists.
        // Codegen must demote to Dynamic — emitting `payload.UnknownField`
        // would not compile against the generated record.
        var module = MakeAgreementModule(DamlPartyAnalysis.Static(
        [
            new DamlPartyPayloadField("unknownField"),
        ]));

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Agreement.cs", StringComparison.Ordinal)).Content;

        // Demoted to dynamic — explicit submitter parameter, no bogus field.
        content.Should().Contain("SubmitterInfo submitter,");
        content.Should().NotContain("payload.UnknownField");
    }

    [Fact]
    public void CreateAsync_emits_extension_method_taking_ILedgerClient_as_this()
    {
        var module = MakeAgreementModule(DamlPartyAnalysis.Static(
        [
            new DamlPartyPayloadField("platform"),
        ]));

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Agreement.cs", StringComparison.Ordinal)).Content;

        // The wrapper is an extension method on ILedgerClient — the call site
        // reads `client.CreateAsync(payload)`.
        content.Should().Contain("this ILedgerClient client");
    }

    #endregion

    #region <Choice>Async — payload-derived controllers

    [Fact]
    public void ChoiceAsync_with_single_payload_derived_controller_emits_one_party_parameter()
    {
        // The typed-controller <Choice>Async surface is emitted on the
        // sibling <TemplateName>Extensions class (CSharpCodeGenerator.ChoiceResults.cs)
        // — its method takes one named Party parameter per declared
        // controller. Accept's controller list is `[counterparty]`, so the
        // wrapper signature carries a single `Party counterparty` parameter
        // and no fallback `SubmitterInfo submitter`.
        var module = new DamlModule
        {
            Name = "Sample.Agreements",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Offer",
                    Fields =
                    [
                        new DamlField("platform", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlField("counterparty", new DamlPrimitiveType(DamlPrimitive.Party)),
                    ],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Accept",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.ContractId),
                                [new DamlTypeRef("test-pkg", "Sample.Agreements", "Agreement")]),
                            Controllers = DamlPartyAnalysis.Static(
                                [new DamlPartyPayloadField("counterparty")]),
                        }
                    ],
                    Signatories = DamlPartyAnalysis.Static(
                    [
                        new DamlPartyPayloadField("platform"),
                        new DamlPartyPayloadField("counterparty"),
                    ]),
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Offer",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("platform", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlField("counterparty", new DamlPrimitiveType(DamlPrimitive.Party)),
                    ]),
                },
                new DamlDataType
                {
                    Name = "Agreement",
                    Definition = new DamlRecordDefinition([]),
                },
            ],
            Interfaces = [],
        };

        var files = CreateGenerator().Generate(CreateDar(module));
        var offer = files.First(f => f.RelativePath.EndsWith("Offer.cs", StringComparison.Ordinal)).Content;

        // The choice has a single Party-typed controller (counterparty). The
        // wrapper signature carries one named Party parameter — no string actAs,
        // no SubmitterInfo fallback.
        offer.Should().Contain("public static async Task<ExerciseOutcome<AcceptResult>> AcceptAsync(");
        offer.Should().Contain("Party counterparty,");
        offer.Should().NotContain("string actAs,");
        offer.Should().NotContain("SubmitterInfo submitter,");
    }

    [Fact]
    public void ChoiceAsync_with_multiple_payload_derived_controllers_emits_one_party_per_controller()
    {
        // A choice declared `controller initiator, counterparty` should accept
        // both parties as separate Party arguments. The typed-result
        // <Choice>Async wrapper (sibling <TemplateName>Extensions class) is
        // emitted only for create-bearing choices, so the test choice's return
        // type is a list of created contracts.
        var module = new DamlModule
        {
            Name = "Sample.Agreements",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Agreement",
                    Fields =
                    [
                        new DamlField("platform", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlField("initiator", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlField("counterparty", new DamlPrimitiveType(DamlPrimitive.Party)),
                    ],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Cancel",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.ContractId),
                                [new DamlTypeRef("test-pkg", "Sample.Agreements", "Agreement")]),
                            Controllers = DamlPartyAnalysis.Static(
                            [
                                new DamlPartyPayloadField("initiator"),
                                new DamlPartyPayloadField("counterparty"),
                            ]),
                        }
                    ],
                    Signatories = DamlPartyAnalysis.Static(
                    [
                        new DamlPartyPayloadField("platform"),
                        new DamlPartyPayloadField("initiator"),
                        new DamlPartyPayloadField("counterparty"),
                    ]),
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Agreement",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlField("platform", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlField("initiator", new DamlPrimitiveType(DamlPrimitive.Party)),
                        new DamlField("counterparty", new DamlPrimitiveType(DamlPrimitive.Party)),
                    ]),
                }
            ],
            Interfaces = [],
        };

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Agreement.cs", StringComparison.Ordinal)).Content;

        // Both controllers surface as named parameters in declaration order.
        content.Should().Contain("Party initiator,");
        content.Should().Contain("Party counterparty,");
        // Order: initiator must appear before counterparty in the signature.
        var idxInit = content.IndexOf("Party initiator,", StringComparison.Ordinal);
        var idxCp = content.IndexOf("Party counterparty,", StringComparison.Ordinal);
        idxInit.Should().BeLessThan(idxCp);
        // SubmitterInfo unions both controllers in actAs.
        content.Should().Contain("new SubmitterInfo(new HashSet<Party> { initiator, counterparty });");
    }

    [Fact]
    public void ChoiceAsync_with_dynamic_controllers_keeps_explicit_submitter_parameter()
    {
        // When the analyzer can't resolve controllers (e.g. they reference the
        // choice argument), codegen falls back to an explicit SubmitterInfo
        // parameter — preserving single-party callers via implicit conversion.
        var module = new DamlModule
        {
            Name = "Test",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Holding",
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Transfer",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            Controllers = DamlPartyAnalysis.Dynamic,
                        }
                    ],
                    Signatories = DamlPartyAnalysis.Dynamic,
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Holding",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                }
            ],
            Interfaces = [],
        };

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Holding.cs", StringComparison.Ordinal)).Content;

        // Both surfaces fall back to the explicit submitter shape.
        content.Should().Contain("SubmitterInfo submitter,");
    }

    [Fact]
    public void ChoiceAsync_for_archive_choice_is_not_emitted()
    {
        // The synthetic Archive choice is excluded — consumers exercise it via
        // the existing low-level Choice<T,A,R> property. A typed wrapper would
        // duplicate that surface without adding value.
        var module = new DamlModule
        {
            Name = "Test",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Asset",
                    Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
                    Choices =
                    [
                        // Synthetic Archive matches the shape Daml-LF emits at
                        // codegen time: argument is a DamlTypeRef pointing at
                        // DA.Internal.Template:Archive (the empty stdlib record),
                        // not a bare DamlPrimitive.Unit. The non-CID emitter's
                        // IsArchiveChoice filter keys on that shape to suppress
                        // a duplicate ArchiveAsync wrapper.
                        new DamlChoice
                        {
                            Name = "Archive",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef(
                                StdlibStub.PackageId,
                                "DA.Internal.Template",
                                "Archive"),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        }
                    ],
                    Signatories = DamlPartyAnalysis.Static(
                        [new DamlPartyPayloadField("owner")]),
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Asset",
                    Definition = new DamlRecordDefinition(
                        [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                }
            ],
            Interfaces = [],
        };

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Asset.cs", StringComparison.Ordinal)).Content;

        // CreateAsync still emitted, but no ArchiveAsync wrapper.
        content.Should().Contain("public static class AssetSubmissionExtensions");
        content.Should().Contain("public static Task<ExerciseOutcome<ContractId<Asset>>> CreateAsync(");
        content.Should().NotContain("ArchiveAsync(");
    }

    #endregion

    #region Mixed signatory shapes

    [Fact]
    public void Generate_brings_in_Daml_Ledger_Abstractions_using_for_named_submitter_extensions()
    {
        // Generated templates reference ILedgerClient — that interface lifted to
        // Daml.Ledger.Abstractions in #74, so the using is brought in
        // unconditionally so consumers don't need to add it. The transport-
        // specific Canton.Ledger.Grpc.Client using is no longer emitted.
        var module = MakeAgreementModule(DamlPartyAnalysis.Static(
            [new DamlPartyPayloadField("platform")]));

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Agreement.cs", StringComparison.Ordinal)).Content;

        content.Should().Contain("using Daml.Ledger.Abstractions;");
        content.Should().NotContain("using Canton.Ledger.Grpc.Client;");
    }

    #endregion

    #region Observer wiring (template- and choice-level)

    /// <summary>
    /// Builds a fixture template with three Party fields and configurable
    /// signatory / observer / controller analyses. Used by the observer-wiring
    /// tests below to construct minimal DARs that exercise the full
    /// payload-derived submitter code path.
    /// </summary>
    private static DamlModule MakeAgreementWithObservers(
        DamlPartyAnalysis signatories,
        DamlPartyAnalysis templateObservers,
        DamlPartyAnalysis choiceControllers,
        DamlPartyAnalysis choiceObservers)
    {
        var fields = new[]
        {
            new DamlField("platform", new DamlPrimitiveType(DamlPrimitive.Party)),
            new DamlField("holder", new DamlPrimitiveType(DamlPrimitive.Party)),
            new DamlField("issuer", new DamlPrimitiveType(DamlPrimitive.Party)),
        };

        return new DamlModule
        {
            Name = "Sample.Agreements",
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
                            ReturnType = new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.ContractId),
                                [new DamlTypeRef("test-pkg", "Sample.Agreements", "Agreement")]),
                            Controllers = choiceControllers,
                            Observers = choiceObservers,
                        }
                    ],
                    Signatories = signatories,
                    Observers = templateObservers,
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Agreement",
                    Definition = new DamlRecordDefinition(fields),
                }
            ],
            Interfaces = [],
        };
    }

    [Fact]
    public void Generate_emits_observers_helper_for_static_template_observer()
    {
        // Template with `observer holder, issuer` (both payload-field refs).
        // The SubmissionExtensions class should expose an
        // Observers(payload) helper that returns the derived party set.
        var module = MakeAgreementWithObservers(
            signatories: DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
            templateObservers: DamlPartyAnalysis.Static(
                [new DamlPartyPayloadField("holder"), new DamlPartyPayloadField("issuer")]),
            choiceControllers: DamlPartyAnalysis.Dynamic,
            choiceObservers: DamlPartyAnalysis.Dynamic);

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Agreement.cs", StringComparison.Ordinal)).Content;

        // Helper signature plus payload-derived body — declaration order preserved.
        content.Should().Contain("public static IReadOnlyList<Party> Observers(Agreement payload)");
        content.Should().Contain("payload.Holder");
        content.Should().Contain("payload.Issuer");
        // Helper returns a Party[] literal, not a SubmitterInfo.
        content.Should().Contain("return new Party[]");
    }

    [Fact]
    public void Generate_does_not_emit_observers_helper_for_dynamic_observer_expression()
    {
        // Dynamic observer expression — codegen can't statically derive the
        // observer set, so emitting a payload-only helper would either lie
        // (omit non-payload observers) or throw at runtime. Skip emission.
        var module = MakeAgreementWithObservers(
            signatories: DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
            templateObservers: DamlPartyAnalysis.Dynamic,
            choiceControllers: DamlPartyAnalysis.Dynamic,
            choiceObservers: DamlPartyAnalysis.Dynamic);

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Agreement.cs", StringComparison.Ordinal)).Content;

        // No documentation helper — caller is on the hook for figuring out
        // the observer set themselves, just as they are for the actAs set
        // when signatories are dynamic.
        content.Should().NotContain("Observers(Agreement payload)");
    }

    [Fact]
    public void Generate_does_not_emit_observers_helper_for_static_empty_observer_list()
    {
        // Daml's `observer []` literal — a deliberate "no observers" — resolves
        // statically but with an empty parties list. Emitting a helper that
        // always returns [] would be noise; skip it.
        var module = MakeAgreementWithObservers(
            signatories: DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
            templateObservers: DamlPartyAnalysis.Static([]),
            choiceControllers: DamlPartyAnalysis.Dynamic,
            choiceObservers: DamlPartyAnalysis.Dynamic);

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Agreement.cs", StringComparison.Ordinal)).Content;

        content.Should().NotContain("Observers(Agreement payload)");
    }

    [Fact]
    public void Generate_choice_async_threads_template_observers_into_readAs()
    {
        // Template observers `holder, issuer` plus controllers `[platform]`.
        // The choice async wrapper must:
        // - emit Party platform (the controller),
        // - emit Party holder, Party issuer (observers, which become readAs),
        // - build a SubmitterInfo with actAs={platform} and readAs={holder,issuer}.
        var module = MakeAgreementWithObservers(
            signatories: DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
            templateObservers: DamlPartyAnalysis.Static(
                [new DamlPartyPayloadField("holder"), new DamlPartyPayloadField("issuer")]),
            choiceControllers: DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
            choiceObservers: DamlPartyAnalysis.Static([]));

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Agreement.cs", StringComparison.Ordinal)).Content;

        // Method signature carries the controller and both observer parties.
        content.Should().Contain("Party platform,");
        content.Should().Contain("Party holder,");
        content.Should().Contain("Party issuer,");
        // Body builds a SubmitterInfo that routes platform into actAs and
        // holder/issuer into readAs.
        content.Should().Contain("actAs: new HashSet<Party> { platform }");
        content.Should().Contain("readAs: new HashSet<Party> { holder, issuer }");
        // The submission projects the SubmitterInfo via WithSubmitter.
        content.Should().Contain(".WithSubmitter(submitter)");
    }

    [Fact]
    public void Generate_choice_async_unions_choice_level_observer_into_readAs()
    {
        // Choice-level `observer issuer` adds issuer to the effective readAs.
        // When combined with template-level observer `holder`, the union is
        // {holder, issuer} (deduplicated), and any party already in actAs
        // (e.g. the controller) is excluded from readAs — the wire format
        // reflects act-as authorisation cleanly.
        var module = MakeAgreementWithObservers(
            signatories: DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
            templateObservers: DamlPartyAnalysis.Static([new DamlPartyPayloadField("holder")]),
            choiceControllers: DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
            choiceObservers: DamlPartyAnalysis.Static([new DamlPartyPayloadField("issuer")]));

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Agreement.cs", StringComparison.Ordinal)).Content;

        // Both observer-only parties surface as readAs entries (declaration order:
        // template-level first, then choice-level).
        content.Should().Contain("readAs: new HashSet<Party> { holder, issuer }");
    }

    [Fact]
    public void Generate_choice_async_with_no_observers_emits_no_readAs_contribution()
    {
        // No observers anywhere. The wrapper still uses SubmitterInfo (and the
        // SubmitterInfo overload on ILedgerClient) — readAs stays empty by
        // construction. The single-controller fast-path lets us pass the Party
        // directly via implicit conversion.
        var module = MakeAgreementWithObservers(
            signatories: DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
            templateObservers: DamlPartyAnalysis.Static([]),
            choiceControllers: DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
            choiceObservers: DamlPartyAnalysis.Static([]));

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Agreement.cs", StringComparison.Ordinal)).Content;

        // Single-controller fast-path: SubmitterInfo derived directly from
        // the named Party param (no HashSet allocation, no readAs argument).
        content.Should().Contain("SubmitterInfo submitter = platform;");
        content.Should().NotContain("readAs:");
        // The submission still uses the SubmitterInfo overload via WithSubmitter.
        content.Should().Contain(".WithSubmitter(submitter)");
    }

    [Fact]
    public void Generate_choice_async_with_observer_subset_of_controllers_does_not_duplicate_readAs()
    {
        // Edge case: an observer party that's also a controller. Daml allows
        // this (a signatory can also be observed, etc.) but the readAs set
        // shouldn't duplicate parties already in actAs — the analyzer/codegen
        // dedupes via the partition step.
        var module = MakeAgreementWithObservers(
            signatories: DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
            templateObservers: DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
            choiceControllers: DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
            choiceObservers: DamlPartyAnalysis.Static([]));

        var files = CreateGenerator().Generate(CreateDar(module));
        var content = files.First(f => f.RelativePath.EndsWith("Agreement.cs", StringComparison.Ordinal)).Content;

        // platform is already in actAs as the controller — no separate
        // readAs param, no readAs entry, the single-controller fast-path
        // takes over.
        content.Should().Contain("SubmitterInfo submitter = platform;");
        content.Should().NotContain("readAs:");
    }

    #endregion
}
