// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class ChoiceEmitterInterfaceExtensionTests
{
    private const string LocalPackageId = "pkg-id";

    private sealed class StubResolver : ICrossPackageResolver
    {
        public string Resolve(DamlTypeRef typeRef, PackageEmitContext context) => Identifiers.Sanitize(typeRef.Name);

        public IReadOnlySet<string> DiscoveredExternalPackageIds => new HashSet<string>();

        public DamlPackage? LookupPackage(string packageId) => null;
    }

    private static DamlPackage Package() =>
        new()
        {
            PackageId = LocalPackageId,
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = [],
        };

    private static DamlChoice Choice(string name, DamlType argumentType) =>
        new()
        {
            Name = name,
            ArgumentType = argumentType,
            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
            Consuming = false,
            Controllers = DamlPartyAnalysis.Dynamic,
            Observers = DamlPartyAnalysis.Dynamic,
        };

    private static ChoiceEmitter Emitter()
    {
        var context = PackageEmitContext.ForPackage(Package(), new CodeGenOptions { RootNamespace = "Test.Package" });
        var resolver = new StubResolver();
        return new ChoiceEmitter(context, resolver, new CodeGenOptions { RootNamespace = "Test.Package" }, new DamlTypeMapper(context, resolver), new PartyAnalysis());
    }

    private static string EmitExtensions(DamlInterface iface)
    {
        var sb = new StringBuilder();
        var indent = new IndentWriter(sb);
        Emitter().WriteInterfaceChoiceExtensions(indent, iface, "I" + iface.Name);
        return sb.ToString();
    }

    private static DamlInterface Interface(params DamlChoice[] choices) =>
        new()
        {
            Name = "Asset",
            Choices = choices,
        };

    [Fact]
    public void emits_an_extensions_class_with_one_async_method_per_interface_choice()
    {
        var output = EmitExtensions(Interface(
            Choice("Transfer", new DamlPrimitiveType(DamlPrimitive.Unit)),
            Choice("Freeze", new DamlPrimitiveType(DamlPrimitive.Unit))));

        output.Should().Contain("public static class IAssetExtensions");
        output.Should().Contain("public static async Task<ExerciseOutcome<TransactionResult>> TransferAsync(");
        output.Should().Contain("public static async Task<ExerciseOutcome<TransactionResult>> FreezeAsync(");
        output.Should().Contain("this ContractId<IAsset> contractId,");
    }

    [Fact]
    public void interface_exerciser_builds_an_interface_typed_exercise_command()
    {
        var output = EmitExtensions(Interface(Choice("Transfer", new DamlPrimitiveType(DamlPrimitive.Unit))));

        output.Should().Contain("ExerciseCommand.ForInterface<IAsset>(contractId, new ChoiceName(\"Transfer\"), DamlUnit.Instance)");
    }

    [Fact]
    public void interface_with_no_choices_emits_no_extensions_class()
    {
        EmitExtensions(Interface()).Should().NotContain("public static class");
    }

    [Fact]
    public void interface_exerciser_accepts_optional_command_id_override()
    {
        var output = EmitExtensions(Interface(Choice("Transfer", new DamlPrimitiveType(DamlPrimitive.Unit))));

        output.Should().Contain("CommandId? commandId = null,");
        output.Should().Contain(".WithCommandId(commandId ?? new CommandId(Guid.NewGuid().ToString()));");

        var idxWorkflowId = output.IndexOf("string? workflowId = null,", StringComparison.Ordinal);
        var idxCommandId = output.IndexOf("CommandId? commandId = null,", StringComparison.Ordinal);
        var idxCancellationToken = output.IndexOf("CancellationToken cancellationToken = default)", StringComparison.Ordinal);
        idxWorkflowId.Should().BeLessThan(idxCommandId);
        idxCommandId.Should().BeLessThan(idxCancellationToken);
    }

    [Fact]
    public void interface_exerciser_forwards_optional_timeout()
    {
        var output = EmitExtensions(Interface(Choice("Transfer", new DamlPrimitiveType(DamlPrimitive.Unit))));

        output.Should().Contain("TimeSpan? timeout = null,");
        output.Should().Contain("client.TrySubmitAndWaitForTransactionAsync(submission, actAs, timeout: timeout, cancellationToken: cancellationToken)");

        var idxCommandId = output.IndexOf("CommandId? commandId = null,", StringComparison.Ordinal);
        var idxTimeout = output.IndexOf("TimeSpan? timeout = null,", StringComparison.Ordinal);
        var idxCancellationToken = output.IndexOf("CancellationToken cancellationToken = default)", StringComparison.Ordinal);
        idxCommandId.Should().BeLessThan(idxTimeout);
        idxTimeout.Should().BeLessThan(idxCancellationToken);
    }

    [Fact]
    public void interface_choice_emits_a_command_builder_that_returns_an_exercise_command()
    {
        var output = EmitExtensions(Interface(Choice("Transfer", new DamlPrimitiveType(DamlPrimitive.Unit))));

        output.Should().Contain("public static ExerciseCommand TransferCommand(");
        output.Should().Contain("this ContractId<IAsset> contractId)");
        output.Should().Contain("return ExerciseCommand.ForInterface<IAsset>(contractId, new ChoiceName(\"Transfer\"), DamlUnit.Instance);");
    }

    [Fact]
    public void interface_choice_async_method_delegates_to_the_command_builder_instead_of_building_inline()
    {
        var output = EmitExtensions(Interface(Choice("Transfer", new DamlPrimitiveType(DamlPrimitive.Unit))));

        output.Should().Contain("var command = contractId.TransferCommand();");
        output.Should().Contain("var submission = CommandsSubmission.Single(command)");
        output.Should().NotContain("var command = ExerciseCommand.ForInterface<IAsset>(contractId, new ChoiceName(\"Transfer\")");
    }

    [Fact]
    public void interface_choice_command_builder_accepts_the_typed_argument_when_the_choice_has_one()
    {
        var output = EmitExtensions(Interface(Choice("Transfer", new DamlTypeRef(LocalPackageId, "Main", "TransferArg"))));

        output.Should().Contain("public static ExerciseCommand TransferCommand(");
        output.Should().Contain("this ContractId<IAsset> contractId,");
        output.Should().Contain("TransferArg argument)");
        output.Should().Contain("return ExerciseCommand.ForInterface<IAsset>(contractId, new ChoiceName(\"Transfer\"), argument.ToRecord());");
        output.Should().Contain("var command = contractId.TransferCommand(argument);");
    }
}
