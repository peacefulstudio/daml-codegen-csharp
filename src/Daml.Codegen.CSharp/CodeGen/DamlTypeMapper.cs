// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Turns a <see cref="DamlType"/> into C#: <see cref="MapType(DamlType)"/> produces a C# type
/// name, <c>ToValue</c> and <c>FromValue</c> produce the serialize and
/// deserialize expressions. Constructed once per package over a
/// <see cref="PackageEmitContext"/> and an <see cref="ICrossPackageResolver"/>, which
/// it calls into for cross-package names — it does not own resolution. Pure functions
/// of their inputs, so unit-testable without a real DAR.
/// </summary>
internal sealed class DamlTypeMapper(PackageEmitContext context, ICrossPackageResolver resolver)
{
    private const int MaxTypeDepth = 256;

    /// <summary>Maps <paramref name="type"/> to its C# type name.</summary>
    public string MapType(DamlType type) =>
        MapType(OptionalRepresentation.Rewrite(type, context.Package, resolver), depth: 0);

    /// <summary>
    /// The collection shape <see cref="MapType(DamlType)"/> emits for <paramref name="type"/>:
    /// <see cref="CollectionShape.List"/> for the types it writes as
    /// <c>IReadOnlyList&lt;T&gt;</c>, <see cref="CollectionShape.Map"/> for the ones it writes
    /// as <c>IReadOnlyDictionary&lt;TKey,TValue&gt;</c>, and
    /// <see cref="CollectionShape.None"/> for everything else.
    /// </summary>
    /// <param name="type">The Daml type of the member being emitted.</param>
    public CollectionShape ClassifyCollection(DamlType type) =>
        ClassifyRewritten(OptionalRepresentation.Rewrite(type, context.Package, resolver));

