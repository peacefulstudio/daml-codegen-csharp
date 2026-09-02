// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// The active contract's key slot is named <c>Key</c>, and it sits in a record body that
/// nests <c>Contract</c> and <c>ContractId</c> types of its own. When the Daml key type
/// shares a spelling with one of those, or with the <c>Id</c> / <c>Data</c> / <c>Key</c>
/// members themselves, the slot and its decoder have to reach past the nearer name — a
/// compile question no text-compare drift test can answer. So is a template named after
/// its own key type, a key type named after a C# keyword, an <c>Optional</c> key whose
/// rendered <c>Name?</c> spelling no bare-name comparison matches, and a key-less template
/// whose own name collides with a member of the contract record its decoder stands in.
/// </summary>
public class EmittedContractKeySlotCompilesTests
{
    private static DarModel KeyedDar(string templateName, string keyTypeName) =>
        Dar(templateName, keyTypeName, new DamlTypeRef("", "Test.Module", keyTypeName));

    private static DarModel OptionalKeyedDar(string templateName, string keyTypeName) =>
        Dar(templateName, keyTypeName, OptionalOf(new DamlTypeRef("", "Test.Module", keyTypeName)));

    private static DarModel KeylessDar(string templateName) =>
        Dar(templateName, keyTypeName: null, key: null);

    private static DarModel Dar(string templateName, string? keyTypeName, DamlType? key)
    {
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate
                {
                    Name = templateName,
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Reissue",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                        },
                    ],
                    Key = key,
                },
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = templateName,
                    Definition = new DamlRecordDefinition(
                        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                },
                .. keyTypeName is null || keyTypeName == templateName
                    ? Array.Empty<DamlDataType>()
                    : [new DamlDataType
                    {
                        Name = keyTypeName,
                        Definition = new DamlRecordDefinition(
                            [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))]),
                    }],
            ],
            Interfaces = [],
        };

        return new DarModel
        {
            MainPackage = new DamlPackage
            {
                PackageId = "test-package-id",
                Name = "test-package",
                Version = new Version(1, 0, 0),
                LfVersion = "2.3",
                Modules = [module],
                DependencyReferences = [],
            },
            Dependencies = [],
        };
    }

    private static void CompilesCleanly(DarModel dar, string because)
    {
        var files = CreateGenerator().Generate(dar);

        var errors = CompileEmittedFiles(files).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

        errors.Should().BeEmpty(
            because + ", but got: {0}",
            string.Join("\n", errors.Select(e => e.GetMessage(CultureInfo.InvariantCulture) + " @ " + e.Location)));
    }

    [Theory]
    [InlineData("Key")]
    [InlineData("Data")]
    [InlineData("Id")]
    [InlineData("Contract")]
    [InlineData("ContractId")]
    public void EmittedContractKeySlot_compiles_when_the_key_type_is_named_after_a_contract_member(string keyTypeName)
    {
        CompilesCleanly(
            KeyedDar("Vault", keyTypeName),
            $"a Daml key type named '{keyTypeName}' collides with a positional parameter of the emitted active contract");
    }

    [Fact]
    public void EmittedContractKeySlot_compiles_when_the_template_is_named_after_its_own_key_type()
    {
        CompilesCleanly(
            KeyedDar("Key", "VaultKey"),
            "the template supplies the active contract's Data parameter type while the key supplies its Key parameter type");
    }

    [Fact]
    public void EmittedContractKeySlot_compiles_when_the_key_type_is_a_csharp_keyword()
    {
        CompilesCleanly(
            KeyedDar("Vault", "event"),
            "the key type escapes to @event, which is also the name the decoder gives its CreatedEvent parameter");
    }

    [Theory]
    [InlineData("Contract")]
    [InlineData("ContractId")]
    public void EmittedContractKeySlot_compiles_when_an_optional_key_wraps_a_type_named_after_a_nested_type(string keyTypeName)
    {
        CompilesCleanly(
            OptionalKeyedDar("Vault", keyTypeName),
            $"an Optional key over a Daml type named '{keyTypeName}' renders as '{keyTypeName}?', which binds to the nested {keyTypeName} unless the slot and its decoder qualify it");
    }

    [Theory]
    [InlineData("Id")]
    [InlineData("Data")]
    [InlineData("Key")]
    public void EmittedContractKeySlot_compiles_when_a_keyless_template_is_named_after_a_contract_member(string templateName)
    {
        CompilesCleanly(
            KeylessDar(templateName),
            $"a key-less template named '{templateName}' supplies the active contract's Data parameter type, and its decoder names the template inside a static member of the contract record");
    }

    [Theory]
    [InlineData("Contract")]
    [InlineData("ContractId")]
    public void EmittedContractKeySlot_refuses_a_keyless_template_named_after_a_type_it_nests(string templateName)
    {
        FluentActions.Invoking(() => CreateGenerator().Generate(KeylessDar(templateName)))
            .Should().Throw<CodegenException>(
                "the emitter nests a record of that name inside the template record, which CS0542 forbids, so generation must fail with one diagnostic instead of emitting ten cascading C# errors")
            .WithMessage($"*{templateName}*");
    }

    [Theory]
    [InlineData("Contract")]
    [InlineData("ContractId")]
    public void EmittedContractKeySlot_refuses_a_keyed_template_named_after_a_type_it_nests(string templateName)
    {
        FluentActions.Invoking(() => CreateGenerator().Generate(KeyedDar(templateName, "VaultKey")))
            .Should().Throw<CodegenException>(
                "the collision is in the template record's own body, so carrying a key changes nothing about it")
            .WithMessage($"*{templateName}*");
    }
}
