using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Daml.Runtime.Serialization;

/// <summary>
/// Serializes and deserializes Daml values to/from JSON format compatible with the Ledger API.
/// </summary>
public static class DamlJsonSerializer
{
    private static readonly JsonSerializerOptions DefaultOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new DamlValueJsonConverter() }
    };

    /// <summary>
    /// Serializes a DamlValue to JSON.
    /// </summary>
    public static string Serialize(DamlValue value, JsonSerializerOptions? options = null) =>
        JsonSerializer.Serialize(value, options ?? DefaultOptions);

    /// <summary>
    /// Serializes a DamlRecord to JSON.
    /// </summary>
    public static string Serialize(DamlRecord record, JsonSerializerOptions? options = null) =>
        JsonSerializer.Serialize(RecordToJsonObject(record), options ?? DefaultOptions);

    /// <summary>
    /// Deserializes JSON to a DamlValue.
    /// </summary>
    /// <remarks>
    /// Untyped deserialization disambiguates <c>GenMap</c> from <c>List (List a)</c>
    /// Untyped deserialization disambiguates <c>GenMap</c> from <c>List (List a)</c>
    /// heuristically: a top-level JSON array whose every element is a 2-element
    /// JSON array with non-null first and second elements is reconstructed as a
    /// <see cref="DamlGenMap"/>.
    /// untyped JSON, and callers needing exact round-trip behavior for these
    /// shapes must deserialize against a type schema rather than through this
    /// entry point:
    /// <list type="bullet">
    /// <item><description>A <c>List (List a)</c> where every inner list has length 2
    /// (e.g. matrices, coordinate pairs) is reinterpreted as a <see cref="DamlGenMap"/>.</description></item>
    /// <item><description>An empty JSON array <c>[]</c> is always reconstructed as an empty
    /// <see cref="DamlList"/>, never as an empty <see cref="DamlGenMap"/> — empty <c>[]</c>
    /// is genuinely indistinguishable from an empty list without a type schema, and the
    /// far more common case is the empty list.</description></item>
    /// <item><description>A pair whose first or second element is JSON <c>null</c> is treated as
    /// a list element, not a GenMap entry, so <c>List [[None, Some 5]]</c> and
    /// <c>List [[Some 5, None]]</c> shapes both surface the original
    /// "Null array elements not supported" error rather than a misleading
    /// "GenMap key/value must not be null".</description></item>
    /// </list>
    /// </remarks>
    public static DamlValue Deserialize(string json, JsonSerializerOptions? options = null) =>
        JsonSerializer.Deserialize<DamlValue>(json, options ?? DefaultOptions)
            ?? throw new JsonException("Failed to deserialize JSON to DamlValue");

    /// <summary>
    /// Deserializes JSON to a DamlRecord.
    /// </summary>
    public static DamlRecord DeserializeRecord(string json, JsonSerializerOptions? options = null)
    {
        var node = JsonNode.Parse(json) ?? throw new JsonException("Invalid JSON");
        return JsonObjectToRecord(node.AsObject());
    }

    private static JsonObject RecordToJsonObject(DamlRecord record)
    {
        var obj = new JsonObject();
        foreach (var field in record.Fields)
        {
            obj[field.Label] = ValueToJsonNode(field.Value);
        }
        return obj;
    }

    internal static JsonNode? ValueToJsonNode(DamlValue value) => value switch
    {
        DamlInt64 i => JsonValue.Create(i.Value),
        DamlNumeric n => JsonValue.Create(n.Value.ToString("G", CultureInfo.InvariantCulture)),
        DamlText t => JsonValue.Create(t.Value),
        DamlBool b => JsonValue.Create(b.Value),
        DamlUnit => new JsonObject(),
        DamlDate d => JsonValue.Create(d.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
        DamlTimestamp ts => JsonValue.Create(ts.Value.ToString("O", CultureInfo.InvariantCulture)),
        DamlParty p => JsonValue.Create(p.Value),
        DamlContractId c => JsonValue.Create(c.Value),
        DamlOptional opt => opt.Value is null ? null : ValueToJsonNode(opt.Value),
        DamlList list => new JsonArray(list.Values.Select(ValueToJsonNode).ToArray()),
        DamlTextMap map => MapToJsonObject(map),
        DamlGenMap map => GenMapToJsonArray(map),
        DamlVariant variant => VariantToJsonObject(variant),
        DamlEnum e => JsonValue.Create(e.Constructor),
        DamlRecord rec => RecordToJsonObject(rec),
        _ => throw new NotSupportedException($"Cannot serialize {value.GetType().Name} to JSON")
    };

    private static JsonObject MapToJsonObject(DamlTextMap map)
    {
        var obj = new JsonObject();
        foreach (var (key, val) in map.Values)
        {
            obj[key] = ValueToJsonNode(val);
        }
        return obj;
    }

    private static JsonArray GenMapToJsonArray(DamlGenMap map)
    {
        var entries = new JsonArray();
        foreach (var (key, val) in map.Entries)
        {
            entries.Add(new JsonArray(ValueToJsonNode(key), ValueToJsonNode(val)));
        }
        return entries;
    }

    private static JsonObject VariantToJsonObject(DamlVariant variant)
    {
        var obj = new JsonObject
        {
            ["tag"] = variant.Constructor,
            ["value"] = ValueToJsonNode(variant.Value)
        };
        return obj;
    }

    private static DamlRecord JsonObjectToRecord(JsonObject obj)
    {
        var fields = new List<DamlField>();
        foreach (var prop in obj)
        {
            if (prop.Value is not null)
            {
                fields.Add(new DamlField(prop.Key, JsonNodeToValue(prop.Value)));
            }
        }
        return new DamlRecord(null, fields);
    }

    internal static DamlValue JsonNodeToValue(JsonNode node) => node switch
    {
        JsonValue val => JsonValueToDamlValue(val),
        JsonArray arr when LooksLikeGenMapEntries(arr) => GenMapFromJsonArray(arr),
        JsonArray arr => new DamlList(arr.Select(n => n is null
            ? throw new JsonException("Null array elements not supported")
            : JsonNodeToValue(n)).ToList()),
        JsonObject obj when obj.ContainsKey("tag") && obj.ContainsKey("value") =>
            new DamlVariant(null, obj["tag"]!.GetValue<string>(),
                obj["value"] is null ? DamlUnit.Instance : JsonNodeToValue(obj["value"]!)),
        JsonObject obj => new DamlRecord(null,
            obj.Where(p => p.Value is not null)
               .Select(p => new DamlField(p.Key, JsonNodeToValue(p.Value!)))
               .ToList()),
        _ => throw new JsonException($"Cannot deserialize {node.GetType().Name}")
    };

    private static bool LooksLikeGenMapEntries(JsonArray arr)
    {
        if (arr.Count == 0)
        {
            return false;
        }
        foreach (var element in arr)
        {
            if (element is not JsonArray pair || pair.Count != 2 || pair[0] is null || pair[1] is null)
            {
                return false;
            }
        }
        return true;
    }

    private static DamlGenMap GenMapFromJsonArray(JsonArray arr)
    {
        var entries = new List<(DamlValue Key, DamlValue Value)>(arr.Count);
        foreach (var element in arr)
        {
            var pair = (JsonArray)element!;
            entries.Add((JsonNodeToValue(pair[0]!), JsonNodeToValue(pair[1]!)));
        }
        return new DamlGenMap(entries);
    }

    private static DamlValue JsonValueToDamlValue(JsonValue val)
    {
        var element = val.GetValue<JsonElement>();
        return element.ValueKind switch
        {
            JsonValueKind.String => InferStringValue(element.GetString()!),
            JsonValueKind.Number when element.TryGetInt64(out var i) => new DamlInt64(i),
            JsonValueKind.Number => new DamlNumeric(element.GetDecimal()),
            JsonValueKind.True => new DamlBool(true),
            JsonValueKind.False => new DamlBool(false),
            JsonValueKind.Null => throw new JsonException("Null values should be handled as Optional.None"),
            _ => throw new JsonException($"Cannot deserialize JSON value kind {element.ValueKind}")
        };
    }

    private static DamlValue InferStringValue(string s)
    {
        if (DateOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return new DamlDate(date);
        }

        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ts))
        {
            return new DamlTimestamp(ts);
        }

        return new DamlText(s);
    }
}

/// <summary>
/// JSON converter for DamlValue types. Delegates to the canonical
/// <see cref="DamlJsonSerializer.ValueToJsonNode"/> /
/// <see cref="DamlJsonSerializer.JsonNodeToValue"/> mappers so the
/// top-level <see cref="DamlJsonSerializer.Serialize(DamlValue, JsonSerializerOptions?)"/>
/// and <see cref="DamlJsonSerializer.Deserialize(string, JsonSerializerOptions?)"/>
/// paths share semantics with the record-scoped overloads.
/// </summary>
public sealed class DamlValueJsonConverter : JsonConverter<DamlValue>
{
    public override DamlValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var node = JsonNode.Parse(doc.RootElement.GetRawText());
        return node is null
            ? throw new JsonException("Null JSON not supported")
            : DamlJsonSerializer.JsonNodeToValue(node);
    }

    public override void Write(Utf8JsonWriter writer, DamlValue value, JsonSerializerOptions options)
    {
        var node = DamlJsonSerializer.ValueToJsonNode(value);
        if (node is null)
        {
            writer.WriteNullValue();
            return;
        }
        node.WriteTo(writer);
    }
}
