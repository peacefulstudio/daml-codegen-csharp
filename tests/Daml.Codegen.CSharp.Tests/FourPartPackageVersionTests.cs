// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.Model;
using FluentAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class FourPartPackageVersionTests
{
    [Fact]
    public void TryParse_round_trips_full_four_part_string_through_ToString()
    {
        FourPartPackageVersion.TryParse("1.2.3.4", out var version).Should().BeTrue();

        version.Major.Should().Be(1);
        version.Minor.Should().Be(2);
        version.Patch.Should().Be(3);
        version.Revision.Should().Be(4);
        version.ToString().Should().Be("1.2.3.4");
    }

    [Fact]
    public void TryParse_defaults_Revision_to_zero_when_only_three_segments_supplied()
    {
        FourPartPackageVersion.TryParse("0.1.17", out var version).Should().BeTrue();

        version.Should().Be(new FourPartPackageVersion(0, 1, 17, 0));
        version.ToString().Should().Be("0.1.17.0");
    }

    [Fact]
    public void FromIntrinsic_lifts_three_part_Version_with_given_revision()
    {
        var lifted = FourPartPackageVersion.FromIntrinsic(new Version(0, 1, 17), revision: 3);

        lifted.Should().Be(new FourPartPackageVersion(0, 1, 17, 3));
    }

    [Fact]
    public void FromIntrinsic_normalises_unset_Build_segment_to_zero()
    {
        var lifted = FourPartPackageVersion.FromIntrinsic(new Version(1, 2), revision: 0);

        lifted.Should().Be(new FourPartPackageVersion(1, 2, 0, 0));
    }

    [Fact]
    public void FromIntrinsic_throws_ArgumentOutOfRangeException_when_revision_is_negative()
    {
        var act = () => FourPartPackageVersion.FromIntrinsic(new Version(0, 1, 17), revision: -1);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("revision");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4.5")]
    [InlineData("1.2.three.0")]
    [InlineData("-1.2.3.0")]
    [InlineData("1.2.3.0-rc1")]
    [InlineData("2147483648.0.0.0")]
    public void TryParse_returns_false_for_malformed_strings(string? raw)
    {
        FourPartPackageVersion.TryParse(raw, out var version).Should().BeFalse();

        version.Should().Be(default(FourPartPackageVersion));
    }
}
