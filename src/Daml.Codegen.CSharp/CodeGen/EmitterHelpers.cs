// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

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

    internal static void WriteTypeParamDocs(IndentWriter indent, IReadOnlyList<string> typeParams)
    {
        foreach (var param in typeParams)
        {
            indent.AppendLine($"/// <typeparam name=\"{TypeParameterName(param)}\">Type parameter {param}</typeparam>");
        }
    }
}
