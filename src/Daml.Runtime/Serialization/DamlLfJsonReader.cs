// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Stdlib;

namespace Daml.Runtime.Serialization;

/// <summary>
/// Decodes LF-JSON against the CLR shape of a generated Daml record, producing a
/// <see cref="DamlRecord"/> whose field values carry their true Daml types — a Daml
/// <c>Party</c> field arrives as <see cref="DamlParty"/> rather than the
/// <see cref="DamlText"/> an untyped decode would yield. Callers hand the result to the
/// generated <c>FromRecord</c>.
/// </summary>
/// <remarks>
/// The returned record's <see cref="DamlRecord.RecordId"/> is always <see langword="null"/>:
/// LF-JSON carries no type identifier, and generated <c>FromRecord</c> reads fields by label.
/// </remarks>
public static class DamlLfJsonReader
{
    /// <summary>
    /// Decodes an already-parsed LF-JSON object against the shape of <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The generated Daml record type describing the expected shape.</typeparam>
    /// <param name="json">The LF-JSON object to decode.</param>
    /// <param name="limits">
    /// Decode limits; the shared hardened defaults apply when omitted.
    /// <see cref="DamlJsonDeserializationLimits.MaxInputCharacters"/> is accepted and ignored here:
    /// the caller already parsed the document, so the parse-time size, duplicate-property and
    /// parse-depth caps were theirs to apply. What this overload bounds is the reader's own
    /// allocation amplification and recursion depth while walking an already-materialized
    /// document — decode bounds, not a boundary defence against hostile input.
    /// </param>
    /// <returns>A record whose field values carry their Daml types.</returns>
    /// <exception cref="JsonException">The JSON does not match the expected shape, or a decode limit is exceeded.</exception>
    /// <exception cref="NotSupportedException">A CLR property type lies outside the Daml type mapping, or a generated enum companion cannot name the wire constructor of one of its members.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limits"/> is not a valid limit configuration.</exception>
    /// <remarks>JSON properties the target type does not declare are ignored by design, for tolerance of payloads produced by newer Daml package versions.</remarks>
    public static DamlRecord ReadRecord<T>(JsonElement json, DamlJsonDeserializationLimits? limits = null)
        where T : IDamlRecord =>
        ReadRecord(json, typeof(T), limits);

    /// <summary>
    /// Parses LF-JSON under the shared hardened document options and decodes it against the
    /// shape of <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The generated Daml record type describing the expected shape.</typeparam>
    /// <param name="json">The LF-JSON text to parse and decode.</param>
    /// <param name="limits">Decode limits; the shared hardened defaults apply when omitted.</param>
    /// <returns>A record whose field values carry their Daml types.</returns>
    /// <exception cref="JsonException">The JSON is malformed, does not match the expected shape, or exceeds a limit.</exception>
    /// <exception cref="NotSupportedException">A CLR property type lies outside the Daml type mapping, or a generated enum companion cannot name the wire constructor of one of its members.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limits"/> is not a valid limit configuration.</exception>
    /// <remarks>JSON properties the target type does not declare are ignored by design, for tolerance of payloads produced by newer Daml package versions.</remarks>
    public static DamlRecord ReadRecord<T>(string json, DamlJsonDeserializationLimits? limits = null)
        where T : IDamlRecord =>
        ReadRecord(json, typeof(T), limits);

    /// <summary>
    /// Decodes an already-parsed LF-JSON object against the shape of <paramref name="recordType"/>.
    /// </summary>
    /// <param name="json">The LF-JSON object to decode.</param>
    /// <param name="recordType">The generated Daml record type describing the expected shape.</param>
    /// <param name="limits">
    /// Decode limits; the shared hardened defaults apply when omitted.
    /// <see cref="DamlJsonDeserializationLimits.MaxInputCharacters"/> is accepted and ignored here:
    /// the caller already parsed the document, so the parse-time size, duplicate-property and
    /// parse-depth caps were theirs to apply. What this overload bounds is the reader's own
    /// allocation amplification and recursion depth while walking an already-materialized
    /// document — decode bounds, not a boundary defence against hostile input.
    /// </param>
    /// <returns>A record whose field values carry their Daml types.</returns>
    /// <exception cref="JsonException">The JSON does not match the expected shape, or a decode limit is exceeded.</exception>
    /// <exception cref="NotSupportedException"><paramref name="recordType"/> is not a generated Daml record, a CLR property type lies outside the Daml type mapping, or a generated enum companion cannot name the wire constructor of one of its members.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="recordType"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limits"/> is not a valid limit configuration.</exception>
    /// <remarks>JSON properties the target type does not declare are ignored by design, for tolerance of payloads produced by newer Daml package versions.</remarks>
    public static DamlRecord ReadRecord(JsonElement json, Type recordType, DamlJsonDeserializationLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(recordType);
        var effectiveLimits = limits ?? DamlJsonSerializer.DefaultDeserializationLimits;
        DamlJsonSerializer.ValidateLimits(effectiveLimits);
        return ReadRecordValue(json, recordType, effectiveLimits, depth: 0, recordType.Name);
    }

