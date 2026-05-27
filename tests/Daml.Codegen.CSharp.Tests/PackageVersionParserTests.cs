// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Daml.Codegen.CSharp.Model;
using FluentAssertions;
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
