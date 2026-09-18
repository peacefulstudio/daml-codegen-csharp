// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Collections.Frozen;
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
/// This reflection-driven reader and the composable <see cref="DamlLfJsonDecoders"/> share the
/// same scalar, shape and error primitives — this type supplies the shape via a reflected CLR
/// <see cref="Type"/>, <see cref="DamlLfJsonDecoders"/> supplies it via explicit element readers.
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
    /// <seealso cref="DamlLfJsonDecoders.ReadRecord{T}(JsonElement, DamlLfJsonDecodeContext)"/>
    public static DamlRecord ReadRecord<T>(JsonElement json, DamlJsonDeserializationLimits? limits = null)
        where T : IDamlRecord<T> =>
        T.__ReadDamlLfJson(json, DamlLfJsonDecodeContext.Root(typeof(T).Name, limits));

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
    /// <seealso cref="DamlLfJsonDecoders.ReadRecord{T}(JsonElement, DamlLfJsonDecodeContext)"/>
    public static DamlRecord ReadRecord<T>(string json, DamlJsonDeserializationLimits? limits = null)
        where T : IDamlRecord<T>
    {
        ArgumentNullException.ThrowIfNull(json);
        var effectiveLimits = limits ?? DamlJsonSerializer.DefaultDeserializationLimits;
        DamlJsonSerializer.EnsureWithinInputLimit(json, effectiveLimits);
        using var document = JsonDocument.Parse(json, DamlJsonSerializer.DocumentOptions);
        return T.__ReadDamlLfJson(
            document.RootElement, DamlLfJsonDecodeContext.Root(typeof(T).Name, effectiveLimits));
    }

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
    /// <seealso cref="ReadValue(JsonElement, Type, DamlJsonDeserializationLimits?)"/>
    [Obsolete(
        "Type-keyed Daml-LF JSON decoding is superseded by the emitted decoders reached through "
        + "DamlLfJsonDecoders.ReadRecord<T>/ReadVariant<T>; this overload keeps preview.2 behaviour, "
        + "including its NotSupportedException for shapes a CLR Type cannot express. "
        + "Scheduled for removal once every record/variant/enum/template/view has an emitted "
        + "decoder and the reflection reader retires.",
        DiagnosticId = "DAMLRT0001")]
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
    /// <seealso cref="ReadValue(string, Type, DamlJsonDeserializationLimits?)"/>
    [Obsolete(
        "Type-keyed Daml-LF JSON decoding is superseded by the emitted decoders reached through "
        + "DamlLfJsonDecoders.ReadRecord<T>/ReadVariant<T>; this overload keeps preview.2 behaviour, "
        + "including its NotSupportedException for shapes a CLR Type cannot express. "
        + "Scheduled for removal once every record/variant/enum/template/view has an emitted "
        + "decoder and the reflection reader retires.",
        DiagnosticId = "DAMLRT0001")]
    public static DamlRecord ReadRecord(string json, Type recordType, DamlJsonDeserializationLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(recordType);
        var effectiveLimits = limits ?? DamlJsonSerializer.DefaultDeserializationLimits;
        DamlJsonSerializer.EnsureWithinInputLimit(json, effectiveLimits);
        using var document = JsonDocument.Parse(json, DamlJsonSerializer.DocumentOptions);
        return ReadRecordValue(document.RootElement, recordType, effectiveLimits, depth: 0, recordType.Name);
    }

    /// <summary>
    /// Decodes an already-parsed LF-JSON value against the shape of <typeparamref name="T"/>, whatever
    /// Daml shape that type names — a generated record, variant or enum, a scalar, a <c>ContractId</c> or <c>Unit</c>.
    /// A top-level enum must be a generated Daml enum; a plain CLR enum is refused here even though it decodes
    /// as a record field. Scalars, <c>ContractId</c> and <c>Unit</c> are also accepted in their wire spelling:
    /// <see cref="DamlText"/>, <see cref="DamlParty"/>, <see cref="DamlBool"/>, <see cref="DamlInt64"/>,
    /// <see cref="DamlNumeric"/>, <see cref="DamlDate"/>, <see cref="DamlTimestamp"/>, <see cref="DamlUnit"/>
    /// and <see cref="DamlContractId"/>.
    /// An empty top-level <c>List</c> (<c>[]</c>), <c>TextMap</c> (<c>{}</c>) or <c>GenMap</c> (<c>[]</c>) also
    /// decodes, because an empty collection carries no element whose optionality could be lost; every other
    /// generic Daml type family, and every non-empty one of these, is still refused at top level.
    /// </summary>
    /// <typeparam name="T">The Daml type describing the expected shape.</typeparam>
    /// <param name="json">The LF-JSON value to decode.</param>
    /// <param name="limits">
    /// Decode limits; the shared hardened defaults apply when omitted.
    /// <see cref="DamlJsonDeserializationLimits.MaxInputCharacters"/> is accepted and ignored here:
    /// the caller already parsed the document, so the parse-time size, duplicate-property and
    /// parse-depth caps were theirs to apply. What this overload bounds is the reader's own
    /// allocation amplification and recursion depth while walking an already-materialized
    /// document — decode bounds, not a boundary defence against hostile input.
    /// </param>
    /// <returns>A value carrying its Daml type.</returns>
    /// <exception cref="JsonException">The JSON does not match the expected shape, or a decode limit is exceeded.</exception>
    /// <exception cref="NotSupportedException"><typeparamref name="T"/> names no top-level Daml shape the reader decodes, a CLR property type lies outside the Daml type mapping, or a generated enum companion cannot name the wire constructor of one of its members.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limits"/> is not a valid limit configuration.</exception>
    /// <remarks>JSON properties the target type does not declare are ignored by design, for tolerance of payloads produced by newer Daml package versions.</remarks>
    /// <seealso cref="ReadRecord{T}(JsonElement, DamlJsonDeserializationLimits?)"/>
    [Obsolete(
        "Type-keyed Daml-LF JSON decoding is superseded by the emitted decoders reached through "
        + "DamlLfJsonDecoders.ReadRecord<T>/ReadVariant<T>; this overload keeps preview.2 behaviour, "
        + "including its NotSupportedException for shapes a CLR Type cannot express. "
        + "Scheduled for removal once every record/variant/enum/template/view has an emitted "
        + "decoder and the reflection reader retires.",
        DiagnosticId = "DAMLRT0001")]
    public static DamlValue ReadValue<T>(JsonElement json, DamlJsonDeserializationLimits? limits = null) =>
        ReadValue(json, typeof(T), limits);

    /// <summary>
    /// Parses LF-JSON under the shared hardened document options and decodes it against the
    /// shape of <typeparamref name="T"/>, whatever Daml shape that type names — a generated record, variant
    /// or enum, a scalar, a <c>ContractId</c> or <c>Unit</c>. A top-level enum must be a generated Daml enum;
    /// a plain CLR enum is refused here even though it decodes as a record field. Scalars, <c>ContractId</c>
    /// and <c>Unit</c> are also accepted in their wire spelling: <see cref="DamlText"/>, <see cref="DamlParty"/>,
    /// <see cref="DamlBool"/>, <see cref="DamlInt64"/>, <see cref="DamlNumeric"/>, <see cref="DamlDate"/>,
    /// <see cref="DamlTimestamp"/>, <see cref="DamlUnit"/> and <see cref="DamlContractId"/>.
    /// An empty top-level <c>List</c> (<c>[]</c>), <c>TextMap</c> (<c>{}</c>) or <c>GenMap</c> (<c>[]</c>) also
    /// decodes, because an empty collection carries no element whose optionality could be lost; every other
    /// generic Daml type family, and every non-empty one of these, is still refused at top level.
    /// </summary>
    /// <typeparam name="T">The Daml type describing the expected shape.</typeparam>
    /// <param name="json">The LF-JSON text to parse and decode.</param>
    /// <param name="limits">Decode limits; the shared hardened defaults apply when omitted.</param>
    /// <returns>A value carrying its Daml type.</returns>
    /// <exception cref="JsonException">The JSON is malformed, does not match the expected shape, or exceeds a limit.</exception>
    /// <exception cref="NotSupportedException"><typeparamref name="T"/> names no top-level Daml shape the reader decodes, a CLR property type lies outside the Daml type mapping, or a generated enum companion cannot name the wire constructor of one of its members.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limits"/> is not a valid limit configuration.</exception>
    /// <remarks>JSON properties the target type does not declare are ignored by design, for tolerance of payloads produced by newer Daml package versions.</remarks>
    /// <seealso cref="ReadRecord{T}(string, DamlJsonDeserializationLimits?)"/>
    [Obsolete(
        "Type-keyed Daml-LF JSON decoding is superseded by the emitted decoders reached through "
        + "DamlLfJsonDecoders.ReadRecord<T>/ReadVariant<T>; this overload keeps preview.2 behaviour, "
        + "including its NotSupportedException for shapes a CLR Type cannot express. "
        + "Scheduled for removal once every record/variant/enum/template/view has an emitted "
        + "decoder and the reflection reader retires.",
        DiagnosticId = "DAMLRT0001")]
    public static DamlValue ReadValue<T>(string json, DamlJsonDeserializationLimits? limits = null) =>
        ReadValue(json, typeof(T), limits);

    /// <summary>
    /// Decodes an already-parsed LF-JSON value against the shape of <paramref name="valueType"/>, whatever
    /// Daml shape that type names — a generated record, variant or enum, a scalar, a <c>ContractId</c> or <c>Unit</c>.
    /// A top-level enum must be a generated Daml enum; a plain CLR enum is refused here even though it decodes
    /// as a record field. Scalars, <c>ContractId</c> and <c>Unit</c> are also accepted in their wire spelling:
    /// <see cref="DamlText"/>, <see cref="DamlParty"/>, <see cref="DamlBool"/>, <see cref="DamlInt64"/>,
    /// <see cref="DamlNumeric"/>, <see cref="DamlDate"/>, <see cref="DamlTimestamp"/>, <see cref="DamlUnit"/>
    /// and <see cref="DamlContractId"/>.
    /// An empty top-level <c>List</c> (<c>[]</c>), <c>TextMap</c> (<c>{}</c>) or <c>GenMap</c> (<c>[]</c>) also
    /// decodes, because an empty collection carries no element whose optionality could be lost; every other
    /// generic Daml type family, and every non-empty one of these, is still refused at top level.
    /// </summary>
    /// <param name="json">The LF-JSON value to decode.</param>
    /// <param name="valueType">The Daml type describing the expected shape.</param>
    /// <param name="limits">
    /// Decode limits; the shared hardened defaults apply when omitted.
    /// <see cref="DamlJsonDeserializationLimits.MaxInputCharacters"/> is accepted and ignored here:
    /// the caller already parsed the document, so the parse-time size, duplicate-property and
    /// parse-depth caps were theirs to apply. What this overload bounds is the reader's own
    /// allocation amplification and recursion depth while walking an already-materialized
    /// document — decode bounds, not a boundary defence against hostile input.
    /// </param>
    /// <returns>A value carrying its Daml type.</returns>
    /// <exception cref="JsonException">The JSON does not match the expected shape, or a decode limit is exceeded.</exception>
    /// <exception cref="NotSupportedException"><paramref name="valueType"/> names no top-level Daml shape the reader decodes, a CLR property type lies outside the Daml type mapping, or a generated enum companion cannot name the wire constructor of one of its members.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="valueType"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limits"/> is not a valid limit configuration.</exception>
    /// <remarks>JSON properties the target type does not declare are ignored by design, for tolerance of payloads produced by newer Daml package versions.</remarks>
    /// <seealso cref="ReadRecord(JsonElement, Type, DamlJsonDeserializationLimits?)"/>
    [Obsolete(
        "Type-keyed Daml-LF JSON decoding is superseded by the emitted decoders reached through "
        + "DamlLfJsonDecoders.ReadRecord<T>/ReadVariant<T>; this overload keeps preview.2 behaviour, "
        + "including its NotSupportedException for shapes a CLR Type cannot express. "
        + "Scheduled for removal once every record/variant/enum/template/view has an emitted "
        + "decoder and the reflection reader retires.",
        DiagnosticId = "DAMLRT0001")]
    public static DamlValue ReadValue(JsonElement json, Type valueType, DamlJsonDeserializationLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(valueType);
        var effectiveLimits = limits ?? DamlJsonSerializer.DefaultDeserializationLimits;
        DamlJsonSerializer.ValidateLimits(effectiveLimits);
        return ReadTopLevelValue(json, valueType, effectiveLimits, valueType.Name);
    }

    /// <summary>
    /// Parses LF-JSON under the shared hardened document options and decodes it against the
    /// shape of <paramref name="valueType"/>, whatever Daml shape that type names — a generated record, variant
    /// or enum, a scalar, a <c>ContractId</c> or <c>Unit</c>. A top-level enum must be a generated Daml enum;
    /// a plain CLR enum is refused here even though it decodes as a record field. Scalars, <c>ContractId</c>
    /// and <c>Unit</c> are also accepted in their wire spelling: <see cref="DamlText"/>, <see cref="DamlParty"/>,
    /// <see cref="DamlBool"/>, <see cref="DamlInt64"/>, <see cref="DamlNumeric"/>, <see cref="DamlDate"/>,
    /// <see cref="DamlTimestamp"/>, <see cref="DamlUnit"/> and <see cref="DamlContractId"/>.
    /// An empty top-level <c>List</c> (<c>[]</c>), <c>TextMap</c> (<c>{}</c>) or <c>GenMap</c> (<c>[]</c>) also
    /// decodes, because an empty collection carries no element whose optionality could be lost; every other
    /// generic Daml type family, and every non-empty one of these, is still refused at top level.
    /// </summary>
    /// <param name="json">The LF-JSON text to parse and decode.</param>
    /// <param name="valueType">The Daml type describing the expected shape.</param>
    /// <param name="limits">Decode limits; the shared hardened defaults apply when omitted.</param>
    /// <returns>A value carrying its Daml type.</returns>
    /// <exception cref="JsonException">The JSON is malformed, does not match the expected shape, or exceeds a limit.</exception>
    /// <exception cref="NotSupportedException"><paramref name="valueType"/> names no top-level Daml shape the reader decodes, a CLR property type lies outside the Daml type mapping, or a generated enum companion cannot name the wire constructor of one of its members.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> or <paramref name="valueType"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limits"/> is not a valid limit configuration.</exception>
    /// <remarks>JSON properties the target type does not declare are ignored by design, for tolerance of payloads produced by newer Daml package versions.</remarks>
    /// <seealso cref="ReadRecord(string, Type, DamlJsonDeserializationLimits?)"/>
    [Obsolete(
        "Type-keyed Daml-LF JSON decoding is superseded by the emitted decoders reached through "
        + "DamlLfJsonDecoders.ReadRecord<T>/ReadVariant<T>; this overload keeps preview.2 behaviour, "
        + "including its NotSupportedException for shapes a CLR Type cannot express. "
        + "Scheduled for removal once every record/variant/enum/template/view has an emitted "
        + "decoder and the reflection reader retires.",
        DiagnosticId = "DAMLRT0001")]
    public static DamlValue ReadValue(string json, Type valueType, DamlJsonDeserializationLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(valueType);
        var effectiveLimits = limits ?? DamlJsonSerializer.DefaultDeserializationLimits;
        DamlJsonSerializer.EnsureWithinInputLimit(json, effectiveLimits);
        using var document = JsonDocument.Parse(json, DamlJsonSerializer.DocumentOptions);
        return ReadTopLevelValue(document.RootElement, valueType, effectiveLimits, valueType.Name);
    }

    private static DamlValue ReadTopLevelValue(
        JsonElement json, Type valueType, DamlJsonDeserializationLimits limits, string path)
    {
        if (TopLevelScalarArms.TryGetValue(valueType, out var readScalar))
        {
            return readScalar(json, path);
        }
        if (IsGeneratedDamlEnum(valueType))
        {
            return ReadEnum(json, valueType, path);
        }
        if (IsContractId(valueType) || valueType == typeof(DamlContractId))
        {
            return new DamlContractId(DamlLfJsonDecoders.ReadWireString(json, path));
        }
        if (EmptyTopLevelCollection(json, valueType) is { } emptyCollection)
        {
            return emptyCollection;
        }
        if (CarriesDamlTypeArguments(valueType))
        {
            throw GenericFamilyAtTopLevel(valueType, path);
        }
        if (typeof(IDamlVariant).IsAssignableFrom(valueType))
        {
            return ReadVariant(json, valueType, limits, depth: 0, path);
        }
        if (typeof(IDamlRecord).IsAssignableFrom(valueType))
        {
            return ReadRecordValue(json, valueType, limits, depth: 0, path);
        }
        throw UnsupportedTopLevelValueType(valueType, path);
    }

    private static DamlValue? EmptyTopLevelCollection(JsonElement json, Type valueType)
    {
        if (IsList(valueType))
        {
            return IsEmptyArray(json) ? new DamlList([]) : null;
        }
        if (!IsDictionary(valueType))
        {
            return null;
        }
        if (IsEmptyArray(json))
        {
            return new DamlGenMap([]);
        }
        return IsEmptyObject(json) && valueType.GetGenericArguments()[0] == typeof(string)
            ? new DamlTextMap(new Dictionary<string, DamlValue>(0))
            : null;
    }

    private static bool IsEmptyArray(JsonElement json) =>
        json.ValueKind == JsonValueKind.Array && json.GetArrayLength() == 0;

    private static bool IsEmptyObject(JsonElement json) =>
        json.ValueKind == JsonValueKind.Object && json.GetPropertyCount() == 0;

    private static bool CarriesDamlTypeArguments(Type clrType) =>
        Nullable.GetUnderlyingType(clrType) is not null
        || IsList(clrType)
        || IsDictionary(clrType)
        || IsStdlibTuple(clrType)
        || IsEither(clrType)
        || IsWrappedOptional(clrType)
        || IsStdlibSet(clrType)
        || IsStdlibNonEmpty(clrType)
        || IsStdlibMap(clrType);

    internal static DamlRecord ReadRecordValue(JsonElement json, Type recordType, DamlJsonDeserializationLimits limits, int depth, string path)
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
            throw DamlLfJsonDecoders.ShapeMismatch(path, JsonValueKind.Object, json.ValueKind);
        }

        var fields = new List<DamlField>();
        foreach (var (label, slot) in DamlFieldsOf(recordType))
        {
            var fieldPath = $"{path}.{label}";
            if (!json.TryGetProperty(label, out var element))
            {
                throw DamlLfJsonDecoders.MissingRecordField(fieldPath);
            }
            fields.Add(new DamlField(label, ReadFieldValue(element, slot, limits, depth + 1, fieldPath)));
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

    private static DamlValue ReadFieldValue(JsonElement json, ValueSlot slot, DamlJsonDeserializationLimits limits, int depth, string path)
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
            return new DamlContractId(DamlLfJsonDecoders.ReadWireString(json, path));
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
            [typeof(Party)] = (json, path) => new DamlParty(DamlLfJsonDecoders.ReadWireString(json, path)),
            [typeof(string)] = (json, path) => new DamlText(DamlLfJsonDecoders.ReadWireString(json, path)),
            [typeof(bool)] = DamlLfJsonDecoders.ReadBool,
            [typeof(long)] = DamlLfJsonDecoders.ReadInt64,
            [typeof(decimal)] = DamlLfJsonDecoders.ReadNumeric,
            [typeof(DateOnly)] = DamlLfJsonDecoders.ReadDate,
            [typeof(DateTimeOffset)] = DamlLfJsonDecoders.ReadTimestamp,
            [typeof(DamlUnit)] = DamlLfJsonDecoders.ReadUnit
        }.ToFrozenDictionary();

    private static readonly (Type Alias, Type Canonical)[] TopLevelScalarAliases =
    [
        (typeof(DamlParty), typeof(Party)),
        (typeof(DamlText), typeof(string)),
        (typeof(DamlBool), typeof(bool)),
        (typeof(DamlInt64), typeof(long)),
        (typeof(DamlNumeric), typeof(decimal)),
        (typeof(DamlDate), typeof(DateOnly)),
        (typeof(DamlTimestamp), typeof(DateTimeOffset)),
        (typeof(Unit), typeof(DamlUnit))
    ];

    private static readonly FrozenDictionary<Type, Func<JsonElement, string, DamlValue>> TopLevelScalarArms =
        ScalarArms
            .Concat(TopLevelScalarAliases.Select(alias =>
            {
                if (!ScalarArms.TryGetValue(alias.Canonical, out var reader))
                {
                    throw new InvalidOperationException(
                        $"TopLevelScalarAliases accepts '{alias.Alias}' as a top-level spelling of "
                        + $"'{alias.Canonical}', but '{alias.Canonical}' has no entry in ScalarArms.");
                }
                return KeyValuePair.Create(alias.Alias, reader);
            }))
            .ToFrozenDictionary();

    internal static DamlEnum ReadEnum(JsonElement json, Type enumType, string path)
    {
        var constructor = DamlLfJsonDecoders.ReadWireString(json, path);
        var (known, sorted) = WireConstructorsOf(enumType, path);
        return known.Contains(constructor)
            ? DamlEnum.Create(constructor)
            : throw DamlLfJsonDecoders.UnknownConstructor(DamlLfJsonDecoders.EnumConstructorKind, constructor, path, sorted);
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

    internal static bool IsGeneratedDamlEnum(Type clrType) =>
        clrType.IsEnum && GeneratedEnumCompanion(clrType) is not null;

    private static MethodInfo? GeneratedEnumCompanion(Type enumType)
    {
        if (enumType.FullName is not { } fullName)
        {
            return null;
        }
        var toDamlEnum = enumType.Assembly.GetType(fullName + GeneratedCompanionSuffix)
            ?.GetMethod(ToDamlEnumMethod, BindingFlags.Public | BindingFlags.Static, null, [enumType], null);
        return toDamlEnum?.ReturnType == typeof(DamlEnum) ? toDamlEnum : null;
    }

    private static IReadOnlyList<string> WireConstructorNames(Type enumType, string path)
    {
        var toDamlEnum = GeneratedEnumCompanion(enumType);
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

    private const string VariantValueKey = "value";

    internal static DamlVariant ReadVariant(JsonElement json, Type variantType, DamlJsonDeserializationLimits limits, int depth, string path)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            throw DamlLfJsonDecoders.ShapeMismatch(path, JsonValueKind.Object, json.ValueKind);
        }

        var tag = DamlLfJsonDecoders.ReadVariantTag(json, path);
        var arms = ArmsOf(variantType, path);
        if (!arms.TryGetValue(tag, out var arm))
        {
            throw DamlLfJsonDecoders.UnknownConstructor(DamlLfJsonDecoders.VariantConstructorKind, tag, path, arms.Keys.Order(StringComparer.Ordinal).ToList());
        }
        if (!json.TryGetProperty(VariantValueKey, out var valueElement))
        {
            throw DamlLfJsonDecoders.MissingVariantMember(path, VariantValueKey);
        }

        var payloadPath = $"{path}.{VariantValueKey}";
        if (PayloadSlotOf(arm, payloadPath) is { } payloadSlot)
        {
            return DamlVariant.Create(tag, ReadFieldValue(valueElement, payloadSlot, limits, depth + 1, payloadPath));
        }
        if (depth + 1 > DamlJsonSerializer.MaximumNestingDepth)
        {
            throw DamlJsonSerializer.DepthBoundExceeded();
        }
        return DamlVariant.Create(tag, DamlLfJsonDecoders.ReadUnit(valueElement, payloadPath));
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

    private static bool IsStdlibTuple(Type clrType) =>
        clrType.IsGenericType
        && (clrType.GetGenericTypeDefinition() == typeof(Tuple2<,>)
            || clrType.GetGenericTypeDefinition() == typeof(Tuple3<,,>));

    private static DamlRecord ReadStdlibTuple(JsonElement json, ValueSlot slot, DamlJsonDeserializationLimits limits, int depth, string path)
    {
        var componentCount = slot.ClrType.GetGenericArguments().Length;
        var componentReaders = new Func<JsonElement, string, DamlValue>[componentCount];
        for (var component = 0; component < componentCount; component++)
        {
            var componentSlot = slot.TypeArgument(component);
            componentReaders[component] = (element, componentPath) =>
                ReadFieldValue(element, componentSlot, limits, depth + 1, componentPath);
        }
        return DamlLfJsonDecoders.ReadTuple(json, componentReaders, path);
    }

    private static bool IsEither(Type clrType) =>
        clrType.IsGenericType && clrType.GetGenericTypeDefinition() == typeof(Either<,>);

    private static bool IsWrappedOptional(Type clrType) =>
        clrType.IsGenericType && clrType.GetGenericTypeDefinition() == typeof(Optional<>);

    private static DamlOptional ReadWrappedOptional(
        JsonElement json, ValueSlot slot, DamlJsonDeserializationLimits limits, int depth, string path)
    {
        var carried = slot.TypeArgument(0);
        return DamlLfJsonDecoders.ReadOptional(
            json,
            (element, elementPath) => ReadFieldValue(element, carried, limits, depth + 1, elementPath),
            path);
    }

    private static bool IsOptionalChainRoot(Type wrappedOptional)
    {
        var carried = wrappedOptional.GetGenericArguments()[0];
        return IsWrappedOptional(carried);
    }

    private static DamlValue ReadOptionalChain(
        JsonElement json, ValueSlot slot, DamlJsonDeserializationLimits limits, int depth, string path)
    {
        var carried = slot.TypeArgument(0);
        return DamlLfJsonDecoders.ReadOptionalChain(
            json,
            (element, elementPath) => IsWrappedOptional(carried.ClrType)
                ? ReadOptionalChain(element, carried, limits, depth + 1, elementPath)
                : ReadFieldValue(element, carried, limits, depth + 1, elementPath),
            path);
    }

    private static DamlVariant ReadEither(JsonElement json, ValueSlot slot, DamlJsonDeserializationLimits limits, int depth, string path)
    {
        var leftSlot = slot.TypeArgument(0);
        var rightSlot = slot.TypeArgument(1);
        return DamlLfJsonDecoders.ReadEither(
            json,
            (element, elementPath) => ReadFieldValue(element, leftSlot, limits, depth + 1, elementPath),
            (element, elementPath) => ReadFieldValue(element, rightSlot, limits, depth + 1, elementPath),
            path);
    }

    private static bool IsStdlibSet(Type clrType) =>
        clrType.IsGenericType && clrType.GetGenericTypeDefinition() == typeof(Set<>);

    private static bool IsStdlibNonEmpty(Type clrType) =>
        clrType.IsGenericType && clrType.GetGenericTypeDefinition() == typeof(NonEmpty<>);

    private static bool IsStdlibMap(Type clrType) =>
        clrType.IsGenericType && clrType.GetGenericTypeDefinition() == typeof(Map<,>);

    private static DamlRecord ReadStdlibSet(JsonElement json, ValueSlot slot, DamlJsonDeserializationLimits limits, int depth, string path)
    {
        var (entries, entriesPath) = DamlLfJsonDecoders.ReadStdlibMapField(json, path);
        var elementSlot = slot.TypeArgument(0);
        return DamlLfJsonDecoders.WrapStdlibMap(DamlLfJsonDecoders.ReadGenMapEntries(
            entries,
            (key, keyPath) => ReadFieldValue(key, elementSlot, limits, depth + 2, keyPath),
            DamlLfJsonDecoders.ReadUnit,
            DamlLfJsonDecoders.StdlibSetContainerName,
            limits,
            entriesPath));
    }

    private static DamlRecord ReadStdlibMap(JsonElement json, ValueSlot slot, DamlJsonDeserializationLimits limits, int depth, string path)
    {
        var (entries, entriesPath) = DamlLfJsonDecoders.ReadStdlibMapField(json, path);
        var keySlot = slot.TypeArgument(0);
        var valueSlot = slot.TypeArgument(1);
        return DamlLfJsonDecoders.WrapStdlibMap(DamlLfJsonDecoders.ReadGenMapEntries(
            entries,
            (key, keyPath) => ReadFieldValue(key, keySlot, limits, depth + 2, keyPath),
            (value, valuePath) => ReadFieldValue(value, valueSlot, limits, depth + 2, valuePath),
            DamlLfJsonDecoders.StdlibMapContainerName,
            limits,
            entriesPath));
    }

    private static DamlRecord ReadStdlibNonEmpty(JsonElement json, ValueSlot slot, DamlJsonDeserializationLimits limits, int depth, string path)
    {
        var elementSlot = slot.TypeArgument(0);
        return DamlLfJsonDecoders.ReadNonEmpty(
            json,
            (head, headPath) => ReadFieldValue(head, elementSlot, limits, depth + 1, headPath),
            (element, elementPath) => ReadFieldValue(element, elementSlot, limits, depth + 2, elementPath),
            limits,
            path);
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
        throw DamlLfJsonDecoders.ShapeMismatch(path, AcceptedMapWireForms(keySlot), json.ValueKind);
    }

    private static string AcceptedMapWireForms(ValueSlot keySlot) =>
        keySlot.ClrType == typeof(string)
            ? "object (TextMap) or array of entry pairs (GenMap)"
            : "array of entry pairs (GenMap)";

    private static DamlGenMap ReadGenMap(JsonElement json, ValueSlot keySlot, ValueSlot valueSlot, DamlJsonDeserializationLimits limits, int depth, string path) =>
        DamlLfJsonDecoders.ReadGenMapEntries(
            json,
            (key, keyPath) => ReadFieldValue(key, keySlot, limits, depth + 1, keyPath),
            (value, valuePath) => ReadFieldValue(value, valueSlot, limits, depth + 1, valuePath),
            DamlLfJsonDecoders.GenMapContainerName,
            limits,
            path);

    private static DamlTextMap ReadTextMap(JsonElement json, ValueSlot valueSlot, DamlJsonDeserializationLimits limits, int depth, string path) =>
        DamlLfJsonDecoders.ReadTextMap(
            json,
            (value, valuePath) => ReadFieldValue(value, valueSlot, limits, depth + 1, valuePath),
            limits,
            path);

    private static DamlList ReadList(JsonElement json, ValueSlot elementSlot, DamlJsonDeserializationLimits limits, int depth, string path) =>
        DamlLfJsonDecoders.ReadList(
            json,
            (element, elementPath) => ReadFieldValue(element, elementSlot, limits, depth + 1, elementPath),
            limits,
            path);

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

    internal static NotSupportedException NotAGeneratedEnum(Type enumType, string path) =>
        new($"Type '{enumType}' at '{path}' is not a generated Daml enum; "
            + $"pass an enum whose companion type exposes a public static {ToDamlEnumMethod} method returning {nameof(DamlEnum)}.");

    internal static NotSupportedException NotAGeneratedVariant(Type variantType, string path) =>
        new($"Type '{variantType}' at '{path}' is not a generated Daml variant; "
            + $"pass a concrete type implementing {nameof(IDamlVariant)}.");

    private static NotSupportedException UnmappedClrType(Type clrType, string path) =>
        new($"CLR type '{clrType}' at '{path}' lies outside the Daml type mapping; "
            + "give the property a mapped Daml type or decode this field without the reader.");

    private static NotSupportedException GenericFamilyAtTopLevel(Type clrType, string path) =>
        new($"Type '{clrType}' at '{path}' names a generic Daml type family the reader does not decode as a "
            + "top-level value; decode it as a field of a generated Daml record, or decode this value without the reader.");

    private static NotSupportedException UnsupportedTopLevelValueType(Type clrType, string path) =>
        new($"Type '{clrType}' at '{path}' lies outside the Daml type mapping for a top-level value; "
            + "pass a generated Daml record, variant or enum, a Daml scalar, a ContractId or Unit.");
}
