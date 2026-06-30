// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.Model;
using AwesomeAssertions;
using Xunit;
using static Daml.Codegen.CSharp.Tests.TestHelpers.DamlModelBuilder;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public partial class CodeGenEdgeCaseTests
{
    #region Interface Placeholder Tests

    // Daml-LF emits a same-named empty record for every `interface I where ...`
    // declaration. Those records are the phantom type parameter for `ContractId I`,
    // and the codegen emits them as `: ITemplate` with throwing static metadata so
    // `ContractId<I>` keeps satisfying the runtime's `where T : ITemplate` constraint
    // while loudly failing any caller that tries to read template metadata directly.
    // See WriteInterfacePlaceholderRecord in CSharpCodeGenerator for the rationale.

    [Fact]
    public void Generate_should_emit_interface_placeholder_record_as_ITemplate_with_throwing_stubs()
    {
        // Arrange — declare an interface and the LF placeholder record that always
        // accompanies it.
        var module = new DamlModule
        {
            Name = "Test.Holding",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Holding",
                    Definition = new DamlRecordDefinition([])
                }
            ],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = "Holding",
                    Choices = [],
                    ViewType = null
                }
            ]
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var holdingRecord = files.FirstOrDefault(f =>
            f.RelativePath.EndsWith("Holding.cs", StringComparison.Ordinal)
            && !f.RelativePath.Contains("IHolding", StringComparison.Ordinal));

        // Assert
        holdingRecord.Should().NotBeNull("the LF placeholder record should be emitted alongside the interface");
        var code = holdingRecord!.Content;

        // Sealed record implementing ITemplate (NOT just IDamlRecord)
        code.Should().Contain("public sealed record Holding : ITemplate");
        // Throwing static metadata — InvalidOperationException with the qualified Daml name in the message
        code.Should().Contain("public static Identifier TemplateId =>");
        code.Should().Contain("throw new InvalidOperationException(\"'Holding' is the C# placeholder for the Daml interface 'Test.Holding:Holding'");
        code.Should().Contain("public static string PackageId =>");
        code.Should().Contain("public static string PackageName =>");
        code.Should().Contain("public static Version PackageVersion =>");
        // Empty ToRecord/FromRecord — placeholders carry no data
        code.Should().Contain("public DamlRecord ToRecord() => DamlRecord.Create();");
        code.Should().Contain("public static Holding FromRecord(DamlRecord record) => new Holding();");
    }

    [Fact]
    public void placeholder_emits_throwing_daml_type_descriptor()
    {
        var module = new DamlModule
        {
            Name = "Test.Holding",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Holding",
                    Definition = new DamlRecordDefinition([])
                }
            ],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = "Holding",
                    Choices = [],
                    ViewType = null
                }
            ]
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        var files = generator.Generate(dar);
        var holdingRecord = files.FirstOrDefault(f =>
            f.RelativePath.EndsWith("Holding.cs", StringComparison.Ordinal)
            && !f.RelativePath.Contains("IHolding", StringComparison.Ordinal));

        holdingRecord.Should().NotBeNull("the LF placeholder record should be emitted alongside the interface");
        var code = holdingRecord!.Content;

        code.Should().Contain("public static DamlTypeDescriptor DamlTypeId =>");
        code.Should().Contain(
            "public static DamlTypeDescriptor DamlTypeId =>\n        throw new InvalidOperationException(\"'Holding' is the C# placeholder for the Daml interface 'Test.Holding:Holding'");
        code.Should().NotContain("public static DamlTypeDescriptor DamlTypeId { get; }");
    }

    [Fact]
    public void Generate_should_emit_regular_record_when_no_matching_interface_in_same_module()
    {
        // Arrange — record name matches an interface in a DIFFERENT module; should
        // NOT be treated as a placeholder.
        var module = new DamlModule
        {
            Name = "Test.Records",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Holding",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("amount", new DamlPrimitiveType(DamlPrimitive.Numeric))
                    ])
                }
            ],
            Interfaces = []  // No interface in *this* module — the simple-name match in
                             // some other module must not leak in.
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var holdingFile = files.FirstOrDefault(f =>
            f.RelativePath.EndsWith("Holding.cs", StringComparison.Ordinal));

        // Assert
        holdingFile.Should().NotBeNull();
        var code = holdingFile!.Content;
        code.Should().Contain("public sealed record Holding([property: DamlFieldAttribute(\"amount\")] decimal Amount) : IDamlRecord");
        code.Should().NotContain(": ITemplate");
        code.Should().NotContain("InvalidOperationException");
    }

    [Fact]
    public void Generate_should_distinguish_interface_placeholders_across_modules_with_same_name()
    {
        // Arrange — module A has an interface `Token` (so the same-named record is a
        // placeholder); module B has an unrelated record `Token`. Each module must be
        // emitted with its own treatment.
        var modA = new DamlModule
        {
            Name = "App.A",
            Templates = [],
            DataTypes =
            [
                new DamlDataType { Name = "Token", Definition = new DamlRecordDefinition([]) }
            ],
            Interfaces =
            [
                new DamlInterface { Name = "Token", Choices = [], ViewType = null }
            ]
        };
        var modB = new DamlModule
        {
            Name = "App.B",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Token",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("symbol", new DamlPrimitiveType(DamlPrimitive.Text))
                    ])
                }
            ],
            Interfaces = []
        };

        var package = new DamlPackage
        {
            PackageId = "test-package-id",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [modA, modB],
            DependencyReferences = []
        };
        var dar = new DarModel { MainPackage = package, Dependencies = [] };
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar).ToList();
        // The codegen flattens all modules into a single namespace derived from the
        // package name, so file paths collide on simple names. Both `Token.cs` files
        // share a path; the latter overwrites the former. Verify by content instead:
        // the two Token records produce different bodies, and only one of them
        // becomes the placeholder in the emitted set.
        var tokenFiles = files.Where(f => f.RelativePath.EndsWith("Token.cs", StringComparison.Ordinal)).ToList();

        // Assert — both Token records exist (last-wins file path is acceptable here:
        // the regular record keeps its IDamlRecord shape, and the placeholder keeps
        // its ITemplate shape, with their qualifying logic running independently).
        tokenFiles.Should().NotBeEmpty();
        var hasPlaceholder = tokenFiles.Any(f => f.Content.Contains("public sealed record Token : ITemplate", StringComparison.Ordinal));
        var hasRegular = tokenFiles.Any(f => f.Content.Contains("public sealed record Token([property: DamlFieldAttribute(\"symbol\")] string Symbol) : IDamlRecord", StringComparison.Ordinal));
        hasPlaceholder.Should().BeTrue("module A's Token must be emitted as the interface placeholder");
        hasRegular.Should().BeTrue("module B's Token must keep its IDamlRecord regular-record shape");
    }

    // -------------------------------------------------------------------
    // Interface choice extension method tests — for every Daml interface
    // choice, codegen now emits a typed `<Choice>Async`-style helper on
    // `ContractId<I>` so consumers can do `await cid.TransferAsync(arg)`
    // without naming the concrete template. The generated extension class
    // sits beside the interface declaration in the same file.
    // -------------------------------------------------------------------

    [Fact]
    public void Generate_should_emit_extension_class_for_interface_choices()
    {
        // Arrange — interface with one record-argument choice and one Unit choice.
        // Both shapes are common: Splice's IHolding has both styles.
        var module = new DamlModule
        {
            Name = "Test.Holding",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Holding",
                    Definition = new DamlRecordDefinition([])
                },
                new DamlDataType
                {
                    Name = "Transfer",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("amount", new DamlPrimitiveType(DamlPrimitive.Numeric))
                    ])
                },
                new DamlDataType
                {
                    Name = "Transfer_Result",
                    Definition = new DamlRecordDefinition([])
                }
            ],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = "Holding",
                    ViewType = null,
                    Choices =                     [
                        new DamlChoice
                        {
                            Name = "Transfer",
                            Consuming = true,
                            ArgumentType = new DamlTypeRef("", "Test.Holding", "Transfer"),
                            ReturnType = new DamlTypeRef("", "Test.Holding", "Transfer_Result")
                        },
                        new DamlChoice
                        {
                            Name = "Lock",
                            Consuming = false,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit)
                        }
                    ]
                }
            ]
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var ifaceFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("IHolding.cs", StringComparison.Ordinal));

        // Assert — the file contains both the interface declaration AND a
        // sibling static extensions class with one method per choice.
        ifaceFile.Should().NotBeNull();
        var code = ifaceFile!.Content;

        // Marker-typed interface declaration is unchanged
        code.Should().Contain("public interface IHolding : IDamlInterface");

        // Sibling extensions class with one method per choice
        code.Should().Contain("public static class IHoldingExtensions");

        // Record-argument choice: async signature returning ExerciseOutcome<TransactionResult>
        // (mirrors the concrete-template <Choice>Async shape). Interface choices
        // surface the raw ExerciseOutcome<TransactionResult> because the implementing
        // template — and therefore any typed <Choice>Result projection — is unknown at
        // the call site.
        code.Should().Contain("public static async Task<ExerciseOutcome<TransactionResult>> TransferAsync(");
        code.Should().Contain("this ContractId<IHolding> contractId,");
        code.Should().Contain("ILedgerClient client,");
        code.Should().Contain("Transfer argument,");
        code.Should().Contain("Party actAs,");
        // Internally builds the command via the runtime ForInterface helper — the
        // wire-level template_id slot carries IHolding.InterfaceId, and the choice
        // argument is serialised via argument.ToRecord().
        code.Should().Contain("ExerciseCommand.ForInterface<IHolding>(contractId, new ChoiceName(\"Transfer\"), argument.ToRecord())");
        // Submission is funnelled through ILedgerClient.TrySubmitAndWaitForTransactionAsync
        // — same submission path as concrete-template <Choice>Async.
        code.Should().Contain("await client.TrySubmitAndWaitForTransactionAsync(submission, cancellationToken)");

        // Unit-argument choice: no `argument` parameter, DamlUnit.Instance is passed
        code.Should().Contain("public static async Task<ExerciseOutcome<TransactionResult>> LockAsync(");
        code.Should().Contain("ExerciseCommand.ForInterface<IHolding>(contractId, new ChoiceName(\"Lock\"), DamlUnit.Instance)");
    }

    [Fact]
    public void Generate_should_skip_extension_class_when_interface_has_no_methods()
    {
        // Arrange — view-only interface with no choices. No exerciser methods to
        // emit, so the extension class is suppressed (avoids an empty static
        // class littering the namespace).
        var module = new DamlModule
        {
            Name = "Test.Marker",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Marker",
                    Definition = new DamlRecordDefinition([])
                }
            ],
            Interfaces =
            [
                new DamlInterface { Name = "Marker", Choices = [], ViewType = null }
            ]
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var ifaceFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("IMarker.cs", StringComparison.Ordinal));

        // Assert
        ifaceFile.Should().NotBeNull();
        ifaceFile!.Content.Should().Contain("public interface IMarker : IDamlInterface");
        ifaceFile.Content.Should().NotContain("IMarkerExtensions");
    }

    [Fact]
    public void Generate_should_map_interface_typed_contract_id_fields_to_the_marker()
    {
        // Arrange
        var module = new DamlModule
        {
            Name = "Test.Holding",
            Templates =
            [
                new DamlTemplate { Name = "Account", Fields = [], Choices = [] }
            ],
            DataTypes =
            [
                new DamlDataType { Name = "Holding", Definition = new DamlRecordDefinition([]) },
                new DamlDataType
                {
                    Name = "Wallet",
                    Definition = new DamlRecordDefinition(
                    [
                        new DamlFieldDefinition("holding", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.ContractId),
                            [new DamlTypeRef("", "Test.Holding", "Holding")])),
                        new DamlFieldDefinition("holdings", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.List),
                            [new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.ContractId),
                                [new DamlTypeRef("", "Test.Holding", "Holding")])])),
                        new DamlFieldDefinition("maybeHolding", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.Optional),
                            [new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.ContractId),
                                [new DamlTypeRef("", "Test.Holding", "Holding")])])),
                        new DamlFieldDefinition("account", new DamlTypeApp(
                            new DamlPrimitiveType(DamlPrimitive.ContractId),
                            [new DamlTypeRef("", "Test.Holding", "Account")]))
                    ])
                }
            ],
            Interfaces =
            [
                new DamlInterface { Name = "Holding", Choices = [], ViewType = null }
            ]
        };

        var dar = CreateTestDar(module);
        var generator = CreateGenerator();

        // Act
        var files = generator.Generate(dar);
        var walletFile = files.FirstOrDefault(f => f.RelativePath.EndsWith("Wallet.cs", StringComparison.Ordinal));

        // Assert
        walletFile.Should().NotBeNull();
        var code = walletFile!.Content;

        code.Should().Contain("ContractId<IHolding>");
        code.Should().Contain("IReadOnlyList<ContractId<IHolding>>");
        code.Should().Contain("ContractId<IHolding>?", "an Optional interface contract id maps to the nullable marker");
        code.Should().NotContain("ContractId<Holding>", "the interface placeholder must never appear as a contract-id type argument");
        code.Should().Contain("ContractId<Account>", "contract ids to a real template are unchanged");
    }

    #endregion
}
