// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.DarReader;
using FluentAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Drift-detection snapshot test for the canonical Splice DAR
/// (<c>splice-api-token-holding-v1</c>). Regenerates C# bindings against a
/// vendored DAR fixture and asserts byte-equal output against the committed
/// snapshot.
///
/// Catches accidental codegen output changes — even semantically-equivalent
/// reformatting — before they ship as a behavior change in the published
/// per-family Splice NuGet packages (issue #57 — drift-detection epic). When
/// codegen output legitimately changes, refresh the snapshot per the README
/// in <c>Snapshots/splice-api-token-holding-v1/</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Coverage caveat.</b> This is a format-stability test for one specific
/// DAR — not a feature-coverage test. The chosen fixture is interface-only
/// (no concrete Daml templates, no contract keys, no non-Unit choice bodies),
/// so paths added by recent runtime work — partial-property contract Key
/// (#65), typed <c>WitnessParties</c> (#88), <c>SynchronizerId</c>
/// reassignment fields (#89) — are <em>not</em> exercised here. Feature
/// coverage is the responsibility of <see cref="EmittedCodeCompilesTests"/>
/// and the per-feature shape tests. A second canonical DAR exercising those
/// paths is tracked as a follow-up.
/// </para>
/// </remarks>
public class DriftDetectionTests
{
    /// <summary>
    /// Name of the canonical DAR. Interface-only fixture: one Daml interface
    /// (<c>Holding</c>) plus a handful of records (<c>HoldingView</c>,
    /// <c>InstrumentId</c>, <c>Lock</c>); no concrete templates, no contract
    /// keys. Small enough to keep snapshot diffs reviewable, no cross-family
    /// cycles.
    /// </summary>
    private const string SnapshotName = "splice-api-token-holding-v1";

    [Fact]
    public async Task Codegen_output_matches_snapshot_for_canonical_splice_dar()
    {
        // Resolve the vendored fixture relative to the test assembly. The
        // Snapshots/ directory is copied next to the test DLL via
        // <Content CopyToOutputDirectory="PreserveNewest"> so the test does
        // not depend on the repo source layout at runtime.
        var snapshotDir = Path.Combine(AppContext.BaseDirectory, "Snapshots", SnapshotName);
        var darPath = Path.Combine(snapshotDir, $"{SnapshotName}.dar");
        var expectedDir = Path.Combine(snapshotDir, "expected");

        File.Exists(darPath).Should().BeTrue(
            "the canonical DAR fixture must ship alongside the test assembly at {0}",
            darPath);
        Directory.Exists(expectedDir).Should().BeTrue(
            "the snapshot fixtures directory must ship alongside the test assembly at {0}",
            expectedDir);

        // Use the codegen defaults the publish-splice workflow relies on by
        // *not* pinning them here — the workflow doesn't pass these as CLI
        // flags (they're CodeGenOptions defaults), so the test should track
        // default changes the workflow would also see. Pinning every default
        // explicitly would silently mask a default-shift regression: the
        // workflow output drifts, but the test stays on the old pinned values
        // and reports green. OutputDirectory is required (no default) and
        // unused by Generate(dar) which runs in-memory — set to a portable
        // placeholder via Path.GetTempPath() in case a future refactor starts
        // honoring it.
        var options = new CodeGenOptions
        {
            OutputDirectory = Path.Combine(Path.GetTempPath(), "drift-detection-unused"),
        };
        var generator = new CSharpCodeGenerator(options, new ConsoleLogger(0));

        var dar = await DarArchive.ReadAsync(darPath);
        var allGenerated = generator.Generate(dar);

        // Sentinel `.daml-needs-csharp13` is emitted by CSharpCodeGenerator only
        // when the output contains partial-property `Key` syntax (key-bearing
        // template). This canonical DAR is interface-only — no keys — so the
        // marker must be absent. Pin it explicitly so a future codegen change
        // can't add the marker for an interface-only emission without being
        // caught (the `*.cs` filter below would otherwise hide non-.cs drift).
        allGenerated.Should().NotContain(f => f.RelativePath.EndsWith(".daml-needs-csharp13", StringComparison.Ordinal),
            "the snapshot DAR has no key-bearing templates, so no C#-13 sentinel marker should be emitted");

        var actualFiles = allGenerated
            .Where(f => f.RelativePath.EndsWith(".cs", StringComparison.Ordinal))
            // Normalize to forward slashes: CSharpCodeGenerator builds RelativePath
            // with Path.Combine, which uses backslashes on Windows. Both sides of
            // the comparison need the same separator.
            .Select(f => new { RelativePath = f.RelativePath.Replace('\\', '/'), f.Content })
            .OrderBy(f => f.RelativePath, StringComparer.Ordinal)
            .ToList();

        // Snapshot enumeration: read every file under expected/ and key it by
        // relative path with forward slashes — same normalization as actualFiles
        // so cross-platform comparison is stable.
        var expectedFiles = Directory.EnumerateFiles(expectedDir, "*.cs", SearchOption.AllDirectories)
            .Select(absPath => new
            {
                RelativePath = Path.GetRelativePath(expectedDir, absPath).Replace('\\', '/'),
                AbsolutePath = absPath,
            })
            .OrderBy(f => f.RelativePath, StringComparer.Ordinal)
            .ToList();

        const string RefreshHint =
            "Codegen output drifted from the snapshot. If the change is intentional, refresh " +
            "the snapshot per tests/Daml.Codegen.CSharp.Tests/Snapshots/" + SnapshotName +
            "/README.md and re-commit. If the change is unintentional, fix the codegen. " +
            "Re-run only this test with: dotnet test --filter FullyQualifiedName~DriftDetectionTests";

        // 0. Both sides must be non-empty. Without this guard, an empty `expected/`
        //    tree (e.g. partially-completed snapshot refresh) plus an empty codegen
        //    output (e.g. broken DarArchive.ReadAsync) would compare two empty
        //    sequences and pass vacuously — the worst possible failure mode for a
        //    drift-detection test.
        expectedFiles.Should().NotBeEmpty(
            "the snapshot must contain at least one .cs file; an empty fixture would let the test pass vacuously. " + RefreshHint);
        actualFiles.Should().NotBeEmpty(
            "codegen must emit at least one .cs file from the canonical DAR; zero output indicates a regression in DarArchive.ReadAsync or Generate.");

        // 1. Same set of files (catches added/removed emission).
        actualFiles.Select(f => f.RelativePath).Should().Equal(
            expectedFiles.Select(f => f.RelativePath),
            because: "the set of generated files must match the snapshot. " + RefreshHint);

        // 2. Byte-equal contents, file by file. Compare bytes (not just text)
        //    so encoding / BOM / line-ending changes also surface.
        foreach (var (actual, expected) in actualFiles.Zip(expectedFiles))
        {
            var actualBytes = System.Text.Encoding.UTF8.GetBytes(actual.Content);
            var expectedBytes = await File.ReadAllBytesAsync(expected.AbsolutePath);

            actualBytes.Should().Equal(
                expectedBytes,
                because: $"`{actual.RelativePath}` must match the snapshot byte-for-byte. {RefreshHint}");
        }
    }
}
