// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.Model;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Turns a <see cref="DamlType"/> into C#: <see cref="MapType"/> produces a C# type
/// name, <see cref="ToValue"/> and <see cref="FromValue"/> produce the serialize and
/// deserialize expressions. Constructed once per package over a
/// <see cref="PackageEmitContext"/> and an <see cref="ICrossPackageResolver"/>, which
/// it calls into for cross-package names — it does not own resolution. Pure functions
/// of their inputs, so unit-testable without a real DAR.
/// </summary>
public sealed class DamlTypeMapper(PackageEmitContext context, ICrossPackageResolver resolver)
{
    /// <summary>Maps <paramref name="type"/> to its C# type name.</summary>
    public string MapType(DamlType type) => type switch
    {
        DamlPrimitiveType primitive => MapBarePrimitiveToCSharp(primitive.Primitive),
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Numeric } } => "decimal",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.ContractId }, Arguments: [var arg] } =>
            $"{context.Qualifier.Qualify(RuntimeTypeNames.ContractId, context.RootNamespace)}<{MapType(arg)}>",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional },
                      Arguments: [DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional } }] } =>
            throw new NotSupportedException("Codegen does not support nested Optional types (Optional (Optional t)). C# nullable syntax cannot represent the Some Nothing / Nothing distinction without a wrapper type. Refactor the Daml signature, or open a feature request to introduce a representable CLR model."),
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional }, Arguments: [var arg] } =>
            $"{MapType(arg)}?",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.List }, Arguments: [var arg] } =>
            $"{context.Qualifier.Qualify("IReadOnlyList", context.RootNamespace)}<{MapType(arg)}>",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.TextMap }, Arguments: [var arg] } =>
            $"{context.Qualifier.Qualify("IReadOnlyDictionary", context.RootNamespace)}<string, {MapType(arg)}>",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.GenMap }, Arguments: [var keyArg, var valueArg] } =>
            $"{context.Qualifier.Qualify("IReadOnlyDictionary", context.RootNamespace)}<{MapType(keyArg)}, {MapType(valueArg)}>",
        DamlTypeApp { Base: DamlTypeRef typeRef } app =>
            app.Arguments.Count > 0
                ? $"{resolver.Resolve(typeRef, context)}<{string.Join(", ", app.Arguments.Select(MapType))}>"
                : resolver.Resolve(typeRef, context),
        DamlTypeRef typeRef => resolver.Resolve(typeRef, context),
        DamlTypeVar typeVar => $"T{ToPascalCase(SanitizeIdentifier(typeVar.Name))}",
        _ => FallbackTypeName
    };

    private const string FallbackTypeName = "object";

    /// <summary>Produces the expression that serializes <paramref name="fieldName"/> of <paramref name="type"/> to a Daml value.</summary>
    public string ToValue(DamlType type, string fieldName) => type switch
    {
        DamlPrimitiveType primitive => GetBarePrimitiveToValueConversion(primitive.Primitive, fieldName),
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Numeric } } =>
            $"new {context.Qualifier.Qualify(RuntimeTypeNames.DamlNumeric, context.RootNamespace)}({fieldName})",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.ContractId } } =>
            $"{fieldName}.ToDamlValue()",
        // The TrimStart('@') here protects against an Optional field whose name is a
        // C# keyword (e.g. `lock`, `class`, `event`). SanitizeIdentifier escapes those
        // by prepending '@', which is valid as a property name but invalid as the
        // local-variable name produced by `is { } __<name>` below. Without the trim,
        // a Daml `Optional <something>` field called `lock` produces `__@lock`, which
        // does not parse. The trim is local-variable-only — the property reference
        // (`{fieldName}`) keeps its `@` escape so the original record property is
        // still addressable.
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional } } app =>
            $"{fieldName} is {{ }} __{fieldName.TrimStart('@')} ? new {context.Qualifier.Qualify(RuntimeTypeNames.DamlOptional, context.RootNamespace)}({ToValue(app.Arguments[0], $"__{fieldName.TrimStart('@')}")}) : {context.Qualifier.Qualify(RuntimeTypeNames.DamlOptional, context.RootNamespace)}.None",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.List } } app =>
            $"new {context.Qualifier.Qualify(RuntimeTypeNames.DamlList, context.RootNamespace)}({fieldName}.Select(x => ({context.Qualifier.Qualify(RuntimeTypeNames.DamlValue, context.RootNamespace)}){ToValue(app.Arguments[0], "x")}).ToList())",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.TextMap } } app =>
            $"new {context.Qualifier.Qualify(RuntimeTypeNames.DamlTextMap, context.RootNamespace)}({fieldName}.ToDictionary(kv => kv.Key, kv => ({context.Qualifier.Qualify(RuntimeTypeNames.DamlValue, context.RootNamespace)}){ToValue(app.Arguments[0], "kv.Value")}))",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.GenMap } } app =>
            $"new {context.Qualifier.Qualify(RuntimeTypeNames.DamlGenMap, context.RootNamespace)}({fieldName}.Select(kv => (({context.Qualifier.Qualify(RuntimeTypeNames.DamlValue, context.RootNamespace)}){ToValue(app.Arguments[0], "kv.Key")}, ({context.Qualifier.Qualify(RuntimeTypeNames.DamlValue, context.RootNamespace)}){ToValue(app.Arguments[1], "kv.Value")})).ToList())",
        DamlTypeApp { Base: DamlTypeRef typeRef } app
            when StdlibPackages.IsStdlibTypeRef(resolver, typeRef, parametric: true) =>
            EmitParametricStdlibToValue(typeRef, app.Arguments, fieldName),
        DamlTypeRef typeRef when IsLocalEnumTypeRef(typeRef) =>
            $"{fieldName}.ToDamlEnum()",
        DamlTypeRef typeRef when IsCrossPackageEnumTypeRef(typeRef) =>
            QualifiedEnumExtensionsCall(typeRef, "ToDamlEnum", fieldName),
        DamlTypeApp { Base: DamlTypeRef typeRef } when IsVariantTypeRef(typeRef) =>
            $"{fieldName}.ToVariant()",
        DamlTypeRef typeRef when IsVariantTypeRef(typeRef) =>
            $"{fieldName}.ToVariant()",
        DamlTypeVar => FallbackToValueStub(fieldName),
        _ when MapsToFallbackObject(type) => FallbackToValueStub(fieldName),
        _ => $"{fieldName}.ToRecord()"
    };

    private string FallbackToValueStub(string fieldName) =>
        $"{context.Qualifier.Qualify(RuntimeTypeNames.GenericStub, context.RootNamespace)}.NotImplemented<{context.Qualifier.Qualify(RuntimeTypeNames.DamlValue, context.RootNamespace)}>(\"{fieldName}\")";

    private bool MapsToFallbackObject(DamlType type) => MapType(type) == FallbackTypeName;

    /// <summary>Produces the expression that deserializes <paramref name="valueName"/> back into <paramref name="type"/>.</summary>
    public string FromValue(DamlType type, string valueName) => type switch
    {
        DamlPrimitiveType primitive => GetBarePrimitiveFromValueConversion(primitive.Primitive, valueName),
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Numeric } } =>
            $"{valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlNumeric, context.RootNamespace)}>().Value",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional }, Arguments: [var arg] } =>
            $"{valueName}.AsOptional().HasValue ? {FromValue(arg, $"{valueName}.AsOptional().Value!")} : null",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.List }, Arguments: [var arg] } =>
            $"({context.Qualifier.Qualify("IReadOnlyList", context.RootNamespace)}<{MapType(arg)}>){valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlList, context.RootNamespace)}>().Values.Select(x => {FromValue(arg, "x")}).ToList()",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.TextMap }, Arguments: [var arg] } =>
            $"{valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlTextMap, context.RootNamespace)}>().Values.ToDictionary(kv => kv.Key, kv => {FromValue(arg, "kv.Value")})",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.GenMap }, Arguments: [var keyArg, var valueArg] } =>
            $"{valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlGenMap, context.RootNamespace)}>().Entries.ToDictionary(kv => {FromValue(keyArg, "kv.Key")}, kv => {FromValue(valueArg, "kv.Value")})",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.ContractId }, Arguments: [var arg] } =>
            $"new {context.Qualifier.Qualify(RuntimeTypeNames.ContractId, context.RootNamespace)}<{MapType(arg)}>({valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlContractId, context.RootNamespace)}>().Value)",
        DamlTypeApp { Base: DamlTypeRef typeRef } app
            when StdlibPackages.IsStdlibTypeRef(resolver, typeRef, parametric: true) =>
            EmitParametricStdlibFromValue(typeRef, app.Arguments, valueName),
        DamlTypeRef typeRef when IsEnumTypeRef(typeRef) =>
            QualifiedEnumExtensionsCall(typeRef, "FromDamlEnum", $"{valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlEnum, context.RootNamespace)}>()"),
        DamlTypeRef typeRef when IsVariantTypeRef(typeRef) =>
            $"{resolver.Resolve(typeRef, context)}.FromVariant({valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlVariant, context.RootNamespace)}>())",
        DamlTypeRef typeRef => $"{resolver.Resolve(typeRef, context)}.FromRecord({valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlRecord, context.RootNamespace)}>())",
        DamlTypeApp { Base: DamlTypeRef typeRef } app when IsVariantTypeRef(typeRef) =>
            $"{resolver.Resolve(typeRef, context)}<{string.Join(", ", app.Arguments.Select(MapType))}>.FromVariant({valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlVariant, context.RootNamespace)}>())",
        DamlTypeVar typeVar => $"{context.Qualifier.Qualify(RuntimeTypeNames.GenericStub, context.RootNamespace)}.NotImplemented<T{ToPascalCase(SanitizeIdentifier(typeVar.Name))}>(\"{typeVar.Name}\")",
        _ => $"default({MapType(type)})! /* TODO: Implement deserialization for {type} */"
    };

    // CS8524 disabled (not CS8509): the no-default switch covers every named
    // DamlPrimitive so an out-of-range cast is the only uncovered input. CS8509
    // (a newly-added named member left unhandled) stays an error — that is the
    // compiler-enforced checklist for adding a Daml primitive.
