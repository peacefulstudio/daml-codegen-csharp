// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;
using RuntimeNamespaces = Daml.Runtime.RuntimeNamespaces;

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
    /// <param name="nestedArgTypeNames">
    /// The names <see cref="ChoiceEmitter.GetNestedChoiceArgumentTypeNames"/> resolved for the
    /// enclosing template's choices, so a same-package choice-argument record nested inside the
    /// template partial that happens to be named <c>DamlRecord</c> gets root-qualified in every
    /// runtime <c>DamlRecord</c> cast this method emits, instead of shadowing the runtime
    /// <see cref="Daml.Runtime.Data.DamlRecord"/>. <c>null</c> qualifies through the ordinary
    /// <see cref="TypeReferenceQualifier"/>.
    /// </param>
    public string FromValue(
        DamlType type,
        string valueName,
        IReadOnlyDictionary<string, string>? typeVarDelegates = null,
        IReadOnlySet<string>? nestedArgTypeNames = null) =>
        FromValue(OptionalRepresentation.Rewrite(type, context.Package, resolver), valueName, typeVarDelegates, nestedArgTypeNames, depth: 0);

    private string FromValue(
        DamlType type,
        string valueName,
        IReadOnlyDictionary<string, string>? typeVarDelegates,
        IReadOnlySet<string>? nestedArgTypeNames,
        int depth)
    {
        ThrowIfTooDeep(depth, nameof(FromValue));

        return type switch
    {
        DamlPrimitiveType primitive => GetBarePrimitiveFromValueConversion(primitive.Primitive, valueName),
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Numeric } } =>
            $"{valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlNumeric)}>().Value",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional }, Arguments: [var arg] } =>
            $"{valueName}.AsOptional().HasValue ? {FromValue(arg, $"{valueName}.AsOptional().Value!", typeVarDelegates, nestedArgTypeNames, depth + 1)} : null",
        DamlWrappedOptional wrapped =>
            $"{context.Qualifier.Qualify(RuntimeTypeNames.Optional)}<{MapType(wrapped.Argument, depth + 1)}>.{WrappedOptionalDeserializer(wrapped.Encoding)}({valueName}, __optional{depth} => {FromValue(wrapped.Argument, $"__optional{depth}", typeVarDelegates, nestedArgTypeNames, depth + 1)})",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.List }, Arguments: [var arg] } =>
            $"({context.Qualifier.Qualify("IReadOnlyList")}<{MapType(arg, depth + 1)}>){valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlList)}>().Values.Select(x => {FromValue(arg, "x", typeVarDelegates, nestedArgTypeNames, depth + 1)}).ToList()",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.TextMap }, Arguments: [var arg] } =>
            $"({context.Qualifier.Qualify("IReadOnlyDictionary")}<string, {MapType(arg, depth + 1)}>){valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlTextMap)}>().Values.ToDictionary(kv => kv.Key, kv => {FromValue(arg, "kv.Value", typeVarDelegates, nestedArgTypeNames, depth + 1)})",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.GenMap }, Arguments: [var keyArg, var valueArg] } =>
            $"({context.Qualifier.Qualify("IReadOnlyDictionary")}<{MapType(keyArg, depth + 1)}, {MapType(valueArg, depth + 1)}>){valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlGenMap)}>().Entries.ToDictionary(kv => {FromValue(keyArg, "kv.Key", typeVarDelegates, nestedArgTypeNames, depth + 1)}, kv => {FromValue(valueArg, "kv.Value", typeVarDelegates, nestedArgTypeNames, depth + 1)})",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.ContractId }, Arguments: [var arg] } =>
            $"new {context.Qualifier.Qualify(RuntimeTypeNames.ContractId)}<{MapType(arg, depth + 1)}>({valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlContractId)}>().Value)",
        DamlTypeApp { Base: DamlTypeRef typeRef } app
            when StdlibPackages.IsStdlibTypeRef(resolver, typeRef, parametric: true) =>
            EmitParametricStdlibFromValue(typeRef, app.Arguments, valueName, typeVarDelegates, nestedArgTypeNames, depth),
        DamlTypeRef typeRef when IsEnumTypeRef(typeRef) =>
            QualifiedEnumExtensionsCall(typeRef, "FromDamlEnum", $"{valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlEnum)}>()"),
        DamlTypeRef typeRef when IsVariantTypeRef(typeRef) =>
            $"{resolver.Resolve(typeRef, context)}.FromVariant({valueName}.As<{DamlVariantReference(nestedArgTypeNames)}>())",
        DamlTypeRef typeRef => $"{resolver.Resolve(typeRef, context)}.FromRecord({valueName}.As<{DamlRecordReference(nestedArgTypeNames)}>())",
        DamlTypeApp { Base: DamlTypeRef typeRef } app when IsVariantTypeRef(typeRef) =>
            $"{resolver.Resolve(typeRef, context)}<{string.Join(", ", app.Arguments.Select(arg => MapType(arg, depth + 1)))}>.FromVariant({valueName}.As<{DamlVariantReference(nestedArgTypeNames)}>(), {string.Join(", ", FromValueConverterLambdas(app.Arguments, typeVarDelegates, nestedArgTypeNames, depth))})",
        DamlTypeApp { Base: DamlTypeRef typeRef } app when IsRecordTypeRef(typeRef) =>
            $"{resolver.Resolve(typeRef, context)}<{string.Join(", ", app.Arguments.Select(arg => MapType(arg, depth + 1)))}>.FromRecord({valueName}.As<{DamlRecordReference(nestedArgTypeNames)}>(), {string.Join(", ", FromValueConverterLambdas(app.Arguments, typeVarDelegates, nestedArgTypeNames, depth))})",
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

    private string DamlRecordReference(IReadOnlySet<string>? nestedArgTypeNames) =>
        nestedArgTypeNames?.Contains(RuntimeTypeNames.DamlRecord) == true
            ? Identifiers.GlobalQualified(RuntimeNamespaces.Data, RuntimeTypeNames.DamlRecord)
            : context.Qualifier.Qualify(RuntimeTypeNames.DamlRecord);

    private string DamlFieldReference(IReadOnlySet<string>? nestedArgTypeNames) =>
        nestedArgTypeNames?.Contains(RuntimeTypeNames.DamlField) == true
            ? Identifiers.GlobalQualified(RuntimeNamespaces.Data, RuntimeTypeNames.DamlField)
            : context.Qualifier.Qualify(RuntimeTypeNames.DamlField);

    private string DamlVariantReference(IReadOnlySet<string>? nestedArgTypeNames) =>
        nestedArgTypeNames?.Contains(RuntimeTypeNames.DamlVariant) == true
            ? Identifiers.GlobalQualified(RuntimeNamespaces.Data, RuntimeTypeNames.DamlVariant)
            : context.Qualifier.Qualify(RuntimeTypeNames.DamlVariant);

    /// <summary>
    /// Fully qualified name of the runtime's <c>DamlLfJsonDecoders</c> class, spelled as a literal
    /// rather than routed through <see cref="TypeReferenceQualifier"/> — the same choice
    /// <c>ContractIdJsonConverterFactory</c> makes in <c>TemplateEmitter</c>, since this type is
    /// never an emitted member type that the shared qualifier infrastructure needs to know about.
    /// Internal, not private: <c>ChoiceEmitter</c> and <c>TemplateEmitter</c> compose their own
    /// hand-written <c>DamlLfJsonDecoders</c> calls (for a choice argument's nested record type and
    /// a template's key type respectively) alongside calls into
    /// <see cref="FromJson(DamlType, string, string, IReadOnlySet{string}, IReadOnlyDictionary{string, string})"/>,
    /// and reuse this constant rather than respelling the same literal at each of those call sites.
    /// </summary>
    internal const string DamlLfJsonDecodersQualifiedName = "global::Daml.Runtime.Serialization.DamlLfJsonDecoders";

    /// <summary>
    /// Fully qualified name of the runtime's <c>DamlLfElementReader</c> delegate, spelled as a
    /// literal for the same reason as <see cref="DamlLfJsonDecodersQualifiedName"/>.
    /// </summary>
    internal const string DamlLfElementReaderQualifiedName = "global::Daml.Runtime.Serialization.DamlLfElementReader";

    /// <summary>
    /// Fully qualified name of the runtime's <c>DamlLfJsonDecodeContext</c> struct, spelled as a
    /// literal for the same reason as <see cref="DamlLfJsonDecodersQualifiedName"/>.
    /// </summary>
    internal const string DamlLfJsonDecodeContextQualifiedName = "global::Daml.Runtime.Serialization.DamlLfJsonDecodeContext";

    /// <summary>
    /// Produces the expression that decodes Daml-LF JSON at <paramref name="jsonName"/> into a
    /// <c>DamlValue</c> for <paramref name="type"/>, composing calls into the runtime's
    /// <c>DamlLfJsonDecoders</c> and, for a record or variant's own type, into its emitted
    /// <c>__ReadDamlLfJson</c> capability directly — never through the constrained
    /// <c>DamlLfJsonDecoders.ReadRecord&lt;T&gt;</c>/<c>ReadVariant&lt;T&gt;</c> forwarders, which
    /// cost an extra hop generated code has no reason to pay.
    /// </summary>
    /// <param name="type">The Daml type of the value being decoded.</param>
    /// <param name="jsonName">The C# expression referencing the <c>System.Text.Json.JsonElement</c>.</param>
    /// <param name="contextName">The C# expression referencing the in-scope <c>DamlLfJsonDecodeContext</c>.</param>
    /// <param name="nestedArgTypeNames">
    /// The names <see cref="ChoiceEmitter.GetNestedChoiceArgumentTypeNames"/> resolved for the
    /// enclosing template's choices, so a same-package choice-argument record nested inside the
    /// template partial that happens to be named <c>DamlRecord</c> or <c>DamlField</c> gets
    /// root-qualified wherever this method still composes runtime <c>DamlRecord</c>/<c>DamlField</c>
    /// surface, instead of shadowing the runtime <see cref="Daml.Runtime.Data.DamlRecord"/> or
    /// <see cref="Daml.Runtime.Data.DamlField"/> respectively; <c>null</c> qualifies through the
    /// ordinary <see cref="TypeReferenceQualifier"/>.
    /// </param>
    /// <param name="typeVarReaders">
    /// Maps a Daml type-variable name to the injected <c>DamlLfElementReader</c> parameter name in
    /// scope, supplied when emitting a generic record or variant's own <c>__ReadDamlLfJson</c> so a
    /// <see cref="DamlTypeVar"/> field resolves to its injected reader instead of the lazy
    /// unsupported-type leaf. <c>null</c> outside a generic body.
    /// </param>
    /// <remarks>
    /// The result is a <c>DamlValue</c>-producing expression, not a fully deserialized
    /// CLR value — a caller composes it with
    /// <see cref="FromValue(DamlType, string, IReadOnlyDictionary{string, string}, IReadOnlySet{string})"/>
    /// to reach the CLR type, exactly as the generated two-phase <c>XxxJsonReader</c> lambdas do:
    /// decode JSON to a <c>DamlValue</c> in a local, then reuse the existing, unmodified
    /// <c>FromValue</c>-generated expression on that local. A generated decoder cannot instead call
    /// its own sibling <c>XxxDecoder</c> property from within the same object initializer — object
    /// initializers bring no implicit <c>this</c> into scope, so the sibling member name would be
    /// unresolved at that point — and substituting this method's own result directly into
    /// <c>FromValue</c>'s <c>valueName</c> slot would double-evaluate the JSON decode, since the
    /// flat-<c>Optional</c> arm of <c>FromValue</c> uses <c>valueName</c> twice.
    /// </remarks>
    public string FromJson(
        DamlType type,
        string jsonName,
        string contextName,
        IReadOnlySet<string>? nestedArgTypeNames = null,
        IReadOnlyDictionary<string, string>? typeVarReaders = null) =>
        FromJson(OptionalRepresentation.Rewrite(type, context.Package, resolver), jsonName, contextName, nestedArgTypeNames, typeVarReaders, depth: 0);

    private string FromJson(
        DamlType type,
        string jsonName,
        string contextName,
        IReadOnlySet<string>? nestedArgTypeNames,
        IReadOnlyDictionary<string, string>? typeVarReaders,
        int depth)
    {
        ThrowIfTooDeep(depth, nameof(FromJson));

        return type switch
    {
        DamlPrimitiveType primitive => GetBarePrimitiveFromJsonConversion(primitive.Primitive, jsonName, contextName),
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Numeric } } =>
            $"{DamlLfJsonDecodersQualifiedName}.ReadNumeric({jsonName}, {contextName})",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.ContractId } } =>
            $"{DamlLfJsonDecodersQualifiedName}.ReadContractId({jsonName}, {contextName})",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional } } app =>
            $"{DamlLfJsonDecodersQualifiedName}.ReadOptional({jsonName}, {contextName}, {FromJsonElementReader(app.Arguments[0], nestedArgTypeNames, typeVarReaders, depth)})",
        DamlWrappedOptional wrapped =>
            $"{DamlLfJsonDecodersQualifiedName}.{WrappedOptionalJsonReader(wrapped.Encoding)}({jsonName}, {contextName}, {FromJsonElementReader(wrapped.Argument, nestedArgTypeNames, typeVarReaders, depth)})",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.List } } app =>
            $"{DamlLfJsonDecodersQualifiedName}.ReadList({jsonName}, {contextName}, {FromJsonElementReader(app.Arguments[0], nestedArgTypeNames, typeVarReaders, depth)})",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.TextMap } } app =>
            $"{DamlLfJsonDecodersQualifiedName}.ReadTextMap({jsonName}, {contextName}, {FromJsonElementReader(app.Arguments[0], nestedArgTypeNames, typeVarReaders, depth)})",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.GenMap } } app =>
            $"{DamlLfJsonDecodersQualifiedName}.ReadGenMap({jsonName}, {contextName}, {FromJsonElementReader(app.Arguments[0], nestedArgTypeNames, typeVarReaders, depth)}, {FromJsonElementReader(app.Arguments[1], nestedArgTypeNames, typeVarReaders, depth)})",
        DamlTypeApp { Base: DamlTypeRef typeRef } app
            when StdlibPackages.IsStdlibTypeRef(resolver, typeRef, parametric: true) =>
            EmitParametricStdlibFromJson(typeRef, app.Arguments, jsonName, contextName, nestedArgTypeNames, typeVarReaders, depth),
        DamlTypeRef typeRef when IsEnumTypeRef(typeRef) =>
            QualifiedEnumExtensionsCall(typeRef, "__ReadDamlLfJson", $"{jsonName}, {contextName}"),
        DamlTypeRef typeRef when IsVariantTypeRef(typeRef) =>
            $"{resolver.Resolve(typeRef, context)}.__ReadDamlLfJson({jsonName}, {contextName})",
        DamlTypeRef typeRef => $"{resolver.Resolve(typeRef, context)}.__ReadDamlLfJson({jsonName}, {contextName})",
        DamlTypeApp { Base: DamlTypeRef typeRef } app when IsVariantTypeRef(typeRef) && app.Arguments.Count > 0 =>
            $"{resolver.Resolve(typeRef, context)}<{string.Join(", ", app.Arguments.Select(arg => MapType(arg, depth + 1)))}>.__ReadDamlLfJson({jsonName}, {contextName}, {string.Join(", ", FromJsonElementReaders(app.Arguments, nestedArgTypeNames, typeVarReaders, depth))})",
        DamlTypeApp { Base: DamlTypeRef typeRef } when IsVariantTypeRef(typeRef) =>
            $"{resolver.Resolve(typeRef, context)}.__ReadDamlLfJson({jsonName}, {contextName})",
        DamlTypeApp { Base: DamlTypeRef typeRef } app when IsRecordTypeRef(typeRef) && app.Arguments.Count > 0 =>
            $"{resolver.Resolve(typeRef, context)}<{string.Join(", ", app.Arguments.Select(arg => MapType(arg, depth + 1)))}>.__ReadDamlLfJson({jsonName}, {contextName}, {string.Join(", ", FromJsonElementReaders(app.Arguments, nestedArgTypeNames, typeVarReaders, depth))})",
        DamlTypeApp { Base: DamlTypeRef typeRef } when IsRecordTypeRef(typeRef) =>
            $"{resolver.Resolve(typeRef, context)}.__ReadDamlLfJson({jsonName}, {contextName})",
        DamlTypeVar typeVar when TryResolveDelegate(typeVarReaders, typeVar, out var read) =>
            $"{read}({jsonName}, {contextName})",
        DamlTypeVar typeVar =>
            $"{DamlLfJsonDecodersQualifiedName}.ReadUnsupported({jsonName}, {contextName}, \"{DescribeUnsupportedDamlType(type)}\")",
        _ when MapsToFallbackObject(type, depth) =>
            $"{DamlLfJsonDecodersQualifiedName}.ReadUnsupported({jsonName}, {contextName}, \"{DescribeUnsupportedDamlType(type)}\")",
        _ => throw new CodegenException(
            $"Cannot emit a JSON-decode expression for Daml type '{type}'. "
            + "The C# code generator does not support this type shape, so generation fails "
            + "here instead of emitting a silent 'default!' fallback into generated code.")
    };
    }

    /// <summary>
    /// Renders <paramref name="type"/> in its Daml wire spelling for
    /// <see cref="Daml.Runtime.Serialization.DamlLfJsonDecoders.ReadUnsupported"/>'s error message —
    /// <c>Module.Name</c> for a user-defined type reference (bare or applied), or the bare type
    /// variable name for an open type variable that escaped its generic body.
    /// </summary>
    private static string DescribeUnsupportedDamlType(DamlType type) => type switch
    {
        DamlTypeRef typeRef => $"{typeRef.Module}.{typeRef.Name}",
        DamlTypeApp { Base: DamlTypeRef typeRef } => $"{typeRef.Module}.{typeRef.Name}",
        DamlTypeVar typeVar => typeVar.Name,
        _ => type.ToString() ?? "?",
    };

    /// <summary>
    /// Builds a <c>DamlLfJsonDecoders</c> element-reader lambda decoding one composite argument of
    /// <paramref name="argument"/>'s type, for use as a <c>DamlLfElementReader</c> delegate
    /// argument to a composite reader such as <c>ReadList</c> or <c>ReadGenMap</c>.
    /// </summary>
    /// <remarks>
    /// Parameter names are suffixed by <paramref name="depth"/> rather than by argument index: two
    /// readers passed as sibling arguments to the same call (for example <c>ReadGenMap</c>'s key
    /// and value readers) can safely share the same depth, since each lambda body is an
    /// independently scoped expression and the two never see each other's parameters.
    /// </remarks>
    private string FromJsonElementReader(DamlType argument, IReadOnlySet<string>? nestedArgTypeNames, IReadOnlyDictionary<string, string>? typeVarReaders, int depth) =>
        $"(__json{depth}, __ctx{depth}) => {FromJson(argument, $"__json{depth}", $"__ctx{depth}", nestedArgTypeNames, typeVarReaders, depth + 1)}";

    private IReadOnlyList<string> FromJsonElementReaders(IReadOnlyList<DamlType> arguments, IReadOnlySet<string>? nestedArgTypeNames, IReadOnlyDictionary<string, string>? typeVarReaders, int depth) =>
        arguments.Select(arg => FromJsonElementReader(arg, nestedArgTypeNames, typeVarReaders, depth)).ToList();

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

    /// <remarks>Suppresses CS8524 for the reason given on <see cref="WrappedOptionalSerializer"/>.</remarks>
