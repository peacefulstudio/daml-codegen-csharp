// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.Model;
using Daml.Codegen.CSharp.Versioning;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class NuGetVersionResolverTests : IDisposable
{
    private readonly string _storePath;

    public NuGetVersionResolverTests()
    {
        _storePath = Path.Combine(
            Path.GetTempPath(),
            $"nuget-version-resolver-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_storePath)) File.Delete(_storePath);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Compute_returns_FourPartPackageVersion_with_Revision_zero_on_first_codegen_version()
    {
        var store = JsonReleaseCounterStore.OpenOrCreate(_storePath);

        var version = NuGetVersionResolver.Compute(
            intrinsicVersion: new Version(0, 1, 17),
            codegenVersion: "1.4.0",
            counterStore: store);

        version.Should().Be(new FourPartPackageVersion(0, 1, 17, 0));
        version.ToString().Should().Be("0.1.17.0");
    }

    [Fact]
    public void Compute_holds_Revision_steady_for_a_different_intrinsicVersion_under_the_same_codegenVersion()
    {
        var store = JsonReleaseCounterStore.OpenOrCreate(_storePath);

        NuGetVersionResolver.Compute(new Version(0, 1, 17), "1.4.0", store).Revision.Should().Be(0);
        NuGetVersionResolver.Compute(new Version(0, 2, 0), "1.4.0", store).Revision.Should().Be(0);
    }

    [Fact]
    public void Compute_bumps_Revision_when_codegenVersion_changes_on_the_same_store()
    {
        var store = JsonReleaseCounterStore.OpenOrCreate(_storePath);

        NuGetVersionResolver.Compute(new Version(0, 1, 17), "1.4.0", store)
            .Should().Be(new FourPartPackageVersion(0, 1, 17, 0));
        NuGetVersionResolver.Compute(new Version(0, 1, 17), "1.4.1", store)
            .Should().Be(new FourPartPackageVersion(0, 1, 17, 1));
    }

    [Fact]
    public void Compute_returns_the_identical_Revision_for_every_package_emitted_in_the_same_run()
    {
        var store = JsonReleaseCounterStore.OpenOrCreate(_storePath);

        var mainPackage = NuGetVersionResolver.Compute(new Version(0, 1, 17), "1.4.0", store);
        var dependencyOne = NuGetVersionResolver.Compute(new Version(0, 3, 2), "1.4.0", store);
        var dependencyTwo = NuGetVersionResolver.Compute(new Version(1, 0, 0), "1.4.0", store);

        mainPackage.Revision.Should().Be(dependencyOne.Revision);
        dependencyOne.Revision.Should().Be(dependencyTwo.Revision);
    }
}
