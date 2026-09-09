// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Testing.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Daml.Ledger.Abstractions.Tests;

public sealed class ConsumerReferenceSetTests
{
    [Fact]
    public void ConsumerReferenceSet_rejects_a_type_from_an_assembly_no_consumer_references()
    {
        var diagnostics = Compile(ProbeUsing("Microsoft.CodeAnalysis.SyntaxTree"));

        diagnostics.Should().Contain(
            d => d.Severity == DiagnosticSeverity.Error && d.Id == "CS0234",
            "the compiler that runs this guard is itself loaded in the test host, so a reference set "
            + "drawn from the loaded assemblies would resolve Roslyn's own types — which no consumer "
            + "of the published packages can reference");
    }

    [Fact]
    public void ConsumerReferenceSet_resolves_a_framework_type_the_test_host_has_not_loaded()
    {
        LoadedAssemblyNames().Should().NotContain(
            HttpListener,
            "System.Net.HttpListener names both the type and the assembly holding it, and this guard "
            + "only proves the reference set is independent of load order while nothing in the run "
            + "has loaded that assembly");

        var diagnostics = Compile(ProbeUsing(HttpListener));

        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty(
            "every assembly of the target framework is entitled, whether or not this run happened to "
            + "load it");
    }

    private const string HttpListener = "System.Net.HttpListener";

    private static string ProbeUsing(string typeName) =>
        $$"""
        internal static class Probe
        {
            internal static object Use() => typeof({{typeName}});
        }
        """;

    private static IReadOnlyList<string> LoadedAssemblyNames() =>
        [.. AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetName().Name!)];

    private static IReadOnlyList<Diagnostic> Compile(string source) =>
        CSharpCompilation.Create(
            assemblyName: "ConsumerReferenceSetTests-probe",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references: ConsumerReferenceSet.Assemblies,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .GetDiagnostics();
}
