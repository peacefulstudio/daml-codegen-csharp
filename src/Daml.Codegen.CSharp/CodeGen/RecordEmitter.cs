// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.Model;
using RuntimeNamespaces = Daml.Runtime.RuntimeNamespaces;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Emits the C# for a Daml record: the sealed record declaration and, for the
/// interface-placeholder case, the throwing <see cref="Daml.Runtime.Contracts.ITemplate"/>
/// stub. The field-bearing serialization surface (constructor parameters,
/// <c>ToRecord</c> / <c>FromRecord</c>) is delegated to the shared
/// <see cref="RecordSerializationEmitter"/> so record, template, and nested
/// choice-argument output stay byte-identical. Constructed once per package over
/// the package's <see cref="PackageEmitContext"/>, the shared
/// <see cref="CodeGenOptions"/>, and the package's
/// <see cref="RecordSerializationEmitter"/>. The caller owns the file scaffold and
/// the common usings; this emitter writes the record body into the provided
/// <see cref="IndentWriter"/>.
/// </summary>
public sealed class RecordEmitter(
    PackageEmitContext context,
    CodeGenOptions options,
    RecordSerializationEmitter serialization)
{
    /// <summary>
    /// Writes the record declaration and its serialization round-trip for
    /// <paramref name="dataType"/> into <paramref name="indent"/>, routing the
    /// interface-placeholder case to its throwing stub.
    /// </summary>
    internal void WriteRecordType(IndentWriter indent, DamlModule module, DamlDataType dataType, DamlRecordDefinition record)
    {
        if (context.InterfacePlaceholderQualifiedNames.Contains($"{module.Name}:{dataType.Name}"))
        {
            WriteInterfacePlaceholderRecord(indent, module, dataType);
            return;
        }

        var className = EmitterHelpers.SanitizeIdentifier(dataType.Name);
        indent.CurrentTypeName = className;
        var typeParams = EmitterHelpers.GetTypeParametersDeclaration(dataType.TypeParams);
        var fullClassName = $"{className}{typeParams}";

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Generated from Daml record {dataType.Name}");
            if (dataType.TypeParams.Count > 0)
            {
                indent.AppendLine($"/// Type parameters: {string.Join(", ", dataType.TypeParams)}");
            }
            indent.AppendLine("/// </summary>");
            EmitterHelpers.WriteTypeParamDocs(indent, dataType.TypeParams);
        }

        if (options.UseRecordTypes && options.UsePrimaryConstructors && record.Fields.Count > 0)
        {
            indent.Append($"public sealed record {fullClassName}(");
            serialization.WriteRecordParameters(indent, record.Fields);
            indent.AppendLine($") : {context.Qualifier.Qualify(RuntimeTypeNames.IDamlRecord, context.RootNamespace)}");
        }
        else
        {
            indent.AppendLine($"public sealed record {fullClassName} : {context.Qualifier.Qualify(RuntimeTypeNames.IDamlRecord, context.RootNamespace)}");
        }

        indent.AppendLine("{");
        indent.Indent();

        serialization.WriteToRecordMethod(indent, record.Fields);
        serialization.WriteFromRecordMethod(indent, fullClassName, record.Fields);

        indent.Dedent();
        indent.AppendLine("}");
    }

    /// <summary>
    /// Emits the C# placeholder for a Daml interface declaration. The Daml-LF emits a
    /// same-named empty record alongside every <c>interface I where ...</c> so that
    /// <c>ContractId I</c> can be expressed at the type level. We surface that record
    /// as a sealed record implementing <see cref="Daml.Runtime.Contracts.ITemplate"/> with throwing static
    /// metadata: it lets <c>ContractId&lt;I&gt;</c> compile (the runtime constraint is
    /// <c>where T : ITemplate</c>) but loudly fails any code path that tries to read
    /// <c>I.TemplateId</c> directly — which would be a logic error, since interface
    /// placeholders carry no template identity. Coerce the contract id to the
    /// underlying template type before reading metadata or constructing commands.
    /// </summary>
    private void WriteInterfacePlaceholderRecord(IndentWriter indent, DamlModule module, DamlDataType dataType)
    {
        indent.Require("System");
        indent.Require(RuntimeNamespaces.Contracts);
        var className = EmitterHelpers.SanitizeIdentifier(dataType.Name);
        var qualifiedDamlName = $"{module.Name}:{dataType.Name}";
        var throwMessage =
            $"'{className}' is the C# placeholder for the Daml interface "
            + $"'{qualifiedDamlName}' and carries no template metadata. "
            + "Coerce ContractId<" + className + "> to a typed ContractId<TConcrete> before reading template metadata or exercising commands.";

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Phantom placeholder for the Daml interface <c>{qualifiedDamlName}</c>.");
            indent.AppendLine("/// Implements <see cref=\"ITemplate\"/> so that <c>ContractId&lt;" + className + "&gt;</c>");
            indent.AppendLine("/// satisfies its <c>where T : ITemplate</c> constraint, but every static");
            indent.AppendLine("/// metadata accessor throws — interface placeholders carry no template identity.");
            indent.AppendLine("/// </summary>");
        }

        indent.AppendLine($"public sealed record {className} : {context.Qualifier.Qualify(RuntimeTypeNames.ITemplate, context.RootNamespace)}");
        indent.AppendLine("{");
        indent.Indent();

        void WritePlaceholderDoc(string summary)
        {
            if (options.GenerateXmlDocs)
            {
                indent.AppendLine($"/// <summary>{summary}</summary>");
            }
        }

        WritePlaceholderDoc($"Always throws — the <c>{qualifiedDamlName}</c> interface placeholder carries no template identity.");
        indent.AppendLine($"public static {context.Qualifier.Qualify(RuntimeTypeNames.Identifier, context.RootNamespace)} TemplateId =>");
        indent.Indent();
        indent.AppendLine($"throw new InvalidOperationException(\"{throwMessage}\");");
        indent.Dedent();
        indent.AppendLine();
        WritePlaceholderDoc($"Always throws — the <c>{qualifiedDamlName}</c> interface placeholder carries no package identity.");
        indent.AppendLine("public static string PackageId =>");
        indent.Indent();
        indent.AppendLine($"throw new InvalidOperationException(\"{throwMessage}\");");
        indent.Dedent();
        indent.AppendLine();
        WritePlaceholderDoc($"Always throws — the <c>{qualifiedDamlName}</c> interface placeholder carries no package identity.");
        indent.AppendLine("public static string PackageName =>");
        indent.Indent();
        indent.AppendLine($"throw new InvalidOperationException(\"{throwMessage}\");");
        indent.Dedent();
        indent.AppendLine();
        WritePlaceholderDoc($"Always throws — the <c>{qualifiedDamlName}</c> interface placeholder carries no package identity.");
        indent.AppendLine("public static Version PackageVersion =>");
        indent.Indent();
        indent.AppendLine($"throw new InvalidOperationException(\"{throwMessage}\");");
        indent.Dedent();
        indent.AppendLine();
        WritePlaceholderDoc($"Always throws — the <c>{qualifiedDamlName}</c> interface placeholder carries no Daml type identity.");
        indent.AppendLine($"public static {context.Qualifier.Qualify(RuntimeTypeNames.DamlTypeDescriptor, context.RootNamespace)} DamlTypeId =>");
        indent.Indent();
        indent.AppendLine($"throw new InvalidOperationException(\"{throwMessage}\");");
        indent.Dedent();
        indent.AppendLine();

        WritePlaceholderDoc("Converts this placeholder to an empty DamlRecord.");
        indent.AppendLine($"public {context.Qualifier.Qualify(RuntimeTypeNames.DamlRecord, context.RootNamespace)} ToRecord() => {context.Qualifier.Qualify(RuntimeTypeNames.DamlRecord, context.RootNamespace)}.Create();");
        indent.AppendLine();
        WritePlaceholderDoc("Creates an empty placeholder instance from a DamlRecord.");
        indent.AppendLine($"public static {className} FromRecord({context.Qualifier.Qualify(RuntimeTypeNames.DamlRecord, context.RootNamespace)} record) => new {className}();");

        indent.Dedent();
        indent.AppendLine("}");
    }
}
