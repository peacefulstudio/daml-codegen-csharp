// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.Model;
using Daml.Codegen.CSharp.Versioning;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class VersioningApiSurfaceTests
{
    public static TheoryData<Type> VersioningClusterTypes() =>
        [
            typeof(JsonReleaseCounterStore),
            typeof(EmitterVersion),
            typeof(IntermediatePackageContentHash),
            typeof(NuGetVersionResolver),
            typeof(FourPartPackageVersion),
        ];

    [Theory]
    [MemberData(nameof(VersioningClusterTypes))]
    public void Versioning_cluster_is_internal_to_the_emitter(Type versioningType)
    {
        versioningType.IsPublic.Should().BeFalse(
            "the release-counter versioning machinery is this project's own CI release plumbing, " +
            "consumed only by the unpackable CLI, and must not freeze into the published library's public API");
    }
}
