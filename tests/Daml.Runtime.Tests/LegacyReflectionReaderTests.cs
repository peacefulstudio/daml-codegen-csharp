// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Runtime.Tests;

/// <summary>
/// Workaround for the legacy reflection reader's inability to express every CLR shape: pins
/// its behaviour so it still throws <see cref="NotSupportedException"/> for an unmapped shape
/// a <see cref="Type"/> cannot express, and still does not count a present Optional's carried
/// value as a nesting level.
/// </summary>
public class LegacyReflectionReaderTests
{
    public sealed record UnmappedFieldHolder(
        [property: DamlFieldAttribute("value")] Uri Value) : IDamlRecord
    {
        public DamlRecord ToRecord() => throw new NotSupportedException("decode-only shape");
    }

    [Fact]
    public void ReadValue_should_still_throw_NotSupportedException_for_a_shape_a_CLR_Type_cannot_express()
    {
#pragma warning disable DAMLRT0001, CA2263
        var act = () => DamlLfJsonReader.ReadValue(
            """{"value":"https://example.com"}""", typeof(UnmappedFieldHolder));
#pragma warning restore DAMLRT0001, CA2263

        act.Should().Throw<NotSupportedException>().WithMessage(
            "CLR type 'System.Uri' at 'UnmappedFieldHolder.value' lies outside the Daml type mapping; "
            + "give the property a mapped Daml type or decode this field without the reader.");
    }

    public sealed class LegacyOptionalDepthHolder(LegacyOptionalDepthHolder? nested) : IDamlRecord
    {
        [DamlFieldAttribute("nested")]
        public LegacyOptionalDepthHolder? Nested { get; } = nested;

        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create(
            "nested",
            Nested is null ? DamlOptional.None : DamlOptional.Some(Nested.ToRecord())));
    }

    private const int LevelsThatStayUnderTheReflectionReaderBound = 100;

    private static string NestedOptionalJson(int levels) =>
        string.Concat(Enumerable.Repeat("""{"nested":""", levels))
        + "null"
        + string.Concat(Enumerable.Repeat("}", levels));

    [Fact]
    public void ReadValue_should_still_not_count_a_present_Optional_field_toward_the_nesting_depth_bound()
    {
#pragma warning disable DAMLRT0001, CA2263
        var value = DamlLfJsonReader.ReadValue(
            NestedOptionalJson(LevelsThatStayUnderTheReflectionReaderBound),
            typeof(LegacyOptionalDepthHolder));
#pragma warning restore DAMLRT0001, CA2263

        value.Should().BeOfType<DamlRecord>()
            .Which.GetRequiredField("nested").Should().BeOfType<DamlOptional>()
            .Which.Value.Should().NotBeNull();
    }
}
