// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using FluentAssertions;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

public class ConformanceCorpusTests
{
    [Fact]
    public void open_dar_returns_a_readable_non_empty_stream()
    {
        using var stream = ConformanceCorpus.OpenDar();

        stream.Should().NotBeNull();
        stream.CanRead.Should().BeTrue();
        stream.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void open_dar_returns_a_zip_archive_with_pk_magic_bytes()
    {
        using var stream = ConformanceCorpus.OpenDar();
        var header = new byte[4];
        stream.ReadExactly(header);

        header[0].Should().Be((byte)'P');
        header[1].Should().Be((byte)'K');
        header[2].Should().Be(0x03);
        header[3].Should().Be(0x04);
    }

    [Fact]
    public void open_dar_returns_independent_streams_on_each_call()
    {
        using var first = ConformanceCorpus.OpenDar();
        using var second = ConformanceCorpus.OpenDar();

        first.Should().NotBeSameAs(second);
        first.ReadByte().Should().Be((byte)'P');
        second.Position.Should().Be(0);
    }
}