    private static CollectionShape ClassifyRewritten(DamlType type) => type switch
    {
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.List } } => CollectionShape.List,
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.TextMap or DamlPrimitive.GenMap } } =>
            CollectionShape.Map,
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional }, Arguments: [var argument] } =>
            ClassifyRewritten(argument),
        _ => CollectionShape.None,
    };

    private string MapType(DamlType type, int depth)
    {
        ThrowIfTooDeep(depth, nameof(MapType));

        return type switch
    {
        DamlPrimitiveType primitive => MapBarePrimitiveToCSharp(primitive.Primitive),
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Numeric } } => "decimal",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.ContractId }, Arguments: [var arg] } =>
            $"{context.Qualifier.Qualify(RuntimeTypeNames.ContractId)}<{MapType(arg, depth + 1)}>",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional }, Arguments: [var arg] } =>
            $"{MapType(arg, depth + 1)}?",
        DamlWrappedOptional wrapped =>
            $"{context.Qualifier.Qualify(RuntimeTypeNames.Optional)}<{MapType(wrapped.Argument, depth + 1)}>",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.List }, Arguments: [var arg] } =>
            $"{context.Qualifier.Qualify("IReadOnlyList")}<{MapType(arg, depth + 1)}>",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.TextMap }, Arguments: [var arg] } =>
            $"{context.Qualifier.Qualify("IReadOnlyDictionary")}<string, {MapType(arg, depth + 1)}>",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.GenMap }, Arguments: [var keyArg, var valueArg] } =>
            $"{context.Qualifier.Qualify("IReadOnlyDictionary")}<{MapType(keyArg, depth + 1)}, {MapType(valueArg, depth + 1)}>",
        DamlTypeApp { Base: DamlTypeRef typeRef } app =>
            app.Arguments.Count > 0
                ? $"{resolver.Resolve(typeRef, context)}<{string.Join(", ", app.Arguments.Select(arg => MapType(arg, depth + 1)))}>"
                : resolver.Resolve(typeRef, context),
        DamlTypeRef typeRef => resolver.Resolve(typeRef, context),
        DamlTypeVar typeVar => EmitterHelpers.TypeParameterName(typeVar.Name),
        _ => FallbackTypeName
    };
    }

    private const string FallbackTypeName = "object";

    /// <summary>Produces the expression that serializes <paramref name="fieldName"/> of <paramref name="type"/> to a Daml value.</summary>
    /// <param name="type">The Daml type of the field being serialized.</param>
    /// <param name="fieldName">The C# expression referencing the field value.</param>
    /// <param name="typeVarDelegates">
    /// Maps a Daml type-variable name to the injected converter-delegate parameter name
    /// in scope, supplied when emitting a generic record or variant's own body so a
    /// <see cref="DamlTypeVar"/> field resolves to its converter instead of the runtime
    /// stub. <c>null</c> outside a generic body.
    /// </param>
    public string ToValue(DamlType type, string fieldName, IReadOnlyDictionary<string, string>? typeVarDelegates = null) =>
        ToValue(OptionalRepresentation.Rewrite(type, context.Package, resolver), fieldName, typeVarDelegates, depth: 0);

    /// <remarks>
    /// The optional arm strips a leading <c>@</c> from the field name when deriving its local
    /// variable. Identifier sanitization escapes a field whose name is a C# keyword — <c>lock</c>,
    /// <c>class</c>, <c>event</c> — by prepending <c>@</c>, which is legal on a property but not
    /// on the local bound by the <c>is { } __name</c> pattern, so an <c>Optional</c> field called
    /// <c>lock</c> would emit the unparsable <c>__@lock</c>. Only the local is stripped; the
    /// property reference keeps its escape so the record property stays addressable.
    /// </remarks>
    private string ToValue(DamlType type, string fieldName, IReadOnlyDictionary<string, string>? typeVarDelegates, int depth)
    {
        ThrowIfTooDeep(depth, nameof(ToValue));

        return type switch
    {
        DamlPrimitiveType primitive => GetBarePrimitiveToValueConversion(primitive.Primitive, fieldName),
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Numeric } } =>
            $"new {context.Qualifier.Qualify(RuntimeTypeNames.DamlNumeric)}({fieldName})",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.ContractId } } =>
            $"{fieldName}.ToDamlValue()",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional } } app =>
            $"{fieldName} is {{ }} __{fieldName.TrimStart('@')} ? new {context.Qualifier.Qualify(RuntimeTypeNames.DamlOptional)}({ToValue(app.Arguments[0], $"__{fieldName.TrimStart('@')}", typeVarDelegates, depth + 1)}) : {context.Qualifier.Qualify(RuntimeTypeNames.DamlOptional)}.None",
        DamlWrappedOptional wrapped =>
            $"{fieldName}.{WrappedOptionalSerializer(wrapped.Encoding)}(__optional{depth} => {ToValue(wrapped.Argument, $"__optional{depth}", typeVarDelegates, depth + 1)})",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.List } } app =>
            $"new {context.Qualifier.Qualify(RuntimeTypeNames.DamlList)}({fieldName}.Select(x => ({context.Qualifier.Qualify(RuntimeTypeNames.DamlValue)}){ToValue(app.Arguments[0], "x", typeVarDelegates, depth + 1)}).ToList())",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.TextMap } } app =>
            $"new {context.Qualifier.Qualify(RuntimeTypeNames.DamlTextMap)}({fieldName}.ToDictionary(kv => kv.Key, kv => ({context.Qualifier.Qualify(RuntimeTypeNames.DamlValue)}){ToValue(app.Arguments[0], "kv.Value", typeVarDelegates, depth + 1)}))",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.GenMap } } app =>
            $"new {context.Qualifier.Qualify(RuntimeTypeNames.DamlGenMap)}({fieldName}.Select(kv => (({context.Qualifier.Qualify(RuntimeTypeNames.DamlValue)}){ToValue(app.Arguments[0], "kv.Key", typeVarDelegates, depth + 1)}, ({context.Qualifier.Qualify(RuntimeTypeNames.DamlValue)}){ToValue(app.Arguments[1], "kv.Value", typeVarDelegates, depth + 1)})).ToList())",
        DamlTypeApp { Base: DamlTypeRef typeRef } app
            when StdlibPackages.IsStdlibTypeRef(resolver, typeRef, parametric: true) =>
            EmitParametricStdlibToValue(typeRef, app.Arguments, fieldName, typeVarDelegates, depth),
        DamlTypeRef typeRef when IsEnumTypeRef(typeRef) =>
            EnumToDamlEnumCall(typeRef, fieldName),
        DamlTypeApp { Base: DamlTypeRef typeRef } app when IsVariantTypeRef(typeRef) =>
            $"{fieldName}.ToVariant({string.Join(", ", ToValueConverterLambdas(app.Arguments, typeVarDelegates, depth))})",
        DamlTypeApp { Base: DamlTypeRef typeRef } app when IsRecordTypeRef(typeRef) =>
            $"{fieldName}.ToRecord({string.Join(", ", ToValueConverterLambdas(app.Arguments, typeVarDelegates, depth))})",
        DamlTypeRef typeRef when IsVariantTypeRef(typeRef) =>
            $"{fieldName}.ToVariant()",
        DamlTypeVar typeVar when TryResolveDelegate(typeVarDelegates, typeVar, out var convert) =>
            $"{convert}({fieldName})",
        DamlTypeVar => FallbackToValueStub(fieldName),
        _ when MapsToFallbackObject(type, depth) => FallbackToValueStub(fieldName),
        _ => $"{fieldName}.ToRecord()"
    };
    }

    private string FallbackToValueStub(string fieldName) =>
        $"{context.Qualifier.Qualify(RuntimeTypeNames.GenericStub)}.NotImplemented<{context.Qualifier.Qualify(RuntimeTypeNames.DamlValue)}>(\"{fieldName}\")";

    private string FallbackFromValueStub(string valueName) =>
        $"{context.Qualifier.Qualify(RuntimeTypeNames.GenericStub)}.NotImplemented<{FallbackTypeName}>(\"{valueName.Replace("\"", "\\\"", StringComparison.Ordinal)}\")";

    private bool MapsToFallbackObject(DamlType type, int depth = 0) => MapType(type, depth) == FallbackTypeName;

    /// <summary>
    /// True when <see cref="MapType(DamlType)"/> renders <paramref name="type"/> as a C#
    /// reference type, so a parameter of that type can carry an
    /// <c>ArgumentNullException.ThrowIfNull</c> guard. Answers <c>false</c> for anything it
    /// cannot place with certainty, which costs a missing guard rather than a boxed
    /// value-type argument or a guard on an already-nullable parameter.
    /// </summary>
    /// <remarks>
    /// Answers over the same representation pre-pass <see cref="MapType(DamlType)"/> runs, so an
    /// Optional the pre-pass moves off C# nullable syntax and onto the wrapper is placed as the
    /// non-nullable reference type it becomes rather than as the nullable one it would have been.
    /// </remarks>
    public bool MapsToReferenceType(DamlType type) =>
        MapsToRewrittenReferenceType(OptionalRepresentation.Rewrite(type, context.Package, resolver));

    private bool MapsToRewrittenReferenceType(DamlType type) => type switch
    {
        DamlPrimitiveType { Primitive: DamlPrimitive.Text } => true,
        DamlPrimitiveType => false,
        DamlWrappedOptional => true,
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Numeric or DamlPrimitive.Optional } } => false,
        DamlTypeApp { Base: DamlPrimitiveType } => true,
        DamlTypeApp { Base: DamlTypeRef typeRef } => !IsEnumTypeRef(typeRef),
        DamlTypeRef typeRef => !IsEnumTypeRef(typeRef),
        _ => false,
    };

    /// <summary>Produces the expression that deserializes <paramref name="valueName"/> back into <paramref name="type"/>.</summary>
    /// <param name="type">The Daml type to reconstruct.</param>
    /// <param name="valueName">The C# expression referencing the Daml value.</param>
    /// <param name="typeVarDelegates">
    /// Maps a Daml type-variable name to the injected converter-delegate parameter name
    /// in scope, supplied when emitting a generic record or variant's own body so a
    /// <see cref="DamlTypeVar"/> field resolves to its converter instead of the runtime
    /// stub. <c>null</c> outside a generic body.
    /// </param>
    public string FromValue(DamlType type, string valueName, IReadOnlyDictionary<string, string>? typeVarDelegates = null) =>
        FromValue(OptionalRepresentation.Rewrite(type, context.Package, resolver), valueName, typeVarDelegates, depth: 0);

    private string FromValue(DamlType type, string valueName, IReadOnlyDictionary<string, string>? typeVarDelegates, int depth)
    {
        ThrowIfTooDeep(depth, nameof(FromValue));

        return type switch
    {
        DamlPrimitiveType primitive => GetBarePrimitiveFromValueConversion(primitive.Primitive, valueName),
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Numeric } } =>
            $"{valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlNumeric)}>().Value",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional }, Arguments: [var arg] } =>
            $"{valueName}.AsOptional().HasValue ? {FromValue(arg, $"{valueName}.AsOptional().Value!", typeVarDelegates, depth + 1)} : null",
        DamlWrappedOptional wrapped =>
            $"{context.Qualifier.Qualify(RuntimeTypeNames.Optional)}<{MapType(wrapped.Argument, depth + 1)}>.{WrappedOptionalDeserializer(wrapped.Encoding)}({valueName}, __optional{depth} => {FromValue(wrapped.Argument, $"__optional{depth}", typeVarDelegates, depth + 1)})",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.List }, Arguments: [var arg] } =>
            $"({context.Qualifier.Qualify("IReadOnlyList")}<{MapType(arg, depth + 1)}>){valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlList)}>().Values.Select(x => {FromValue(arg, "x", typeVarDelegates, depth + 1)}).ToList()",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.TextMap }, Arguments: [var arg] } =>
            $"({context.Qualifier.Qualify("IReadOnlyDictionary")}<string, {MapType(arg, depth + 1)}>){valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlTextMap)}>().Values.ToDictionary(kv => kv.Key, kv => {FromValue(arg, "kv.Value", typeVarDelegates, depth + 1)})",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.GenMap }, Arguments: [var keyArg, var valueArg] } =>
            $"({context.Qualifier.Qualify("IReadOnlyDictionary")}<{MapType(keyArg, depth + 1)}, {MapType(valueArg, depth + 1)}>){valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlGenMap)}>().Entries.ToDictionary(kv => {FromValue(keyArg, "kv.Key", typeVarDelegates, depth + 1)}, kv => {FromValue(valueArg, "kv.Value", typeVarDelegates, depth + 1)})",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.ContractId }, Arguments: [var arg] } =>
            $"new {context.Qualifier.Qualify(RuntimeTypeNames.ContractId)}<{MapType(arg, depth + 1)}>({valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlContractId)}>().Value)",
        DamlTypeApp { Base: DamlTypeRef typeRef } app
            when StdlibPackages.IsStdlibTypeRef(resolver, typeRef, parametric: true) =>
            EmitParametricStdlibFromValue(typeRef, app.Arguments, valueName, typeVarDelegates, depth),
        DamlTypeRef typeRef when IsEnumTypeRef(typeRef) =>
            QualifiedEnumExtensionsCall(typeRef, "FromDamlEnum", $"{valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlEnum)}>()"),
        DamlTypeRef typeRef when IsVariantTypeRef(typeRef) =>
            $"{resolver.Resolve(typeRef, context)}.FromVariant({valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlVariant)}>())",
        DamlTypeRef typeRef => $"{resolver.Resolve(typeRef, context)}.FromRecord({valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlRecord)}>())",
        DamlTypeApp { Base: DamlTypeRef typeRef } app when IsVariantTypeRef(typeRef) =>
            $"{resolver.Resolve(typeRef, context)}<{string.Join(", ", app.Arguments.Select(arg => MapType(arg, depth + 1)))}>.FromVariant({valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlVariant)}>(), {string.Join(", ", FromValueConverterLambdas(app.Arguments, typeVarDelegates, depth))})",
        DamlTypeApp { Base: DamlTypeRef typeRef } app when IsRecordTypeRef(typeRef) =>
            $"{resolver.Resolve(typeRef, context)}<{string.Join(", ", app.Arguments.Select(arg => MapType(arg, depth + 1)))}>.FromRecord({valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlRecord)}>(), {string.Join(", ", FromValueConverterLambdas(app.Arguments, typeVarDelegates, depth))})",
        DamlTypeVar typeVar when TryResolveDelegate(typeVarDelegates, typeVar, out var convert) =>
            $"{convert}({valueName})",
        DamlTypeVar typeVar => $"{context.Qualifier.Qualify(RuntimeTypeNames.GenericStub)}.NotImplemented<{EmitterHelpers.TypeParameterName(typeVar.Name)}>(\"{typeVar.Name}\")",
        _ when MapsToFallbackObject(type, depth) => FallbackFromValueStub(valueName),
        _ => throw new CodegenException(
            $"Cannot emit a deserialization expression for Daml type '{type}'. "
            + "The C# code generator does not support this type shape, so generation fails "
            + "here instead of emitting a silent 'default!' fallback into generated code.")
    };
    }

    private static bool TryResolveDelegate(
        IReadOnlyDictionary<string, string>? typeVarDelegates,
        DamlTypeVar typeVar,
        out string convert)
    {
        if (typeVarDelegates is not null && typeVarDelegates.TryGetValue(typeVar.Name, out var resolved))
        {
            convert = resolved;
            return true;
        }

        convert = string.Empty;
        return false;
    }

    /// <remarks>
    /// A switch, not a ternary, for the reason given on <see cref="MapBarePrimitiveToCSharp"/>: a
    /// ternary would emit a newly added <see cref="OptionalEncoding"/> in the wrong wire form, in
    /// code that still compiles. Only CS8524 is suppressed.
    /// </remarks>
