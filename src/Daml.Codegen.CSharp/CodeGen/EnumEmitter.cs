// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.Model;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Emits the C# for a Daml enum: the <c>enum</c> declaration itself plus the
/// <c>&lt;Enum&gt;Extensions</c> static class carrying the <c>ToDamlEnum</c> /
/// <c>FromDamlEnum</c> serialization round-trip. Constructed once per package over
/// the package's <see cref="PackageEmitContext"/> and the shared
/// <see cref="CodeGenOptions"/>. The caller owns the file scaffold and the common
/// usings; this emitter writes the enum body into the provided
/// <see cref="IndentWriter"/>.
/// </summary>
public sealed class EnumEmitter(
    PackageEmitContext context,
    CodeGenOptions options)
{
    /// <summary>
    /// Writes the enum declaration and its serialization extension class for
    /// <paramref name="dataType"/> into <paramref name="indent"/>.
    /// </summary>
    internal void WriteEnumType(IndentWriter indent, DamlDataType dataType, DamlEnumDefinition enumDef)
    {
        indent.Require("System");
        var enumName = EmitterHelpers.SanitizeIdentifier(dataType.Name);

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Generated from Daml enum {dataType.Name}");
            indent.AppendLine("/// </summary>");
        }

        indent.AppendLine($"public enum {enumName}");
        indent.AppendLine("{");
        indent.Indent();

        foreach (var ctor in enumDef.Constructors)
        {
            indent.AppendLine($"{EmitterHelpers.SanitizeIdentifier(ctor)},");
        }

        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine();

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Extension methods for {enumName} serialization.");
            indent.AppendLine("/// </summary>");
        }
        indent.AppendLine($"public static class {enumName}Extensions");
        indent.AppendLine("{");
        indent.Indent();

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>Converts to a DamlEnum value.</summary>");
        }
        indent.AppendLine($"public static {context.Qualifier.Qualify(RuntimeTypeNames.DamlEnum, context.RootNamespace)} ToDamlEnum(this {enumName} value)");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine("return value switch");
        indent.AppendLine("{");
        indent.Indent();
        foreach (var ctor in enumDef.Constructors)
        {
            indent.AppendLine($"{enumName}.{EmitterHelpers.SanitizeIdentifier(ctor)} => {context.Qualifier.Qualify(RuntimeTypeNames.DamlEnum, context.RootNamespace)}.Create(\"{ctor}\"),");
        }
        indent.AppendLine("_ => throw new ArgumentOutOfRangeException(nameof(value), value, null)");
        indent.Dedent();
        indent.AppendLine("};");
        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine();

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>Creates an instance from a DamlEnum value.</summary>");
        }
        indent.AppendLine($"public static {enumName} FromDamlEnum({context.Qualifier.Qualify(RuntimeTypeNames.DamlEnum, context.RootNamespace)} value)");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine("return value.Constructor switch");
        indent.AppendLine("{");
        indent.Indent();
        foreach (var ctor in enumDef.Constructors)
        {
            indent.AppendLine($"\"{ctor}\" => {enumName}.{EmitterHelpers.SanitizeIdentifier(ctor)},");
        }
        indent.AppendLine("_ => throw new ArgumentOutOfRangeException(nameof(value), value.Constructor, null)");
        indent.Dedent();
        indent.AppendLine("};");
        indent.Dedent();
        indent.AppendLine("}");

        indent.Dedent();
        indent.AppendLine("}");
    }
}