    /// <summary>
    /// Parses LF-JSON under the shared hardened document options and decodes it against the
    /// shape of <paramref name="recordType"/>.
    /// </summary>
    /// <param name="json">The LF-JSON text to parse and decode.</param>
    /// <param name="recordType">The generated Daml record type describing the expected shape.</param>
    /// <param name="limits">Decode limits; the shared hardened defaults apply when omitted.</param>
    /// <returns>A record whose field values carry their Daml types.</returns>
    /// <exception cref="JsonException">The JSON is malformed, does not match the expected shape, or exceeds a limit.</exception>
    /// <exception cref="NotSupportedException"><paramref name="recordType"/> is not a generated Daml record, a CLR property type lies outside the Daml type mapping, or a generated enum companion cannot name the wire constructor of one of its members.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> or <paramref name="recordType"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limits"/> is not a valid limit configuration.</exception>
    /// <remarks>JSON properties the target type does not declare are ignored by design, for tolerance of payloads produced by newer Daml package versions.</remarks>
    public static DamlRecord ReadRecord(string json, Type recordType, DamlJsonDeserializationLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(recordType);
        var effectiveLimits = limits ?? DamlJsonSerializer.DefaultDeserializationLimits;
        DamlJsonSerializer.EnsureWithinInputLimit(json, effectiveLimits);
        using var document = JsonDocument.Parse(json, DamlJsonSerializer.DocumentOptions);
        return ReadRecordValue(document.RootElement, recordType, effectiveLimits, depth: 0, recordType.Name);
    }

    private static DamlRecord ReadRecordValue(JsonElement json, Type recordType, DamlJsonDeserializationLimits limits, int depth, string path)
    {
        if (recordType.IsInterface && typeof(IDamlInterface).IsAssignableFrom(recordType))
        {
            throw InterfaceMarkerTarget(recordType, path);
        }
        if (recordType.IsInterface || recordType.IsAbstract || !typeof(IDamlRecord).IsAssignableFrom(recordType))
        {
            throw NotAGeneratedRecord(recordType, path);
        }
        if (json.ValueKind != JsonValueKind.Object)
        {
            throw ShapeMismatch(path, JsonValueKind.Object, json.ValueKind);
        }

        var fields = new List<DamlField>();
        foreach (var (label, slot) in DamlFieldsOf(recordType))
        {
            var fieldPath = $"{path}.{label}";
            if (!json.TryGetProperty(label, out var element))
            {
                throw MissingRecordField(fieldPath);
            }
            fields.Add(new DamlField(label, ReadValue(element, slot, limits, depth + 1, fieldPath)));
        }
        return new DamlRecord(null, fields);
    }

    private static readonly ConcurrentDictionary<Type, (string Label, ValueSlot Slot)[]> RecordFields = new();

    private static (string Label, ValueSlot Slot)[] DamlFieldsOf(Type recordType) =>
        RecordFields.GetOrAdd(recordType, ResolveDamlFields);

    private static (string Label, ValueSlot Slot)[] ResolveDamlFields(Type recordType)
    {
        var nullability = new NullabilityInfoContext();
        var fields = new List<(string Label, ValueSlot Slot)>();
        foreach (var property in recordType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .OrderBy(DeclarationOrderWithinTheDeclaringModule))
        {
            if (property.GetCustomAttribute<DamlFieldAttribute>() is { } damlField)
            {
                fields.Add((damlField.Name, new ValueSlot(nullability.Create(property))));
            }
        }
        return fields.ToArray();
    }

    private static int DeclarationOrderWithinTheDeclaringModule(PropertyInfo property) => property.MetadataToken;

    private readonly record struct ValueSlot(NullabilityInfo Nullability)
    {
        public Type ClrType => Nullable.GetUnderlyingType(Nullability.Type) ?? Nullability.Type;

        public bool IsOptional => Nullability.ReadState == NullabilityState.Nullable;

        public ValueSlot TypeArgument(int index) => new(Nullability.GenericTypeArguments[index]);
    }

