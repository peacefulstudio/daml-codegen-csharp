// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class ChoiceEmitterNonContractExerciserTests
{
    private const string LocalPackageId = "pkg-id";

    private sealed class StubResolver : ICrossPackageResolver
    {
        public string Resolve(DamlTypeRef typeRef, PackageEmitContext context) => Identifiers.Sanitize(typeRef.Name);

        public IReadOnlySet<string> DiscoveredExternalPackageIds => new HashSet<string>();

        public DamlPackage? LookupPackage(string packageId) => null;
    }

    private static DamlPackage Package(DamlTemplate template) =>
        new()
        {
            PackageId = LocalPackageId,
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
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

    private static DamlChoice Choice(string name, DamlType returnType) =>
        new()
        {
            Name = name,
            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
            ReturnType = returnType,
            Consuming = false,
            Controllers = DamlPartyAnalysis.Dynamic,
            Observers = DamlPartyAnalysis.Dynamic,
        };

    private static DamlTemplate Template(params DamlChoice[] choices) =>
        new()
        {
            Name = "Vault",
            Fields = [],
            Choices = choices,
            Signatories = DamlPartyAnalysis.Dynamic,
            Observers = DamlPartyAnalysis.Dynamic,
        };

    private static string Emit(DamlTemplate template)
    {
        var package = Package(template);
        var context = PackageEmitContext.ForPackage(package, new CodeGenOptions { RootNamespace = "Test.Package" });
        var resolver = new StubResolver();
        var emitter = new ChoiceEmitter(context, resolver, new CodeGenOptions { RootNamespace = "Test.Package" }, new DamlTypeMapper(context, resolver), new PartyAnalysis());
        var sb = new StringBuilder();
        var indent = new IndentWriter(sb) { CurrentTypeName = template.Name };
        emitter.TryWriteNonContractChoiceExtensions(indent, template, context.DataTypes);
        return sb.ToString();
    }

    [Fact]
    public void value_returning_choice_emits_a_typed_async_exerciser_over_the_return_type()
    {
        var output = Emit(Template(Choice("Quote", new DamlPrimitiveType(DamlPrimitive.Numeric))));

        output.Should().Contain("public static class VaultNonContractExtensions");
        output.Should().Contain("public static async Task<ExerciseOutcome<decimal>> QuoteAsync(");
        output.Should().Contain("this ContractId<Vault> contractId,");
    }

    [Fact]
    public void value_returning_choice_emits_an_exercised_events_projector()
    {
        var output = Emit(Template(Choice("Quote", new DamlPrimitiveType(DamlPrimitive.Numeric))));

        output.Should().Contain("private static ExerciseOutcome<decimal> ProjectQuoteResult(TransactionResult tx, string contractId)");
        output.Should().Contain("foreach (var exercised in tx.ExercisedEvents)");
    }

    [Fact]
    public void unit_returning_choice_uses_the_stdlib_unit_decoder_path()
    {
        var output = Emit(Template(Choice("Touch", new DamlPrimitiveType(DamlPrimitive.Unit))));

        output.Should().Contain("public static async Task<ExerciseOutcome<Unit>> TouchAsync(");
        output.Should().Contain("ProjectTouchResult");
    }

    [Fact]
    public void contract_id_returning_choice_is_not_handled_here()
    {
        var cidReturn = new DamlTypeApp(
            new DamlPrimitiveType(DamlPrimitive.ContractId),
            [new DamlTypeRef(LocalPackageId, "Main", "Token")]);

        var output = Emit(Template(Choice("Spawn", cidReturn)));

        output.Should().BeEmpty();
    }

    [Fact]
    public void value_returning_choice_exerciser_accepts_optional_command_id_override()
    {
        var output = Emit(Template(Choice("Quote", new DamlPrimitiveType(DamlPrimitive.Numeric))));

        output.Should().Contain("CommandId? commandId = null,");
        output.Should().Contain(".WithCommandId(commandId ?? new CommandId(Guid.NewGuid().ToString()));");

        var idxWorkflowId = output.IndexOf("string? workflowId = null,", StringComparison.Ordinal);
        var idxCommandId = output.IndexOf("CommandId? commandId = null,", StringComparison.Ordinal);
        var idxCancellationToken = output.IndexOf("CancellationToken cancellationToken = default)", StringComparison.Ordinal);
        idxWorkflowId.Should().BeLessThan(idxCommandId);
        idxCommandId.Should().BeLessThan(idxCancellationToken);
    }

    [Fact]
    public void value_returning_choice_exerciser_forwards_optional_timeout()
    {
        var output = Emit(Template(Choice("Quote", new DamlPrimitiveType(DamlPrimitive.Numeric))));

        output.Should().Contain("TimeSpan? timeout = null,");
        output.Should().Contain("client.TrySubmitAndWaitForTransactionAsync(submission, timeout: timeout, cancellationToken: cancellationToken)");

        var idxCommandId = output.IndexOf("CommandId? commandId = null,", StringComparison.Ordinal);
        var idxTimeout = output.IndexOf("TimeSpan? timeout = null,", StringComparison.Ordinal);
        var idxCancellationToken = output.IndexOf("CancellationToken cancellationToken = default)", StringComparison.Ordinal);
        idxCommandId.Should().BeLessThan(idxTimeout);
        idxTimeout.Should().BeLessThan(idxCancellationToken);
    }
}
