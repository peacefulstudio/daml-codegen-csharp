// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Pins the guarantee the emitter relies on when it hands a marker a static
/// <c>View</c> witness: <see cref="Daml.Runtime.Contracts.ViewDescriptor{TInterface, TView}"/>
/// is unconstructible for a mismatched pair. Its <c>TInterface : IHasView&lt;TView&gt;</c>
/// constraint is what stops a call site from smuggling one interface's view record past
/// another interface's marker, and only a rejected compilation proves the constraint is
/// still load-bearing — every other test in the suite names a matching pair, which would
/// keep passing if the constraint were dropped.
/// </summary>
public class ViewDescriptorPairingCompilesTests
{
    private const string PairingSource = """
        using System;
        using Daml.Runtime;
        using Daml.Runtime.Contracts;
        using Daml.Runtime.Data;

        namespace Pairing;

        public interface ITestHolding : IDamlInterface, IHasView<TestHoldingView>
        {
            static Identifier IDamlInterface.InterfaceId => new("p", "M", "TestHolding");
            static string IDamlInterface.PackageId => "p";
            static string IDamlInterface.PackageName => "pkg";
            static Version IDamlInterface.PackageVersion => new(1, 0, 0);
            static DamlTypeDescriptor global::Daml.Runtime.IDamlType.DamlTypeId =>
                new(new Identifier("p", "M", "TestHolding"), DamlTypeKind.Interface, "pkg");
        }

        public sealed record TestHoldingView(decimal Amount) : IDamlRecord<TestHoldingView>
        {
            public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("amount", new DamlNumeric(Amount)));

            public static TestHoldingView FromRecord(DamlRecord record) =>
                new(record.GetRequiredField("amount").As<DamlNumeric>().Value);
        }

        public sealed record WrongView(decimal Amount) : IDamlRecord<WrongView>
        {
            public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("amount", new DamlNumeric(Amount)));

            public static WrongView FromRecord(DamlRecord record) =>
                new(record.GetRequiredField("amount").As<DamlNumeric>().Value);
        }
        """;

    private static IReadOnlyList<Diagnostic> CompilePairing(string descriptorDeclaration) =>
        CompileEmittedFiles(
        [
            GeneratedFile.Text("Pairing.cs", PairingSource),
            GeneratedFile.Text(
                "Witness.cs",
                $$"""
                using Daml.Runtime.Contracts;

                namespace Pairing;

                public static class Witness
                {
                    public static readonly {{descriptorDeclaration}} Value = new();
                }
                """),
        ]);

    [Fact]
    public void ViewDescriptor_accepts_the_marker_paired_with_its_own_view_record()
    {
        var errors = CompilePairing("ViewDescriptor<ITestHolding, TestHoldingView>")
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        errors.Should().BeEmpty(
            "the mismatch below only proves something if the matching pair is accepted; got: {0}",
            string.Join("; ", errors.Select(d => d.ToString())));
    }

    [Fact]
    public void ViewDescriptor_rejects_a_marker_paired_with_a_foreign_view_record()
    {
        var errors = CompilePairing("ViewDescriptor<ITestHolding, WrongView>")
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        errors.Should().Contain(
            d => d.Id == "CS0311",
            "a marker that does not declare IHasView<WrongView> must not satisfy the descriptor's "
            + "TInterface constraint, so the mismatched pair is unconstructible rather than silently accepted");
    }
}
