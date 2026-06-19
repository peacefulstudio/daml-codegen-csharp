// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using Daml.Codegen.Intermediate;
using FluentAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// End-to-end integration test for NuGet packing. Generates
/// C# code + a <c>.csproj</c> from the <c>splice-api-token-holding-v1</c>
/// proto snapshot, runs <c>dotnet pack</c> against the generated project, and asserts
/// that a <c>.nupkg</c> file is produced. The pack uses a temp NuGet config
/// pointing at the repo's <c>output/nuget/</c> directory so the generated
/// <c>PackageReference</c> entries for <c>Daml.Runtime</c> and
/// <c>Daml.Ledger.Abstractions</c> resolve to the locally packed bits.
/// Skipped on environments where <c>dotnet</c> is not on PATH or the local
/// runtime packs have not been produced (developer responsibility — run
/// <c>dotnet pack -c Release</c> first).
/// </summary>
public class NuGetPackIntegrationTests
{
    private const string FixtureSnapshotName = "splice-api-token-holding-v1";

    [Fact]
    public async Task generated_csproj_packs_into_nupkg_for_splice_holding_v1_fixture()
    {
        var snapshotDir = Path.Combine(AppContext.BaseDirectory, "Snapshots", FixtureSnapshotName);
        var protoPath = Path.Combine(snapshotDir, "intermediate.binpb");
        File.Exists(protoPath).Should().BeTrue(
            $"the integration test requires the intermediate.binpb proto snapshot at {protoPath}");

        var localNuGetSource = LocateLocalNuGetSource();
        Assert.SkipUnless(
            localNuGetSource is not null,
            "Integration test requires local NuGet packs of Daml.Runtime and Daml.Ledger.Abstractions under <repo>/output/nuget/. " +
            "Produce them with `dotnet pack src/Daml.Runtime -c Release && dotnet pack src/Daml.Ledger.Abstractions -c Release`.");

        var workspace = CreateTempWorkspace();
        try
        {
            var options = new CodeGenOptions
            {
                GenerateProjectFile = true,
                TargetFramework = "net10.0",
                RuntimePackageVersion = ReadRepoVersion(),
            };

            var generator = new CSharpCodeGenerator(options, new ConsoleLogger(0));
            IntermediateDar proto;
            await using (var stream = File.OpenRead(protoPath))
            {
                proto = IntermediateDar.Parser.ParseFrom(stream);
            }
            var dar = IntermediateDarReader.Read(proto);
            var generated = generator.Generate(dar);

            foreach (var file in generated)
            {
                var fullPath = Path.Combine(workspace, file.RelativePath);
                var dir = Path.GetDirectoryName(fullPath);
                if (dir is not null && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                if (file.BinaryContent is not null)
                {
                    await File.WriteAllBytesAsync(fullPath, file.BinaryContent, TestContext.Current.CancellationToken);
                }
                else
                {
                    await File.WriteAllTextAsync(fullPath, file.Content, TestContext.Current.CancellationToken);
                }
            }

            var csprojFiles = Directory.GetFiles(workspace, "*.csproj", SearchOption.TopDirectoryOnly);
            csprojFiles.Should().HaveCount(1,
                "exactly one .csproj should be emitted at the workspace root for the main package");
            var csprojPath = csprojFiles[0];

            WriteLocalNuGetConfig(workspace, localNuGetSource);

            var packOutput = Path.Combine(workspace, "nupkg-out");
            Directory.CreateDirectory(packOutput);

            var packResult = RunDotnet(
                workspace,
                $"pack \"{csprojPath}\" -c Release -o \"{packOutput}\" --verbosity minimal");

            packResult.ExitCode.Should().Be(0,
                $"dotnet pack must succeed.\nSTDOUT:\n{packResult.StdOut}\nSTDERR:\n{packResult.StdErr}");

            var producedNupkgs = Directory.GetFiles(packOutput, "*.nupkg");
            producedNupkgs.Should().NotBeEmpty("dotnet pack should produce at least one .nupkg");

            var nupkg = producedNupkgs[0];
            var fileName = Path.GetFileName(nupkg);
            fileName.Should().StartWith("Splice.Api.Token.Holding.V1.",
                "the .nupkg id must match the Daml package name converted to PascalCase per the generated-package naming convention");

            var nuspecVersion = ReadNuspecVersion(nupkg);
            nuspecVersion.Should().MatchRegex(@"^\d+\.\d+\.\d+(\.\d+)?$",
                "the .nuspec must carry an M.m.p[.r] version (4th segment is normalized away by NuGet when r=0)");
            nuspecVersion.Split('.').Length.Should().BeGreaterThanOrEqualTo(3,
                "the M.m.p.r versioning scheme requires at least the 3-part DAR-intrinsic version in the manifest");

            var nuspecLicense = ReadNuspecLicense(nupkg);
            nuspecLicense.Should().Be("Apache-2.0",
                "OSS-published NuGets must declare Apache-2.0 in the manifest");

            var contents = ListNupkgEntries(nupkg);
            contents.Should().Contain(e => e.EndsWith(".dll", StringComparison.OrdinalIgnoreCase),
                "the pack must include the compiled assembly under lib/");
        }
        finally
        {
            TryCleanup(workspace);
        }
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

    private static void WriteLocalNuGetConfig(string workspace, string localSource)
    {
        var configPath = Path.Combine(workspace, "NuGet.config");
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

    private static (int ExitCode, string StdOut, string StdErr) RunDotnet(string workingDir, string args)
    {
        var psi = new ProcessStartInfo("dotnet", args)
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var proc = Process.Start(psi)!;
        var ct = TestContext.Current.CancellationToken;
        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        if (!proc.WaitForExit(TimeSpan.FromMinutes(5)))
        {
            try { proc.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            throw new TimeoutException($"`dotnet {args}` did not exit within 5 minutes.");
        }
        return (proc.ExitCode, stdoutTask.GetAwaiter().GetResult(), stderrTask.GetAwaiter().GetResult());
    }
}
