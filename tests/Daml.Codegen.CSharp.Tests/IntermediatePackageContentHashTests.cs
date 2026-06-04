// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.Versioning;
using Daml.Codegen.Intermediate;
using FluentAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class IntermediatePackageContentHashTests
{
    [Fact]
    public void Compute_returns_the_same_hash_for_field_identical_packages()
    {
        var first = MakePackage("Splice.Amulet", "0.1.17");
        var second = MakePackage("Splice.Amulet", "0.1.17");

        IntermediatePackageContentHash.Compute(first)
            .Should().Be(IntermediatePackageContentHash.Compute(second));
    }

    [Fact]
    public void Compute_returns_a_different_hash_when_any_field_changes()
    {
        var baseline = MakePackage("Splice.Amulet", "0.1.17");
        var renamed = MakePackage("Splice.Util", "0.1.17");
        var bumped = MakePackage("Splice.Amulet", "0.1.18");

        var baselineHash = IntermediatePackageContentHash.Compute(baseline);
        IntermediatePackageContentHash.Compute(renamed).Should().NotBe(baselineHash);
        IntermediatePackageContentHash.Compute(bumped).Should().NotBe(baselineHash);
    }

    private static IntermediatePackage MakePackage(string name, string version) =>
        new()
        {
            PackageId = "deadbeef",
            PackageName = name,
            PackageVersion = version,
            LanguageVersion = "2.1",
        };
}
