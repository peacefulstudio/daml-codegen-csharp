// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class ChoiceEmitterValueReturnExerciserTests
{
    private const string LocalPackageId = "pkg-id";

    private sealed class StubResolver : ICrossPackageResolver
    {
        public string Resolve(DamlTypeRef typeRef, PackageEmitContext context) => Identifiers.Sanitize(typeRef.Name);

        public IReadOnlySet<string> DiscoveredExternalPackageIds => new HashSet<string>();

        public DamlPackage? LookupPackage(string packageId) => null;
    }

    private static DamlPackage Package(DamlTemplate template, params DamlDataType[] dataTypes) =>
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
                    DataTypes = dataTypes,
                    Interfaces = [],
                },
            ],
            DependencyReferences = [],
        };

    private static DamlChoice Choice(string name, DamlType argumentType, DamlType returnType) =>
        new()
        {
            Name = name,
            ArgumentType = argumentType,
            ReturnType = returnType,
            Consuming = false,
        };

    private static DamlTemplate Template(string name, params DamlChoice[] choices) =>
        new()
        {
            Name = name,
            Fields = [],
            Choices = choices,
        };

    private static string EmitNonContract(DamlTemplate template)
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
    public void non_contract_exerciser_emits_typed_wrapper_with_error_passthrough_and_filtering_projector_for_decimal_return()
    {
        var template = Template(
            "Oracle",
            Choice("GetTrailingTwap", new DamlPrimitiveType(DamlPrimitive.Unit), new DamlPrimitiveType(DamlPrimitive.Numeric)));

        var output = EmitNonContract(template);

        output.Should().Contain("public static class OracleNonContractExtensions");
        output.Should().Contain("public static async Task<ExerciseOutcome<decimal>> GetTrailingTwapAsync(");
        output.Should().Contain("this ContractId<Oracle> contractId,");
        output.Should().Contain("ILedgerClient client,");
        output.Should().Contain(".TrySubmitAndWaitForTransactionAsync(submission, cancellationToken)");
        output.Should().Contain("ExerciseOutcome<TransactionResult>.DamlError damlError => new ExerciseOutcome<decimal>.DamlError(");
        output.Should().Contain("ExerciseOutcome<TransactionResult>.InfraError infraError => new ExerciseOutcome<decimal>.InfraError(");
        output.Should().Contain("ProjectGetTrailingTwapResult(success.Result, contractId.Value)");
        output.Should().Contain("string.Equals(exercised.ContractId, contractId, StringComparison.Ordinal)");
        output.Should().Contain("string.Equals(exercised.TemplateId.ModuleName, Oracle.TemplateId.ModuleName, StringComparison.Ordinal)");
        output.Should().Contain("string.Equals(exercised.TemplateId.EntityName, Oracle.TemplateId.EntityName, StringComparison.Ordinal)");
        output.Should().NotContain("exercised.TemplateId.Equals(");
    }

    [Fact]
    public void non_contract_exerciser_emits_async_wrapper_for_record_returning_choice()
    {
        var template = Template(
            "Reporter",
            Choice("ComputeReport", new DamlPrimitiveType(DamlPrimitive.Unit), new DamlTypeRef("", "Test.Reports", "Report")));

        var output = EmitNonContract(template);

        output.Should().Contain("public static class ReporterNonContractExtensions");
        output.Should().Contain("public static async Task<ExerciseOutcome<Report>> ComputeReportAsync(");
    }

    [Fact]
    public void non_contract_exerciser_emits_async_wrapper_for_list_returning_choice()
    {
        var template = Template(
            "Oracle",
            Choice(
                "RecentTwaps",
                new DamlPrimitiveType(DamlPrimitive.Unit),
                new DamlTypeApp(new DamlPrimitiveType(DamlPrimitive.List), [new DamlPrimitiveType(DamlPrimitive.Numeric)])));

        var output = EmitNonContract(template);

        output.Should().Contain("public static async Task<ExerciseOutcome<IReadOnlyList<decimal>>> RecentTwapsAsync(");
    }

    [Fact]
    public void non_contract_exerciser_skips_choice_with_fallback_argument_shape()
    {
        var template = Template(
            "Trader",
            Choice("Quote", new DamlPrimitiveType(DamlPrimitive.Text), new DamlPrimitiveType(DamlPrimitive.Numeric)));

        var output = EmitNonContract(template);

        output.Should().NotContain("TraderNonContractExtensions");
        output.Should().NotContain("QuoteAsync(");
    }

    [Fact]
    public void non_contract_exerciser_throws_for_nested_optional_return_type()
    {
        var template = Template(
            "Sink",
            Choice(
                "MaybeMaybe",
                new DamlPrimitiveType(DamlPrimitive.Unit),
                new DamlTypeApp(
                    new DamlPrimitiveType(DamlPrimitive.Optional),
                    [new DamlTypeApp(new DamlPrimitiveType(DamlPrimitive.Optional), [new DamlPrimitiveType(DamlPrimitive.Text)])])));

        var act = () => EmitNonContract(template);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*nested Optional*Optional (Optional t)*");
    }
}
