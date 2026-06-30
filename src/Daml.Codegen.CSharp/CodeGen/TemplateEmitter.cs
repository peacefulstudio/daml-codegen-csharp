// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.Model;
using RuntimeNamespaces = Daml.Runtime.RuntimeNamespaces;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Emits the C# for a Daml template: the sealed template record with its
/// <see cref="Daml.Runtime.Contracts.ITemplate"/> facet (plus the optional
/// <see cref="Daml.Runtime.Contracts.IHasKey{TKey}"/> and
/// <c>IUpgradeable</c> facets), the static template metadata, the throwing
/// contract-key accessor, the nested <c>ContractId</c> / <c>Contract</c> records,
/// and the namespace-level choice / submission extension surface. The
/// field-bearing serialization surface (constructor parameters, properties,
/// <c>ToRecord</c> / <c>FromRecord</c>) is delegated to the shared
/// <see cref="RecordSerializationEmitter"/>, the choice descriptors / exercisers
/// to the shared <see cref="ChoiceEmitter"/>, and the typed-submitter surface to
/// the shared <see cref="SubmissionExtensionsEmitter"/> — the same per-package
/// instances the sibling emitters use, so record, template, and choice output stay
/// byte-identical. Constructed once per package over the package's
/// <see cref="PackageEmitContext"/>, the DAR-scoped <see cref="ICrossPackageResolver"/>,
/// the package's <see cref="DamlTypeMapper"/>, those three composed emitters, and the
/// shared <see cref="CodeGenOptions"/>. The caller owns the file scaffold and the
/// common usings; this emitter writes the template body into the provided
/// <see cref="IndentWriter"/>.
/// </summary>
public sealed class TemplateEmitter(
    PackageEmitContext context,
    ICrossPackageResolver resolver,
    DamlTypeMapper mapper,
    RecordSerializationEmitter recordSerialization,
    ChoiceEmitter choiceEmitter,
    SubmissionExtensionsEmitter submissionExtensions,
    CodeGenOptions options)
{
    /// <summary>
    /// Writes the template record, its static metadata, the optional key accessor,
    /// the serialization round-trip, the nested <c>ContractId</c> / <c>Contract</c>
    /// records, and the sibling choice / submission extension classes for
    /// <paramref name="template"/> into <paramref name="indent"/>.
    /// </summary>
    internal void WriteTemplateType(
        IndentWriter indent,
        DamlPackage package,
        DamlModule module,
        DamlTemplate template,
        IReadOnlyList<DamlFieldDefinition> fields)
    {
        var moduleNamespace = context.RootNamespace;
        var dataTypes = context.DataTypes;

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Generated from Daml template {module.Name}:{template.Name}");
            indent.AppendLine("/// </summary>");
        }

        var className = EmitterHelpers.SanitizeIdentifier(template.Name);
        indent.CurrentTypeName = className;

        var keyType = template.Key is not null ? mapper.MapType(template.Key) : null;
        var interfacesList = new List<string> { context.Qualifier.Qualify(RuntimeTypeNames.ITemplate, context.RootNamespace) };
        if (keyType is not null)
            interfacesList.Add($"{context.Qualifier.Qualify(RuntimeTypeNames.IHasKey, context.RootNamespace)}<{keyType}>");
        if (package.UpgradedPackageId is not null)
            interfacesList.Add(context.Qualifier.Qualify(RuntimeTypeNames.IUpgradeable, context.RootNamespace));
        var interfaces = string.Join(", ", interfacesList);

        if (options.UseRecordTypes && options.UsePrimaryConstructors && fields.Count > 0)
        {
            indent.Append($"public sealed partial record {className}(");
            recordSerialization.WriteRecordParameters(indent, fields);
            indent.AppendLine($") : {interfaces}");
        }
        else if (options.UseRecordTypes)
        {
            indent.AppendLine($"public sealed partial record {className} : {interfaces}");
        }
        else
        {
            indent.AppendLine($"public sealed partial class {className} : {interfaces}");
        }

        indent.AppendLine("{");
        indent.Indent();

        WriteTemplateMetadata(indent, package, module, template);

        if (template.Key is not null)
        {
            WriteKeyProperty(indent, template.Key);
        }

        if (!options.UsePrimaryConstructors || !options.UseRecordTypes)
        {
            recordSerialization.WriteProperties(indent, fields);
        }

        recordSerialization.WriteToRecordMethod(indent, fields);
        recordSerialization.WriteFromRecordMethod(indent, className, fields);

        choiceEmitter.WriteChoiceDescriptors(indent, template);

        WriteContractIdClass(indent, className);
        WriteContractClass(indent, className);

        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine();

        choiceEmitter.WriteChoiceResultStructs(indent, template, moduleNamespace);
        choiceEmitter.WriteChoiceAsyncExercisersClass(indent, template, className, fields, dataTypes);
        submissionExtensions.TryWriteSubmissionExtensions(indent, template, fields);
        choiceEmitter.TryWriteNonContractChoiceExtensions(indent, template, dataTypes);
    }

    /// <summary>
    /// Writes a partial template record body whose sole member is the nested
    /// choice-argument record for <paramref name="choice"/>, mirroring the
    /// serialization surface the standalone record would have carried.
    /// </summary>
    internal void WriteNestedChoiceArgumentType(
        IndentWriter indent,
        DamlTemplate template,
        DamlChoice choice,
        DamlDataType argDataType)
    {
        var templateClassName = EmitterHelpers.SanitizeIdentifier(template.Name);
        indent.AppendLine($"public sealed partial record {templateClassName}");
        indent.AppendLine("{");
        indent.Indent();

        if (argDataType.Definition is DamlRecordDefinition record)
        {
            var choiceTypeName = EmitterHelpers.SanitizeIdentifier(choice.Name);
            indent.CurrentTypeName = choiceTypeName;

            if (options.GenerateXmlDocs)
            {
                indent.AppendLine("/// <summary>");
                indent.AppendLine($"/// Choice argument type for {choice.Name}.");
                indent.AppendLine("/// </summary>");
            }

            if (options.UseRecordTypes && options.UsePrimaryConstructors && record.Fields.Count > 0)
            {
                indent.Append($"public sealed record {choiceTypeName}(");
                recordSerialization.WriteRecordParameters(indent, record.Fields);
                indent.AppendLine($") : {context.Qualifier.Qualify(RuntimeTypeNames.IDamlRecord, context.RootNamespace)}");
            }
            else
            {
                indent.AppendLine($"public sealed record {choiceTypeName} : {context.Qualifier.Qualify(RuntimeTypeNames.IDamlRecord, context.RootNamespace)}");
            }

            indent.AppendLine("{");
            indent.Indent();

            recordSerialization.WriteToRecordMethod(indent, record.Fields);
            recordSerialization.WriteFromRecordMethod(indent, choiceTypeName, record.Fields);

            indent.Dedent();
            indent.AppendLine("}");
        }

        indent.Dedent();
        indent.AppendLine("}");
    }

    private void WriteTemplateMetadata(
        IndentWriter indent,
        DamlPackage package,
        DamlModule module,
        DamlTemplate template)
    {
        indent.Require("System");

        if (options.GenerateXmlDocs)
            indent.AppendLine("/// <summary>Gets the template identifier.</summary>");
        indent.AppendLine($"public static {context.Qualifier.Qualify(RuntimeTypeNames.Identifier, context.RootNamespace)} TemplateId {{ get; }} = new(\"{package.PackageId}\", \"{module.Name}\", \"{template.Name}\");");
        indent.AppendLine();

        if (options.GenerateXmlDocs)
            indent.AppendLine("/// <summary>Gets the package ID.</summary>");
        indent.AppendLine($"public static string {nameof(Daml.Runtime.Contracts.ITemplate.PackageId)} => \"{package.PackageId}\";");
        indent.AppendLine();

        if (options.GenerateXmlDocs)
            indent.AppendLine("/// <summary>Gets the package name.</summary>");
        indent.AppendLine($"public static string {nameof(Daml.Runtime.Contracts.ITemplate.PackageName)} => \"{package.Name}\";");
        indent.AppendLine();

        if (options.GenerateXmlDocs)
            indent.AppendLine("/// <summary>Gets the package version.</summary>");
        indent.AppendLine($"public static Version {nameof(Daml.Runtime.Contracts.ITemplate.PackageVersion)} {{ get; }} = new({package.Version.Major}, {package.Version.Minor}, {package.Version.Build});");
        indent.AppendLine();

        if (options.GenerateXmlDocs)
            indent.AppendLine("/// <summary>Gets the compile-time Daml type descriptor.</summary>");
        indent.AppendLine($"public static {context.Qualifier.Qualify(RuntimeTypeNames.DamlTypeDescriptor, context.RootNamespace)} DamlTypeId {{ get; }} = new(TemplateId, {context.Qualifier.Qualify(RuntimeTypeNames.DamlTypeKind, context.RootNamespace)}.Template, PackageName);");
        indent.AppendLine();

        if (package.UpgradedPackageId is not null)
        {
            if (options.GenerateXmlDocs)
                indent.AppendLine("/// <summary>Gets the package ID that this package upgrades.</summary>");
            indent.AppendLine($"public static string? UpgradedPackageId => \"{package.UpgradedPackageId}\";");
            indent.AppendLine();
        }
    }

    private static string ToCrefTypeArgument(string csharpType) =>
        csharpType
            .Replace("global::", string.Empty, StringComparison.Ordinal)
            .Replace('<', '{')
            .Replace('>', '}');

    private void WriteKeyProperty(IndentWriter indent, DamlType keyType)
    {
        indent.Require(RuntimeNamespaces.Contracts);
        StdlibPackages.RequireForFieldType(resolver, indent, keyType);
        var csharpKeyType = mapper.MapType(keyType);
        var crefKeyType = ToCrefTypeArgument(csharpKeyType);

        // Translating the template's `key` Daml expression to a C# projection is
        // not yet implemented; the intermediate model carries only the key type, not
        // the expression. Until that projection (or an upstream Daml-LF projection)
        // lands, the accessor throws. ADR 0013 records why this reverts the
        // partial-property contract (the CS9248 compile gate blocked the automated
        // DAR publish pipeline, which has no human to supply an implementing partial).
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Gets the contract key of type <c>{crefKeyType}</c>, satisfying <see cref=\"global::Daml.Runtime.Contracts.IHasKey{{TKey}}\"/>.");
            indent.AppendLine("/// </summary>");
            indent.AppendLine("/// <remarks>");
            indent.AppendLine("/// Throws <see cref=\"global::System.NotImplementedException\"/>: the codegen does not");
            indent.AppendLine("/// yet translate the template's <c>key</c> expression into a C# projection.");
            indent.AppendLine("/// The key type is fully generated and serializable — construct a key value");
            indent.AppendLine("/// explicitly for key-based ledger operations rather than reading it here.");
            indent.AppendLine("/// </remarks>");
        }
        indent.AppendLine($"public {csharpKeyType} Key => throw new global::System.NotImplementedException(");
        indent.AppendLine($"    \"Contract-key projection is not generated yet by daml-codegen-csharp; construct the {csharpKeyType} key value explicitly for key-based ledger operations.\");");
        indent.AppendLine();
    }

    private void WriteContractIdClass(IndentWriter indent, string className)
    {
        indent.Require(RuntimeNamespaces.Commands);
        indent.Require(RuntimeNamespaces.Contracts);
        if (options.GenerateXmlDocs)
            indent.AppendLine($"/// <summary>Contract ID for {className}.</summary>");
        indent.AppendLine($"public sealed record ContractId(string Value) : {context.Qualifier.Qualify(RuntimeTypeNames.ContractId, context.RootNamespace)}<{className}>(Value), {context.Qualifier.Qualify(RuntimeTypeNames.IExercises, context.RootNamespace)}<{className}>");
        indent.AppendLine("{");
        indent.Indent();

        indent.AppendLine($"{context.Qualifier.Qualify(RuntimeTypeNames.ContractId, context.RootNamespace)}<{className}> {context.Qualifier.Qualify(RuntimeTypeNames.IExercises, context.RootNamespace)}<{className}>.ContractId => this;");

        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine();
    }

    private void WriteContractClass(IndentWriter indent, string className)
    {
        indent.Require(RuntimeNamespaces.Contracts);
        if (options.GenerateXmlDocs)
            indent.AppendLine($"/// <summary>Active contract for {className}.</summary>");
        indent.AppendLine($"public sealed record Contract(ContractId Id, {className} Data) : {context.Qualifier.Qualify(RuntimeTypeNames.IContract, context.RootNamespace)}<ContractId, {className}>");
        indent.AppendLine("{");
        indent.Indent();

        if (options.GenerateXmlDocs)
            indent.AppendLine("/// <summary>Creates a Contract from a CreatedEvent.</summary>");
        indent.AppendLine($"public static Contract FromCreatedEvent({context.Qualifier.Qualify(RuntimeTypeNames.CreatedEvent, context.RootNamespace)} @event) =>");
        indent.Indent();
        indent.AppendLine($"new(new ContractId(@event.ContractId), {className}.FromRecord(@event.CreateArguments));");
        indent.Dedent();

        indent.Dedent();
        indent.AppendLine("}");
    }
}