#pragma warning disable CS8524
    private static string WrappedOptionalSerializer(OptionalEncoding encoding) => encoding switch
    {
        OptionalEncoding.Flat => "ToValue",
        OptionalEncoding.NestedChain => "ToChainValue",
    };
#pragma warning restore CS8524

    /// <remarks>Suppresses CS8524 for the reason given on <see cref="WrappedOptionalSerializer"/>.</remarks>
#pragma warning disable CS8524
    private static string WrappedOptionalDeserializer(OptionalEncoding encoding) => encoding switch
    {
        OptionalEncoding.Flat => "FromValue",
        OptionalEncoding.NestedChain => "FromChainValue",
    };
#pragma warning restore CS8524

    /// <remarks>
    /// Only CS8524 is suppressed, never CS8509. The switch has no default arm and covers every
    /// named <see cref="DamlPrimitive"/>, so the sole uncovered input is an out-of-range cast.
    /// CS8509 — a newly added named member left unhandled — stays an error, because that warning
    /// is the compiler-enforced checklist for adding a Daml primitive.
    /// </remarks>
#pragma warning disable CS8524
    private string MapBarePrimitiveToCSharp(DamlPrimitive primitive) => primitive switch
    {
        DamlPrimitive.Unit => context.Qualifier.Qualify(RuntimeTypeNames.DamlUnit),
        DamlPrimitive.Bool => "bool",
        DamlPrimitive.Int64 => "long",
        DamlPrimitive.Numeric => "decimal",
        DamlPrimitive.Text => "string",
        DamlPrimitive.Date => "DateOnly",
        DamlPrimitive.Timestamp => "DateTimeOffset",
        DamlPrimitive.Party => context.Qualifier.Qualify(RuntimeTypeNames.Party),
        DamlPrimitive.ContractId
            or DamlPrimitive.List
            or DamlPrimitive.Optional
            or DamlPrimitive.TextMap
            or DamlPrimitive.GenMap =>
            throw new NotSupportedException(
                $"Daml primitive '{primitive}' is a type constructor and cannot appear bare — it must be applied to argument types (handled by the DamlTypeApp arms of MapType)."),
    };
