// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.Model;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Emits the C# for a Daml variant: the abstract base record carrying the
/// <c>Tag</c> / <c>ToVariant</c> / <c>FromVariant</c> serialization surface plus one
/// sealed derived record per constructor. Constructed once per package over the
/// package's <see cref="PackageEmitContext"/>, the DAR-scoped
/// <see cref="ICrossPackageResolver"/>, the shared <see cref="CodeGenOptions"/>, and
/// the package's <see cref="DamlTypeMapper"/>. The caller owns the file scaffold and
/// the common usings; this emitter writes the variant body into the provided
/// <see cref="IndentWriter"/>.
/// </summary>
public sealed class VariantEmitter(
    PackageEmitContext context,
    ICrossPackageResolver resolver,
    CodeGenOptions options,
    DamlTypeMapper mapper)
{
    /// <summary>
    /// Writes the abstract variant base record and its per-constructor derived
    /// records for <paramref name="dataType"/> into <paramref name="indent"/>.
    /// </summary>
    internal void WriteVariantType(IndentWriter indent, DamlDataType dataType, DamlVariantDefinition variant)
    {
        indent.Require("System");
        var className = EmitterHelpers.SanitizeIdentifier(dataType.Name);
        var typeParams = EmitterHelpers.GetTypeParametersDeclaration(dataType.TypeParams);
        var fullClassName = $"{className}{typeParams}";

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Generated from Daml variant {dataType.Name}");
            if (dataType.TypeParams.Count > 0)
            {
                indent.AppendLine($"/// Type parameters: {string.Join(", ", dataType.TypeParams)}");
            }
            indent.AppendLine("/// </summary>");
            EmitterHelpers.WriteTypeParamDocs(indent, dataType.TypeParams);
        }

        indent.AppendLine($"public abstract record {fullClassName} : {context.Qualifier.Qualify(RuntimeTypeNames.IDamlVariant, context.RootNamespace)}");
        indent.AppendLine("{");
        indent.Indent();

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>Gets the variant constructor name.</summary>");
        }
        indent.AppendLine("public abstract string Tag { get; }");
        indent.AppendLine();

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>Converts to a DamlVariant.</summary>");
        }
        indent.AppendLine($"public abstract {context.Qualifier.Qualify(RuntimeTypeNames.DamlVariant, context.RootNamespace)} ToVariant();");
        indent.AppendLine();

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine($"/// <summary>Reconstructs {IndefiniteArticleFor(className)} {className} by dispatching on the DamlVariant constructor tag.</summary>");
        }
        indent.AppendLine($"public static {fullClassName} FromVariant({context.Qualifier.Qualify(RuntimeTypeNames.DamlVariant, context.RootNamespace)} variant) =>");
        indent.Indent();
        indent.AppendLine("variant.Constructor switch");
        indent.AppendLine("{");
        indent.Indent();
        foreach (var ctor in variant.Constructors)
        {
            var ctorName = VariantConstructorName(ctor.Name, className);
            if (HasVariantPayload(ctor))
            {
                indent.AppendLine($"\"{ctor.Name}\" => new {ctorName}({mapper.FromValue(ctor.ArgumentType!, "variant.Value")}),");
            }
            else
            {
                indent.AppendLine($"\"{ctor.Name}\" => new {ctorName}(),");
            }
        }
        indent.AppendLine($"_ => throw new ArgumentOutOfRangeException(nameof(variant), variant.Constructor, \"Unknown {className} constructor\")");
        indent.Dedent();
        indent.AppendLine("};");
        indent.Dedent();
        indent.AppendLine();

        foreach (var ctor in variant.Constructors)
        {
            var ctorName = VariantConstructorName(ctor.Name, className);
            var hasArg = HasVariantPayload(ctor);
            var argType = hasArg ? mapper.MapType(ctor.ArgumentType!) : null;

            if (argType is not null)
            {
                StdlibPackages.RequireForFieldType(resolver, indent, ctor.ArgumentType!);
                if (options.GenerateXmlDocs)
                {
                    indent.AppendLine($"/// <summary>{ctor.Name} constructor.</summary>");
                }
                indent.AppendLine($"public sealed record {ctorName}({argType} Value) : {fullClassName}");
            }
            else
            {
                if (options.GenerateXmlDocs)
                {
                    indent.AppendLine($"/// <summary>{ctor.Name} constructor (no arguments).</summary>");
                }
                indent.AppendLine($"public sealed record {ctorName}() : {fullClassName}");
            }

            indent.AppendLine("{");
            indent.Indent();

            if (options.GenerateXmlDocs)
            {
                indent.AppendLine("/// <inheritdoc />");
            }
            indent.AppendLine($"public override string Tag => \"{ctor.Name}\";");
            indent.AppendLine();
            var payload = hasArg
                ? mapper.ToValue(ctor.ArgumentType!, "Value")
                : $"{context.Qualifier.Qualify(RuntimeTypeNames.DamlUnit, context.RootNamespace)}.Instance";
            if (options.GenerateXmlDocs)
            {
                indent.AppendLine("/// <inheritdoc />");
            }
            indent.AppendLine($"public override {context.Qualifier.Qualify(RuntimeTypeNames.DamlVariant, context.RootNamespace)} ToVariant() => {context.Qualifier.Qualify(RuntimeTypeNames.DamlVariant, context.RootNamespace)}.Create(\"{ctor.Name}\", {payload});");

            indent.Dedent();
            indent.AppendLine("}");
            indent.AppendLine();
        }

        indent.Dedent();
        indent.AppendLine("}");
    }

    private static bool HasVariantPayload(DamlVariantConstructor ctor) =>
        ctor.ArgumentType is not null
        && ctor.ArgumentType is not DamlPrimitiveType { Primitive: DamlPrimitive.Unit };

    private static string IndefiniteArticleFor(string name) =>
        name.Length > 0 && "aeiou".Contains(char.ToLowerInvariant(name[0])) ? "an" : "a";

    private static string VariantConstructorName(string ctorName, string enclosingTypeName) =>
        Identifiers.Disambiguate(EmitterHelpers.SanitizeIdentifier(ctorName), enclosingTypeName);
}
