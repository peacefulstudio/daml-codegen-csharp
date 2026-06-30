// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class DamlValueStructuralEqualityTests
{
    private static DamlRecord SampleRecord() => DamlRecord.Create(
        DamlField.Create("issuer", new DamlParty("Alice::1220abcd")),
        DamlField.Create("amount", new DamlNumeric(100.5m)),
        DamlField.Create("observers", DamlList.Create(new DamlParty("Bob::1220ef01"))));

    [Fact]
    public void DamlRecord_equals_another_record_with_equal_fields()
    {
        var left = SampleRecord();
        var right = SampleRecord();

        left.Should().Be(right);
        (left == right).Should().BeTrue();
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Fact]
    public void DamlRecord_differs_when_a_field_value_differs()
    {
        var left = DamlRecord.Create(DamlField.Create("amount", new DamlNumeric(100.5m)));
        var right = DamlRecord.Create(DamlField.Create("amount", new DamlNumeric(99.5m)));

        left.Should().NotBe(right);
    }

    [Fact]
    public void DamlRecord_differs_when_a_field_label_differs()
    {
        var left = DamlRecord.Create(DamlField.Create("amount", new DamlNumeric(100.5m)));
        var right = DamlRecord.Create(DamlField.Create("total", new DamlNumeric(100.5m)));

        left.Should().NotBe(right);
    }

    [Fact]
    public void DamlRecord_differs_when_RecordId_differs()
    {
        var fields = new[] { DamlField.Create("amount", new DamlNumeric(100.5m)) };
        var left = DamlRecord.Create(new Identifier("pkg1", "Module", "Entity"), fields);
        var right = DamlRecord.Create(new Identifier("pkg2", "Module", "Entity"), fields);

        left.Should().NotBe(right);
    }

    [Fact]
    public void DamlRecord_differs_when_field_order_differs()
    {
        var left = DamlRecord.Create(
            DamlField.Create("a", new DamlInt64(1)),
            DamlField.Create("b", new DamlInt64(2)));
        var right = DamlRecord.Create(
            DamlField.Create("b", new DamlInt64(2)),
            DamlField.Create("a", new DamlInt64(1)));

        left.Should().NotBe(right);
    }

    [Fact]
    public void DamlRecord_equals_compares_nested_collections_structurally()
    {
        var left = DamlRecord.Create(
            DamlField.Create("entries", DamlGenMap.Create((new DamlText("k"), new DamlInt64(1)))));
        var right = DamlRecord.Create(
            DamlField.Create("entries", DamlGenMap.Create((new DamlText("k"), new DamlInt64(1)))));

        left.Should().Be(right);
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Fact]
    public void DamlOptional_equals_compares_nested_collections_structurally()
    {
        var left = DamlOptional.Some(DamlList.Create(new DamlInt64(1), new DamlInt64(2)));
        var right = DamlOptional.Some(DamlList.Create(new DamlInt64(1), new DamlInt64(2)));

        left.Should().Be(right);
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Fact]
    public void DamlVariant_equals_compares_nested_collections_structurally()
    {
        var left = new DamlVariant(null, "Entries", DamlGenMap.Create((new DamlText("k"), new DamlInt64(1))));
        var right = new DamlVariant(null, "Entries", DamlGenMap.Create((new DamlText("k"), new DamlInt64(1))));

        left.Should().Be(right);
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Fact]
    public void DamlList_equals_another_list_with_equal_elements()
    {
        var left = DamlList.Create(new DamlInt64(1), new DamlInt64(2));
        var right = DamlList.Create(new DamlInt64(1), new DamlInt64(2));

        left.Should().Be(right);
        (left == right).Should().BeTrue();
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Fact]
    public void DamlList_differs_when_elements_differ()
    {
        var left = DamlList.Create(new DamlInt64(1), new DamlInt64(2));
        var right = DamlList.Create(new DamlInt64(1), new DamlInt64(3));

        left.Should().NotBe(right);
    }

    [Fact]
    public void DamlList_differs_when_element_order_differs()
    {
        var left = DamlList.Create(new DamlInt64(1), new DamlInt64(2));
        var right = DamlList.Create(new DamlInt64(2), new DamlInt64(1));

        left.Should().NotBe(right);
    }

    [Fact]
    public void DamlTextMap_equals_another_map_with_equal_entries()
    {
        var left = DamlTextMap.Create(("k1", new DamlInt64(1)), ("k2", new DamlText("v")));
        var right = DamlTextMap.Create(("k1", new DamlInt64(1)), ("k2", new DamlText("v")));

        left.Should().Be(right);
        (left == right).Should().BeTrue();
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Fact]
    public void DamlTextMap_equals_is_insertion_order_independent()
    {
        var left = DamlTextMap.Create(("k1", new DamlInt64(1)), ("k2", new DamlInt64(2)));
        var right = DamlTextMap.Create(("k2", new DamlInt64(2)), ("k1", new DamlInt64(1)));

        left.Should().Be(right);
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Fact]
    public void DamlTextMap_differs_when_a_value_differs()
    {
        var left = DamlTextMap.Create(("k1", new DamlInt64(1)));
        var right = DamlTextMap.Create(("k1", new DamlInt64(2)));

        left.Should().NotBe(right);
    }

    [Fact]
    public void DamlTextMap_differs_when_a_key_is_missing()
    {
        var left = DamlTextMap.Create(("k1", new DamlInt64(1)));
        var right = DamlTextMap.Create(("k2", new DamlInt64(1)));

        left.Should().NotBe(right);
    }

    [Fact]
    public void DamlGenMap_equals_another_map_with_equal_entries()
    {
        var left = DamlGenMap.Create((new DamlText("k"), new DamlInt64(1)));
        var right = DamlGenMap.Create((new DamlText("k"), new DamlInt64(1)));

        left.Should().Be(right);
        (left == right).Should().BeTrue();
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Fact]
    public void DamlGenMap_differs_when_an_entry_differs()
    {
        var left = DamlGenMap.Create((new DamlText("k"), new DamlInt64(1)));
        var right = DamlGenMap.Create((new DamlText("k"), new DamlInt64(2)));

        left.Should().NotBe(right);
    }

    [Fact]
    public void DamlGenMap_differs_when_entry_order_differs()
    {
        var left = DamlGenMap.Create(
            (new DamlText("a"), new DamlInt64(1)),
            (new DamlText("b"), new DamlInt64(2)));
        var right = DamlGenMap.Create(
            (new DamlText("b"), new DamlInt64(2)),
            (new DamlText("a"), new DamlInt64(1)));

        left.Should().NotBe(right);
    }
}
