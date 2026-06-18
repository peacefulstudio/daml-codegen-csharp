// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using FluentAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class GeneratedFileTests
{
    [Fact]
    public void Text_should_carry_its_content_and_not_be_binary()
    {
        var file = GeneratedFile.Text("README.md", "hello");

        file.RelativePath.Should().Be("README.md");
        file.Content.Should().Be("hello");
        file.BinaryContent.Should().BeNull();
        file.IsBinary.Should().BeFalse();
    }

    [Fact]
    public void Binary_should_carry_its_bytes_leave_content_empty_and_be_binary()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };

        var file = GeneratedFile.Binary("icon.png", bytes);

        file.RelativePath.Should().Be("icon.png");
        file.Content.Should().BeEmpty();
        file.BinaryContent.Should().Equal(bytes);
        file.IsBinary.Should().BeTrue();
    }

    [Fact]
    public void Binary_should_reject_empty_content()
    {
        var act = () => GeneratedFile.Binary("icon.png", []);

        act.Should().Throw<ArgumentException>(
            "a binary file with no bytes would pack as a 0-byte icon and fail dotnet pack downstream (NU5046)");
    }
}
