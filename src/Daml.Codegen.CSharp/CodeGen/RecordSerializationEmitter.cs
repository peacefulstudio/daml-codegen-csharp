// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;

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
internal sealed class RecordSerializationEmitter(
    PackageEmitContext context,
    ICrossPackageResolver resolver,
    CodeGenOptions options,
    DamlTypeMapper mapper)
{
    /// <summary>
    /// Writes the primary-constructor parameters for <paramref name="fields"/> into
    /// <paramref name="indent"/>, one per line and indented one level, leaving the writer
    /// at the start of the line that closes the parameter list. The caller has already
    /// written the opening parenthesis and writes the closing one, so the closing
    /// parenthesis and any base list land on their own line.
    /// </summary>
    internal void WriteRecordParameters(IndentWriter indent, IReadOnlyList<DamlFieldDefinition> fields)
    {
        indent.AppendLine();
        indent.Indent();
        for (var i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            var csharpType = mapper.MapType(field.Type);
            var fieldName = MemberName(field.Name, indent.CurrentTypeName);
            StdlibPackages.RequireForFieldType(resolver, context.Package, indent, field.Type);
            var separator = i == fields.Count - 1 ? "" : ",";
            indent.AppendLine($"[property: {DamlFieldAttributeSyntax(field.Name)}] {csharpType} {fieldName}{separator}");
        }
        indent.Dedent();
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
            StdlibPackages.RequireForFieldType(resolver, context.Package, indent, field.Type);

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
    /// DamlRecord into <paramref name="indent"/>. When <paramref name="typeParams"/> is
    /// non-empty the method accepts one <c>Func&lt;T, DamlValue&gt;</c> converter per type
    /// parameter, and type-variable fields serialize through the matching converter.
    /// </summary>
    internal void WriteToRecordMethod(IndentWriter indent, IReadOnlyList<DamlFieldDefinition> fields, IReadOnlyList<string> typeParams)
    {
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>Converts this value to a DamlRecord.</summary>");
        }

        var parameters = ConverterParameters(indent, typeParams, EmitterHelpers.SerializeConverterParameters);
        var delegates = EmitterHelpers.ConverterNameMap(typeParams);

        if (fields.Count == 0)
        {
            indent.AppendLine($"public {context.Qualifier.Qualify(RuntimeTypeNames.DamlRecord, context.RootNamespace)} ToRecord({parameters}) => {context.Qualifier.Qualify(RuntimeTypeNames.DamlRecord, context.RootNamespace)}.Create();");
            indent.AppendLine();
            return;
        }

        indent.AppendLine($"public {context.Qualifier.Qualify(RuntimeTypeNames.DamlRecord, context.RootNamespace)} ToRecord({parameters}) => {context.Qualifier.Qualify(RuntimeTypeNames.DamlRecord, context.RootNamespace)}.Create(");
        indent.Indent();

        for (int i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            var fieldName = MemberName(field.Name, indent.CurrentTypeName);
            var conversion = mapper.ToValue(field.Type, fieldName, delegates);
            var comma = i < fields.Count - 1 ? "," : "";
            StdlibPackages.RequireForFieldType(resolver, context.Package, indent, field.Type);

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
    internal void WriteFromRecordMethod(IndentWriter indent, string className, IReadOnlyList<DamlFieldDefinition> fields, IReadOnlyList<string> typeParams)
    {
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>Creates an instance from a DamlRecord.</summary>");
        }

        var converterParameters = ConverterParameters(indent, typeParams, EmitterHelpers.DeserializeConverterParameters);
        var parameters = $"{context.Qualifier.Qualify(RuntimeTypeNames.DamlRecord, context.RootNamespace)} record{Prefixed(converterParameters)}";
        var delegates = EmitterHelpers.ConverterNameMap(typeParams);

        if (fields.Count == 0)
        {
            indent.AppendLine($"public static {className} FromRecord({parameters}) => new {className}();");
            indent.AppendLine();
            return;
        }

        foreach (var field in fields)
        {
            StdlibPackages.RequireForFieldType(resolver, context.Package, indent, field.Type);
        }

        if (options.UseRecordTypes && options.UsePrimaryConstructors)
        {
            indent.AppendLine($"public static {className} FromRecord({parameters}) => new {className}(");
            indent.Indent();

            for (int i = 0; i < fields.Count; i++)
            {
                var field = fields[i];
                var fieldName = MemberName(field.Name, indent.CurrentTypeName);
                var conversion = mapper.FromValue(field.Type, $"record.GetRequiredField(\"{field.Name}\")", delegates);
                var comma = i < fields.Count - 1 ? "," : "";

                indent.AppendLine($"{fieldName}: {conversion}{comma}");
            }

            indent.Dedent();
            indent.AppendLine(");");
        }
        else
        {
            indent.AppendLine($"public static {className} FromRecord({parameters})");
            indent.AppendLine("{");
            indent.Indent();

            indent.AppendLine($"return new {className}");
            indent.AppendLine("{");
            indent.Indent();

            foreach (var field in fields)
            {
                var fieldName = MemberName(field.Name, indent.CurrentTypeName);
                var conversion = mapper.FromValue(field.Type, $"record.GetRequiredField(\"{field.Name}\")", delegates);
                indent.AppendLine($"{fieldName} = {conversion},");
            }

            indent.Dedent();
            indent.AppendLine("};");

            indent.Dedent();
            indent.AppendLine("}");
        }
        indent.AppendLine();
    }

    private string ConverterParameters(
        IndentWriter indent,
        IReadOnlyList<string> typeParams,
        Func<IReadOnlyList<string>, string, string> build)
    {
        if (typeParams.Count == 0)
        {
            return string.Empty;
        }

        indent.Require("System");
        return build(typeParams, context.Qualifier.Qualify(RuntimeTypeNames.DamlValue, context.RootNamespace));
    }

    private static string Prefixed(string parameters) =>
        string.IsNullOrEmpty(parameters) ? string.Empty : $", {parameters}";

    private string DamlFieldAttributeSyntax(string damlFieldName) =>
        $"{context.Qualifier.Qualify(RuntimeTypeNames.DamlFieldAttribute, context.RootNamespace)}(\"{damlFieldName}\")";

    private static string MemberName(string damlFieldName, string enclosingTypeName) =>
        Identifiers.MemberName(damlFieldName, enclosingTypeName);
}
