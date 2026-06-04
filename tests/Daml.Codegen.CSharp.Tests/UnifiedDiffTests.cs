// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using FluentAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class UnifiedDiffTests
{
    [Fact]
    public void Render_returns_null_for_identical_inputs()
    {
        var text = "line one\nline two\nline three";

        UnifiedDiff.Render(text, text).Should().BeNull();
    }

    [Fact]
    public void Render_shows_deleted_line_with_minus_prefix()
    {
        var expected = "line one\nline two\nline three";
        var actual = "line one\nline three";

        var diff = UnifiedDiff.Render(expected, actual);

        diff.Should().NotBeNull();
        diff!.Should().Contain("-line two");
    }

    [Fact]
    public void Render_shows_inserted_line_with_plus_prefix()
    {
        var expected = "line one\nline three";
        var actual = "line one\nline two\nline three";

        var diff = UnifiedDiff.Render(expected, actual);

        diff.Should().NotBeNull();
        diff!.Should().Contain("+line two");
    }

    [Fact]
    public void Render_shows_context_lines_around_change()
    {
        var expected = "a\nb\nc\nd\ne";
        var actual = "a\nb\nX\nd\ne";

        var diff = UnifiedDiff.Render(expected, actual);

        diff.Should().NotBeNull();
        diff!.Should().Contain(" b");
        diff!.Should().Contain("-c");
        diff!.Should().Contain("+X");
        diff!.Should().Contain(" d");
    }

    [Fact]
    public void Render_byte_overload_decodes_utf8_and_diffs_correctly()
    {
        var expected = System.Text.Encoding.UTF8.GetBytes("alpha\nbeta\ngamma");
        var actual = System.Text.Encoding.UTF8.GetBytes("alpha\nbeta_modified\ngamma");

        var diff = UnifiedDiff.Render(expected, actual);

        diff.Should().NotBeNull();
        diff!.Should().Contain("-beta");
        diff!.Should().Contain("+beta_modified");
    }

    [Fact]
    public void Render_byte_overload_returns_null_for_identical_bytes()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("same content\nsecond line");

        UnifiedDiff.Render(bytes, bytes).Should().BeNull();
    }

    [Fact]
    public void Render_truncates_after_max_hunks()
    {
        var expectedLines = Enumerable.Range(0, 300)
            .Select(i => i % 20 == 0 ? $"original-{i}" : $"line-{i}");
        var actualLines = Enumerable.Range(0, 300)
            .Select(i => i % 20 == 0 ? $"changed-{i}" : $"line-{i}");

        var diff = UnifiedDiff.Render(
            string.Join('\n', expectedLines),
            string.Join('\n', actualLines));

        diff.Should().NotBeNull();
        diff!.Should().Contain("truncated");
    }

    [Fact]
    public void Render_includes_hunk_header()
    {
        var expected = "a\nb\nc";
        var actual = "a\nB\nc";

        var diff = UnifiedDiff.Render(expected, actual);

        diff.Should().NotBeNull();
        diff!.Should().Contain("@@");
    }

    [Fact]
    public void Render_treats_crlf_and_lf_line_endings_as_equivalent()
    {
        var withLf = "line one\nline two\nline three";
        var withCrlf = "line one\r\nline two\r\nline three";

        UnifiedDiff.Render(withLf, withCrlf).Should().BeNull();
    }
}
