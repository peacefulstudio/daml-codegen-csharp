// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Identifier and type-parameter helpers shared across the per-type emitters
/// (<see cref="RecordEmitter"/>, <see cref="VariantEmitter"/>,
/// <see cref="EnumEmitter"/>) and the <see cref="CSharpCodeGenerator"/> file
/// scaffold. Pure functions over <see cref="Identifiers"/> so every emitter
/// sanitises names and declares type parameters identically.
/// </summary>
internal static class EmitterHelpers
{
    internal static string SanitizeIdentifier(string name) => Identifiers.Sanitize(name);

    internal static string ToPascalCase(string name) => Identifiers.ToPascalCase(name);

    /// <summary>
    /// Derives the C# type-parameter name for a Daml type variable: PascalCased and
    /// sanitised behind a <c>T</c> prefix. The <c>T</c> prefix combined with PascalCasing
    /// makes a keyword collision unreachable — all C# reserved keywords are lowercase, so
    /// <c>T</c> + an uppercase-initial identifier can never be one. The name is therefore
    /// deliberately built from <see cref="Identifiers.SanitizeBare"/> — escaping first
    /// would emit <c>T@event</c>, which parses as two identifiers rather than one.
    /// </summary>
    internal static string TypeParameterName(string damlTypeParam) =>
        $"T{ToPascalCase(Identifiers.SanitizeBare(damlTypeParam))}";

    internal static string GetTypeParametersDeclaration(IReadOnlyList<string> typeParams)
    {
        if (typeParams.Count == 0)
            return string.Empty;

        return $"<{string.Join(", ", typeParams.Select(TypeParameterName))}>";
    }

    /// <summary>
    /// Builds the <c>where T : notnull</c> clauses trailing a generic record's or variant's
    /// declaration; empty for a non-generic type. A Daml type variable ranges only over
    /// serialisable Daml types, none of which is nullable — the nullable positions are exactly
    /// the ones Daml spells <c>Optional</c>, which <see cref="DamlTypeMapper"/> renders as an
    /// explicit <c>?</c> or as a wrapper rather than as a bare type variable. Left
    /// unconstrained, a field typed by the type variable reports a nullable read-state through
    /// <see cref="System.Reflection.NullabilityInfoContext"/> at every reference-type
    /// instantiation, so a reflection-driven reader decodes it as an <c>Optional</c> the wire
    /// never carried.
    /// </summary>
    internal static string GetTypeParameterConstraints(IReadOnlyList<string> typeParams)
    {
        if (typeParams.Count == 0)
            return string.Empty;

        return string.Concat(typeParams.Select(param => $" where {TypeParameterName(param)} : notnull"));
    }

    /// <summary>
    /// Derives the injected converter-delegate parameter name for a Daml type
    /// variable — the delegate a generic record/variant's <c>ToRecord</c>/<c>FromRecord</c>
    /// (or <c>ToVariant</c>/<c>FromVariant</c>) accepts to bridge that type parameter's
    /// concrete CLR argument to and from <see cref="Daml.Runtime.Data.DamlValue"/>.
    /// </summary>
    internal static string ConverterParameterName(string damlTypeParam) =>
        $"convert{TypeParameterName(damlTypeParam)}";

    /// <summary>
    /// Maps each Daml type-variable name to its <see cref="ConverterParameterName"/>,
    /// threaded into the <see cref="DamlTypeMapper"/>'s <c>ToValue</c>/<c>FromValue</c>
    /// so a <see cref="DamlTypeVar"/> field resolves to its injected converter.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> ConverterNameMap(IReadOnlyList<string> typeParams) =>
        typeParams.ToDictionary(param => param, ConverterParameterName);

    internal static string SerializeConverterParameters(IReadOnlyList<string> typeParams, string qualifiedDamlValue) =>
        string.Join(", ", typeParams.Select(param =>
            $"Func<{TypeParameterName(param)}, {qualifiedDamlValue}> {ConverterParameterName(param)}"));

    internal static string DeserializeConverterParameters(IReadOnlyList<string> typeParams, string qualifiedDamlValue) =>
        string.Join(", ", typeParams.Select(param =>
            $"Func<{qualifiedDamlValue}, {TypeParameterName(param)}> {ConverterParameterName(param)}"));

    internal static void WriteTypeParamDocs(IndentWriter indent, IReadOnlyList<string> typeParams)
    {
        foreach (var param in typeParams)
        {
            indent.AppendLine($"/// <typeparam name=\"{TypeParameterName(param)}\">Type parameter {param}</typeparam>");
        }
    }
}
