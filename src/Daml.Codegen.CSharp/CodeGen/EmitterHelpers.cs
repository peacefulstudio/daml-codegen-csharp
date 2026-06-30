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

    internal static string GetTypeParametersDeclaration(IReadOnlyList<string> typeParams)
    {
        if (typeParams.Count == 0)
            return string.Empty;

        var sanitized = typeParams.Select(p => $"T{ToPascalCase(SanitizeIdentifier(p))}");
        return $"<{string.Join(", ", sanitized)}>";
    }

    internal static void WriteTypeParamDocs(IndentWriter indent, IReadOnlyList<string> typeParams)
    {
        foreach (var param in typeParams)
        {
            var sanitized = $"T{ToPascalCase(SanitizeIdentifier(param))}";
            indent.AppendLine($"/// <typeparam name=\"{sanitized}\">Type parameter {param}</typeparam>");
        }
    }
}