#pragma warning restore CS8524

    /// <remarks>Suppresses CS8524 for the reason given on <see cref="MapBarePrimitiveToCSharp"/>.</remarks>
#pragma warning disable CS8524
    private string GetBarePrimitiveToValueConversion(DamlPrimitive primitive, string fieldName) => primitive switch
    {
        DamlPrimitive.Unit => $"{context.Qualifier.Qualify(RuntimeTypeNames.DamlUnit)}.Instance",
        DamlPrimitive.Bool => $"new {context.Qualifier.Qualify(RuntimeTypeNames.DamlBool)}({fieldName})",
        DamlPrimitive.Int64 => $"new {context.Qualifier.Qualify(RuntimeTypeNames.DamlInt64)}({fieldName})",
        DamlPrimitive.Numeric => $"new {context.Qualifier.Qualify(RuntimeTypeNames.DamlNumeric)}({fieldName})",
        DamlPrimitive.Text => $"new {context.Qualifier.Qualify(RuntimeTypeNames.DamlText)}({fieldName})",
        DamlPrimitive.Date => $"new {context.Qualifier.Qualify(RuntimeTypeNames.DamlDate)}({fieldName})",
        DamlPrimitive.Timestamp => $"new {context.Qualifier.Qualify(RuntimeTypeNames.DamlTimestamp)}({fieldName})",
        DamlPrimitive.Party => $"{fieldName}.ToDamlValue()",
        DamlPrimitive.ContractId
            or DamlPrimitive.List
            or DamlPrimitive.Optional
            or DamlPrimitive.TextMap
            or DamlPrimitive.GenMap =>
            throw new NotSupportedException(
                $"Daml primitive '{primitive}' is a type constructor and cannot appear bare — it must be applied to argument types (handled by the DamlTypeApp arms of ToValue)."),
    };
