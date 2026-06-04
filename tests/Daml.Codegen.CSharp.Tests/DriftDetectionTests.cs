// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using Daml.Codegen.DarParser;
using FluentAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Drift-detection snapshot tests. Each sub-directory of <c>Snapshots/</c>
/// that contains a matching <c>&lt;name&gt;.dar</c> file becomes one theory
/// invocation; the <c>expected/</c> sub-tree is asserted per test case so
/// that a partially-committed snapshot fails explicitly rather than being
/// silently skipped at discovery time.
///
/// Catches accidental codegen output changes — even semantically-equivalent
/// reformatting — before they ship as a behavior change in the published
/// per-family Splice NuGet packages (issue #57 — drift-detection epic). When
/// codegen output legitimately changes, refresh the snapshot by running
/// the refresh procedure described in <c>Snapshots/&lt;name&gt;/README.md</c>.
/// </summary>
public class DriftDetectionTests
{
    /// <summary>
    /// Enumerates every sub-directory under <c>Snapshots/</c> that has a
    /// matching <c>&lt;name&gt;.dar</c> file, yielding the directory name
    /// (snapshot name) as the sole theory parameter. The presence of the
    /// <c>expected/</c> sub-tree is validated inside each theory case, not
    /// here, so that a half-committed snapshot produces an explicit failure
    /// rather than being silently excluded from discovery. Sorted by name
    /// (<see cref="StringComparer.Ordinal"/>) so discovery order is
    /// deterministic across platforms.
    /// </summary>
    public static TheoryData<string> SnapshotNames()
    {
        var snapshotsRoot = Path.Combine(AppContext.BaseDirectory, "Snapshots");
        var data = new TheoryData<string>();

        if (!Directory.Exists(snapshotsRoot))
            throw new DirectoryNotFoundException(
                $"Snapshots root not found at '{snapshotsRoot}'. " +
                "Ensure the Snapshots/ directory is present in the test output; " +
                "check that snapshot fixture content is copied to the output directory in the .csproj.");

        foreach (var dir in Directory.EnumerateDirectories(snapshotsRoot)
                     .Where(d => File.Exists(Path.Combine(d, $"{Path.GetFileName(d)}.dar")))
                     .OrderBy(d => Path.GetFileName(d), StringComparer.Ordinal))
        {
            data.Add(Path.GetFileName(dir)!);
        }

        if (data.Count == 0)
            throw new InvalidOperationException(
                $"No snapshot directories with a matching <name>.dar file were found under '{snapshotsRoot}'. " +
                "The expected/ sub-directory is validated later for each discovered snapshot. " +
                "A zero-case theory would silently skip drift detection.");

        return data;
    }

    [Theory]
    [MemberData(nameof(SnapshotNames))]
    public async Task Codegen_output_matches_snapshot(string snapshotName)
    {
        var snapshotDir = Path.Combine(AppContext.BaseDirectory, "Snapshots", snapshotName);
        var darPath = Path.Combine(snapshotDir, $"{snapshotName}.dar");
        var expectedDir = Path.Combine(snapshotDir, "expected");

        File.Exists(darPath).Should().BeTrue(
            "the DAR fixture must ship alongside the test assembly at {0}",
            darPath);
        Directory.Exists(expectedDir).Should().BeTrue(
            "the snapshot fixtures directory must ship alongside the test assembly at {0}",
            expectedDir);

        var options = new CodeGenOptions
        {
            OutputDirectory = Path.Combine(Path.GetTempPath(), "drift-detection-unused"),
        };
        var generator = new CSharpCodeGenerator(options, new ConsoleLogger(0));

        var dar = await DarArchive.ReadAsync(darPath);
        var allGenerated = generator.Generate(dar);

        allGenerated.Should().ContainSingle(
            f => f.RelativePath.EndsWith(".daml-langversion", StringComparison.Ordinal),
            "the codegen always emits the LangVersion state file");

        var actualFiles = allGenerated
            .Where(f => f.RelativePath.EndsWith(".cs", StringComparison.Ordinal)
                     || f.RelativePath.EndsWith(".daml-langversion", StringComparison.Ordinal))
            .Select(f => new { RelativePath = f.RelativePath.Replace('\\', '/'), f.Content })
            .OrderBy(f => f.RelativePath, StringComparer.Ordinal)
            .ToList();

        var expectedFiles = Directory.EnumerateFiles(expectedDir, "*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".cs", StringComparison.Ordinal)
                     || p.EndsWith(".daml-langversion", StringComparison.Ordinal))
            .Select(absPath => new
            {
                RelativePath = Path.GetRelativePath(expectedDir, absPath).Replace('\\', '/'),
                AbsolutePath = absPath,
            })
            .OrderBy(f => f.RelativePath, StringComparer.Ordinal)
            .ToList();

        var refreshHint =
            $"Codegen output drifted from the snapshot. If the change is intentional, run " +
            $"the snapshot refresh procedure in Snapshots/{snapshotName}/README.md and re-commit. " +
            $"If the change is unintentional, fix the codegen. " +
            $"Re-run only this snapshot with: dotnet test --filter \"FullyQualifiedName~DriftDetectionTests&DisplayName~{snapshotName}\"";

        expectedFiles.Should().Contain(
            f => f.RelativePath.EndsWith(".cs", StringComparison.Ordinal),
            "the snapshot must contain at least one .cs file; an empty fixture would let the test pass vacuously. " + refreshHint);
        actualFiles.Should().Contain(
            f => f.RelativePath.EndsWith(".cs", StringComparison.Ordinal),
            "codegen must emit at least one .cs file from the DAR; zero .cs output indicates a regression in DarArchive.ReadAsync or Generate.");

        actualFiles.Select(f => f.RelativePath).Should().Equal(
            expectedFiles.Select(f => f.RelativePath),
            because: "the set of generated files must match the snapshot. " + refreshHint);

        foreach (var (actual, expected) in actualFiles.Zip(expectedFiles))
        {
            var actualBytes = System.Text.Encoding.UTF8.GetBytes(actual.Content);
            var expectedBytes = await File.ReadAllBytesAsync(expected.AbsolutePath, TestContext.Current.CancellationToken);

            if (!actualBytes.SequenceEqual(expectedBytes))
            {
                var diff = UnifiedDiff.Render(expectedBytes, actualBytes)
                    ?? "(files differ in encoding or BOM but produce identical text)";
                throw new Xunit.Sdk.XunitException(
                    $"`{actual.RelativePath}` does not match the snapshot byte-for-byte.\n\n" +
                    $"{diff}\n{refreshHint}");
            }
        }
    }
}