    private static DamlValue ReadValue(JsonElement json, ValueSlot slot, DamlJsonDeserializationLimits limits, int depth, string path)
    {
        if (depth > DamlJsonSerializer.MaximumNestingDepth)
        {
            throw DamlJsonSerializer.DepthBoundExceeded();
        }
        if (!slot.IsOptional)
        {
            return ReadPresentValue(json, slot, limits, depth, path);
        }
        return json.ValueKind == JsonValueKind.Null
            ? DamlOptional.None
            : DamlOptional.Some(ReadPresentValue(json, slot, limits, depth, path));
    }

    private static DamlValue ReadPresentValue(JsonElement json, ValueSlot slot, DamlJsonDeserializationLimits limits, int depth, string path)
    {
        var clrType = slot.ClrType;
        if (ScalarArms.TryGetValue(clrType, out var readScalar))
        {
            return readScalar(json, path);
        }
        if (clrType.IsEnum)
        {
            return ReadEnum(json, clrType, path);
        }
        if (IsContractId(clrType))
        {
            return new DamlContractId(ReadWireString(json, path));
        }
        if (IsList(clrType))
        {
            return ReadList(json, slot.TypeArgument(0), limits, depth, path);
        }
        if (IsDictionary(clrType))
        {
            return ReadMap(json, slot, limits, depth, path);
        }
        if (IsStdlibTuple(clrType))
        {
            return ReadStdlibTuple(json, slot, limits, depth, path);
        }
        if (IsEither(clrType))
        {
            return ReadEither(json, slot, limits, depth, path);
        }
        if (IsWrappedOptional(clrType))
        {
            return IsOptionalChainRoot(clrType)
                ? ReadOptionalChain(json, slot, limits, depth, path)
                : ReadWrappedOptional(json, slot, limits, depth, path);
        }
        if (IsStdlibSet(clrType))
        {
            return ReadStdlibSet(json, slot, limits, depth, path);
        }
        if (IsStdlibNonEmpty(clrType))
        {
            return ReadStdlibNonEmpty(json, slot, limits, depth, path);
        }
        if (IsStdlibMap(clrType))
        {
            return ReadStdlibMap(json, slot, limits, depth, path);
        }
        if (typeof(IDamlVariant).IsAssignableFrom(clrType))
        {
            return ReadVariant(json, clrType, limits, depth, path);
        }
        if (typeof(IDamlRecord).IsAssignableFrom(clrType))
        {
            return ReadRecordValue(json, clrType, limits, depth, path);
        }
        throw UnmappedClrType(clrType, path);
    }

    private static readonly FrozenDictionary<Type, Func<JsonElement, string, DamlValue>> ScalarArms =
        new Dictionary<Type, Func<JsonElement, string, DamlValue>>
        {
            [typeof(Party)] = (json, path) => new DamlParty(ReadWireString(json, path)),
            [typeof(string)] = (json, path) => new DamlText(ReadWireString(json, path)),
            [typeof(bool)] = ReadBool,
            [typeof(long)] = ReadInt64,
            [typeof(decimal)] = ReadNumeric,
            [typeof(DateOnly)] = ReadDate,
            [typeof(DateTimeOffset)] = ReadTimestamp,
            [typeof(DamlUnit)] = ReadUnit
        }.ToFrozenDictionary();

    private static string ReadWireString(JsonElement json, string path) =>
        json.ValueKind == JsonValueKind.String
            ? json.GetString()!
            : throw ShapeMismatch(path, JsonValueKind.String, json.ValueKind);

    private static DamlValue ReadBool(JsonElement json, string path) =>
        json.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? new DamlBool(json.GetBoolean())
            : throw ShapeMismatch(path, "boolean", json.ValueKind);

    private static DamlValue ReadInt64(JsonElement json, string path)
    {
        var raw = ReadWireString(json, path);
        return DamlJsonSerializer.MatchesCanonicalIntegerGrammar(raw)
            && long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            ? new DamlInt64(value)
            : throw MalformedScalar(path, raw, "Int64");
    }

    private static DamlValue ReadNumeric(JsonElement json, string path)
    {
        var raw = ReadWireString(json, path);
        return DamlNumeric.TryParseCanonical(raw, out var numeric)
            ? numeric
            : throw MalformedScalar(path, raw, "Numeric");
    }

