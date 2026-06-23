// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using FluentAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// End-to-end integration test for the C# codegen → NuGet packing path. It is
/// <b>generator-agnostic</b>: the thing that turns the DAR into a <c>.csproj</c>
/// is injected via the <c>CODEGEN_CS_CMD</c> environment variable, so the same
/// fixture, the same <c>dotnet pack</c>, and the same <c>.nupkg</c> assertions
/// (id, version, <c>Apache-2.0</c> license, <c>.dll</c> under <c>lib/</c>) cover
/// every gate, parameterised <i>only</i> on where codegen comes from:
///
/// <list type="bullet">
///   <item><b>Test B (always-on):</b> a branch-local bundle entrypoint that runs
///   this branch's JVM helper + <c>Daml.Codegen.CSharp.Cli</c> emitter — always
///   reflects the code under review, no GHCR pull, no version pin.</item>
///   <item><b>Test A (key moments):</b> a locally-built OCI bundle's entrypoint —
///   additionally exercises the dpm/OCI packaging layer on branch code.</item>
///   <item><b>Test C (follow-up):</b> the published <c>dpm codegen-cs</c> against a
///   freshly-pushed canary (set <c>CODEGEN_CS_CMD=dpm</c>,
///   <c>CODEGEN_CS_BASE_ARGS=codegen-cs</c>).</item>
/// </list>
///
/// The injected command must honour the shipped <c>dpm codegen-cs</c> interface:
/// <c>&lt;cmd&gt; [base-args] --dar &lt;dar&gt; --out &lt;out&gt; -- &lt;emitter args&gt;</c>.
///
/// Skipped unless <c>CODEGEN_CS_CMD</c> is set AND the local runtime packs exist
/// under <c>&lt;repo&gt;/output/nuget/</c> (run <c>dotnet pack -c Release</c> first).
/// </summary>
public class NuGetPackIntegrationTests
{
    private const string FixtureSnapshotName = "splice-api-token-holding-v1";
    private const string ExpectedPackageIdPrefix = "Splice.Api.Token.Holding.V1.";

