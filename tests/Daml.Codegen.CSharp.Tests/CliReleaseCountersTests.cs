// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
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
            "the CLI must persist the resolved generation ordinal back to the store path");

        var expectedCodegenVersion = typeof(Program).Assembly
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()!
            .InformationalVersion.Split('+')[0];

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(counters, TestContext.Current.CancellationToken));
        var generations = document.RootElement.GetProperty("codegen_generations").EnumerateObject().ToList();
        generations.Should().ContainSingle(
            "a fresh run against one fixture proto must persist exactly one codegen-version entry");
        generations[0].Name.Should().Be(expectedCodegenVersion,
            "the store is keyed by the codegen tool's own version, shared across every package it emits");
        generations[0].Value.ValueKind.Should().Be(JsonValueKind.Number,
            "the persisted value is the flat generation ordinal, not the retired content_hash/revision object shape");
        generations[0].Value.GetInt32().Should().Be(0,
            "a first-ever codegen version against an empty store mints generation 0");
    }

    [Fact]
    public async Task release_counters_flag_holds_generation_steady_on_re_emission_of_the_same_intermediate()
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
        document.RootElement.GetProperty("codegen_generations").EnumerateObject().Single().Value
            .GetInt32().Should().Be(0,
                "re-emissions under the same codegen version must hold the generation steady");
    }

    [Fact]
    public async Task codegen_version_flag_overrides_the_assembly_version_as_the_store_key()
    {
        var intermediate = Path.Combine(AppContext.BaseDirectory, "Snapshots", FixtureSnapshotName, "intermediate.binpb");
        var counters = Path.Combine(_workspace, "release-counters.json");

        var exit = await Program.Main(
        [
            "--intermediate", intermediate,
            "-o", _workspace,
            "--release-counters", counters,
            "--codegen-version", "9.9.9-ci-override",
            "--generate-project"
        ]);

        exit.Should().Be(0);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(counters, TestContext.Current.CancellationToken));
        var generations = document.RootElement.GetProperty("codegen_generations").EnumerateObject().ToList();
        generations.Should().ContainSingle();
        generations[0].Name.Should().Be("9.9.9-ci-override",
            "--codegen-version, when supplied, takes priority over the assembly's own informational version");
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
