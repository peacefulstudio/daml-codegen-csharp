// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using FluentAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Tests for typed choice-result struct emission.
///
/// For each choice whose return type carries one or more <c>ContractId T</c>
/// references, the generator should emit a nested <c>&lt;Choice&gt;Result</c> record
/// with one strongly-typed field per template, plus a static
/// <c>FromCreatedContracts(IEnumerable&lt;CreatedContract&gt;)</c> projector
/// that returns <c>ExerciseOutcome&lt;&lt;Choice&gt;Result&gt;</c> from
/// <c>Daml.Runtime.Outcomes</c>.
/// </summary>
public class ChoiceResultStructTests
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
            UsePrimaryConstructors = true
        };
        var logger = new ConsoleLogger(0);
        return new CSharpCodeGenerator(options, logger);
    }

    private static DarModel CreateTestDar(DamlModule module)
    {
        var package = new DamlPackage
        {
            PackageId = "test-package-id",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = []
        };

        var damlPrim = new DamlPackage
        {
            PackageId = "daml-prim",
            Name = "daml-prim",
            Version = new Version(0, 0, 0),
            LfVersion = "2.1",
            Modules = [],
            DependencyReferences = []
        };

        return new DarModel
        {
            MainPackage = package,
            Dependencies = [damlPrim]
        };
    }

    /// <summary>
    /// Daml-LF represents <c>(a, b, c)</c> tuples as <c>DA.Types:Tuple{N}</c>. This
    /// helper builds the corresponding type ref so test fixtures can model real
    /// choice return signatures without depending on a DAR fixture.
    /// </summary>
    private static DamlType TupleType(params DamlType[] componentTypes) =>
        new DamlTypeApp(
            new DamlTypeRef("daml-prim", "DA.Types", $"Tuple{componentTypes.Length}"),
            componentTypes);

    private static DamlType ContractIdOf(string templateName) =>
        new DamlTypeApp(
            new DamlPrimitiveType(DamlPrimitive.ContractId),
            [new DamlTypeRef("", "Test.Module", templateName)]);

    private static DamlType OptionalOf(DamlType inner) =>
        new DamlTypeApp(new DamlPrimitiveType(DamlPrimitive.Optional), [inner]);

    private static DamlType ListOf(DamlType inner) =>
        new DamlTypeApp(new DamlPrimitiveType(DamlPrimitive.List), [inner]);

    private static DamlTemplate Template(
        string name,
        DamlType returnType,
        string choiceName = "Run") =>
        new()
        {
            Name = name,
            Fields = [new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))],
            Choices =
            [
                new DamlChoice
                {
                    Name = choiceName,
                    Consuming = true,
                    ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                    ReturnType = returnType
                }
            ]
        };

    private static DamlModule ModuleWith(
        DamlTemplate template,
        params string[] siblingTemplateNames)
    {
        var dataTypes = new List<DamlDataType>
        {
            new()
            {
                Name = template.Name,
                Definition = new DamlRecordDefinition([new DamlField("owner", new DamlPrimitiveType(DamlPrimitive.Party))])
            }
        };
        foreach (var sibling in siblingTemplateNames)
        {
            dataTypes.Add(new DamlDataType
            {
                Name = sibling,
                Definition = new DamlRecordDefinition([])
            });
        }

        var templates = new List<DamlTemplate> { template };
        foreach (var sibling in siblingTemplateNames)
        {
            templates.Add(new DamlTemplate
            {
                Name = sibling,
                Fields = [],
                Choices = []
            });
        }

        return new DamlModule
        {
            Name = "Test.Module",
            Templates = templates,
            DataTypes = dataTypes,
            Interfaces = []
        };
    }

    private static string GenerateAndReadTemplate(DamlModule module, string templateName)
    {
        var dar = CreateTestDar(module);
        var generator = CreateGenerator();
        var files = generator.Generate(dar);
        var file = files.FirstOrDefault(f => f.RelativePath.EndsWith($"{templateName}.cs", StringComparison.Ordinal));
        file.Should().NotBeNull("the codegen should emit a file for template '{0}'", templateName);
        return file!.Content;
    }

    [Fact]
    public void Generate_should_emit_result_struct_for_single_create_tuple_return()
    {
        // Choice ExecuteSwap : (ContractId Agreement, ContractId SwapRecord)
        var module = ModuleWith(
            Template("Agreement", TupleType(
                ContractIdOf("Agreement"),
                ContractIdOf("SwapRecord")),
                choiceName: "ExecuteSwap"),
            siblingTemplateNames: ["SwapRecord"]);

        var code = GenerateAndReadTemplate(module, "Agreement");

        code.Should().Contain("public sealed record ExecuteSwapResult(ContractId<Agreement> Agreement, ContractId<SwapRecord> SwapRecord)");
        code.Should().Contain("public static ExerciseOutcome<ExecuteSwapResult> FromCreatedContracts(IEnumerable<CreatedContract> created)");
    }

    [Fact]
    public void Generate_should_emit_optional_field_for_optional_contract_id()
    {
        // Choice ExecuteSwap : (ContractId Agreement, Optional (ContractId AgreementRecord))
        var module = ModuleWith(
            Template("Agreement", TupleType(
                ContractIdOf("Agreement"),
                OptionalOf(ContractIdOf("AgreementRecord"))),
                choiceName: "ExecuteSwap"),
            siblingTemplateNames: ["AgreementRecord"]);

        var code = GenerateAndReadTemplate(module, "Agreement");

        code.Should().Contain("public sealed record ExecuteSwapResult(ContractId<Agreement> Agreement, ContractId<AgreementRecord>? AgreementRecord)");
    }

    [Fact]
    public void Generate_should_emit_list_field_for_list_of_contract_ids()
    {
        // Choice BulkCreate : [ContractId Item]
        var module = ModuleWith(
            Template("Factory", ListOf(ContractIdOf("Item")), choiceName: "BulkCreate"),
            siblingTemplateNames: ["Item"]);

        var code = GenerateAndReadTemplate(module, "Factory");

        code.Should().Contain("public sealed record BulkCreateResult(IReadOnlyList<ContractId<Item>> Item)");
    }

    [Fact]
    public void Generate_should_emit_single_create_alias_for_bare_contract_id_return()
    {
        // Choice CreateChild : ContractId Child
        var module = ModuleWith(
            Template("Parent", ContractIdOf("Child"), choiceName: "CreateChild"),
            siblingTemplateNames: ["Child"]);

        var code = GenerateAndReadTemplate(module, "Parent");

        code.Should().Contain("public sealed record CreateChildResult(ContractId<Child> Child)");
        code.Should().Contain("public static ExerciseOutcome<CreateChildResult> FromCreatedContracts(IEnumerable<CreatedContract> created)");
    }

    [Fact]
    public void Generate_should_skip_result_struct_when_return_type_carries_no_contract_ids()
    {
        // Choice GetCount : Int — non-creating, codegen should not emit a public
        // <Choice>Result record (the slot-based projector type). Non-CID
        // returns flow through the ExercisedEvents projector, which
        // emits a private `Project<Choice>Result` helper instead.
        var module = ModuleWith(
            Template("Counter", new DamlPrimitiveType(DamlPrimitive.Int64), choiceName: "GetCount"));

        var code = GenerateAndReadTemplate(module, "Counter");

        // The public <Choice>Result record is not emitted.
        code.Should().NotContain("public sealed record GetCountResult");
        // The slot-based FromCreatedContracts projector is not emitted.
        code.Should().NotContain("FromCreatedContracts");
    }

    [Fact]
    public void Generate_should_skip_result_struct_when_return_type_is_unit()
    {
        // Archive returns Unit — also non-creating; same rules as above.
        var module = ModuleWith(
            Template("Thing", new DamlPrimitiveType(DamlPrimitive.Unit), choiceName: "Close"));

        var code = GenerateAndReadTemplate(module, "Thing");

        code.Should().NotContain("public sealed record CloseResult");
    }

    [Fact]
    public void Generate_should_disambiguate_field_names_for_duplicate_templates_in_return()
    {
        // Choice that returns (ContractId Foo, ContractId Foo) needs distinct field names.
        var module = ModuleWith(
            Template("Splitter", TupleType(
                ContractIdOf("Half"),
                ContractIdOf("Half")),
                choiceName: "Split"),
            siblingTemplateNames: ["Half"]);

        var code = GenerateAndReadTemplate(module, "Splitter");

        // First slot keeps the bare name; second gets a numeric suffix.
        code.Should().Contain("public sealed record SplitResult(ContractId<Half> Half, ContractId<Half> Half2)");
    }

    [Fact]
    public void Generate_should_emit_three_field_struct_for_three_tuple_with_optional_third()
    {
        // Mirrors a real ExecuteSwap signature:
        //   (ContractId Agreement, ContractId SwapRecord, Optional (ContractId AgreementRecord))
        var module = ModuleWith(
            Template("Agreement", TupleType(
                ContractIdOf("Agreement"),
                ContractIdOf("SwapRecord"),
                OptionalOf(ContractIdOf("AgreementRecord"))),
                choiceName: "ExecuteSwap"),
            siblingTemplateNames: ["SwapRecord", "AgreementRecord"]);

        var code = GenerateAndReadTemplate(module, "Agreement");

        code.Should().Contain(
            "public sealed record ExecuteSwapResult("
            + "ContractId<Agreement> Agreement, "
            + "ContractId<SwapRecord> SwapRecord, "
            + "ContractId<AgreementRecord>? AgreementRecord)");
    }

    [Fact]
    public void Generate_should_validate_single_cardinality_in_projector()
    {
        // Single-cardinality slot should produce None when missing and Many when over.
        var module = ModuleWith(
            Template("Agreement", ContractIdOf("Agreement"), choiceName: "Renew"));

        var code = GenerateAndReadTemplate(module, "Agreement");

        code.Should().Contain("ExerciseOutcome<RenewResult>.None");
        code.Should().Contain("ExerciseOutcome<RenewResult>.Many");
    }

    [Fact]
    public void Generate_should_validate_optional_cardinality_in_projector()
    {
        // Optional slot tolerates 0 contracts but caps at 1.
        var module = ModuleWith(
            Template("Agreement", OptionalOf(ContractIdOf("AgreementRecord")), choiceName: "Cancel"),
            siblingTemplateNames: ["AgreementRecord"]);

        var code = GenerateAndReadTemplate(module, "Agreement");

        // Optional slots should NOT produce None (zero is valid). They SHOULD
        // produce Many when more than one contract of the template is created.
        code.Should().NotContain("ExerciseOutcome<CancelResult>.None");
        code.Should().Contain("ExerciseOutcome<CancelResult>.Many");
    }

    [Fact]
    public void Generate_should_match_templates_by_module_and_entity_only()
    {
        // The projector should match created contracts by (ModuleName, EntityName) so
        // package-id-only differences (e.g. upgrades) don't break matching. Verify
        // the generated comparison uses ModuleName/EntityName, not the full TemplateId.
        var module = ModuleWith(
            Template("Agreement", ContractIdOf("Agreement"), choiceName: "Renew"));

        var code = GenerateAndReadTemplate(module, "Agreement");

        code.Should().Contain("item.TemplateId.ModuleName");
        code.Should().Contain("item.TemplateId.EntityName");
        code.Should().Contain("Agreement.TemplateId.ModuleName");
        code.Should().Contain("Agreement.TemplateId.EntityName");
    }

    [Fact]
    public void Generate_should_share_one_template_bucket_when_slots_repeat()
    {
        // Duplicate-template choice return (ContractId Half, ContractId Half) must
        // emit a single per-template bucket and distribute matches across both slots.
        // The earlier if/else-if-per-slot shape was unreachable for the second slot
        // and silently always returned .None.
        var module = ModuleWith(
            Template("Splitter", TupleType(
                ContractIdOf("Half"),
                ContractIdOf("Half")),
                choiceName: "Split"),
            siblingTemplateNames: ["Half"]);

        var code = GenerateAndReadTemplate(module, "Splitter");

        // One bucket per unique template (group 0), two per-slot output lists.
        code.Should().Contain("var templateMatches0 = new List<string>();");
        code.Should().NotContain("var templateMatches1 = new List<string>();");
        code.Should().Contain("var matches0 = new List<string>();");
        code.Should().Contain("var matches1 = new List<string>();");

        // Distribution loop pulls from the shared bucket into each slot in order.
        code.Should().Contain("matches0.Add(templateMatches0[templateMatchIndex0]);");
        code.Should().Contain("matches1.Add(templateMatches0[templateMatchIndex0]);");
    }
}
