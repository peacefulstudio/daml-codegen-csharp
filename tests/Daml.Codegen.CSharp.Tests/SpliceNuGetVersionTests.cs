// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Daml.Codegen.CSharp.Model;
using Daml.Codegen.CSharp.Versioning;
using FluentAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class SpliceNuGetVersionTests : IDisposable
{
    private readonly string _storePath;

    public SpliceNuGetVersionTests()
    {
        _storePath = Path.Combine(
            Path.GetTempPath(),
            $"splice-version-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_storePath)) File.Delete(_storePath);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Compute_returns_FourPartPackageVersion_with_Revision_zero_on_first_emission()
    {
        var store = JsonReleaseCounterStore.OpenOrCreate(_storePath);

        var version = SpliceNuGetVersion.Compute(
            packageName: "Splice.Amulet",
            intrinsicVersion: new Version(0, 1, 17),
            contentHash: "deadbeef",
            counterStore: store);

        version.Should().Be(new FourPartPackageVersion(0, 1, 17, 0));
        version.ToString().Should().Be("0.1.17.0");
    }

    [Fact]
    public void Compute_bumps_Revision_when_contentHash_changes_under_same_intrinsicVersion()
    {
        var store = JsonReleaseCounterStore.OpenOrCreate(_storePath);

        SpliceNuGetVersion.Compute("Splice.Amulet", new Version(0, 1, 17), "hash-a", store)
            .Should().Be(new FourPartPackageVersion(0, 1, 17, 0));
        SpliceNuGetVersion.Compute("Splice.Amulet", new Version(0, 1, 17), "hash-b", store)
            .Should().Be(new FourPartPackageVersion(0, 1, 17, 1));
    }
}
