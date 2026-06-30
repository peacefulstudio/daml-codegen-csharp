// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class DamlJsonSerializerTextInferenceTests
{
    [Fact]
    public void DeserializeRecord_should_handle_DamlDate()
    {
        var json = """{"date":"2023-12-25"}""";

        var record = DamlJsonSerializer.DeserializeRecord(json);

        var date = record.GetField("date")!.As<DamlDate>();
        date.Value.Should().Be(new DateOnly(2023, 12, 25));
    }

    [Fact]
    public void DeserializeRecord_should_handle_DamlTimestamp()
    {
        var json = """{"timestamp":"2023-06-15T12:30:45+00:00"}""";

        var record = DamlJsonSerializer.DeserializeRecord(json);

        var ts = record.GetField("timestamp")!.As<DamlTimestamp>();
        ts.Value.Should().Be(new DateTimeOffset(2023, 6, 15, 12, 30, 45, TimeSpan.Zero));
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
    public void DeserializeRecord_should_assume_utc_for_offsetless_canonical_timestamp()
    {
        var record = DamlJsonSerializer.DeserializeRecord("""{"at":"2023-06-15T12:30:45"}""");

        var timestamp = record.GetRequiredField("at").As<DamlTimestamp>().Value;
        timestamp.Offset.Should().Be(TimeSpan.Zero);
        timestamp.Should().Be(new DateTimeOffset(2023, 6, 15, 12, 30, 45, TimeSpan.Zero));
    }
}