    private static DamlValue ReadDate(JsonElement json, string path)
    {
        var raw = ReadWireString(json, path);
        return DateOnly.TryParseExact(raw, DamlJsonSerializer.CanonicalDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? new DamlDate(date)
            : throw MalformedScalar(path, raw, "Date");
    }

    private static DamlValue ReadTimestamp(JsonElement json, string path)
    {
        var raw = ReadWireString(json, path);
        return DateTimeOffset.TryParseExact(raw, DamlJsonSerializer.CanonicalTimestampParseFormat, CultureInfo.InvariantCulture, DamlJsonSerializer.UtcNormalizingTimestampParseStyles, out var timestamp)
            ? new DamlTimestamp(timestamp)
            : throw MalformedScalar(path, raw, "Timestamp");
    }

    private static DamlValue ReadUnit(JsonElement json, string path) =>
        json.ValueKind == JsonValueKind.Object
            ? DamlUnit.Instance
            : throw ShapeMismatch(path, JsonValueKind.Object, json.ValueKind);

    private static DamlValue ReadEnum(JsonElement json, Type enumType, string path)
    {
        var constructor = ReadWireString(json, path);
        var (known, sorted) = WireConstructorsOf(enumType, path);
        return known.Contains(constructor)
            ? DamlEnum.Create(constructor)
            : throw UnknownConstructor("enum constructor", constructor, path, sorted);
    }

    private const string ToDamlEnumMethod = "ToDamlEnum";
    private const string GeneratedCompanionSuffix = "Extensions";

    private readonly record struct EnumConstructors(FrozenSet<string> Known, IReadOnlyList<string> Sorted);

    private static readonly ConcurrentDictionary<Type, EnumConstructors> EnumWireConstructors = new();

    private static EnumConstructors WireConstructorsOf(Type enumType, string path) =>
        EnumWireConstructors.GetOrAdd(enumType, ResolveWireConstructors, path);

    private static EnumConstructors ResolveWireConstructors(Type enumType, string path)
    {
        var names = WireConstructorNames(enumType, path);
        return new EnumConstructors(names.ToFrozenSet(), names.Order(StringComparer.Ordinal).ToList());
    }

    private static IReadOnlyList<string> WireConstructorNames(Type enumType, string path)
    {
        var toDamlEnum = enumType.FullName is { } fullName
            ? enumType.Assembly.GetType(fullName + GeneratedCompanionSuffix)
                ?.GetMethod(ToDamlEnumMethod, BindingFlags.Public | BindingFlags.Static, null, [enumType], null)
            : null;
        if (toDamlEnum is null)
        {
            return Enum.GetNames(enumType);
        }

        var constructors = new List<string>();
        foreach (var member in Enum.GetValues(enumType))
        {
            try
            {
                constructors.Add((toDamlEnum.Invoke(null, [member]) as DamlEnum)?.Constructor ?? member.ToString()!);
            }
            catch (Exception exception)
                when (exception is TargetInvocationException or MemberAccessException or ArgumentException)
            {
                throw UnmappableEnumMember(enumType, member, path, exception);
            }
        }
        return constructors;
    }

    private static NotSupportedException UnmappableEnumMember(Type enumType, object member, string path, Exception cause) =>
        new($"Enum '{enumType}' at '{path}' has a companion whose {ToDamlEnumMethod} fails for member '{member}', "
            + "so its wire constructors cannot be determined; pass a generated Daml enum.", cause);

    private const string VariantTagKey = "tag";
    private const string VariantValueKey = "value";
    private const string VariantConstructorKind = "variant constructor";

    private static DamlVariant ReadVariant(JsonElement json, Type variantType, DamlJsonDeserializationLimits limits, int depth, string path)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            throw ShapeMismatch(path, JsonValueKind.Object, json.ValueKind);
        }

        var tag = ReadVariantTag(json, path);
        var arms = ArmsOf(variantType, path);
        if (!arms.TryGetValue(tag, out var arm))
        {
            throw UnknownConstructor(VariantConstructorKind, tag, path, arms.Keys.Order(StringComparer.Ordinal).ToList());
        }
        if (!json.TryGetProperty(VariantValueKey, out var valueElement))
        {
            throw MissingVariantMember(path, VariantValueKey);
        }

