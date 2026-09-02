// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.IO.Compression;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

public class ConformanceCorpusTests
{
    public static TheoryData<ConformancePackage> EveryPackage =>
        new(Enum.GetValues<ConformancePackage>());

    [Theory]
    [MemberData(nameof(EveryPackage))]
    public void OpenDar_returns_a_readable_stream_holding_a_whole_archive(ConformancePackage package)
    {
        using var stream = ConformanceCorpus.OpenDar(package);

        stream.Should().NotBeNull();
        stream.CanRead.Should().BeTrue();
        stream.Length.Should().BeGreaterThan(1000);
    }

    [Theory]
    [MemberData(nameof(EveryPackage))]
    public void OpenDar_returns_a_zip_archive_with_pk_magic_bytes(ConformancePackage package)
    {
        using var stream = ConformanceCorpus.OpenDar(package);
        var header = new byte[4];
        stream.ReadExactly(header);

        header[0].Should().Be((byte)'P');
        header[1].Should().Be((byte)'K');
        header[2].Should().Be(0x03);
        header[3].Should().Be(0x04);
    }

    [Theory]
    [MemberData(nameof(EveryPackage))]
    public void OpenDar_returns_independent_streams_on_each_call(ConformancePackage package)
    {
        using var first = ConformanceCorpus.OpenDar(package);
        using var second = ConformanceCorpus.OpenDar(package);

        first.Should().NotBeSameAs(second);
        first.ReadByte().Should().Be((byte)'P');
        second.Position.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(EveryPackage))]
    public void OpenDar_returns_the_archive_whose_entries_name_that_package(ConformancePackage package)
    {
        using var zip = new ZipArchive(ConformanceCorpus.OpenDar(package));
        var expectedPrefix = package + "-";

        zip.Entries.Should().Contain(entry =>
            entry.FullName.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OpenDar_with_no_argument_opens_the_RichTypes_dar()
    {
        using var noArg = ConformanceCorpus.OpenDar();
        using var richTypes = ConformanceCorpus.OpenDar(ConformancePackage.RichTypes);

        ReadAll(noArg).Should().Equal(ReadAll(richTypes));
    }

    private static byte[] ReadAll(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