#pragma warning restore CS8524

    /// <remarks>Suppresses CS8524 for the reason given on <see cref="MapBarePrimitiveToCSharp"/>.</remarks>
#pragma warning disable CS8524
    private string GetBarePrimitiveFromValueConversion(DamlPrimitive primitive, string valueName) => primitive switch
    {
        DamlPrimitive.Bool => $"{valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlBool)}>().Value",
        DamlPrimitive.Int64 => $"{valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlInt64)}>().Value",
        DamlPrimitive.Numeric => $"{valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlNumeric)}>().Value",
        DamlPrimitive.Text => $"{valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlText)}>().Value",
        DamlPrimitive.Date => $"{valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlDate)}>().Value",
        DamlPrimitive.Timestamp => $"{valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlTimestamp)}>().Value",
        DamlPrimitive.Party => $"{context.Qualifier.Qualify(RuntimeTypeNames.Party)}.FromDamlValue({valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlParty)}>())",
        DamlPrimitive.Unit => $"{valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlUnit)}>()",
        DamlPrimitive.ContractId
            or DamlPrimitive.List
            or DamlPrimitive.Optional
            or DamlPrimitive.TextMap
            or DamlPrimitive.GenMap =>
            throw new NotSupportedException(
                $"Daml primitive '{primitive}' is a type constructor and cannot appear bare — it must be applied to argument types (handled by the DamlTypeApp arms of FromValue)."),
    };