        var payloadPath = $"{path}.{VariantValueKey}";
        return DamlVariant.Create(tag, PayloadSlotOf(arm, payloadPath) is { } payloadSlot
            ? ReadValue(valueElement, payloadSlot, limits, depth + 1, payloadPath)
            : ReadUnit(valueElement, payloadPath));
    }

    private static readonly ConcurrentDictionary<Type, ValueSlot?> VariantArmPayloads = new();

    private static ValueSlot? PayloadSlotOf(Type arm, string payloadPath) =>
        VariantArmPayloads.GetOrAdd(arm, ResolvePayloadSlot, payloadPath);

    private static ValueSlot? ResolvePayloadSlot(Type arm, string payloadPath)
    {
        var constructors = arm.GetConstructors();
        if (constructors.Length != 1)
        {
            throw new NotSupportedException(
                $"Variant arm '{arm}' at '{payloadPath}' must expose exactly one public constructor; pass a generated variant.");
        }
        return constructors[0].GetParameters() switch
        {
            [] => null,
            [var payload] => new ValueSlot(new NullabilityInfoContext().Create(payload)),
            var parameters => throw new NotSupportedException(
                $"Variant arm '{arm}' at '{payloadPath}' constructor takes {parameters.Length} parameters; generated arms take 0 (nullary) or 1 (payload); pass a generated variant.")
        };
    }

    private static string ReadVariantTag(JsonElement json, string path)
    {
        if (!json.TryGetProperty(VariantTagKey, out var tagElement))
        {
            throw MissingVariantMember(path, VariantTagKey);
        }
        return tagElement.ValueKind == JsonValueKind.String
            ? tagElement.GetString()!
            : throw ShapeMismatch($"{path}.{VariantTagKey}", JsonValueKind.String, tagElement.ValueKind);
    }

    private static readonly ConcurrentDictionary<Type, FrozenDictionary<string, Type>> VariantArms = new();

    private static FrozenDictionary<string, Type> ArmsOf(Type variantType, string path) =>
        VariantArms.GetOrAdd(variantType, ResolveArms, path);

    private static FrozenDictionary<string, Type> ResolveArms(Type variantType, string path)
    {
        var arms = variantType.GetNestedTypes(BindingFlags.Public)
            .Where(variantType.IsAssignableFrom)
            .ToFrozenDictionary(arm => WireTagOf(arm, path));
        return arms.Count > 0 ? arms : throw ArmlessVariant(variantType, path);
    }

    private const string VariantTagProperty = "Tag";

    private static string WireTagOf(Type arm, string path)
    {
        var tagProperty = arm.GetProperty(VariantTagProperty, BindingFlags.Public | BindingFlags.Instance);
        return tagProperty?.PropertyType == typeof(string)
            ? ReadTagLiteral(tagProperty, arm) ?? throw UntaggedVariantArm(arm, path)
            : throw UntaggedVariantArm(arm, path);
    }

    private static string? ReadTagLiteral(PropertyInfo tagProperty, Type arm)
    {
        try
        {
            return tagProperty.GetValue(RuntimeHelpers.GetUninitializedObject(arm)) as string;
        }
        catch (Exception exception)
            when (exception is TargetInvocationException or MemberAccessException
                or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    private static JsonException MissingVariantMember(string path, string member) =>
        new($"Required Daml variant field '{path}.{member}' is missing from the JSON object");

    private static readonly string[] StdlibTupleFieldLabels = ["_1", "_2", "_3"];

    private static bool IsStdlibTuple(Type clrType) =>
        clrType.IsGenericType
        && (clrType.GetGenericTypeDefinition() == typeof(Tuple2<,>)
            || clrType.GetGenericTypeDefinition() == typeof(Tuple3<,,>));

    private static DamlRecord ReadStdlibTuple(JsonElement json, ValueSlot slot, DamlJsonDeserializationLimits limits, int depth, string path)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            throw ShapeMismatch(path, JsonValueKind.Object, json.ValueKind);
        }

        var componentCount = slot.ClrType.GetGenericArguments().Length;
        var fields = new List<DamlField>(componentCount);
        for (var component = 0; component < componentCount; component++)
        {
            var label = StdlibTupleFieldLabels[component];
            var componentPath = $"{path}.{label}";
            if (!json.TryGetProperty(label, out var element))
            {
                throw MissingRecordField(componentPath);
            }
            fields.Add(new DamlField(
                label,
                ReadValue(element, slot.TypeArgument(component), limits, depth + 1, componentPath)));
        }
        return new DamlRecord(null, fields);
    }

    private static readonly string[] EitherConstructors = ["Left", "Right"];

    private static bool IsEither(Type clrType) =>
        clrType.IsGenericType && clrType.GetGenericTypeDefinition() == typeof(Either<,>);

    private static bool IsWrappedOptional(Type clrType) =>
        clrType.IsGenericType && clrType.GetGenericTypeDefinition() == typeof(Optional<>);

    private static DamlOptional ReadWrappedOptional(
        JsonElement json, ValueSlot slot, DamlJsonDeserializationLimits limits, int depth, string path) =>
        json.ValueKind == JsonValueKind.Null
            ? DamlOptional.None
            : DamlOptional.Some(ReadValue(json, slot.TypeArgument(0), limits, depth + 1, path));

    private static bool IsOptionalChainRoot(Type wrappedOptional)
    {
        var carried = wrappedOptional.GetGenericArguments()[0];
        return IsWrappedOptional(carried);
    }

    private static DamlValue ReadOptionalChain(
        JsonElement json, ValueSlot slot, DamlJsonDeserializationLimits limits, int depth, string path)
    {
        if (json.ValueKind != JsonValueKind.Array)
        {
            throw ShapeMismatch(path, JsonValueKind.Array, json.ValueKind);
        }

        var length = json.GetArrayLength();
        if (length == 0)
        {
            return DamlOptionalChain.None;
        }
        if (length > 1)
        {
            throw OverfullOptionalChain(path, length);
        }

        var carried = slot.TypeArgument(0);
        var carriedPath = $"{path}[0]";
        return DamlOptionalChain.Some(
            IsWrappedOptional(carried.ClrType)
                ? ReadOptionalChain(json[0], carried, limits, depth + 1, carriedPath)
                : ReadValue(json[0], carried, limits, depth + 1, carriedPath));
    }

    private static DamlVariant ReadEither(JsonElement json, ValueSlot slot, DamlJsonDeserializationLimits limits, int depth, string path)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            throw ShapeMismatch(path, JsonValueKind.Object, json.ValueKind);
        }

        var tag = ReadVariantTag(json, path);
        var component = Array.IndexOf(EitherConstructors, tag);
        if (component < 0)
        {
            throw UnknownConstructor(VariantConstructorKind, tag, path, EitherConstructors);
        }
        if (!json.TryGetProperty(VariantValueKey, out var valueElement))
        {
            throw MissingVariantMember(path, VariantValueKey);
        }

        var payloadPath = $"{path}.{VariantValueKey}";
        return DamlVariant.Create(
            tag,
            ReadValue(valueElement, slot.TypeArgument(component), limits, depth + 1, payloadPath));
    }

    private const string StdlibMapFieldLabel = "map";
    private const string NonEmptyHeadFieldLabel = "hd";
    private const string NonEmptyTailFieldLabel = "tl";

    private static bool IsStdlibSet(Type clrType) =>
        clrType.IsGenericType && clrType.GetGenericTypeDefinition() == typeof(Set<>);

    private static bool IsStdlibNonEmpty(Type clrType) =>
        clrType.IsGenericType && clrType.GetGenericTypeDefinition() == typeof(NonEmpty<>);

    private static bool IsStdlibMap(Type clrType) =>
        clrType.IsGenericType && clrType.GetGenericTypeDefinition() == typeof(Map<,>);

    private static DamlRecord ReadStdlibSet(JsonElement json, ValueSlot slot, DamlJsonDeserializationLimits limits, int depth, string path)
    {
        var (entries, entriesPath) = ReadStdlibMapField(json, path);
        return WrapStdlibMap(
            ReadGenMapEntries(
                entries, slot.TypeArgument(0), ReadUnit, StdlibSetContainerName, limits, depth + 1, entriesPath));
    }

    private static DamlRecord ReadStdlibMap(JsonElement json, ValueSlot slot, DamlJsonDeserializationLimits limits, int depth, string path)
    {
        var (entries, entriesPath) = ReadStdlibMapField(json, path);
        var valueSlot = slot.TypeArgument(1);
        return WrapStdlibMap(ReadGenMapEntries(
            entries,
            slot.TypeArgument(0),
            (value, valuePath) => ReadValue(value, valueSlot, limits, depth + 2, valuePath),
            StdlibMapContainerName,
            limits,
            depth + 1,
            entriesPath));
    }

    private static (JsonElement Entries, string Path) ReadStdlibMapField(JsonElement json, string path)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            throw ShapeMismatch(path, JsonValueKind.Object, json.ValueKind);
        }

        var entriesPath = $"{path}.{StdlibMapFieldLabel}";
        return json.TryGetProperty(StdlibMapFieldLabel, out var entries)
            ? (entries, entriesPath)
            : throw MissingRecordField(entriesPath);
    }

    private static DamlRecord WrapStdlibMap(DamlGenMap entries) =>
        new(null, [new DamlField(StdlibMapFieldLabel, entries)]);

    private static DamlRecord ReadStdlibNonEmpty(JsonElement json, ValueSlot slot, DamlJsonDeserializationLimits limits, int depth, string path)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            throw ShapeMismatch(path, JsonValueKind.Object, json.ValueKind);
        }

        var elementSlot = slot.TypeArgument(0);
        var headPath = $"{path}.{NonEmptyHeadFieldLabel}";
        if (!json.TryGetProperty(NonEmptyHeadFieldLabel, out var head))
        {
            throw MissingRecordField(headPath);
        }

        var tailPath = $"{path}.{NonEmptyTailFieldLabel}";
        if (!json.TryGetProperty(NonEmptyTailFieldLabel, out var tail))
        {
            throw MissingRecordField(tailPath);
        }

        return new DamlRecord(null, [
            new DamlField(NonEmptyHeadFieldLabel, ReadValue(head, elementSlot, limits, depth + 1, headPath)),
            new DamlField(NonEmptyTailFieldLabel, ReadList(tail, elementSlot, limits, depth + 1, tailPath))]);
    }

    private static bool IsContractId(Type clrType) =>
        clrType.IsGenericType && clrType.GetGenericTypeDefinition() == typeof(ContractId<>);

    private static bool IsList(Type clrType) =>
        clrType.IsGenericType && clrType.GetGenericTypeDefinition() == typeof(IReadOnlyList<>);

    private static bool IsDictionary(Type clrType) =>
        clrType.IsGenericType && clrType.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>);

    private static DamlValue ReadMap(JsonElement json, ValueSlot slot, DamlJsonDeserializationLimits limits, int depth, string path)
    {
        var keySlot = slot.TypeArgument(0);
        if (json.ValueKind == JsonValueKind.Array)
        {
            return ReadGenMap(json, keySlot, slot.TypeArgument(1), limits, depth, path);
        }
        if (json.ValueKind == JsonValueKind.Object && keySlot.ClrType == typeof(string))
        {
            return ReadTextMap(json, slot.TypeArgument(1), limits, depth, path);
        }
        throw ShapeMismatch(path, AcceptedMapWireForms(keySlot), json.ValueKind);
    }

    private static string AcceptedMapWireForms(ValueSlot keySlot) =>
        keySlot.ClrType == typeof(string)
            ? "object (TextMap) or array of entry pairs (GenMap)"
            : "array of entry pairs (GenMap)";

    private static DamlGenMap ReadGenMap(JsonElement json, ValueSlot keySlot, ValueSlot valueSlot, DamlJsonDeserializationLimits limits, int depth, string path) =>
        ReadGenMapEntries(
            json,
            keySlot,
            (value, valuePath) => ReadValue(value, valueSlot, limits, depth + 1, valuePath),
            GenMapContainerName,
            limits,
            depth,
            path);

    private const string GenMapContainerName = "GenMap";
    private const string StdlibSetContainerName = "Set";
    private const string StdlibMapContainerName = "Map";

    private static DamlGenMap ReadGenMapEntries(
        JsonElement json,
        ValueSlot keySlot,
        Func<JsonElement, string, DamlValue> readEntryValue,
        string containerName,
        DamlJsonDeserializationLimits limits,
        int depth,
        string path)
    {
        if (json.ValueKind != JsonValueKind.Array)
        {
            throw ShapeMismatch(path, JsonValueKind.Array, json.ValueKind);
        }

        var length = json.GetArrayLength();
        if (length > limits.MaxArrayElements)
        {
            throw DamlJsonSerializer.ArrayBreadthExceeded(length, limits.MaxArrayElements);
        }

        var entries = new List<(DamlValue Key, DamlValue Value)>(length);
        var seenKeys = new HashSet<DamlValue>();
        foreach (var entry in json.EnumerateArray())
        {
            var entryPath = $"{path}[{entries.Count}]";
            if (entry.ValueKind != JsonValueKind.Array)
            {
                throw ShapeMismatch(entryPath, JsonValueKind.Array, entry.ValueKind);
            }
            if (entry.GetArrayLength() != 2)
            {
                throw new JsonException(
                    $"Expected a two-element key/value pair at '{entryPath}' but found {entry.GetArrayLength()} element(s)");
            }

            var key = ReadValue(entry[0], keySlot, limits, depth + 1, $"{entryPath}.key");
            if (!seenKeys.Add(key))
            {
                throw new JsonException($"Duplicate key at '{entryPath}' in a Daml {containerName}");
            }
            entries.Add((key, readEntryValue(entry[1], $"{entryPath}.value")));
        }
        return new DamlGenMap(entries);
    }

    private static DamlTextMap ReadTextMap(JsonElement json, ValueSlot valueSlot, DamlJsonDeserializationLimits limits, int depth, string path)
    {
        var count = json.GetPropertyCount();
        if (count > limits.MaxArrayElements)
        {
            throw MapBreadthExceeded(count, limits.MaxArrayElements);
        }

        var values = new Dictionary<string, DamlValue>(count);
        foreach (var entry in json.EnumerateObject())
        {
            if (!values.TryAdd(entry.Name, ReadValue(entry.Value, valueSlot, limits, depth + 1, MapEntryPath(path, entry.Name))))
            {
                throw new JsonException($"Duplicate key '{entry.Name}' at '{path}' in a Daml TextMap");
            }
        }
        return new DamlTextMap(values);
    }

    private static JsonException MapBreadthExceeded(int count, int maxEntries) =>
        new($"JSON object property count {count} exceeds the maximum supported Daml TextMap entry count of {maxEntries}");

    private static string MapEntryPath(string path, string key) =>
        $"{path}['{Elide(key).Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal)}']";

    private static DamlList ReadList(JsonElement json, ValueSlot elementSlot, DamlJsonDeserializationLimits limits, int depth, string path)
    {
        if (json.ValueKind != JsonValueKind.Array)
        {
            throw ShapeMismatch(path, JsonValueKind.Array, json.ValueKind);
        }

        var length = json.GetArrayLength();
        if (length > limits.MaxArrayElements)
        {
            throw DamlJsonSerializer.ArrayBreadthExceeded(length, limits.MaxArrayElements);
        }

        var values = new List<DamlValue>(length);
        foreach (var element in json.EnumerateArray())
        {
            values.Add(ReadValue(element, elementSlot, limits, depth + 1, $"{path}[{values.Count}]"));
        }
        return new DamlList(values);
    }

    private static JsonException ShapeMismatch(string path, JsonValueKind expected, JsonValueKind actual) =>
        ShapeMismatch(path, expected.ToString(), actual);

    private static JsonException ShapeMismatch(string path, string expected, JsonValueKind actual) =>
        new($"Expected JSON {expected} at '{path}' but found {actual}");

    private static JsonException OverfullOptionalChain(string path, int length) =>
        new($"A nested Daml Optional at '{path}' encodes as an array of at most one element "
            + $"but found {length}");

    private static JsonException MissingRecordField(string fieldPath) =>
        new($"Required Daml field '{fieldPath}' is missing from the JSON object");

    private static JsonException UnknownConstructor(string kind, string name, string path, IReadOnlyList<string> known) =>
        new($"Unknown Daml {kind} '{Elide(name)}' at '{path}'; expected one of {string.Join(", ", known)}");

    private const int MaximumEchoedValueLength = 64;

    private static JsonException MalformedScalar(string path, string raw, string damlType) =>
        new($"Value '{Elide(raw)}' at '{path}' is not a valid Daml {damlType}");

    private static string Elide(string raw)
    {
        if (raw.Length <= MaximumEchoedValueLength)
        {
            return raw;
        }
        var boundary = char.IsHighSurrogate(raw[MaximumEchoedValueLength - 1])
            ? MaximumEchoedValueLength - 1
            : MaximumEchoedValueLength;
        return string.Concat(raw.AsSpan(0, boundary), "…");
    }

    private static NotSupportedException ArmlessVariant(Type variantType, string path) =>
        new($"Type '{variantType}' at '{path}' declares no variant arms; "
            + $"pass a generated variant whose constructors are nested types carrying a {VariantTagProperty} property.");

    private static NotSupportedException UntaggedVariantArm(Type arm, string path) =>
        new($"Variant arm '{arm}' at '{path}' exposes no readable {VariantTagProperty} property, so its wire "
            + "constructor cannot be determined; pass a generated variant.");

    private static NotSupportedException InterfaceMarkerTarget(Type interfaceType, string path) =>
        new($"Type '{interfaceType}' at '{path}' is a Daml interface marker, which has no wire record of its own; "
            + "read the interface's view type instead.");

    private static NotSupportedException NotAGeneratedRecord(Type recordType, string path) =>
        new($"Type '{recordType}' at '{path}' is not a generated Daml record; "
            + $"pass a concrete type implementing {nameof(IDamlRecord)} whose properties carry {nameof(DamlFieldAttribute)}.");

    private static NotSupportedException UnmappedClrType(Type clrType, string path) =>
        new($"CLR type '{clrType}' at '{path}' lies outside the Daml type mapping; "
            + "give the property a mapped Daml type or decode this field without the reader.");
}