    [Fact]
    public async Task generated_csproj_packs_into_nupkg_for_splice_holding_v1_fixture()
    {
        var snapshotDir = Path.Combine(AppContext.BaseDirectory, "Snapshots", FixtureSnapshotName);
        var darPath = Path.Combine(snapshotDir, $"{FixtureSnapshotName}.dar");
        File.Exists(darPath).Should().BeTrue(
            $"the integration test requires the DAR fixture at {darPath}");

        var generatorCmd = Environment.GetEnvironmentVariable("CODEGEN_CS_CMD");
        Assert.SkipUnless(
            !string.IsNullOrWhiteSpace(generatorCmd),
            "Integration test requires CODEGEN_CS_CMD — the codegen-cs generator to exercise (a branch-local " +
            "bundle entrypoint, a locally-built OCI bundle entrypoint, or `dpm`). CI sets it per gate; " +
            "see .github/workflows/nuget-pack-integration*.yaml.");

        var localNuGetSource = LocateLocalNuGetSource();
        Assert.SkipUnless(
            localNuGetSource is not null,
            "Integration test requires local NuGet packs of Daml.Runtime and Daml.Ledger.Abstractions under <repo>/output/nuget/. " +
            "Produce them with `dotnet pack src/Daml.Runtime -c Release && dotnet pack src/Daml.Ledger.Abstractions -c Release`.");

        var runtimeVersion = ReadRepoVersion();
        runtimeVersion.Should().NotBeNullOrEmpty(
            "the generated csproj needs a Daml.Runtime version, read from Directory.Build.props <Version>");

        var ct = TestContext.Current.CancellationToken;
        var workspace = CreateTempWorkspace();
        try
        {
            var outDir = Path.Combine(workspace, "out");
            Directory.CreateDirectory(outDir);

            // Optional leading args (e.g. "codegen-cs" for the dpm subcommand). The
            // bundle entrypoints take none.
            var baseArgs = (Environment.GetEnvironmentVariable("CODEGEN_CS_BASE_ARGS") ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            // The shipped `dpm codegen-cs` interface: --dar/--out before `--`, emitter
            // flags after. Identical for every generator, so the gate only swaps the cmd.
            var generatorArgs = new List<string>(baseArgs)
            {
                "--dar", darPath,
                "--out", outDir,
                "--",
                "--generate-project",
                "--runtime-version", runtimeVersion!,
                "--package-license", "Apache-2.0",
                "--target-framework", "net10.0",
            };

            var codegen = await RunProcessAsync(
                generatorCmd!,
                generatorArgs,
                workspace,
                TimeSpan.FromMinutes(10),
                ct);

            codegen.ExitCode.Should().Be(0,
                $"`{generatorCmd}` codegen must succeed.\nSTDOUT:\n{codegen.StdOut}\nSTDERR:\n{codegen.StdErr}");

            var csprojPath = FindMainCsproj(outDir);
            csprojPath.Should().NotBeNull(
                $"`--generate-project` must emit a .csproj for {ExpectedPackageIdPrefix}* under {outDir}");

            // dotnet pack walks up from the csproj for NuGet.config; place it at the
            // --out root (a csproj ancestor) so the generated Daml.* PackageReferences
            // resolve against the local feed.
            WriteLocalNuGetConfig(outDir, localNuGetSource!);

            var packOutput = Path.Combine(workspace, "nupkg-out");
            Directory.CreateDirectory(packOutput);

            var packResult = await RunProcessAsync(
                "dotnet",
                new[] { "pack", csprojPath!, "-c", "Release", "-o", packOutput, "--verbosity", "minimal" },
                outDir,
                TimeSpan.FromMinutes(5),
                ct);

            packResult.ExitCode.Should().Be(0,
                $"dotnet pack must succeed.\nSTDOUT:\n{packResult.StdOut}\nSTDERR:\n{packResult.StdErr}");

            var producedNupkgs = Directory.GetFiles(packOutput, "*.nupkg");
            producedNupkgs.Should().NotBeEmpty("dotnet pack should produce at least one .nupkg");

            var nupkg = producedNupkgs.FirstOrDefault(
                p => Path.GetFileName(p).StartsWith(ExpectedPackageIdPrefix, StringComparison.Ordinal));
            nupkg.Should().NotBeNull(
                $"a .nupkg whose id starts with '{ExpectedPackageIdPrefix}' must be produced — the id must match " +
                "the Daml package name converted to PascalCase per the generated-package naming convention");

            var nuspecVersion = ReadNuspecVersion(nupkg!);
            nuspecVersion.Should().MatchRegex(@"^\d+\.\d+\.\d+(\.\d+)?$",
                "the .nuspec must carry an M.m.p[.r] version (4th segment is normalized away by NuGet when r=0)");
            nuspecVersion.Split('.').Length.Should().BeGreaterThanOrEqualTo(3,
                "the M.m.p.r versioning scheme requires at least the 3-part DAR-intrinsic version in the manifest");

            var nuspecLicense = ReadNuspecLicense(nupkg!);
            nuspecLicense.Should().Be("Apache-2.0",
                "OSS-published NuGets must declare Apache-2.0 in the manifest");

            var contents = ListNupkgEntries(nupkg!);
            contents.Should().Contain(e => e.EndsWith(".dll", StringComparison.OrdinalIgnoreCase),
                "the pack must include the compiled assembly under lib/");
        }
        finally
        {
            TryCleanup(workspace);
        }
    }

    /// <summary>
    /// Locates the main generated project — the one whose file name matches the
    /// fixture's package id. <c>--generate-project</c> may nest the csproj under a
    /// per-package subdirectory, so this searches recursively.
    /// </summary>
    private static string? FindMainCsproj(string outDir)
    {
        var all = Directory.GetFiles(outDir, "*.csproj", SearchOption.AllDirectories);
        var main = all.FirstOrDefault(
            p => Path.GetFileName(p).StartsWith(ExpectedPackageIdPrefix, StringComparison.Ordinal));
        return main ?? (all.Length == 1 ? all[0] : null);
    }

    private static string CreateTempWorkspace()
    {
        var path = Path.Combine(Path.GetTempPath(), $"daml-codegen-pack-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryCleanup(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string? LocateLocalNuGetSource()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "output", "nuget");
            if (Directory.Exists(candidate)
                && Directory.GetFiles(candidate, "Daml.Runtime.*.nupkg").Length > 0
                && Directory.GetFiles(candidate, "Daml.Ledger.Abstractions.*.nupkg").Length > 0)
            {
                return candidate;
            }
            current = current.Parent;
        }
        return null;
    }

    private static string? ReadRepoVersion()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var props = Path.Combine(current.FullName, "Directory.Build.props");
            if (File.Exists(props))
            {
                var content = File.ReadAllText(props);
                var match = System.Text.RegularExpressions.Regex.Match(
                    content,
                    @"<Version>([^<]+)</Version>");
                if (match.Success)
                {
                    return match.Groups[1].Value.Trim();
                }
            }
            current = current.Parent;
        }
        return null;
    }

    private static void WriteLocalNuGetConfig(string directory, string localSource)
    {
        var configPath = Path.Combine(directory, "NuGet.config");
        var content =
$@"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <packageSources>
    <clear />
    <add key=""local-output"" value=""{localSource}"" />
    <add key=""nuget.org"" value=""https://api.nuget.org/v3/index.json"" protocolVersion=""3"" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key=""local-output"">
      <package pattern=""Daml.*"" />
    </packageSource>
    <packageSource key=""nuget.org"">
      <package pattern=""*"" />
    </packageSource>
  </packageSourceMapping>
</configuration>";
        File.WriteAllText(configPath, content);
    }

    private static string ReadNuspecVersion(string nupkgPath)
    {
        using var archive = System.IO.Compression.ZipFile.OpenRead(nupkgPath);
        var nuspecEntry = archive.Entries.FirstOrDefault(
            e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        nuspecEntry.Should().NotBeNull("a .nupkg must contain a .nuspec manifest");
        using var stream = nuspecEntry!.Open();
        using var reader = new StreamReader(stream);
        var content = reader.ReadToEnd();
        var match = System.Text.RegularExpressions.Regex.Match(
            content,
            @"<version>([^<]+)</version>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        match.Success.Should().BeTrue("the .nuspec must declare a <version> element");
        return match.Groups[1].Value.Trim();
    }

    private static string ReadNuspecLicense(string nupkgPath)
    {
        using var archive = System.IO.Compression.ZipFile.OpenRead(nupkgPath);
        var nuspecEntry = archive.Entries.FirstOrDefault(
            e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        nuspecEntry.Should().NotBeNull("a .nupkg must contain a .nuspec manifest");
        using var stream = nuspecEntry!.Open();
        using var reader = new StreamReader(stream);
        var content = reader.ReadToEnd();
        var match = System.Text.RegularExpressions.Regex.Match(
            content,
            @"<license\s+type=""expression""[^>]*>([^<]+)</license>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    private static IReadOnlyList<string> ListNupkgEntries(string nupkgPath)
    {
        using var archive = System.IO.Compression.ZipFile.OpenRead(nupkgPath);
        return archive.Entries.Select(e => e.FullName).ToList();
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDir,
        TimeSpan timeout,
        CancellationToken ct)
    {
        // Windows can't CreateProcess a .cmd/.bat directly — launch it via cmd.exe.
        var program = fileName;
        var argList = new List<string>(arguments);
        if (OperatingSystem.IsWindows()
            && (fileName.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)))
        {
            program = "cmd.exe";
            argList.Insert(0, fileName);
            argList.Insert(0, "/c");
        }

        var psi = new ProcessStartInfo(program)
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in argList)
        {
            psi.ArgumentList.Add(arg);
        }

        using var proc = Process.Start(psi)!;
        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        try
        {
            await proc.WaitForExitAsync(ct).WaitAsync(timeout, ct);
        }
        catch (TimeoutException)
        {
            KillProcessTree(proc);
            throw new TimeoutException($"`{program} {string.Join(' ', argList)}` did not exit within {timeout}.");
        }
        catch (OperationCanceledException)
        {
            // The test's cancellation token fired (Ctrl-C / CI timeout): don't orphan
            // the spawned generator/dotnet process — it can hold locks on the temp workspace.
            KillProcessTree(proc);
            throw;
        }
        return (proc.ExitCode, await stdoutTask, await stderrTask);
    }

    private static void KillProcessTree(Process proc)
    {
        try
        {
            proc.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Process already exited.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Access denied / already gone.
        }
    }
}
