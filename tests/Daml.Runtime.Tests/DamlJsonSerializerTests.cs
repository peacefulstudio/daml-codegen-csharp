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
