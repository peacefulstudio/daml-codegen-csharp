// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.Model;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Emits the record-serialization surface shared by every field-bearing C# type:
/// the primary-constructor parameters, the <c>required</c> properties, and the
/// <c>ToRecord</c> / <c>FromRecord</c> round-trip. The same emitter feeds all three
/// consumers — plain records (<see cref="RecordEmitter"/>), templates, and nested
/// choice-argument records — so their serialization output stays byte-identical.
/// Constructed once per package over the package's <see cref="PackageEmitContext"/>,
/// the DAR-scoped <see cref="ICrossPackageResolver"/>, the shared
/// <see cref="CodeGenOptions"/>, and the package's <see cref="DamlTypeMapper"/>.
/// </summary>
public sealed class RecordSerializationEmitter(
    PackageEmitContext context,
    ICrossPackageResolver resolver,
    CodeGenOptions options,
    DamlTypeMapper mapper)
{
    /// <summary>
    /// Writes the comma-separated primary-constructor parameters for
    /// <paramref name="fields"/> into <paramref name="indent"/>.
    /// </summary>
    internal void WriteRecordParameters(IndentWriter indent, IReadOnlyList<DamlFieldDefinition> fields)
    {
        var first = true;
        foreach (var field in fields)
        {
            if (!first)
            {
                indent.Append(", ");
            }
            first = false;

            var csharpType = mapper.MapType(field.Type);
            var fieldName = MemberName(field.Name, indent.CurrentTypeName);
            StdlibPackages.RequireForFieldType(resolver, indent, field.Type);
            indent.Append($"[property: {DamlFieldAttributeSyntax(field.Name)}] {csharpType} {fieldName}");
        }
    }

    /// <summary>
    /// Writes a <c>required</c> init-only property for each of <paramref name="fields"/>
    /// into <paramref name="indent"/>.
    /// </summary>
    internal void WriteProperties(IndentWriter indent, IReadOnlyList<DamlFieldDefinition> fields)
    {
        foreach (var field in fields)
        {
            var csharpType = mapper.MapType(field.Type);
            var fieldName = MemberName(field.Name, indent.CurrentTypeName);
            StdlibPackages.RequireForFieldType(resolver, indent, field.Type);

            if (options.GenerateXmlDocs)
            {
                indent.AppendLine($"/// <summary>Gets the {field.Name} field.</summary>");
            }

            indent.AppendLine($"[{DamlFieldAttributeSyntax(field.Name)}]");
            indent.AppendLine($"public required {csharpType} {fieldName} {{ get; init; }}");
            indent.AppendLine();
        }
    }

    /// <summary>
    /// Writes the <c>ToRecord</c> method that serializes <paramref name="fields"/> to a
    /// DamlRecord into <paramref name="indent"/>.
    /// </summary>
    internal void WriteToRecordMethod(IndentWriter indent, IReadOnlyList<DamlFieldDefinition> fields)
    {
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>Converts this value to a DamlRecord.</summary>");
        }

        if (fields.Count == 0)
        {
            indent.AppendLine($"public {context.Qualifier.Qualify(RuntimeTypeNames.DamlRecord, context.RootNamespace)} ToRecord() => {context.Qualifier.Qualify(RuntimeTypeNames.DamlRecord, context.RootNamespace)}.Create();");
            indent.AppendLine();
            return;
        }

        indent.AppendLine($"public {context.Qualifier.Qualify(RuntimeTypeNames.DamlRecord, context.RootNamespace)} ToRecord() => {context.Qualifier.Qualify(RuntimeTypeNames.DamlRecord, context.RootNamespace)}.Create(");
        indent.Indent();

        for (int i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            var fieldName = MemberName(field.Name, indent.CurrentTypeName);
            var conversion = mapper.ToValue(field.Type, fieldName);
            var comma = i < fields.Count - 1 ? "," : "";
            StdlibPackages.RequireForFieldType(resolver, indent, field.Type);

            indent.AppendLine($"{context.Qualifier.Qualify(RuntimeTypeNames.DamlField, context.RootNamespace)}.Create(\"{field.Name}\", {conversion}){comma}");
        }

        indent.Dedent();
        indent.AppendLine(");");
        indent.AppendLine();
    }

    /// <summary>
    /// Writes the static <c>FromRecord</c> factory that reconstructs a
    /// <paramref name="className"/> instance from a DamlRecord into
    /// <paramref name="indent"/>.
    /// </summary>
    internal void WriteFromRecordMethod(IndentWriter indent, string className, IReadOnlyList<DamlFieldDefinition> fields)
    {
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>Creates an instance from a DamlRecord.</summary>");
        }

        if (fields.Count == 0)
        {
            indent.AppendLine($"public static {className} FromRecord({context.Qualifier.Qualify(RuntimeTypeNames.DamlRecord, context.RootNamespace)} record) => new {className}();");
            indent.AppendLine();
            return;
        }

        foreach (var field in fields)
        {
            StdlibPackages.RequireForFieldType(resolver, indent, field.Type);
        }

        if (options.UseRecordTypes && options.UsePrimaryConstructors)
        {
            indent.AppendLine($"public static {className} FromRecord({context.Qualifier.Qualify(RuntimeTypeNames.DamlRecord, context.RootNamespace)} record) => new {className}(");
            indent.Indent();

            for (int i = 0; i < fields.Count; i++)
            {
                var field = fields[i];
                var fieldName = MemberName(field.Name, indent.CurrentTypeName);
                var conversion = mapper.FromValue(field.Type, $"record.GetRequiredField(\"{field.Name}\")");
                var comma = i < fields.Count - 1 ? "," : "";

                indent.AppendLine($"{fieldName}: {conversion}{comma}");
            }

            indent.Dedent();
            indent.AppendLine(");");
        }
        else
        {
            indent.AppendLine($"public static {className} FromRecord({context.Qualifier.Qualify(RuntimeTypeNames.DamlRecord, context.RootNamespace)} record)");
            indent.AppendLine("{");
            indent.Indent();

            indent.AppendLine($"return new {className}");
            indent.AppendLine("{");
            indent.Indent();

            foreach (var field in fields)
            {
                var fieldName = MemberName(field.Name, indent.CurrentTypeName);
                var conversion = mapper.FromValue(field.Type, $"record.GetRequiredField(\"{field.Name}\")");
                indent.AppendLine($"{fieldName} = {conversion},");
            }

            indent.Dedent();
            indent.AppendLine("};");

            indent.Dedent();
            indent.AppendLine("}");
        }
        indent.AppendLine();
    }

    private string DamlFieldAttributeSyntax(string damlFieldName) =>
        $"{context.Qualifier.Qualify(RuntimeTypeNames.DamlFieldAttribute, context.RootNamespace)}(\"{damlFieldName}\")";

    private static string MemberName(string damlFieldName, string enclosingTypeName) =>
        Identifiers.MemberName(damlFieldName, enclosingTypeName);
}