#pragma warning restore CS8524

    private sealed record StdlibConversion(
        Func<string, IReadOnlyList<string>, string> Serialize,
        Func<string, string, string, IReadOnlyList<string>, string> Deserialize);

    private readonly StdlibConversion _recordRoundTrip = new(
        Serialize: (fieldName, lambdas) =>
            $"{fieldName}.ToRecord({string.Join(", ", lambdas)})",
        Deserialize: (valueName, stdlibName, typeArgs, lambdas) =>
            $"{stdlibName}<{typeArgs}>.FromRecord({valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlRecord)}>(), {string.Join(", ", lambdas)})");

    private readonly StdlibConversion _valueRoundTrip = new(
        Serialize: (fieldName, lambdas) =>
            $"{fieldName}.ToValue({string.Join(", ", lambdas)})",
        Deserialize: (valueName, stdlibName, typeArgs, lambdas) =>
            $"{stdlibName}<{typeArgs}>.FromValue({valueName}, {string.Join(", ", lambdas)})");

    private IReadOnlyDictionary<(string Module, string Name), StdlibConversion> BuildStdlibConversions() => new Dictionary<(string, string), StdlibConversion>
    {
        [("DA.Set.Types", "Set")] = _recordRoundTrip,
        [("DA.NonEmpty.Types", "NonEmpty")] = _recordRoundTrip,
        [("DA.Types", "Either")] = _valueRoundTrip,
        [("DA.Types", "Tuple2")] = _recordRoundTrip,
        [("DA.Types", "Tuple3")] = _recordRoundTrip,
        [("DA.Map.Types", "Map")] = _recordRoundTrip,
        [("DA.Internal.Map", "Map")] = _recordRoundTrip,
    };

    private IReadOnlyDictionary<(string Module, string Name), StdlibConversion>? _stdlibConversions;
    private IReadOnlyDictionary<(string Module, string Name), StdlibConversion> StdlibConversions =>
        _stdlibConversions ??= BuildStdlibConversions();

    internal IReadOnlySet<(string Module, string Name)> StdlibConversionKeys =>
        StdlibConversions.Keys.ToHashSet();

    private string EmitParametricStdlibToValue(DamlTypeRef typeRef, IReadOnlyList<DamlType> arguments, string fieldName, IReadOnlyDictionary<string, string>? typeVarDelegates, int depth) =>
        ConversionFor(typeRef).Serialize(fieldName, ToValueConverterLambdas(arguments, typeVarDelegates, depth));

    private string EmitParametricStdlibFromValue(DamlTypeRef typeRef, IReadOnlyList<DamlType> arguments, string valueName, IReadOnlyDictionary<string, string>? typeVarDelegates, int depth)
    {
        var stdlibName = context.Qualifier.Qualify(
            StdlibPackages.MapStdlibType(typeRef.Module, typeRef.Name)
                ?? throw new InvalidOperationException($"No stdlib mapping for {typeRef.Module}:{typeRef.Name}"));
        var typeArgs = string.Join(", ", arguments.Select(arg => MapType(arg, depth + 1)));
        return ConversionFor(typeRef).Deserialize(valueName, stdlibName, typeArgs, FromValueConverterLambdas(arguments, typeVarDelegates, depth));
    }

    private IReadOnlyList<string> ToValueConverterLambdas(IReadOnlyList<DamlType> arguments, IReadOnlyDictionary<string, string>? typeVarDelegates, int depth) =>
        arguments.Select((arg, i) =>
            $"__t{i} => ({context.Qualifier.Qualify(RuntimeTypeNames.DamlValue)})({ToValue(arg, $"__t{i}", typeVarDelegates, depth + 1)})").ToList();

    private IReadOnlyList<string> FromValueConverterLambdas(IReadOnlyList<DamlType> arguments, IReadOnlyDictionary<string, string>? typeVarDelegates, int depth) =>
        arguments.Select((arg, i) =>
            $"__v{i} => {FromValue(arg, $"__v{i}", typeVarDelegates, depth + 1)}").ToList();

    private StdlibConversion ConversionFor(DamlTypeRef typeRef) =>
        StdlibConversions.TryGetValue((typeRef.Module, typeRef.Name), out var conversion)
            ? conversion
            : throw new InvalidOperationException($"No stdlib conversion for {typeRef.Module}:{typeRef.Name}");

    private bool IsLocalEnumTypeRef(DamlTypeRef typeRef) =>
        context.IsLocalRef(typeRef)
        && context.LocalEnumQualifiedNames.Contains($"{typeRef.Module}:{typeRef.Name}");

    private bool IsCrossPackageEnumTypeRef(DamlTypeRef typeRef) =>
        IsCrossPackageTypeRef(typeRef, static def => def is DamlEnumDefinition);

    private bool IsEnumTypeRef(DamlTypeRef typeRef) =>
        IsLocalEnumTypeRef(typeRef) || IsCrossPackageEnumTypeRef(typeRef);

    private bool IsLocalVariantTypeRef(DamlTypeRef typeRef) =>
        context.IsLocalRef(typeRef)
        && context.LocalVariantQualifiedNames.Contains($"{typeRef.Module}:{typeRef.Name}");

    private bool IsCrossPackageVariantTypeRef(DamlTypeRef typeRef) =>
        IsCrossPackageTypeRef(typeRef, static def => def is DamlVariantDefinition);

    private bool IsVariantTypeRef(DamlTypeRef typeRef) =>
        IsLocalVariantTypeRef(typeRef) || IsCrossPackageVariantTypeRef(typeRef);

    private bool IsLocalRecordTypeRef(DamlTypeRef typeRef) =>
        context.IsLocalRef(typeRef)
        && context.DataTypes.TryGetValue($"{typeRef.Module}:{typeRef.Name}", out var dataType)
        && dataType.Definition is DamlRecordDefinition;

    private bool IsCrossPackageRecordTypeRef(DamlTypeRef typeRef) =>
        IsCrossPackageTypeRef(typeRef, static def => def is DamlRecordDefinition);

    private bool IsRecordTypeRef(DamlTypeRef typeRef) =>
        IsLocalRecordTypeRef(typeRef) || IsCrossPackageRecordTypeRef(typeRef);

    private bool IsCrossPackageTypeRef(DamlTypeRef typeRef, Func<DamlDataTypeDefinition, bool> matchesDefinition) =>
        !context.IsLocalRef(typeRef)
        && resolver.DataTypeDefinitions(typeRef.PackageId)[(typeRef.Module, typeRef.Name)].Any(matchesDefinition);

    private string QualifiedEnumExtensionsCall(DamlTypeRef typeRef, string method, string argument) =>
        $"{resolver.Resolve(typeRef, context)}Extensions.{method}({argument})";

    /// <summary>
    /// Extension-method syntax binds only while the enum's <c>…Extensions</c> class is in
    /// scope, which holds for an enum declared in the emitting module's namespace. An enum the
    /// resolver spells with a qualifier — another module of this package, or another package —
    /// is converted through the qualified static call, since its extensions class lives in a
    /// namespace the emitted file does not import.
    /// </summary>
    private string EnumToDamlEnumCall(DamlTypeRef typeRef, string fieldName)
    {
        var resolved = resolver.Resolve(typeRef, context);
        return resolved.Contains('.', StringComparison.Ordinal)
            ? $"{resolved}Extensions.ToDamlEnum({fieldName})"
            : $"{fieldName}.ToDamlEnum()";
    }

    private static void ThrowIfTooDeep(int depth, string operation)
    {
        if (depth > MaxTypeDepth)
        {
            throw new InvalidDataException(
                $"{operation} exceeded the maximum Daml type depth of {MaxTypeDepth}. The type is too deeply nested to emit safely.");
        }
    }
}
