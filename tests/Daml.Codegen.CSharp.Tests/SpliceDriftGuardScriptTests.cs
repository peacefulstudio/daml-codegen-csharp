// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Exercises <c>.github/scripts/check-splice-drift.sh</c>, the publish-time
/// drift guard for the canonical <c>splice-api-token-holding-v1</c> snapshot
/// (issue #317). The script re-checks the same golden tree
/// <see cref="DriftDetectionTests"/> (issue #95) covers in-process, but
/// against whatever <c>build-pack.sh</c> actually generated for the DAR
/// selected in a given publish run — so an upstream Splice content change is
/// caught before <c>push-packages.sh</c> runs, not just a codegen
/// regression.
/// </summary>
public class SpliceDriftGuardScriptTests
{
    private static readonly string ScriptPath =
        Path.Combine(LocateRepoRoot(), ".github", "scripts", "check-splice-drift.sh");

    private static readonly string GoldenDir =
        Path.Combine(AppContext.BaseDirectory, "Snapshots", "splice-api-token-holding-v1", "expected");

    [Fact]
    public void exits_zero_when_generated_output_matches_the_golden_snapshot()
    {
        using var generatedDir = new TempDirectory();
        CopyDirectory(GoldenDir, generatedDir.Path);

        var result = RunScript(generatedDir.Path);

        result.ExitCode.Should().Be(0, because: "the generated tree is byte-identical to the golden snapshot. stderr: " + result.StdErr);
    }

    [Fact]
    public void exits_nonzero_and_names_the_file_when_a_generated_file_content_drifts()
    {
        using var generatedDir = new TempDirectory();
        CopyDirectory(GoldenDir, generatedDir.Path);
        var driftedFile = Directory.EnumerateFiles(generatedDir.Path, "*.cs", SearchOption.AllDirectories).First();
        File.AppendAllText(driftedFile, "// unexpected drift" + Environment.NewLine);
        var driftedRelativePath = Path.GetRelativePath(generatedDir.Path, driftedFile).Replace('\\', '/');

        var result = RunScript(generatedDir.Path);

        result.ExitCode.Should().NotBe(0);
        result.StdErr.Should().Contain(driftedRelativePath);
    }

    [Fact]
    public void exits_nonzero_when_the_generated_file_set_differs_from_the_golden_snapshot()
    {
        using var generatedDir = new TempDirectory();
        CopyDirectory(GoldenDir, generatedDir.Path);
        var removedFile = Directory.EnumerateFiles(generatedDir.Path, "*.cs", SearchOption.AllDirectories).First();
        File.Delete(removedFile);

        var result = RunScript(generatedDir.Path);

        result.ExitCode.Should().NotBe(0);
    }

    [Fact]
    public void exits_nonzero_when_the_generated_file_set_has_an_extra_file()
    {
        using var generatedDir = new TempDirectory();
        CopyDirectory(GoldenDir, generatedDir.Path);
        var extraFile = Path.Combine(generatedDir.Path, "unexpected-extra.cs");
        File.WriteAllText(extraFile, "// emitted but absent from the golden snapshot" + Environment.NewLine);

        var result = RunScript(generatedDir.Path);

        result.ExitCode.Should().NotBe(0);
        result.StdErr.Should().Contain("unexpected-extra.cs");
    }

    [Fact]
    public void exits_nonzero_when_the_generated_directory_does_not_exist()
    {
        var missingDir = Path.Combine(Path.GetTempPath(), "check-splice-drift-missing-" + Guid.NewGuid().ToString("N"));

        var result = RunScript(missingDir);

        result.ExitCode.Should().NotBe(0);
        result.StdErr.Should().Contain("not found");
    }

    [Fact]
    public void exits_zero_when_non_snapshot_scaffolding_files_differ_from_golden()
    {
        using var generatedDir = new TempDirectory();
        CopyDirectory(GoldenDir, generatedDir.Path);
        File.WriteAllText(
            Path.Combine(generatedDir.Path, "splice-api-token-holding-v1.csproj"),
            "<Project><!-- version-stamped scaffolding, not covered by drift detection --></Project>");

        var result = RunScript(generatedDir.Path);

        result.ExitCode.Should().Be(0, because: "the script only compares .cs and .daml-langversion files. stderr: " + result.StdErr);
    }

    private static (int ExitCode, string StdOut, string StdErr) RunScript(string generatedDir)
    {
        var psi = new ProcessStartInfo("bash")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(ScriptPath);
        psi.ArgumentList.Add(generatedDir);

        using var proc = Process.Start(psi)!;
        var stdOutTask = proc.StandardOutput.ReadToEndAsync();
        var stdErrTask = proc.StandardError.ReadToEndAsync();
        if (!proc.WaitForExit(TimeSpan.FromSeconds(30)))
        {
            proc.Kill(entireProcessTree: true);
            throw new TimeoutException($"check-splice-drift.sh did not exit within 30 seconds (arg: {generatedDir}).");
        }
        return (proc.ExitCode, stdOutTask.GetAwaiter().GetResult(), stdErrTask.GetAwaiter().GetResult());
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("check-splice-drift-test-").FullName;
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private static string LocateRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Directory.Build.props"))
                && Directory.Exists(Path.Combine(current.FullName, ".github", "scripts")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException(
            $"Cannot locate repo root from {AppContext.BaseDirectory}. Expected a Directory.Build.props alongside .github/scripts/.");
    }
}
