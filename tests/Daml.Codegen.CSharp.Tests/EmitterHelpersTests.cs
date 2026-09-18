// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class EmitterHelpersTests
{
    [Theory]
    [InlineData("Agreement", "Agreement")]
    [InlineData("IReadOnlyDictionary<string, long>", "IReadOnlyDictionary&lt;string, long&gt;")]
    [InlineData("Tuple2<Party, long>", "Tuple2&lt;Party, long&gt;")]
    [InlineData("A & B", "A &amp; B")]
    [InlineData("O'Brien's \"quote\"", "O&apos;Brien&apos;s &quot;quote&quot;")]
    public void EscapeXmlText_escapes_reserved_xml_characters(string value, string expected)
    {
        EmitterHelpers.EscapeXmlText(value).Should().Be(expected);
    }
}
