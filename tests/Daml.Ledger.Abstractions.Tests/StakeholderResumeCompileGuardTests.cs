// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Daml.Ledger.Abstractions.Tests;

/// <summary>
/// Negative-compilation coverage for the snapshot↔stream <see cref="Daml.Runtime.StakeholderResume"/>
/// guardrail: pipes small
/// hand-written call sites through Roslyn against the real <c>Daml.Runtime</c> +
/// <c>Daml.Ledger.Abstractions</c> assemblies, the same pattern
/// <c>Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers</c> uses to pin emitted-code
/// compilation.
/// </summary>
public sealed class StakeholderResumeCompileGuardTests
{
    private const string Prelude = """
        using Daml.Ledger.Abstractions;
        using Daml.Runtime;
        using Daml.Runtime.Commands;
        using Daml.Runtime.Contracts;
        using Daml.Runtime.Data;

        internal sealed record Probe : ITemplate
        {
            public static Identifier TemplateId { get; } = new("pkg", "Module", "Probe");
            public static string PackageId => "pkg";
            public static string PackageName => "probe";
            public static System.Version PackageVersion { get; } = new(1, 0, 0);
            public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
            public DamlRecord ToRecord() => DamlRecord.Create();
        }

        """;

    [Fact]
    public void SubscribeAsync_accepts_a_StakeholderResume()
    {
        var diagnostics = Compile(Prelude + """
            internal static class Caller
            {
                internal static void Call(ILedgerStreamer streamer, SubmitterInfo submitter, StakeholderResume resume)
                {
                    _ = streamer.SubscribeAsync<Probe>(submitter, resume);
                }
            }
            """);

        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    [Fact]
    public void SubscribeLedgerEffectsAsync_rejects_a_StakeholderResume_at_compile_time()
    {
        var diagnostics = Compile(Prelude + """
            internal static class Caller
            {
                internal static void Call(ILedgerStreamer streamer, SubmitterInfo submitter, StakeholderResume resume)
                {
                    _ = streamer.SubscribeLedgerEffectsAsync<Probe>(submitter, resume);
                }
            }
            """);

        diagnostics.Should().Contain(
            d => d.Severity == DiagnosticSeverity.Error && d.Id == "CS1503",
            "passing a StakeholderResume where a LedgerOffset? is expected is an argument-conversion "
            + "failure, not merely any compile error — pinning the id keeps a typo in the call itself "
            + "from satisfying this guard vacuously");
    }

    [Fact]
    public void SubscribeLedgerEffectsAsync_accepts_the_raw_offset_escape_hatch()
    {
        var diagnostics = Compile(Prelude + """
            internal static class Caller
            {
                internal static void Call(ILedgerStreamer streamer, SubmitterInfo submitter, StakeholderResume resume)
                {
                    _ = streamer.SubscribeLedgerEffectsAsync<Probe>(submitter, resume.Offset);
                }
            }
            """);

        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    private static IReadOnlyList<Diagnostic> Compile(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
            .ToList();

        var compilation = CSharpCompilation.Create(
            assemblyName: "StakeholderResumeCompileGuardTests-probe",
            syntaxTrees: [tree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return compilation.GetDiagnostics();
    }
}
