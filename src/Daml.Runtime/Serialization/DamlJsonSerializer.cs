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

    private static JsonNode? ValueToJsonNode(DamlValue value) => value switch
    {
        DamlInt64 i => JsonValue.Create(i.Value),
        DamlNumeric n => JsonValue.Create(n.Value.ToString("G")), // String for precision
        DamlText t => JsonValue.Create(t.Value),
        DamlBool b => JsonValue.Create(b.Value),
        DamlUnit => JsonValue.Create(new JsonObject()),
        DamlDate d => JsonValue.Create(d.Value.ToString("yyyy-MM-dd")),
        DamlTimestamp ts => JsonValue.Create(ts.Value.ToString("O")),
        DamlParty p => JsonValue.Create(p.Value),
        DamlContractId c => JsonValue.Create(c.Value),
        DamlOptional opt => opt.Value is null ? null : ValueToJsonNode(opt.Value),
        DamlList list => new JsonArray(list.Values.Select(ValueToJsonNode).ToArray()),
        DamlTextMap map => MapToJsonObject(map),
        DamlVariant var => VariantToJsonObject(var),
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

    private static DamlValue JsonNodeToValue(JsonNode node) => node switch
    {
        JsonValue val => JsonValueToDamlValue(val),
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
        // Try to parse as date
        if (DateOnly.TryParse(s, out var date))
        {
            return new DamlDate(date);
        }

        // Try to parse as timestamp
        if (DateTimeOffset.TryParse(s, out var ts))
        {
            return new DamlTimestamp(ts);
        }

        // Default to text
        return new DamlText(s);
    }
}

/// <summary>
/// JSON converter for DamlValue types.
/// </summary>
public sealed class DamlValueJsonConverter : JsonConverter<DamlValue>
{
    public override DamlValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var node = JsonNode.Parse(doc.RootElement.GetRawText());
        return node is null
            ? throw new JsonException("Null JSON not supported")
            : DeserializeNode(node);
    }

    public override void Write(Utf8JsonWriter writer, DamlValue value, JsonSerializerOptions options)
    {
        var json = DamlJsonSerializer.Serialize(value, options);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.WriteTo(writer);
    }

    private static DamlValue DeserializeNode(JsonNode node) => node switch
    {
        JsonValue val => DeserializeJsonValue(val),
        JsonArray arr => new DamlList(arr
            .Where(n => n is not null)
            .Select(n => DeserializeNode(n!))
            .ToList()),
        JsonObject obj => DeserializeJsonObject(obj),
        _ => throw new JsonException($"Unexpected JSON node type: {node.GetType().Name}")
    };

    private static DamlValue DeserializeJsonValue(JsonValue val)
    {
        var element = val.GetValue<JsonElement>();
        return element.ValueKind switch
        {
            JsonValueKind.String => new DamlText(element.GetString()!),
            JsonValueKind.Number when element.TryGetInt64(out var i) => new DamlInt64(i),
            JsonValueKind.Number => new DamlNumeric(element.GetDecimal()),
            JsonValueKind.True => new DamlBool(true),
            JsonValueKind.False => new DamlBool(false),
            _ => throw new JsonException($"Unsupported JSON value kind: {element.ValueKind}")
        };
    }

    private static DamlValue DeserializeJsonObject(JsonObject obj)
    {
        // Check if it's a variant
        if (obj.ContainsKey("tag") && obj.ContainsKey("value"))
        {
            var tag = obj["tag"]!.GetValue<string>();
            var value = obj["value"] is null ? DamlUnit.Instance : DeserializeNode(obj["value"]!);
            return new DamlVariant(null, tag, value);
        }

        // Otherwise it's a record
        var fields = obj
            .Where(p => p.Value is not null)
            .Select(p => new DamlField(p.Key, DeserializeNode(p.Value!)))
            .ToList();

        return new DamlRecord(null, fields);
    }
}
