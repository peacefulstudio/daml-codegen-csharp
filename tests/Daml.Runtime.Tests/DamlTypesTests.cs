// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using FluentAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class DamlTypesTests
{
    [Fact]
    public void DamlInt64_should_convert_to_and_from_long()
    {
        // Arrange
        long expected = 42;

        // Act
        DamlInt64 damlValue = expected;
        long result = damlValue;

        // Assert
        result.Should().Be(expected);
        damlValue.Value.Should().Be(expected);
    }

    [Fact]
    public void DamlNumeric_should_preserve_decimal_precision()
    {
        // Arrange
        decimal expected = 123.456789m;

        // Act
        var damlValue = new DamlNumeric(expected, 6);

        // Assert
        damlValue.Value.Should().Be(expected);
        damlValue.Scale.Should().Be(6);
    }

    [Fact]
    public void DamlNumeric_should_support_implicit_conversions()
    {
        // Arrange
        decimal value = 123.456m;

        // Act
        DamlNumeric numeric = value;
        decimal result = numeric;

        // Assert
        result.Should().Be(value);
    }

    [Fact]
    public void DamlText_should_convert_to_and_from_string()
    {
        // Arrange
        string expected = "Hello, Daml!";

        // Act
        DamlText damlValue = expected;
        string result = damlValue;

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void DamlBool_should_convert_to_and_from_bool()
    {
        // Arrange
        bool expected = true;

        // Act
        DamlBool damlValue = expected;
        bool result = damlValue;

        // Assert
        result.Should().Be(expected);
        damlValue.Value.Should().BeTrue();
    }

    [Fact]
    public void DamlBool_false_should_convert_correctly()
    {
        // Arrange & Act
        DamlBool damlValue = false;

        // Assert
        damlValue.Value.Should().BeFalse();
        ((bool)damlValue).Should().BeFalse();
    }

    [Fact]
    public void DamlUnit_should_be_singleton()
    {
        // Act
        var unit1 = DamlUnit.Instance;
        var unit2 = DamlUnit.Instance;

        // Assert
        unit1.Should().BeSameAs(unit2);
    }

    [Fact]
    public void DamlParty_should_convert_to_and_from_string()
    {
        // Arrange
        string partyId = "Alice::12345";

        // Act
        DamlParty party = partyId;
        string result = party;

        // Assert
        result.Should().Be(partyId);
        party.Value.Should().Be(partyId);
        party.ToString().Should().Be(partyId);
    }

    [Fact]
    public void Party_should_convert_to_string_implicitly()
    {
        // Arrange
        var party = new Party("alice");

        // Act
        string s = party;

        // Assert
        s.Should().Be("alice");
    }

    [Fact]
    public void Party_should_convert_from_string_explicitly()
    {
        // Arrange & Act
        var party = (Party)"alice";

        // Assert
        party.Id.Should().Be("alice");
    }

    [Fact]
    public void Party_should_support_equality()
    {
        // Arrange
        var p1 = new Party("a");
        var p2 = new Party("a");
        var p3 = new Party("b");

        // Assert
        p1.Should().Be(p2);
        p1.Should().NotBe(p3);
    }

    [Fact]
    public void Party_should_round_trip_through_DamlParty()
    {
        // Arrange
        var party = new Party("alice");

        // Act
        var roundTripped = Party.FromDamlValue(party.ToDamlValue());

        // Assert
        roundTripped.Should().Be(party);
    }

    [Fact]
    public void Party_ToString_should_return_id()
    {
        // Arrange
        var party = new Party("alice");

        // Act & Assert
        party.ToString().Should().Be("alice");
    }

    [Fact]
    public void Party_constructor_should_throw_on_null()
    {
        // Act
        var act = () => new Party(null!);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Party_constructor_should_throw_on_empty()
    {
        // Act
        var act = () => new Party("");

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Party_constructor_should_throw_on_whitespace()
    {
        // Act
        var act = () => new Party("  ");

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Party_default_ToDamlValue_should_throw()
    {
        // Arrange
        var party = default(Party);

        // Act
        var act = () => party.ToDamlValue();

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Party_default_implicit_string_should_throw()
    {
        // Arrange
        var party = default(Party);

        // Act
        var act = () => { string _ = party; };

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Party_FromDamlValue_should_throw_on_null_value()
    {
        // Act
        var act = () => Party.FromDamlValue(new DamlParty(null!));

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Party_FromDamlValue_should_throw_on_null_argument()
    {
        // Act
        var act = () => Party.FromDamlValue(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Party_default_ToString_should_return_diagnostic_string()
    {
        // Arrange
        var party = default(Party);

        // Act & Assert
        party.ToString().Should().Be("<uninitialized Party>");
    }

    [Fact]
    public void Party_default_Id_should_throw()
    {
        // Arrange
        var party = default(Party);

        // Act
        var act = () => party.Id;

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void DamlDate_should_convert_from_days_since_epoch()
    {
        // Arrange - January 1, 2020 is 18262 days since Unix epoch
        int days = 18262;

        // Act
        var damlDate = DamlDate.FromDaysSinceEpoch(days);

        // Assert
        damlDate.Value.Should().Be(new DateOnly(2020, 1, 1));
    }

    [Fact]
    public void DamlDate_DaysSinceEpoch_should_calculate_correctly()
    {
        // Arrange - January 1, 2020 is 18262 days since Unix epoch
        var date = new DamlDate(new DateOnly(2020, 1, 1));

        // Act
        var days = date.DaysSinceEpoch;

        // Assert
        days.Should().Be(18262);
    }

    [Fact]
    public void DamlDate_should_support_implicit_conversions()
    {
        // Arrange
        var dateOnly = new DateOnly(2023, 6, 15);

        // Act
        DamlDate damlDate = dateOnly;
        DateOnly result = damlDate;

        // Assert
        result.Should().Be(dateOnly);
    }

    [Fact]
    public void DamlTimestamp_should_convert_from_microseconds_since_epoch()
    {
        // Arrange - 1000000 microseconds = 1 second after epoch
        long microseconds = 1_000_000;

        // Act
        var timestamp = DamlTimestamp.FromMicrosecondsSinceEpoch(microseconds);

        // Assert
        timestamp.Value.Should().Be(DateTimeOffset.UnixEpoch.AddSeconds(1));
    }

    [Fact]
    public void DamlTimestamp_MicrosecondsSinceEpoch_should_calculate_correctly()
    {
        // Arrange - 1 second after epoch = 1,000,000 microseconds
        var timestamp = new DamlTimestamp(DateTimeOffset.UnixEpoch.AddSeconds(1));

        // Act
        var microseconds = timestamp.MicrosecondsSinceEpoch;

        // Assert
        microseconds.Should().Be(1_000_000);
    }

    [Fact]
    public void DamlTimestamp_should_support_implicit_conversions()
    {
        // Arrange
        var dto = new DateTimeOffset(2023, 6, 15, 12, 30, 45, TimeSpan.Zero);

        // Act
        DamlTimestamp timestamp = dto;
        DateTimeOffset result = timestamp;

        // Assert
        result.Should().Be(dto);
    }

    [Fact]
    public void DamlOptional_None_should_have_no_value()
    {
        // Act
        var optional = DamlOptional.None;

        // Assert
        optional.HasValue.Should().BeFalse();
        optional.Value.Should().BeNull();
    }

    [Fact]
    public void DamlOptional_Some_should_have_value()
    {
        // Arrange
        var innerValue = new DamlText("test");

        // Act
        var optional = DamlOptional.Some(innerValue);

        // Assert
        optional.HasValue.Should().BeTrue();
        optional.Value.Should().Be(innerValue);
    }

    [Fact]
    public void DamlOptional_GetValueOrDefault_should_return_value_when_present()
    {
        // Arrange
        var optional = DamlOptional.Some(new DamlInt64(42));

        // Act
        var result = optional.GetValueOrDefault<DamlInt64>();

        // Assert
        result.Should().NotBeNull();
        result!.Value.Should().Be(42);
    }

    [Fact]
    public void DamlOptional_GetValueOrDefault_should_return_null_when_None()
    {
        // Arrange
        var optional = DamlOptional.None;

        // Act
        var result = optional.GetValueOrDefault<DamlInt64>();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void DamlOptional_GetValueOrThrow_should_return_value_when_present()
    {
        // Arrange
        var innerValue = new DamlText("present");
        var optional = DamlOptional.Some(innerValue);

        // Act
        var result = optional.GetValueOrThrow();

        // Assert
        result.Should().Be(innerValue);
    }

    [Fact]
    public void DamlOptional_GetValueOrThrow_should_throw_when_None()
    {
        // Arrange
        var optional = DamlOptional.None;

        // Act
        var act = () => optional.GetValueOrThrow();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Optional value is None.");
    }

    [Fact]
    public void DamlList_should_contain_values()
    {
        // Arrange
        var values = new DamlValue[]
        {
            new DamlInt64(1),
            new DamlInt64(2),
            new DamlInt64(3)
        };

        // Act
        var list = DamlList.Create(values);

        // Assert
        list.Count.Should().Be(3);
        list[0].As<DamlInt64>().Value.Should().Be(1);
        list[1].As<DamlInt64>().Value.Should().Be(2);
        list[2].As<DamlInt64>().Value.Should().Be(3);
    }

    [Fact]
    public void DamlList_AsEnumerable_should_cast_correctly()
    {
        // Arrange
        var list = DamlList.Create(
            new DamlInt64(1),
            new DamlInt64(2),
            new DamlInt64(3));

        // Act
        var result = list.AsEnumerable<DamlInt64>().ToList();

        // Assert
        result.Should().HaveCount(3);
        result[0].Value.Should().Be(1);
        result[1].Value.Should().Be(2);
        result[2].Value.Should().Be(3);
    }

    [Fact]
    public void DamlList_Create_should_work_with_enumerable()
    {
        // Arrange
        var values = new List<DamlValue>
        {
            new DamlText("a"),
            new DamlText("b")
        };

        // Act
        var list = DamlList.Create(values);

        // Assert
        list.Count.Should().Be(2);
        list[0].As<DamlText>().Value.Should().Be("a");
        list[1].As<DamlText>().Value.Should().Be("b");
    }

    [Fact]
    public void DamlTextMap_should_store_and_retrieve_values()
    {
        // Arrange & Act
        var map = DamlTextMap.Create(
            ("key1", new DamlInt64(100)),
            ("key2", new DamlText("value2")));

        // Assert
        map.Count.Should().Be(2);
        map["key1"].As<DamlInt64>().Value.Should().Be(100);
        map["key2"].As<DamlText>().Value.Should().Be("value2");
    }

    [Fact]
    public void DamlTextMap_TryGetValue_should_return_correct_result()
    {
        // Arrange
        var map = DamlTextMap.Create(("existing", new DamlInt64(42)));

        // Act & Assert
        map.TryGetValue("existing", out var existingValue).Should().BeTrue();
        existingValue!.As<DamlInt64>().Value.Should().Be(42);

        map.TryGetValue("nonexistent", out var missingValue).Should().BeFalse();
        missingValue.Should().BeNull();
    }

    [Fact]
    public void DamlGenMap_should_store_key_value_pairs()
    {
        // Arrange & Act
        var map = DamlGenMap.Create(
            (new DamlInt64(1), new DamlText("one")),
            (new DamlInt64(2), new DamlText("two")));

        // Assert
        map.Count.Should().Be(2);
        map.Entries[0].Key.As<DamlInt64>().Value.Should().Be(1);
        map.Entries[0].Value.As<DamlText>().Value.Should().Be("one");
        map.Entries[1].Key.As<DamlInt64>().Value.Should().Be(2);
        map.Entries[1].Value.As<DamlText>().Value.Should().Be("two");
    }

    [Fact]
    public void DamlRecord_should_allow_field_access()
    {
        // Arrange
        var record = DamlRecord.Create(
            DamlField.Create("name", new DamlText("Alice")),
            DamlField.Create("amount", new DamlNumeric(100.50m))
        );

        // Act
        var name = record.GetField("name");
        var amount = record.GetField("amount");

        // Assert
        name.Should().NotBeNull();
        name!.As<DamlText>().Value.Should().Be("Alice");
        amount.Should().NotBeNull();
        amount!.As<DamlNumeric>().Value.Should().Be(100.50m);
    }

    [Fact]
    public void DamlRecord_GetRequiredField_should_return_value_when_exists()
    {
        // Arrange
        var record = DamlRecord.Create(
            DamlField.Create("name", new DamlText("Alice")));

        // Act
        var result = record.GetRequiredField("name");

        // Assert
        result.As<DamlText>().Value.Should().Be("Alice");
    }

    [Fact]
    public void DamlRecord_GetRequiredField_should_throw_when_missing()
    {
        // Arrange
        var record = DamlRecord.Create(
            DamlField.Create("name", new DamlText("Alice")));

        // Act
        var act = () => record.GetRequiredField("missing");

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Required field 'missing' not found in record.");
    }

    [Fact]
    public void DamlRecord_Create_should_store_record_id()
    {
        // Arrange
        var recordId = new Identifier("pkg123", "MyModule", "MyRecord");

        // Act
        var record = DamlRecord.Create(recordId,
            DamlField.Create("field1", new DamlInt64(10)));

        // Assert
        record.RecordId.Should().Be(recordId);
        record.Fields.Should().HaveCount(1);
    }

    [Fact]
    public void DamlVariant_should_store_constructor_and_value()
    {
        // Arrange
        var value = new DamlText("error message");

        // Act
        var variant = DamlVariant.Create("Error", value);

        // Assert
        variant.Constructor.Should().Be("Error");
        variant.Is("Error").Should().BeTrue();
        variant.Is("Success").Should().BeFalse();
        variant.GetValue<DamlText>().Value.Should().Be("error message");
    }

    [Fact]
    public void DamlVariant_Create_should_store_variant_id()
    {
        // Arrange
        var variantId = new Identifier("pkg123", "Either", "Either");

        // Act
        var variant = DamlVariant.Create(variantId, "Left", new DamlText("error"));

        // Assert
        variant.VariantId.Should().Be(variantId);
        variant.Constructor.Should().Be("Left");
        variant.Value.As<DamlText>().Value.Should().Be("error");
    }

    [Fact]
    public void DamlEnum_should_store_constructor()
    {
        // Arrange & Act
        var enumValue = DamlEnum.Create("Red");

        // Assert
        enumValue.Constructor.Should().Be("Red");
        enumValue.EnumId.Should().BeNull();
        enumValue.Is("Red").Should().BeTrue();
        enumValue.Is("Blue").Should().BeFalse();
    }

    [Fact]
    public void DamlEnum_Create_should_store_enum_id()
    {
        // Arrange
        var enumId = new Identifier("pkg123", "Colors", "Color");

        // Act
        var enumValue = DamlEnum.Create(enumId, "Green");

        // Assert
        enumValue.EnumId.Should().Be(enumId);
        enumValue.Constructor.Should().Be("Green");
    }

    [Fact]
    public void DamlValue_As_should_throw_on_invalid_cast()
    {
        // Arrange
        DamlValue value = new DamlInt64(42);

        // Act
        var act = () => value.As<DamlText>();

        // Assert
        act.Should().Throw<InvalidCastException>()
            .WithMessage("Cannot cast DamlInt64 to DamlText");
    }

    [Fact]
    public void DamlValue_TryGet_should_return_true_for_valid_cast()
    {
        // Arrange
        DamlValue value = new DamlInt64(42);

        // Act
        var success = value.TryGet<DamlInt64>(out var result);

        // Assert
        success.Should().BeTrue();
        result!.Value.Should().Be(42);
    }

    [Fact]
    public void DamlValue_TryGet_should_return_false_for_invalid_cast()
    {
        // Arrange
        DamlValue value = new DamlInt64(42);

        // Act
        var success = value.TryGet<DamlText>(out var result);

        // Assert
        success.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void Identifier_Parse_should_parse_three_part_format()
    {
        // Arrange
        var input = "abc123:Module.Name:Entity";

        // Act
        var id = Identifier.Parse(input);

        // Assert
        id.PackageId.Should().Be("abc123");
        id.ModuleName.Should().Be("Module.Name");
        id.EntityName.Should().Be("Entity");
        id.FullyQualifiedName.Should().Be("Module.Name:Entity");
    }

    [Fact]
    public void Identifier_Parse_should_parse_two_part_format()
    {
        // Arrange
        var input = "Module.Name:Entity";

        // Act
        var id = Identifier.Parse(input);

        // Assert
        id.PackageId.Should().BeEmpty();
        id.ModuleName.Should().Be("Module.Name");
        id.EntityName.Should().Be("Entity");
    }

    [Fact]
    public void Identifier_Parse_should_throw_on_invalid_format()
    {
        // Arrange
        var input = "InvalidFormat";

        // Act
        var act = () => Identifier.Parse(input);

        // Assert
        act.Should().Throw<FormatException>()
            .WithMessage("Invalid identifier format: InvalidFormat");
    }

    [Fact]
    public void Identifier_ToString_should_return_full_format()
    {
        // Arrange
        var id = new Identifier("pkg123", "Module.Name", "Entity");

        // Act
        var result = id.ToString();

        // Assert
        result.Should().Be("pkg123:Module.Name:Entity");
    }
}
