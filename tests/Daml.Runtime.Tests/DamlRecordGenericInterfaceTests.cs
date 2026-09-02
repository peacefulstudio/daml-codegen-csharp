// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

/// <summary>
/// Tests for <see cref="IDamlRecord{TSelf}"/>: record-shaped values whose concrete
/// type can be materialized from a <see cref="DamlRecord"/> through the static
/// abstract factory, without reflection.
/// </summary>
public class DamlRecordGenericInterfaceTests
{
    private sealed record Badge(string Name) : IDamlRecord<Badge>
    {
        public DamlRecord ToRecord() =>
            DamlRecord.Create(DamlField.Create("name", new DamlText(Name)));

        public static Badge FromRecord(DamlRecord record) =>
            new(record.GetRequiredField("name").As<DamlText>().Value);
    }

    [Fact]
    public void IDamlRecord_TSelf_materializes_the_concrete_type_through_the_static_abstract_factory()
    {
        var record = new Badge("issuer-badge").ToRecord();

        DamlRecordFacet.Materialize<Badge>(record).Should().Be(new Badge("issuer-badge"));
    }
}
