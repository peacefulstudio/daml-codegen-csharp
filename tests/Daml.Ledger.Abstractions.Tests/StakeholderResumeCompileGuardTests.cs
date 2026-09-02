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

        internal sealed record Probe : ITemplate, IDamlRecord<Probe>
        {
            public static Identifier TemplateId { get; } = new("pkg", "Module", "Probe");
            public static string PackageId => "pkg";
            public static string PackageName => "probe";
            public static System.Version PackageVersion { get; } = new(1, 0, 0);
            public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
            public DamlRecord ToRecord() => DamlRecord.Create();
            public static Probe FromRecord(DamlRecord record) => new();
        }

        internal sealed record ProbeView : IDamlRecord<ProbeView>
        {
            public DamlRecord ToRecord() => DamlRecord.Create();
            public static ProbeView FromRecord(DamlRecord record) => new();
        }

        internal sealed record ProbeInterface : IDamlInterface, IHasView<ProbeView>
        {
            public static Identifier InterfaceId { get; } = new("pkg", "Module", "ProbeInterface");
            public static string PackageId => "pkg";
            public static string PackageName => "probe";
            public static System.Version PackageVersion { get; } = new(1, 0, 0);
            public static DamlTypeDescriptor DamlTypeId { get; } = new(InterfaceId, DamlTypeKind.Interface, PackageName);
            public static ViewDescriptor<ProbeInterface, ProbeView> View { get; } = new();
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

    [Fact]
    public void SubscribeAsync_accepts_a_StakeholderResume_through_the_view_witness()
    {
        var diagnostics = Compile(Prelude + """
            internal static class Caller
            {
                internal static void Call(ILedgerStreamer streamer, SubmitterInfo submitter, StakeholderResume resume)
                {
                    _ = streamer.SubscribeAsync(ProbeInterface.View, submitter, resume);
                }
            }
            """);

        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty(
            "the interface family mirrors the template family's resume overload, and both type "
            + "parameters have to be inferred from the witness alone");
    }

    [Fact]
    public void SubscribeLedgerEffectsAsync_rejects_a_StakeholderResume_through_the_view_witness()
    {
        var diagnostics = Compile(Prelude + """
            internal static class Caller
            {
                internal static void Call(ILedgerStreamer streamer, SubmitterInfo submitter, StakeholderResume resume)
                {
                    _ = streamer.SubscribeLedgerEffectsAsync(ProbeInterface.View, submitter, resume);
                }
            }
            """);

        diagnostics.Should().Contain(
            d => d.Severity == DiagnosticSeverity.Error && d.Id == "CS1503",
            "the ledger-effects shape matches on witnesses rather than stakeholders in the interface "
            + "family too, so a snapshot's resume ticket must not cross the basis silently");
    }

    [Fact]
    public void SubscribeAsync_rejects_a_partial_type_argument_list_on_the_interface_family()
    {
        var diagnostics = Compile(Prelude + """
            internal static class Caller
            {
                internal static void Call(ILedgerStreamer streamer, SubmitterInfo submitter)
                {
                    _ = streamer.SubscribeAsync<ProbeInterface>(submitter);
                }
            }
            """);

        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().NotBeEmpty(
            "naming the marker as a bare type argument is what the witness exists to replace: the "
            + "one-parameter overload is the template family, which a marker does not satisfy");
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
