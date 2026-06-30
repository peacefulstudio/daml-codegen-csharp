// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class SubmissionExtensionsEmitterTests
{
    private sealed record UnknownPartyReference : DamlPartyReference;

    private readonly PartyAnalysis _party = new();

    private static DamlFieldDefinition PartyField(string name) =>
        new(name, new DamlPrimitiveType(DamlPrimitive.Party));

    private static DamlPartyAnalysis StaticParties(params string[] fieldNames) =>
        DamlPartyAnalysis.Static(fieldNames.Select(n => (DamlPartyReference)new DamlPartyPayloadField(n)).ToList());

    private static DamlTemplate Template(
        string name,
        IReadOnlyList<DamlFieldDefinition> fields,
        DamlPartyAnalysis? signatories = null,
        DamlPartyAnalysis? observers = null) =>
        new()
        {
            Name = name,
            Fields = fields,
            Choices = [],
            Signatories = signatories ?? DamlPartyAnalysis.Dynamic,
            Observers = observers ?? DamlPartyAnalysis.Dynamic,
        };

    private static PackageEmitContext Context(DamlTemplate template)
    {
        var package = new DamlPackage
        {
            PackageId = "pkg",
            Name = "Acme",
            Version = new Version(1, 0, 0),
            LfVersion = "1.15",
            Modules =
            [
                new DamlModule
                {
                    Name = "Main",
                    Templates = [template],
                    DataTypes = [],
                    Interfaces = [],
                },
            ],
            DependencyReferences = [],
        };
        return PackageEmitContext.ForPackage(package, new CodeGenOptions());
    }

    private string Emit(DamlTemplate template, CodeGenOptions? options = null)
    {
        var context = Context(template);
        var emitter = new SubmissionExtensionsEmitter(context, options ?? new CodeGenOptions(), _party);
        var sb = new StringBuilder();
        var indent = new IndentWriter(sb);
        emitter.TryWriteSubmissionExtensions(indent, template, template.Fields);
        return sb.ToString();
    }

    [Fact]
    public void emits_the_submission_extensions_class_for_a_template()
    {
        var template = Template(
            "Asset",
            [PartyField("owner")],
            signatories: StaticParties("owner"));

        var output = Emit(template);

        output.Should().Contain("public static class AssetSubmissionExtensions");
    }

    [Fact]
    public void emits_the_submission_extensions_class_even_when_signatories_are_dynamic()
    {
        var template = Template(
            "Vault",
            [PartyField("custodian")],
            signatories: DamlPartyAnalysis.Dynamic);

        var output = Emit(template);

        output.Should().Contain("public static class VaultSubmissionExtensions");
    }

    [Fact]
    public void static_single_signatory_create_derives_submitter_from_the_payload_and_omits_the_parameter()
    {
        var template = Template(
            "Asset",
            [PartyField("owner")],
            signatories: StaticParties("owner"));

        var output = Emit(template);

        output.Should().Contain("SubmitterInfo submitter = payload.Owner;");
        output.Should().NotContain("SubmitterInfo submitter,");
    }

    [Fact]
    public void static_multiple_signatory_create_builds_a_submitter_set_from_the_payload_fields()
    {
        var template = Template(
            "Trade",
            [PartyField("buyer"), PartyField("seller")],
            signatories: StaticParties("buyer", "seller"));

        var output = Emit(template);

        output.Should().Contain("var submitter = new SubmitterInfo(new HashSet<Party>");
        output.Should().Contain("payload.Buyer,");
        output.Should().Contain("payload.Seller");
        output.Should().NotContain("SubmitterInfo submitter,");
    }

    [Fact]
    public void dynamic_signatory_create_takes_an_explicit_submitter_parameter()
    {
        var template = Template(
            "Keyed",
            [PartyField("custodian")],
            signatories: DamlPartyAnalysis.Dynamic);

        var output = Emit(template);

        output.Should().Contain("SubmitterInfo submitter,");
        output.Should().Contain("return client.TryCreateAsync<Keyed>(payload, submitter, cancellationToken: cancellationToken);");
        output.Should().NotContain("= payload.");
    }

    [Fact]
    public void static_non_empty_observers_emit_the_observers_helper()
    {
        var template = Template(
            "Note",
            [PartyField("author"), PartyField("reader")],
            signatories: StaticParties("author"),
            observers: StaticParties("reader"));

        var output = Emit(template);

        output.Should().Contain("public static IReadOnlyList<Party> Observers(Note payload)");
        output.Should().Contain("payload.Reader");
    }

    [Fact]
    public void static_empty_observers_skip_the_observers_helper()
    {
        var template = Template(
            "Note",
            [PartyField("author")],
            signatories: StaticParties("author"),
            observers: DamlPartyAnalysis.Static([]));

        var output = Emit(template);

        output.Should().NotContain("Observers(Note payload)");
    }

    [Fact]
    public void dynamic_observers_skip_the_observers_helper()
    {
        var template = Template(
            "Note",
            [PartyField("author")],
            signatories: StaticParties("author"),
            observers: DamlPartyAnalysis.Dynamic);

        var output = Emit(template);

        output.Should().NotContain("Observers(Note payload)");
    }

    [Fact]
    public void unknown_signatory_subtype_falls_back_to_an_explicit_submitter_parameter()
    {
        var template = Template(
            "Asset",
            [PartyField("owner")],
            signatories: DamlPartyAnalysis.Static([new UnknownPartyReference()]));

        var output = Emit(template);

        output.Should().Contain("SubmitterInfo submitter,");
        output.Should().NotContain("= payload.");
    }
}