#pragma warning disable CS8524
    private static string WrappedOptionalJsonReader(OptionalEncoding encoding) => encoding switch
    {
        OptionalEncoding.Flat => "ReadOptional",
        OptionalEncoding.NestedChain => "ReadOptionalChain",
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

    /// <remarks>Suppresses CS8524 for the reason given on <see cref="MapBarePrimitiveToCSharp"/>.</remarks>
#pragma warning disable CS8524
    private static string GetBarePrimitiveFromJsonConversion(DamlPrimitive primitive, string jsonName, string contextName) => primitive switch
    {
        DamlPrimitive.Unit => $"{DamlLfJsonDecodersQualifiedName}.ReadUnit({jsonName}, {contextName})",
        DamlPrimitive.Bool => $"{DamlLfJsonDecodersQualifiedName}.ReadBool({jsonName}, {contextName})",
        DamlPrimitive.Int64 => $"{DamlLfJsonDecodersQualifiedName}.ReadInt64({jsonName}, {contextName})",
        DamlPrimitive.Numeric => $"{DamlLfJsonDecodersQualifiedName}.ReadNumeric({jsonName}, {contextName})",
        DamlPrimitive.Text => $"{DamlLfJsonDecodersQualifiedName}.ReadText({jsonName}, {contextName})",
        DamlPrimitive.Date => $"{DamlLfJsonDecodersQualifiedName}.ReadDate({jsonName}, {contextName})",
        DamlPrimitive.Timestamp => $"{DamlLfJsonDecodersQualifiedName}.ReadTimestamp({jsonName}, {contextName})",
        DamlPrimitive.Party => $"{DamlLfJsonDecodersQualifiedName}.ReadParty({jsonName}, {contextName})",
        DamlPrimitive.ContractId
            or DamlPrimitive.List
            or DamlPrimitive.Optional
            or DamlPrimitive.TextMap
            or DamlPrimitive.GenMap =>
            throw new NotSupportedException(
                $"Daml primitive '{primitive}' is a type constructor and cannot appear bare — it must be applied to argument types (handled by the DamlTypeApp arms of FromJson)."),
    };
#pragma warning restore CS8524

    private sealed record StdlibConversion(
        Func<string, IReadOnlyList<string>, string> Serialize,
        Func<string, string, string, IReadOnlyList<string>, IReadOnlySet<string>?, string> Deserialize);

    private readonly StdlibConversion _recordRoundTrip = new(
        Serialize: (fieldName, lambdas) =>
            $"{fieldName}.ToRecord({string.Join(", ", lambdas)})",
        Deserialize: (valueName, stdlibName, typeArgs, lambdas, nestedArgTypeNames) =>
        {
            var damlRecordRef = nestedArgTypeNames?.Contains(RuntimeTypeNames.DamlRecord) == true
                ? Identifiers.GlobalQualified(RuntimeNamespaces.Data, RuntimeTypeNames.DamlRecord)
                : context.Qualifier.Qualify(RuntimeTypeNames.DamlRecord);
            return $"{stdlibName}<{typeArgs}>.FromRecord({valueName}.As<{damlRecordRef}>(), {string.Join(", ", lambdas)})";
        });

    private readonly StdlibConversion _valueRoundTrip = new(
        Serialize: (fieldName, lambdas) =>
            $"{fieldName}.ToValue({string.Join(", ", lambdas)})",
        Deserialize: (valueName, stdlibName, typeArgs, lambdas, nestedArgTypeNames) =>
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

    private string EmitParametricStdlibFromValue(
        DamlTypeRef typeRef,
        IReadOnlyList<DamlType> arguments,
        string valueName,
        IReadOnlyDictionary<string, string>? typeVarDelegates,
        IReadOnlySet<string>? nestedArgTypeNames,
        int depth)
    {
        var stdlibName = context.Qualifier.Qualify(
            StdlibPackages.MapStdlibType(typeRef.Module, typeRef.Name)
                ?? throw new InvalidOperationException($"No stdlib mapping for {typeRef.Module}:{typeRef.Name}"));
        var typeArgs = string.Join(", ", arguments.Select(arg => MapType(arg, depth + 1)));
        return ConversionFor(typeRef).Deserialize(
            valueName,
            stdlibName,
            typeArgs,
            FromValueConverterLambdas(arguments, typeVarDelegates, nestedArgTypeNames, depth),
            nestedArgTypeNames);
    }

    /// <summary>
    /// Emits a <c>DamlLfJsonDecoders</c> call for a parametric stdlib type, independent of
    /// <see cref="StdlibConversions"/> — that dictionary's <see cref="StdlibConversion"/> shapes
    /// serve <see cref="ToValue(DamlType, string, IReadOnlyDictionary{string, string})"/> and
    /// <see cref="FromValue(DamlType, string, IReadOnlyDictionary{string, string}, IReadOnlySet{string})"/>'s
    /// CLR-typed conversions and do not fit the JSON reader call signatures this method builds instead.
    /// </summary>
    private string EmitParametricStdlibFromJson(
        DamlTypeRef typeRef,
        IReadOnlyList<DamlType> arguments,
        string jsonName,
        string contextName,
        IReadOnlySet<string>? nestedArgTypeNames,
        IReadOnlyDictionary<string, string>? typeVarReaders,
        int depth)
    {
        var readers = FromJsonElementReaders(arguments, nestedArgTypeNames, typeVarReaders, depth);
        return (typeRef.Module, typeRef.Name) switch
        {
            ("DA.Set.Types", "Set") => $"{DamlLfJsonDecodersQualifiedName}.ReadSet({jsonName}, {contextName}, {readers[0]})",
            ("DA.NonEmpty.Types", "NonEmpty") => $"{DamlLfJsonDecodersQualifiedName}.ReadNonEmpty({jsonName}, {contextName}, {readers[0]})",
            ("DA.Types", "Either") => $"{DamlLfJsonDecodersQualifiedName}.ReadEither({jsonName}, {contextName}, {readers[0]}, {readers[1]})",
            ("DA.Types", "Tuple2") => $"{DamlLfJsonDecodersQualifiedName}.ReadTuple2({jsonName}, {contextName}, {readers[0]}, {readers[1]})",
            ("DA.Types", "Tuple3") => $"{DamlLfJsonDecodersQualifiedName}.ReadTuple3({jsonName}, {contextName}, {readers[0]}, {readers[1]}, {readers[2]})",
            ("DA.Map.Types", "Map") or ("DA.Internal.Map", "Map") =>
                $"{DamlLfJsonDecodersQualifiedName}.ReadStdlibMap({jsonName}, {contextName}, {readers[0]}, {readers[1]})",
            _ => throw new InvalidOperationException($"No JSON stdlib conversion for {typeRef.Module}:{typeRef.Name}"),
        };
    }

    private IReadOnlyList<string> ToValueConverterLambdas(IReadOnlyList<DamlType> arguments, IReadOnlyDictionary<string, string>? typeVarDelegates, int depth) =>
        arguments.Select((arg, i) =>
            $"__t{i} => ({context.Qualifier.Qualify(RuntimeTypeNames.DamlValue)})({ToValue(arg, $"__t{i}", typeVarDelegates, depth + 1)})").ToList();

    private IReadOnlyList<string> FromValueConverterLambdas(
        IReadOnlyList<DamlType> arguments,
        IReadOnlyDictionary<string, string>? typeVarDelegates,
        IReadOnlySet<string>? nestedArgTypeNames,
        int depth) =>
        arguments.Select((arg, i) =>
            $"__v{i} => {FromValue(arg, $"__v{i}", typeVarDelegates, nestedArgTypeNames, depth + 1)}").ToList();

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
