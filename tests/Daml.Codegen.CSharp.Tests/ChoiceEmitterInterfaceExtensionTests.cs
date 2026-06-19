// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using FluentAssertions;
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
}
