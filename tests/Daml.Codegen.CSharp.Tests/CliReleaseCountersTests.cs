// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Xml.Linq;
using Daml.Codegen.CSharp.Cli;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// CLI integration tests for the <c>--release-counters &lt;path&gt;</c> wire-up.
/// The Splice publish workflow points the CLI at a JSON store
/// of <see cref="Daml.Codegen.CSharp.Versioning.JsonReleaseCounterStore"/> entries
/// and the CLI computes the 4th NuGet version segment from the store rather than
/// from the static <c>--emitter-counter</c> override.
/// </summary>
public class CliReleaseCountersTests : IDisposable
{
    private const string FixtureSnapshotName = "splice-api-token-holding-v1";
    private readonly string _workspace;

    public CliReleaseCountersTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), $"cli-counters-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace)) Directory.Delete(_workspace, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task release_counters_flag_persists_a_store_entry_for_the_main_package()
    {
        var intermediate = Path.Combine(AppContext.BaseDirectory, "Snapshots", FixtureSnapshotName, "intermediate.binpb");
        File.Exists(intermediate).Should().BeTrue($"fixture proto must ship at {intermediate}");
        var counters = Path.Combine(_workspace, "release-counters.json");

        var exit = await Program.Main(
        [
            "--intermediate", intermediate,
            "-o", _workspace,
            "--release-counters", counters,
            "--generate-project"
        ]);

        exit.Should().Be(0);
        File.Exists(counters).Should().BeTrue(
            "the CLI must persist the resolved revision back to the store path");

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(counters, TestContext.Current.CancellationToken));
        var properties = document.RootElement.EnumerateObject().ToList();
        properties.Should().ContainSingle(
            "a fresh run against one fixture proto must persist exactly one (name@M.m.p) entry");
        properties[0].Name.Should().MatchRegex(
            @"^.+@\d+\.\d+\.\d+$",
            "JsonReleaseCounterStore keys are `<packageName>@<Major.Minor.Patch>` per its ComposeKey contract");
        var entry = properties[0].Value;
        entry.GetProperty("content_hash").GetString().Should().NotBeNullOrEmpty(
            "the content hash field must round-trip through the snake_case JSON shape");
        entry.GetProperty("revision").GetInt32().Should().Be(0,
            "a first emission of (package, intrinsic-version) resolves to r=0 per JsonReleaseCounterStore.ResolveRevision");
    }

    [Fact]
    public async Task release_counters_flag_holds_revision_steady_on_re_emission_of_the_same_intermediate()
    {
        var intermediate = Path.Combine(AppContext.BaseDirectory, "Snapshots", FixtureSnapshotName, "intermediate.binpb");
        var counters = Path.Combine(_workspace, "release-counters.json");

        var firstExit = await Program.Main(
        [
            "--intermediate", intermediate,
            "-o", _workspace,
            "--release-counters", counters,
            "--generate-project"
        ]);
        firstExit.Should().Be(0);

        var secondWorkspace = Path.Combine(_workspace, "rerun");
        Directory.CreateDirectory(secondWorkspace);
        var secondExit = await Program.Main(
        [
            "--intermediate", intermediate,
            "-o", secondWorkspace,
            "--release-counters", counters,
            "--generate-project"
        ]);
        secondExit.Should().Be(0);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(counters, TestContext.Current.CancellationToken));
        document.RootElement.EnumerateObject().Single().Value
            .GetProperty("revision").GetInt32().Should().Be(0,
                "content-identical re-emissions must hold the revision steady per the M.m.p.r versioning scheme");
    }

    [Fact]
    public async Task release_counters_flag_fails_loudly_when_intermediate_is_not_provided()
    {
        var dar = Path.Combine(AppContext.BaseDirectory, "Snapshots", FixtureSnapshotName, $"{FixtureSnapshotName}.dar");
        var counters = Path.Combine(_workspace, "release-counters.json");

        var exit = await Program.Main(
        [
            "--release-counters", counters,
            "-o", _workspace,
            dar
        ]);

        exit.Should().NotBe(0,
            "the content hash that keys the counter store is only computable from the IntermediateDar proto; the DAR-direct path must reject --release-counters rather than silently emit r=0 against a different content baseline");
    }

    [Fact]
    public async Task release_counters_flag_writes_revision_zero_into_generated_csproj_on_first_emission()
    {
        var intermediate = Path.Combine(AppContext.BaseDirectory, "Snapshots", FixtureSnapshotName, "intermediate.binpb");
        var counters = Path.Combine(_workspace, "release-counters.json");

        var exit = await Program.Main(
        [
            "--intermediate", intermediate,
            "-o", _workspace,
            "--release-counters", counters,
            "--generate-project"
        ]);

        exit.Should().Be(0);

        var csproj = Directory.GetFiles(_workspace, "*.csproj", SearchOption.TopDirectoryOnly).Single();
        var version = XDocument.Load(csproj)
            .Descendants("Version")
            .Single()
            .Value;

        var segments = version.Split('.');
        segments.Should().HaveCount(4);
        segments[3].Should().Be("0",
            "segment 4 specifically must be 0 on a first emission per the M.m.p.r versioning scheme; EndsWith(\".0\") would also match e.g. 10.0 or 0.20");
    }
}