#pragma warning disable CS8524
    private string MapBarePrimitiveToCSharp(DamlPrimitive primitive) => primitive switch
    {
        DamlPrimitive.Unit => context.Qualifier.Qualify(RuntimeTypeNames.DamlUnit, context.RootNamespace),
        DamlPrimitive.Bool => "bool",
        DamlPrimitive.Int64 => "long",
        DamlPrimitive.Numeric => "decimal",
        DamlPrimitive.Text => "string",
        DamlPrimitive.Date => "DateOnly",
        DamlPrimitive.Timestamp => "DateTimeOffset",
        DamlPrimitive.Party => context.Qualifier.Qualify(RuntimeTypeNames.Party, context.RootNamespace),
        DamlPrimitive.ContractId
            or DamlPrimitive.List
            or DamlPrimitive.Optional
            or DamlPrimitive.TextMap
            or DamlPrimitive.GenMap =>
            throw new NotSupportedException(
                $"Daml primitive '{primitive}' is a type constructor and cannot appear bare — it must be applied to argument types (handled by the DamlTypeApp arms of MapType)."),
    };
#pragma warning restore CS8524

    // See MapBarePrimitiveToCSharp for the CS8524-vs-CS8509 rationale.
#pragma warning disable CS8524
    private string GetBarePrimitiveToValueConversion(DamlPrimitive primitive, string fieldName) => primitive switch
    {
        DamlPrimitive.Unit => $"{context.Qualifier.Qualify(RuntimeTypeNames.DamlUnit, context.RootNamespace)}.Instance",
        DamlPrimitive.Bool => $"new {context.Qualifier.Qualify(RuntimeTypeNames.DamlBool, context.RootNamespace)}({fieldName})",
        DamlPrimitive.Int64 => $"new {context.Qualifier.Qualify(RuntimeTypeNames.DamlInt64, context.RootNamespace)}({fieldName})",
        DamlPrimitive.Numeric => $"new {context.Qualifier.Qualify(RuntimeTypeNames.DamlNumeric, context.RootNamespace)}({fieldName})",
        DamlPrimitive.Text => $"new {context.Qualifier.Qualify(RuntimeTypeNames.DamlText, context.RootNamespace)}({fieldName})",
        DamlPrimitive.Date => $"new {context.Qualifier.Qualify(RuntimeTypeNames.DamlDate, context.RootNamespace)}({fieldName})",
        DamlPrimitive.Timestamp => $"new {context.Qualifier.Qualify(RuntimeTypeNames.DamlTimestamp, context.RootNamespace)}({fieldName})",
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

    // See MapBarePrimitiveToCSharp for the CS8524-vs-CS8509 rationale.
#pragma warning disable CS8524
    private string GetBarePrimitiveFromValueConversion(DamlPrimitive primitive, string valueName) => primitive switch
    {
        DamlPrimitive.Bool => $"{valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlBool, context.RootNamespace)}>().Value",
        DamlPrimitive.Int64 => $"{valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlInt64, context.RootNamespace)}>().Value",
        DamlPrimitive.Numeric => $"{valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlNumeric, context.RootNamespace)}>().Value",
        DamlPrimitive.Text => $"{valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlText, context.RootNamespace)}>().Value",
        DamlPrimitive.Date => $"{valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlDate, context.RootNamespace)}>().Value",
        DamlPrimitive.Timestamp => $"{valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlTimestamp, context.RootNamespace)}>().Value",
        DamlPrimitive.Party => $"{context.Qualifier.Qualify(RuntimeTypeNames.Party, context.RootNamespace)}.FromDamlValue({valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlParty, context.RootNamespace)}>())",
        DamlPrimitive.Unit => $"{valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlUnit, context.RootNamespace)}>()",
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
            $"{stdlibName}<{typeArgs}>.FromRecord({valueName}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlRecord, context.RootNamespace)}>(), {string.Join(", ", lambdas)})");

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

    private string EmitParametricStdlibToValue(DamlTypeRef typeRef, IReadOnlyList<DamlType> arguments, string fieldName)
    {
        var converters = arguments.Select((arg, i) =>
            $"({context.Qualifier.Qualify(RuntimeTypeNames.DamlValue, context.RootNamespace)})({ToValue(arg, $"__t{i}")})").ToList();
        var lambdas = arguments.Select((_, i) =>
            $"__t{i} => {converters[i]}").ToList();
        return ConversionFor(typeRef).Serialize(fieldName, lambdas);
    }

    private string EmitParametricStdlibFromValue(DamlTypeRef typeRef, IReadOnlyList<DamlType> arguments, string valueName)
    {
        var stdlibName = context.Qualifier.Qualify(
            StdlibPackages.MapStdlibType(typeRef.Module, typeRef.Name)
                ?? throw new InvalidOperationException($"No stdlib mapping for {typeRef.Module}:{typeRef.Name}"),
            context.RootNamespace);
        var typeArgs = string.Join(", ", arguments.Select(MapType));
        var lambdas = arguments.Select((arg, i) =>
            $"__v{i} => {FromValue(arg, $"__v{i}")}").ToList();
        return ConversionFor(typeRef).Deserialize(valueName, stdlibName, typeArgs, lambdas);
    }

    private StdlibConversion ConversionFor(DamlTypeRef typeRef) =>
        StdlibConversions.TryGetValue((typeRef.Module, typeRef.Name), out var conversion)
            ? conversion
            : throw new InvalidOperationException($"No stdlib conversion for {typeRef.Module}:{typeRef.Name}");

    private bool IsLocalEnumTypeRef(DamlTypeRef typeRef) =>
        context.IsLocalRef(typeRef)
        && context.LocalEnumQualifiedNames.Contains($"{typeRef.Module}:{typeRef.Name}");

    private bool IsCrossPackageEnumTypeRef(DamlTypeRef typeRef) =>
        !context.IsLocalRef(typeRef)
        && (resolver.LookupPackage(typeRef.PackageId)?.Modules
            .Where(m => m.Name == typeRef.Module)
            .SelectMany(m => m.DataTypes)
            .Any(dt => dt.Name == typeRef.Name && dt.Definition is DamlEnumDefinition)
            ?? false);

    private bool IsEnumTypeRef(DamlTypeRef typeRef) =>
        IsLocalEnumTypeRef(typeRef) || IsCrossPackageEnumTypeRef(typeRef);

    private bool IsLocalVariantTypeRef(DamlTypeRef typeRef) =>
        context.IsLocalRef(typeRef)
        && context.LocalVariantQualifiedNames.Contains($"{typeRef.Module}:{typeRef.Name}");

    private bool IsCrossPackageVariantTypeRef(DamlTypeRef typeRef) =>
        !context.IsLocalRef(typeRef)
        && (resolver.LookupPackage(typeRef.PackageId)?.Modules
            .Where(m => m.Name == typeRef.Module)
            .SelectMany(m => m.DataTypes)
            .Any(dt => dt.Name == typeRef.Name && dt.Definition is DamlVariantDefinition)
            ?? false);

    private bool IsVariantTypeRef(DamlTypeRef typeRef) =>
        IsLocalVariantTypeRef(typeRef) || IsCrossPackageVariantTypeRef(typeRef);

    private string QualifiedEnumExtensionsCall(DamlTypeRef typeRef, string method, string argument) =>
        $"{resolver.Resolve(typeRef, context)}Extensions.{method}({argument})";

    private static string SanitizeIdentifier(string name) => Identifiers.Sanitize(name);

    private static string ToPascalCase(string name) => Identifiers.ToPascalCase(name);
}
