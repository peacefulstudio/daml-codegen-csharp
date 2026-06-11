// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using FluentAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class DamlJsonSerializerTests
{
    [Fact]
    public void Serialize_should_convert_DamlRecord_to_json()
    {
        // Arrange
        var record = DamlRecord.Create(
            DamlField.Create("name", new DamlText("Bob")),
            DamlField.Create("age", new DamlInt64(30))
        );

        // Act
        var json = DamlJsonSerializer.Serialize(record);

        // Assert
        json.Should().Contain("\"name\"");
        json.Should().Contain("\"Bob\"");
        json.Should().Contain("\"age\"");
        json.Should().Contain("30");
    }

    [Fact]
    public void DeserializeRecord_should_parse_json_to_DamlRecord()
    {
        // Arrange
        var json = """{"name":"Charlie","score":95}""";

        // Act
        var record = DamlJsonSerializer.DeserializeRecord(json);

        // Assert
        record.Fields.Should().HaveCount(2);
        record.GetField("name")!.As<DamlText>().Value.Should().Be("Charlie");
        record.GetField("score")!.As<DamlInt64>().Value.Should().Be(95);
    }

    [Fact]
    public void Serialize_should_handle_nested_DamlRecord()
    {
        // Arrange
        var inner = DamlRecord.Create(
            DamlField.Create("x", new DamlInt64(10)),
            DamlField.Create("y", new DamlInt64(20))
        );
        var outer = DamlRecord.Create(
            DamlField.Create("point", inner)
        );

        // Act
        var json = DamlJsonSerializer.Serialize(outer);

        // Assert
        json.Should().Contain("\"point\"");
        json.Should().Contain("\"x\"");
        json.Should().Contain("\"y\"");
    }

    [Fact]
    public void Serialize_should_handle_DamlList()
    {
        // Arrange
        var list = DamlList.Create(
            new DamlInt64(1),
            new DamlInt64(2),
            new DamlInt64(3)
        );
        var record = DamlRecord.Create(
            DamlField.Create("numbers", list)
        );

        // Act
        var json = DamlJsonSerializer.Serialize(record);

        // Assert
        json.Should().Contain("[1,2,3]");
    }

    [Fact]
    public void Serialize_should_handle_DamlVariant()
    {
        // Arrange
        var variant = DamlVariant.Create("Left", new DamlText("error"));
        var record = DamlRecord.Create(
            DamlField.Create("result", variant)
        );

        // Act
        var json = DamlJsonSerializer.Serialize(record);

        // Assert
        json.Should().Contain("\"tag\"");
        json.Should().Contain("\"Left\"");
        json.Should().Contain("\"value\"");
    }

    [Fact]
    public void Serialize_should_handle_DamlBool()
    {
        // Arrange
        var record = DamlRecord.Create(
            DamlField.Create("isActive", new DamlBool(true)),
            DamlField.Create("isDisabled", new DamlBool(false))
        );

        // Act
        var json = DamlJsonSerializer.Serialize(record);

        // Assert
        json.Should().Contain("\"isActive\":true");
        json.Should().Contain("\"isDisabled\":false");
    }

    [Fact]
    public void Serialize_should_handle_DamlDate()
    {
        // Arrange
        var record = DamlRecord.Create(
            DamlField.Create("createdDate", new DamlDate(new DateOnly(2023, 12, 25)))
        );

        // Act
        var json = DamlJsonSerializer.Serialize(record);

        // Assert
        json.Should().Contain("2023-12-25");
    }

    [Fact]
    public void Serialize_should_handle_DamlTimestamp()
    {
        // Arrange
        var timestamp = new DateTimeOffset(2023, 6, 15, 12, 30, 45, TimeSpan.Zero);
        var record = DamlRecord.Create(
            DamlField.Create("updatedAt", new DamlTimestamp(timestamp))
        );

        // Act
        var json = DamlJsonSerializer.Serialize(record);

        // Assert
        json.Should().Contain("2023-06-15");
        json.Should().Contain("12:30:45");
    }

    [Fact]
    public void Serialize_should_handle_DamlParty()
    {
        // Arrange
        var record = DamlRecord.Create(
            DamlField.Create("owner", new DamlParty("Alice::12345"))
        );

        // Act
        var json = DamlJsonSerializer.Serialize(record);

        // Assert
        json.Should().Contain("\"owner\":\"Alice::12345\"");
    }

    [Fact]
    public void Serialize_should_handle_DamlOptional_None()
    {
        // Arrange
        var record = DamlRecord.Create(
            DamlField.Create("maybeValue", DamlOptional.None)
        );

        // Act
        var json = DamlJsonSerializer.Serialize(record);

        // Assert
        // Optional.None serializes to null
        json.Should().Contain("\"maybeValue\":null");
    }

    [Fact]
    public void Serialize_should_handle_DamlOptional_Some()
    {
        // Arrange
        var record = DamlRecord.Create(
            DamlField.Create("maybeValue", DamlOptional.Some(new DamlInt64(42)))
        );

        // Act
        var json = DamlJsonSerializer.Serialize(record);

        // Assert
        json.Should().Contain("\"maybeValue\":42");
    }

    [Fact]
    public void Serialize_should_handle_DamlTextMap()
    {
        // Arrange
        var textMap = DamlTextMap.Create(
            ("key1", new DamlText("value1")),
            ("key2", new DamlInt64(42))
        );
        var record = DamlRecord.Create(
            DamlField.Create("settings", textMap)
        );

        // Act
        var json = DamlJsonSerializer.Serialize(record);

        // Assert
        json.Should().Contain("\"key1\":\"value1\"");
        json.Should().Contain("\"key2\":42");
    }

    [Fact]
    public void Serialize_should_handle_DamlEnum()
    {
        // Arrange
        var enumValue = DamlEnum.Create("Active");
        var record = DamlRecord.Create(
            DamlField.Create("status", enumValue)
        );

        // Act
        var json = DamlJsonSerializer.Serialize(record);

        // Assert
        json.Should().Contain("\"status\":\"Active\"");
    }

    [Fact]
    public void Serialize_should_handle_DamlNumeric_with_precision()
    {
        var record = DamlRecord.Create(
            DamlField.Create("amount", new DamlNumeric(123.456789m, 10))
        );

        var json = DamlJsonSerializer.Serialize(record);

        json.Should().Contain("\"amount\":\"123.456789\"");
    }

    [Fact]
    public void Serialize_DamlNumeric_should_strip_trailing_zeros()
    {
        var record = DamlRecord.Create(
            DamlField.Create("amount", new DamlNumeric(1.50m))
        );

        var json = DamlJsonSerializer.Serialize(record);

        json.Should().Contain("\"amount\":\"1.5\"");
    }

    [Fact]
    public void Serialize_DamlNumeric_integer_value_should_get_single_trailing_zero()
    {
        var record = DamlRecord.Create(
            DamlField.Create("amount", new DamlNumeric(42m))
        );

        var json = DamlJsonSerializer.Serialize(record);

        json.Should().Contain("\"amount\":\"42.0\"");
    }

    [Fact]
    public void Serialize_DamlNumeric_high_precision_should_not_use_scientific_notation()
    {
        var record = DamlRecord.Create(
            DamlField.Create("amount", new DamlNumeric(0.0000000001m))
        );

        var json = DamlJsonSerializer.Serialize(record);

        json.Should().Contain("\"amount\":\"0.0000000001\"");
    }

    [Fact]
    public void Serialize_DamlNumeric_should_be_independent_of_construction_scale()
    {
        var fromShort = DamlJsonSerializer.Serialize(new DamlNumeric(1.5m));
        var fromPadded = DamlJsonSerializer.Serialize(new DamlNumeric(1.500000m));

        fromShort.Should().Be("\"1.5\"");
        fromPadded.Should().Be(fromShort);
    }

    [Fact]
    public void Serialize_DamlNumeric_negative_value_should_keep_sign_and_strip_trailing_zeros()
    {
        var fractional = DamlJsonSerializer.Serialize(new DamlNumeric(-1.50m));
        var integral = DamlJsonSerializer.Serialize(new DamlNumeric(-42m));

        fractional.Should().Be("\"-1.5\"");
        integral.Should().Be("\"-42.0\"");
    }

    [Fact]
    public void Serialize_DamlNumeric_zero_should_be_canonical_zero_with_trailing_zero()
    {
        var json = DamlJsonSerializer.Serialize(new DamlNumeric(0m));

        json.Should().Be("\"0.0\"");
    }

    [Fact]
    public void DeserializeRecord_should_handle_DamlList()
    {
        // Arrange
        var json = """{"items":[1,2,3]}""";

        // Act
        var record = DamlJsonSerializer.DeserializeRecord(json);

        // Assert
        var items = record.GetField("items")!.As<DamlList>();
        items.Count.Should().Be(3);
        items[0].As<DamlInt64>().Value.Should().Be(1);
    }

    [Fact]
    public void DeserializeRecord_should_handle_DamlBool()
    {
        // Arrange
        var json = """{"active":true,"disabled":false}""";

        // Act
        var record = DamlJsonSerializer.DeserializeRecord(json);

        // Assert
        record.GetField("active")!.As<DamlBool>().Value.Should().BeTrue();
        record.GetField("disabled")!.As<DamlBool>().Value.Should().BeFalse();
    }

    [Fact]
    public void DeserializeRecord_should_handle_DamlNumeric()
    {
        // Arrange
        var json = """{"amount":123.456}""";

        // Act
        var record = DamlJsonSerializer.DeserializeRecord(json);

        // Assert
        record.GetField("amount")!.As<DamlNumeric>().Value.Should().Be(123.456m);
    }

    [Fact]
    public void DeserializeRecord_should_handle_DamlVariant()
    {
        // Arrange
        var json = """{"result":{"tag":"Left","value":"error message"}}""";

        // Act
        var record = DamlJsonSerializer.DeserializeRecord(json);

        // Assert
        var variant = record.GetField("result")!.As<DamlVariant>();
        variant.Constructor.Should().Be("Left");
        variant.Value.As<DamlText>().Value.Should().Be("error message");
    }

    [Fact]
    public void DeserializeRecord_should_keep_object_with_properties_beyond_tag_and_value_as_DamlRecord()
    {
        var json = """{"payload":{"tag":"Left","value":"err","extra":true}}""";

        var record = DamlJsonSerializer.DeserializeRecord(json);

        var payload = record.GetRequiredField("payload").As<DamlRecord>();
        payload.Fields.Should().HaveCount(3);
        payload.GetRequiredField("tag").Should().Be(new DamlText("Left"));
        payload.GetRequiredField("extra").Should().Be(new DamlBool(true));
    }

    [Fact]
    public void DeserializeRecord_should_keep_object_with_non_string_tag_as_DamlRecord()
    {
        var json = """{"payload":{"tag":7,"value":"err"}}""";

        var record = DamlJsonSerializer.DeserializeRecord(json);

        var payload = record.GetRequiredField("payload").As<DamlRecord>();
        payload.GetRequiredField("tag").Should().Be(new DamlInt64(7));
        payload.GetRequiredField("value").Should().Be(new DamlText("err"));
    }

    [Fact]
    public void DeserializeRecord_should_handle_nested_DamlRecord()
    {
        // Arrange
        var json = """{"person":{"name":"Alice","age":30}}""";

        // Act
        var record = DamlJsonSerializer.DeserializeRecord(json);

        // Assert
        var person = record.GetField("person")!.As<DamlRecord>();
        person.GetField("name")!.As<DamlText>().Value.Should().Be("Alice");
        person.GetField("age")!.As<DamlInt64>().Value.Should().Be(30);
    }

    [Fact]
    public void DeserializeRecord_should_handle_DamlDate()
    {
        // Arrange
        var json = """{"date":"2023-12-25"}""";

        // Act
        var record = DamlJsonSerializer.DeserializeRecord(json);

        // Assert
        var date = record.GetField("date")!.As<DamlDate>();
        date.Value.Should().Be(new DateOnly(2023, 12, 25));
    }

    [Fact]
    public void DeserializeRecord_should_handle_DamlTimestamp()
    {
        // Arrange
        var json = """{"timestamp":"2023-06-15T12:30:45+00:00"}""";

        // Act
        var record = DamlJsonSerializer.DeserializeRecord(json);

        // Assert
        var ts = record.GetField("timestamp")!.As<DamlTimestamp>();
        ts.Value.Should().Be(new DateTimeOffset(2023, 6, 15, 12, 30, 45, TimeSpan.Zero));
    }

    [Fact]
    public void DeserializeRecord_should_handle_DamlVariant_with_null_value()
    {
        // Arrange
        var json = """{"result":{"tag":"Empty","value":null}}""";

        // Act
        var record = DamlJsonSerializer.DeserializeRecord(json);

        // Assert
        var variant = record.GetField("result")!.As<DamlVariant>();
        variant.Constructor.Should().Be("Empty");
        variant.Value.Should().BeOfType<DamlUnit>();
    }

    public static TheoryData<string, DamlValue, string> Serialize_value_overload_cases() => new()
    {
        { "DamlText", new DamlText("hello"), "\"hello\"" },
        { "DamlInt64", new DamlInt64(42), "42" },
        { "DamlBool_true", new DamlBool(true), "true" },
        { "DamlBool_false", new DamlBool(false), "false" },
        { "DamlParty", new DamlParty("Alice::12345"), "\"Alice::12345\"" },
        { "DamlDate", new DamlDate(new DateOnly(2023, 12, 25)), "\"2023-12-25\"" },
        { "DamlUnit", DamlUnit.Instance, "{}" },
        { "DamlGenMap", DamlGenMap.Create(
            (new DamlText("k1"), new DamlInt64(1)),
            (new DamlText("k2"), new DamlInt64(2))), "[[\"k1\",1],[\"k2\",2]]" },
        { "DamlOptional_Some", DamlOptional.Some(new DamlInt64(42)), "42" },
        { "DamlOptional_None", DamlOptional.None, "null" },
    };

    [Theory]
    [MemberData(nameof(Serialize_value_overload_cases))]
    public void Serialize_value_overload_should_emit_expected_json(string subtype, DamlValue value, string expectedJson)
    {
        var valueJson = DamlJsonSerializer.Serialize(value);

        valueJson.Should().Be(expectedJson, "Serialize<DamlValue> for {0} must emit the direct JSON shape", subtype);

        var recordJson = DamlJsonSerializer.Serialize(
            DamlRecord.Create(DamlField.Create("f", value)));
        recordJson.Should().Be($"{{\"f\":{expectedJson}}}", "record-path slice for {0} must equal the value-overload output", subtype);
    }

    [Fact]
    public void Serialize_value_overload_should_handle_DamlNumeric()
    {
        var numeric = new DamlNumeric(123.456789m, 10);

        var valueJson = DamlJsonSerializer.Serialize(numeric);
        var recordJson = DamlJsonSerializer.Serialize(
            DamlRecord.Create(DamlField.Create("n", numeric)));

        valueJson.Should().Be("\"123.456789\"");
        recordJson.Should().Be($"{{\"n\":{valueJson}}}");
    }

    [Fact]
    public void Issue_155_Serialize_value_overload_should_not_stack_overflow_on_any_subtype()
    {
        Action act = () =>
        {
            DamlJsonSerializer.Serialize(new DamlText("hello"));
            DamlJsonSerializer.Serialize(new DamlInt64(42));
            DamlJsonSerializer.Serialize(new DamlBool(true));
            DamlJsonSerializer.Serialize(new DamlNumeric(1.5m));
            DamlJsonSerializer.Serialize(new DamlParty("Alice"));
            DamlJsonSerializer.Serialize(new DamlDate(new DateOnly(2024, 1, 1)));
            DamlJsonSerializer.Serialize(new DamlTimestamp(DateTimeOffset.UnixEpoch));
            DamlJsonSerializer.Serialize(new DamlContractId("cid"));
            DamlJsonSerializer.Serialize((DamlValue)DamlUnit.Instance);
            DamlJsonSerializer.Serialize(DamlOptional.Some(new DamlInt64(1)));
            DamlJsonSerializer.Serialize(DamlOptional.None);
            DamlJsonSerializer.Serialize(DamlList.Create(new DamlInt64(1)));
            DamlJsonSerializer.Serialize(DamlTextMap.Create(("k", new DamlInt64(1))));
            DamlJsonSerializer.Serialize(DamlGenMap.Create((new DamlText("k"), new DamlInt64(1))));
            DamlJsonSerializer.Serialize(DamlVariant.Create("Some", new DamlInt64(1)));
            DamlJsonSerializer.Serialize(DamlEnum.Create("Active"));
            DamlJsonSerializer.Serialize((DamlValue)DamlRecord.Create(
                DamlField.Create("x", new DamlInt64(1))));
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void Serialize_value_overload_should_handle_DamlUnit()
    {
        var valueJson = DamlJsonSerializer.Serialize((DamlValue)DamlUnit.Instance);
        var recordJson = DamlJsonSerializer.Serialize(
            DamlRecord.Create(DamlField.Create("u", DamlUnit.Instance)));

        valueJson.Should().Be("{}");
        recordJson.Should().Be($"{{\"u\":{valueJson}}}");
    }

    [Fact]
    public void RoundTrip_value_overload_decodes_DamlUnit_as_empty_DamlRecord()
    {
        var json = DamlJsonSerializer.Serialize((DamlValue)DamlUnit.Instance);
        var deserialized = DamlJsonSerializer.Deserialize(json);

        deserialized.Should().BeOfType<DamlRecord>();
        deserialized.As<DamlRecord>().Fields.Should().BeEmpty();
    }

    [Fact]
    public void Serialize_value_overload_should_handle_DamlRecord()
    {
        var inner = DamlRecord.Create(DamlField.Create("x", new DamlInt64(1)));

        var valueJson = DamlJsonSerializer.Serialize((DamlValue)inner);
        var recordJson = DamlJsonSerializer.Serialize(inner);

        valueJson.Should().Be("{\"x\":1}");
        recordJson.Should().Be(valueJson);
    }

    [Fact]
    public void Serialize_value_overload_should_handle_DamlEnum()
    {
        var enumValue = DamlEnum.Create("Active");

        var valueJson = DamlJsonSerializer.Serialize(enumValue);
        var recordJson = DamlJsonSerializer.Serialize(
            DamlRecord.Create(DamlField.Create("e", enumValue)));

        valueJson.Should().Be("\"Active\"");
        recordJson.Should().Be($"{{\"e\":{valueJson}}}");
    }

    [Fact]
    public void Serialize_value_overload_should_handle_DamlVariant()
    {
        var variant = DamlVariant.Create("Left", new DamlText("err"));

        var valueJson = DamlJsonSerializer.Serialize(variant);
        var recordJson = DamlJsonSerializer.Serialize(
            DamlRecord.Create(DamlField.Create("v", variant)));

        valueJson.Should().Be("{\"tag\":\"Left\",\"value\":\"err\"}");
        recordJson.Should().Be($"{{\"v\":{valueJson}}}");
    }

    [Fact]
    public void Serialize_value_overload_should_handle_DamlTextMap()
    {
        var textMap = DamlTextMap.Create(("k", new DamlInt64(7)));

        var valueJson = DamlJsonSerializer.Serialize(textMap);
        var recordJson = DamlJsonSerializer.Serialize(
            DamlRecord.Create(DamlField.Create("m", textMap)));

        valueJson.Should().Be("{\"k\":7}");
        recordJson.Should().Be($"{{\"m\":{valueJson}}}");
    }

    [Fact]
    public void Serialize_value_overload_should_handle_DamlList()
    {
        var list = DamlList.Create(new DamlInt64(1), new DamlInt64(2), new DamlInt64(3));

        var valueJson = DamlJsonSerializer.Serialize(list);
        var recordJson = DamlJsonSerializer.Serialize(
            DamlRecord.Create(DamlField.Create("l", list)));

        valueJson.Should().Be("[1,2,3]");
        recordJson.Should().Be($"{{\"l\":{valueJson}}}");
    }

    [Fact]
    public void Serialize_value_overload_should_handle_DamlContractId()
    {
        var contractId = new DamlContractId("00abcd1234");

        var valueJson = DamlJsonSerializer.Serialize(contractId);
        var recordJson = DamlJsonSerializer.Serialize(
            DamlRecord.Create(DamlField.Create("cid", contractId)));

        valueJson.Should().Be("\"00abcd1234\"");
        recordJson.Should().Be($"{{\"cid\":{valueJson}}}");
    }

    [Fact]
    public void Serialize_value_overload_should_handle_DamlTimestamp()
    {
        var timestamp = new DateTimeOffset(2023, 6, 15, 12, 30, 45, TimeSpan.Zero);

        var valueJson = DamlJsonSerializer.Serialize(new DamlTimestamp(timestamp));
        var recordJson = DamlJsonSerializer.Serialize(
            DamlRecord.Create(DamlField.Create("t", new DamlTimestamp(timestamp))));

        valueJson.Should().Be("\"2023-06-15T12:30:45.0000000\\u002B00:00\"");
        recordJson.Should().Be($"{{\"t\":{valueJson}}}");
    }

    public static TheoryData<string> DateLikeTextOutsideCanonicalFormats() => new()
    {
        "12:30",
        "12/25/2023",
        "June 15, 2023",
        "2023-06-15 12:30:45",
    };

    [Theory]
    [MemberData(nameof(DateLikeTextOutsideCanonicalFormats))]
    public void DeserializeRecord_should_keep_non_canonical_date_like_text_as_DamlText(string text)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, string> { ["note"] = text });

        var record = DamlJsonSerializer.DeserializeRecord(json);

        record.GetRequiredField("note").Should().Be(new DamlText(text));
    }

    public static TheoryData<string, DateTimeOffset> CanonicalLedgerTimestampShapes() => new()
    {
        { "2023-06-15T12:30:45Z", new DateTimeOffset(2023, 6, 15, 12, 30, 45, TimeSpan.Zero) },
        { "2023-06-15T12:30:45.123Z", new DateTimeOffset(2023, 6, 15, 12, 30, 45, 123, TimeSpan.Zero) },
        { "2023-06-15T12:30:45.0000000+00:00", new DateTimeOffset(2023, 6, 15, 12, 30, 45, TimeSpan.Zero) },
    };

    [Theory]
    [MemberData(nameof(CanonicalLedgerTimestampShapes))]
    public void DeserializeRecord_should_infer_DamlTimestamp_from_canonical_iso8601_text(string text, DateTimeOffset expected)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, string> { ["at"] = text });

        var record = DamlJsonSerializer.DeserializeRecord(json);

        record.GetRequiredField("at").As<DamlTimestamp>().Value.Should().Be(expected);
    }

    public static TheoryData<string, decimal> CanonicalNumericText() => new()
    {
        { "1.5", 1.5m },
        { "42.0", 42m },
        { "-1.5", -1.5m },
        { "0.0000000001", 0.0000000001m },
    };

    [Theory]
    [MemberData(nameof(CanonicalNumericText))]
    public void DeserializeRecord_should_infer_DamlNumeric_from_canonical_numeric_text(string text, decimal expected)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, string> { ["amount"] = text });

        var record = DamlJsonSerializer.DeserializeRecord(json);

        record.GetRequiredField("amount").As<DamlNumeric>().Value.Should().Be(expected);
    }

    public static TheoryData<string> NumericLikeTextOutsideCanonicalGrammar() => new()
    {
        "42",
        "-42",
        ".5",
        "1.",
        "1.5.0",
        "1,5",
        "1e5",
        "+1.5",
    };

    [Theory]
    [MemberData(nameof(NumericLikeTextOutsideCanonicalGrammar))]
    public void DeserializeRecord_should_keep_non_canonical_numeric_like_text_as_DamlText(string text)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, string> { ["note"] = text });

        var record = DamlJsonSerializer.DeserializeRecord(json);

        record.GetRequiredField("note").Should().Be(new DamlText(text));
    }

    public static TheoryData<string> CanonicalNumericTextBeyondDecimalRange() => new()
    {
        new string('9', 38) + ".0",
        "-" + new string('9', 38) + ".0",
        "79228162514264337593543950336.0",
    };

    [Theory]
    [MemberData(nameof(CanonicalNumericTextBeyondDecimalRange))]
    public void DeserializeRecord_should_throw_JsonException_for_canonical_numeric_text_beyond_decimal_range(string text)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, string> { ["amount"] = text });

        var act = () => DamlJsonSerializer.DeserializeRecord(json);

        act.Should().Throw<JsonException>().WithMessage($"*{text}*");
    }

    [Fact]
    public void Deserialize_should_throw_JsonException_for_canonical_numeric_text_beyond_decimal_range()
    {
        var thirtyEightNines = new string('9', 38) + ".0";
        var json = JsonSerializer.Serialize(new Dictionary<string, string> { ["amount"] = thirtyEightNines });

        var act = () => DamlJsonSerializer.Deserialize(json);

        act.Should().Throw<JsonException>().WithMessage($"*{thirtyEightNines}*");
    }

    [Fact]
    public void DeserializeRecord_should_round_canonical_numeric_text_beyond_decimal_precision_per_documented_bound()
    {
        var thirtyEightSignificantDigits = "1.2345678901234567890123456789012345678";
        var json = JsonSerializer.Serialize(new Dictionary<string, string> { ["amount"] = thirtyEightSignificantDigits });

        var record = DamlJsonSerializer.DeserializeRecord(json);

        record.GetRequiredField("amount").As<DamlNumeric>().Value
            .Should().Be(decimal.Parse(thirtyEightSignificantDigits, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void DeserializeRecord_should_round_raw_json_number_beyond_decimal_precision_per_documented_bound()
    {
        var thirtyEightSignificantDigits = "1.2345678901234567890123456789012345678";
        var json = $"{{\"amount\":{thirtyEightSignificantDigits}}}";

        var record = DamlJsonSerializer.DeserializeRecord(json);

        record.GetRequiredField("amount").As<DamlNumeric>().Value
            .Should().Be(decimal.Parse(thirtyEightSignificantDigits, System.Globalization.CultureInfo.InvariantCulture),
                "a raw JSON number takes the TryGetDecimal path, which rounds excess fractional precision to the nearest representable decimal");
    }

    [Fact]
    public void RoundTrip_should_preserve_equality_for_DamlNumeric_with_non_default_scale()
    {
        var original = new DamlNumeric(123.456789m, 38);

        var json = DamlJsonSerializer.Serialize((DamlValue)original);
        var deserialized = DamlJsonSerializer.Deserialize(json);

        deserialized.Should().Be(original,
            "Scale is not part of the wire format, so a Numeric constructed with a non-default scale must still round-trip equal");
    }

    [Fact]
    public void RoundTrip_value_overload_should_preserve_DamlNumeric()
    {
        var original = new DamlNumeric(1.5m);

        var json = DamlJsonSerializer.Serialize((DamlValue)original);
        var deserialized = DamlJsonSerializer.Deserialize(json);

        deserialized.Should().BeOfType<DamlNumeric>();
        deserialized.As<DamlNumeric>().Value.Should().Be(1.5m);
    }

    [Fact]
    public void RoundTrip_should_preserve_DamlNumeric_record_field()
    {
        var original = DamlRecord.Create(DamlField.Create("amount", new DamlNumeric(123.456789m)));

        var json = DamlJsonSerializer.Serialize(original);
        var deserialized = DamlJsonSerializer.DeserializeRecord(json);

        deserialized.GetRequiredField("amount").As<DamlNumeric>().Value.Should().Be(123.456789m);
    }

    [Fact]
    public void DeserializeRecord_should_map_explicit_null_field_to_DamlOptional_None()
    {
        var record = DamlJsonSerializer.DeserializeRecord("""{"maybeValue":null}""");

        record.GetRequiredField("maybeValue").Should().Be(DamlOptional.None);
    }

    [Fact]
    public void Deserialize_should_map_explicit_null_field_to_DamlOptional_None_in_nested_record()
    {
        var deserialized = DamlJsonSerializer.Deserialize("""{"person":{"nickname":null}}""");

        var person = deserialized.As<DamlRecord>().GetRequiredField("person").As<DamlRecord>();
        person.GetRequiredField("nickname").Should().Be(DamlOptional.None);
    }

    [Fact]
    public void RoundTrip_should_reconstruct_DamlOptional_None_field()
    {
        var original = DamlRecord.Create(DamlField.Create("maybeValue", DamlOptional.None));

        var json = DamlJsonSerializer.Serialize(original);
        var deserialized = DamlJsonSerializer.DeserializeRecord(json);

        deserialized.GetRequiredField("maybeValue").AsOptional().Should().Be(DamlOptional.None);
    }

    [Fact]
    public void RoundTrip_should_recover_DamlOptional_Some_field_through_AsOptional()
    {
        var original = DamlRecord.Create(DamlField.Create("maybeValue", DamlOptional.Some(new DamlInt64(42))));

        var json = DamlJsonSerializer.Serialize(original);
        var deserialized = DamlJsonSerializer.DeserializeRecord(json);

        var optional = deserialized.GetRequiredField("maybeValue").AsOptional();
        optional.HasValue.Should().BeTrue();
        optional.Value.Should().Be(new DamlInt64(42));
    }

    [Fact]
    public void RoundTrip_should_preserve_DamlRecord_data()
    {
        // Arrange
        var original = DamlRecord.Create(
            DamlField.Create("name", new DamlText("Alice")),
            DamlField.Create("age", new DamlInt64(30)),
            DamlField.Create("active", new DamlBool(true))
        );

        // Act
        var json = DamlJsonSerializer.Serialize(original);
        var deserialized = DamlJsonSerializer.DeserializeRecord(json);

        // Assert
        deserialized.GetField("name")!.As<DamlText>().Value.Should().Be("Alice");
        deserialized.GetField("age")!.As<DamlInt64>().Value.Should().Be(30);
        deserialized.GetField("active")!.As<DamlBool>().Value.Should().BeTrue();
    }

    [Fact]
    public void Serialize_should_render_DamlGenMap_as_array_of_two_element_arrays()
    {
        var genMap = DamlGenMap.Create(
            (new DamlParty("Alice"), new DamlInt64(1)),
            (new DamlParty("Bob"), new DamlInt64(2))
        );
        var record = DamlRecord.Create(DamlField.Create("entries", genMap));

        var json = DamlJsonSerializer.Serialize(record);

        json.Should().Contain("\"entries\":[[\"Alice\",1],[\"Bob\",2]]");
    }

    [Fact]
    public void RoundTrip_should_preserve_DamlGenMap_entries()
    {
        var original = DamlGenMap.Create(
            (new DamlParty("Alice"), new DamlInt64(1)),
            (new DamlParty("Bob"), new DamlInt64(2))
        );
        var record = DamlRecord.Create(DamlField.Create("entries", original));

        var json = DamlJsonSerializer.Serialize(record);
        var deserialized = DamlJsonSerializer.DeserializeRecord(json);

        var entries = deserialized.GetField("entries")!;
        entries.Should().BeOfType<DamlGenMap>();
        entries.As<DamlGenMap>().Entries.Should().BeEquivalentTo(original.Entries);
    }

    [Fact]
    public void RoundTrip_top_level_Deserialize_should_preserve_DamlGenMap()
    {
        var original = DamlGenMap.Create(
            (new DamlParty("Alice"), new DamlInt64(1)),
            (new DamlParty("Bob"), new DamlInt64(2))
        );

        var json = DamlJsonSerializer.Serialize(original);
        var deserialized = DamlJsonSerializer.Deserialize(json);

        deserialized.Should().BeOfType<DamlGenMap>();
        deserialized.As<DamlGenMap>().Entries.Should().BeEquivalentTo(original.Entries);
    }

    [Fact]
    public void Deserialize_empty_array_should_resolve_to_DamlList_per_documented_contract()
    {
        var deserialized = DamlJsonSerializer.Deserialize("[]");

        deserialized.Should().BeOfType<DamlList>();
        deserialized.As<DamlList>().Values.Should().BeEmpty();
    }

    [Fact]
    public void Deserialize_pair_with_null_key_should_surface_array_null_error_not_GenMap_error()
    {
        var act = () => DamlJsonSerializer.Deserialize("[[null, 5]]");

        act.Should().Throw<JsonException>()
            .WithMessage("Null array elements not supported");
    }

    [Fact]
    public void Deserialize_pair_with_null_value_should_surface_array_null_error_not_GenMap_error()
    {
        var act = () => DamlJsonSerializer.Deserialize("[[5, null]]");

        act.Should().Throw<JsonException>()
            .WithMessage("Null array elements not supported");
    }

    private sealed record UnsupportedDamlValue : DamlValue;

    [Fact]
    public void Serialize_should_throw_JsonException_for_unsupported_DamlValue_subtype()
    {
        var act = () => DamlJsonSerializer.Serialize(new UnsupportedDamlValue());

        act.Should().Throw<JsonException>().WithMessage("*UnsupportedDamlValue*");
    }

    [Fact]
    public void Deserialize_should_throw_JsonException_for_number_that_does_not_fit_decimal()
    {
        var act = () => DamlJsonSerializer.Deserialize("""{"amount":1e300}""");

        act.Should().Throw<JsonException>().WithMessage("*1e300*");
    }

    [Fact]
    public void DeserializeRecord_should_throw_JsonException_when_top_level_json_is_not_an_object()
    {
        var act = () => DamlJsonSerializer.DeserializeRecord("[1,2,3]");

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void DeserializeRecord_should_throw_JsonException_naming_null_for_top_level_null_literal()
    {
        var act = () => DamlJsonSerializer.DeserializeRecord("null");

        act.Should().Throw<JsonException>().WithMessage("*null*");
    }

    private const int SupportedNestingDepth = 128;
    private const int DamlLfValueDepthLimit = 100;

    private static DamlRecord NestRecord(int levels)
    {
        DamlValue value = new DamlInt64(1);
        for (var i = 0; i < levels; i++)
        {
            value = DamlRecord.Create(DamlField.Create("inner", value));
        }
        return (DamlRecord)value;
    }

    private static string NestJson(int levels) =>
        string.Concat(Enumerable.Repeat("""{"inner":""", levels))
        + "1"
        + new string('}', levels);

    [Fact]
    public void Serialize_should_allow_nesting_at_exactly_the_supported_depth()
    {
        var act = () => DamlJsonSerializer.Serialize(NestRecord(SupportedNestingDepth));

        act.Should().NotThrow();
    }

    [Fact]
    public void Serialize_should_throw_JsonException_one_level_beyond_the_supported_depth()
    {
        var act = () => DamlJsonSerializer.Serialize(NestRecord(SupportedNestingDepth + 1));

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Deserialize_should_throw_JsonException_when_nesting_exceeds_supported_depth()
    {
        var act = () => DamlJsonSerializer.Deserialize(NestJson(SupportedNestingDepth + 1));

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void RoundTrip_should_support_values_at_the_DamlLf_depth_limit()
    {
        var json = DamlJsonSerializer.Serialize(NestRecord(DamlLfValueDepthLimit));
        var deserialized = DamlJsonSerializer.Deserialize(json);

        deserialized.Should().Be(NestRecord(DamlLfValueDepthLimit));
    }

    [Fact]
    public void DeserializeRecord_should_support_json_at_the_DamlLf_depth_limit()
    {
        var act = () => DamlJsonSerializer.DeserializeRecord(NestJson(DamlLfValueDepthLimit));

        act.Should().NotThrow();
    }

    [Fact]
    public void DeserializeRecord_should_throw_JsonException_for_duplicate_json_properties()
    {
        var act = () => DamlJsonSerializer.DeserializeRecord("""{"amount":"1.0","amount":"2.0"}""");

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Deserialize_should_throw_JsonException_for_duplicate_json_properties()
    {
        var act = () => DamlJsonSerializer.Deserialize("""{"amount":"1.0","amount":"2.0"}""");

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Deserialize_should_throw_JsonException_for_duplicate_json_properties_in_nested_object()
    {
        var act = () => DamlJsonSerializer.Deserialize("""{"outer":{"a":1,"a":2}}""");

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void DeserializeRecord_should_assume_utc_for_offsetless_canonical_timestamp()
    {
        var record = DamlJsonSerializer.DeserializeRecord("""{"at":"2023-06-15T12:30:45"}""");

        var timestamp = record.GetRequiredField("at").As<DamlTimestamp>().Value;
        timestamp.Offset.Should().Be(TimeSpan.Zero);
        timestamp.Should().Be(new DateTimeOffset(2023, 6, 15, 12, 30, 45, TimeSpan.Zero));
    }

    [Fact]
    public void Serialize_should_throw_JsonException_for_duplicate_DamlField_labels()
    {
        var record = DamlRecord.Create(
            DamlField.Create("amount", new DamlNumeric(1.0m)),
            DamlField.Create("amount", new DamlNumeric(2.0m)));

        var act = () => DamlJsonSerializer.Serialize(record);

        act.Should().Throw<JsonException>().WithMessage("*amount*");
    }

    [Fact]
    public void Serialize_value_overload_should_throw_JsonException_for_duplicate_DamlField_labels()
    {
        var record = DamlRecord.Create(
            DamlField.Create("amount", new DamlNumeric(1.0m)),
            DamlField.Create("amount", new DamlNumeric(2.0m)));

        var act = () => DamlJsonSerializer.Serialize((DamlValue)record);

        act.Should().Throw<JsonException>().WithMessage("*amount*");
    }

    [Fact]
    public void Serialize_DamlNumeric_should_be_locale_independent()
    {
        var previousCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture =
                System.Globalization.CultureInfo.GetCultureInfo("fr-FR");

            var record = DamlRecord.Create(
                DamlField.Create("amount", new DamlNumeric(123.456789m, 10))
            );

            var json = DamlJsonSerializer.Serialize(record);

            json.Should().Contain("\"amount\":\"123.456789\"");
            json.Should().NotContain("\"amount\":\"123,456789\"");
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void Serialize_DamlDate_should_be_locale_independent()
    {
        var previousCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture =
                System.Globalization.CultureInfo.GetCultureInfo("ar-SA");

            var record = DamlRecord.Create(
                DamlField.Create("createdDate", new DamlDate(new DateOnly(2024, 3, 15)))
            );

            var json = DamlJsonSerializer.Serialize(record);

            json.Should().Contain("\"createdDate\":\"2024-03-15\"");
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void Serialize_DamlTimestamp_should_be_locale_independent()
    {
        var previousCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture =
                System.Globalization.CultureInfo.GetCultureInfo("ar-SA");

            var timestamp = new DateTimeOffset(2024, 3, 15, 9, 8, 7, TimeSpan.Zero);
            var record = DamlRecord.Create(
                DamlField.Create("updatedAt", new DamlTimestamp(timestamp))
            );

            var json = DamlJsonSerializer.Serialize(record);

            json.Should().Contain("\"updatedAt\":\"2024-03-15T09:08:07.0000000");
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void RoundTrip_value_overload_should_preserve_DamlDate()
    {
        var original = new DamlDate(new DateOnly(2026, 5, 26));

        var json = DamlJsonSerializer.Serialize((DamlValue)original);
        var deserialized = DamlJsonSerializer.Deserialize(json);

        deserialized.Should().BeOfType<DamlDate>();
        deserialized.As<DamlDate>().Value.Should().Be(original.Value);
    }

    [Fact]
    public void RoundTrip_value_overload_should_preserve_DamlTimestamp()
    {
        var original = new DamlTimestamp(new DateTimeOffset(2026, 5, 26, 14, 30, 45, TimeSpan.Zero));

        var json = DamlJsonSerializer.Serialize((DamlValue)original);
        var deserialized = DamlJsonSerializer.Deserialize(json);

        deserialized.Should().BeOfType<DamlTimestamp>();
        deserialized.As<DamlTimestamp>().Value.Should().Be(original.Value);
    }

    [Fact]
    public void RoundTrip_value_overload_should_preserve_DamlDate_under_non_invariant_culture()
    {
        var previousCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture =
                System.Globalization.CultureInfo.GetCultureInfo("th-TH");

            var original = new DamlDate(new DateOnly(2026, 5, 26));

            var json = DamlJsonSerializer.Serialize((DamlValue)original);
            var deserialized = DamlJsonSerializer.Deserialize(json);

            deserialized.Should().BeOfType<DamlDate>();
            deserialized.As<DamlDate>().Value.Should().Be(original.Value);
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void RoundTrip_value_overload_should_preserve_DamlTimestamp_under_non_invariant_culture()
    {
        var previousCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture =
                System.Globalization.CultureInfo.GetCultureInfo("th-TH");

            var original = new DamlTimestamp(new DateTimeOffset(2026, 5, 26, 14, 30, 45, TimeSpan.Zero));

            var json = DamlJsonSerializer.Serialize((DamlValue)original);
            var deserialized = DamlJsonSerializer.Deserialize(json);

            deserialized.Should().BeOfType<DamlTimestamp>();
            deserialized.As<DamlTimestamp>().Value.Should().Be(original.Value);
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = previousCulture;
        }
    }
}
