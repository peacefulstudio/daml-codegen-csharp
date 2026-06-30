// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.Model;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class PackageVersionParserTests
{
    [Fact]
    public void overflowing_segment_falls_back_to_zero_zero_zero()
    {
        var result = PackageVersionParser.Parse("2147483648.0.0");

        result.Should().Be(new Version(0, 0, 0));
    }
}
