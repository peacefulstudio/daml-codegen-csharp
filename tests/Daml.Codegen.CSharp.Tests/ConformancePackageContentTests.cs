// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Daml.Codegen.Testing.Conformance;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class ConformancePackageContentTests
{
    [Fact]
    public void OpenDar_returns_a_valid_DAR_stream()
    {
        using var dar = ConformanceCorpus.OpenDar();
        Assert.True(dar.Length > 1000);

        var zipLocalFileHeaderMagic = new byte[] { 0x50, 0x4B, 0x03, 0x04 };
        Span<byte> header = stackalloc byte[4];
        Assert.Equal(4, dar.Read(header));
        Assert.Equal(zipLocalFileHeaderMagic, header.ToArray());
    }

    [Fact]
    public void Embedded_resource_is_named_richtypes_dar()
    {
        var names = typeof(ConformanceCorpus).Assembly.GetManifestResourceNames();
        Assert.Contains("richtypes.dar", names);
    }
}
