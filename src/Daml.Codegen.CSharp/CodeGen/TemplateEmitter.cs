// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RuntimeNamespaces = Daml.Runtime.RuntimeNamespaces;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Emits the C# for a Daml template: the sealed template record with its
/// <see cref="Daml.Runtime.Contracts.ITemplate"/> facet (plus the optional
/// <c>IUpgradeable</c> facet, plus one <c>IImplements</c> per implemented
/// interface, plus <c>IHasKey</c> and its static <c>Key</c> witness when the
/// template declares a contract key), the static template metadata, the nested <c>ContractId</c> /
/// <c>Contract</c> records — the latter carrying the contract key read off the
/// created event when the template declares one —
/// and the namespace-level choice / submission extension surface. The
/// field-bearing serialization surface (constructor parameters, properties,
/// <c>ToRecord</c> / <c>FromRecord</c>) is delegated to the shared
/// <see cref="RecordSerializationEmitter"/>, the choice descriptors / exercisers
/// to the shared <see cref="ChoiceEmitter"/>, and the typed-submitter surface to
/// the shared <see cref="SubmissionExtensionsEmitter"/> — the same per-package
/// instances the sibling emitters use, so record, template, and choice output stay
/// byte-identical. Constructed once per package over the package's
/// <see cref="PackageEmitContext"/>, the DAR-scoped <see cref="ICrossPackageResolver"/>,
/// those three composed emitters, and the shared <see cref="CodeGenOptions"/>. The
/// contract-key slot is mapped through its own <see cref="DamlTypeMapper"/> over a
/// <see cref="PackageQualifiedResolver"/>, so it does not share the sibling emitters'
/// unqualified names. The caller owns the file scaffold and the
/// common usings; this emitter writes the template body into the provided
/// <see cref="IndentWriter"/>.
/// </summary>
internal sealed partial class TemplateEmitter(
    PackageEmitContext context,
    ICrossPackageResolver resolver,
    RecordSerializationEmitter recordSerialization,
    ChoiceEmitter choiceEmitter,
    SubmissionExtensionsEmitter submissionExtensions,
    CodeGenOptions options,
    ILogger? logger = null)
{
    private const string NestedContractIdTypeName = "ContractId";
    private const string NestedContractTypeName = "Contract";
    private const string KeyMemberName = "Key";
    private const string KeyEncoderMemberName = "KeyEncoder";
    private const string KeyEncoderParameterName = "key";
    private const string KeyDecoderMemberName = "KeyDecoder";
    private const string KeyDecoderParameterName = "value";

    private readonly ILogger _log = logger ?? NullLogger.Instance;

    /// <summary>
    /// Writes the template record, its static metadata,
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
        if (className is NestedContractIdTypeName or NestedContractTypeName)
        {
            throw new CodegenException(
                $"Daml template {module.Name}:{template.Name} maps to the C# type '{className}', which is also the name of a record this emitter nests inside it. "
                + "A nested type may not share the name of its enclosing type (CS0542), so the generated code would not compile. "
                + "Rename the template in the Daml model.");
        }

        indent.CurrentTypeName = className;

        var keyWitness = DescribeKeyWitness(className, template.Key, fields);

        var interfacesList = new List<string> { context.Qualifier.Qualify(RuntimeTypeNames.ITemplate, context.RootNamespace) };
        if (package.UpgradedPackageId is not null)
            interfacesList.Add(context.Qualifier.Qualify(RuntimeTypeNames.IUpgradeable, context.RootNamespace));
        foreach (var implemented in template.Implements)
            interfacesList.Add($"{context.Qualifier.Qualify(RuntimeTypeNames.IImplements, context.RootNamespace)}<{resolver.Resolve(implemented, context)}>");
        if (keyWitness is not null)
            interfacesList.Add(keyWitness.FacetType);
        interfacesList.Add($"{context.Qualifier.Qualify(RuntimeTypeNames.IDamlRecord, context.RootNamespace)}<{className}>");
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

        if (keyWitness is not null)
        {
            WriteKeyWitness(indent, module, template, keyWitness);
        }

        if (!options.UsePrimaryConstructors || !options.UseRecordTypes)
        {
            recordSerialization.WriteProperties(indent, fields);
        }

        recordSerialization.WriteToRecordMethod(indent, fields, []);
        recordSerialization.WriteFromRecordMethod(indent, className, fields, []);

        choiceEmitter.WriteChoiceDescriptors(indent, template);

        choiceEmitter.WriteChoiceByKeyCommandBuilders(indent, template, className, dataTypes);

        WriteContractIdClass(indent, className);
        WriteContractClass(indent, className, template.Key);

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

            recordSerialization.WriteToRecordMethod(indent, record.Fields, []);
            recordSerialization.WriteFromRecordMethod(indent, choiceTypeName, record.Fields, []);

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

    /// <summary>
    /// The resolved pieces of a keyed template's <c>IHasKey</c> facet: the facet named in the
    /// base list, the descriptor type the witness is declared as, the encode expression over a
    /// lambda parameter named <c>key</c>, the decode expression over a lambda parameter named
    /// <c>value</c>, and whichever declaration already takes the C# member name <c>Key</c> on
    /// the template record.
    /// </summary>
    private sealed record KeyWitness(
        string FacetType,
        string DescriptorType,
        string Encoder,
        string Decoder,
        bool TemplateNameTakesTheMemberName,
        string? FieldTakingTheMemberName)
    {
        public bool MemberNameIsTaken =>
            TemplateNameTakesTheMemberName || FieldTakingTheMemberName is not null;
    }

    private KeyWitness? DescribeKeyWitness(
        string className,
        DamlType? keyType,
        IReadOnlyList<DamlFieldDefinition> fields)
    {
        if (keyType is null)
        {
            return null;
        }

        var typeArguments = $"<{className}, {PackageQualifiedMapper.MapType(keyType)}>";
        var fieldTakingTheMemberName = fields.FirstOrDefault(
            field => Identifiers.MemberName(field.Name, className) == KeyMemberName);

        return new KeyWitness(
            $"{context.Qualifier.Qualify(RuntimeTypeNames.IHasKey, context.RootNamespace)}{typeArguments}",
            $"{context.Qualifier.Qualify(RuntimeTypeNames.KeyDescriptor, context.RootNamespace)}{typeArguments}",
            PackageQualifiedMapper.ToValue(keyType, KeyEncoderParameterName),
            PackageQualifiedMapper.FromValue(keyType, KeyDecoderParameterName),
            className == KeyMemberName,
            fieldTakingTheMemberName?.Name);
    }

    /// <summary>
    /// Writes the keyed template's static <c>Key</c> witness. When the member name is already
    /// taken — by a payload field mapping to it (CS0102), or by the template record itself
    /// being named <c>Key</c> (CS0542) — only the explicit interface implementation is written.
    /// It declares no member name of its own, so the facet stays reachable through a generic
    /// constraint while the template keeps the name it had.
    /// </summary>
    private void WriteKeyWitness(
        IndentWriter indent,
        DamlModule module,
        DamlTemplate template,
        KeyWitness witness)
    {
        indent.Require(RuntimeNamespaces.Contracts);
        StdlibPackages.RequireForFieldType(resolver, context.Package, indent, template.Key!);

        if (witness.MemberNameIsTaken)
        {
            if (witness.FieldTakingTheMemberName is { } fieldName)
            {
                LogKeyFieldTakesTheWitnessName(_log, module.Name, template.Name, fieldName);
            }
            else
            {
                LogTemplateNameTakesTheWitnessName(_log, module.Name, template.Name);
            }

            indent.AppendLine($"static {witness.DescriptorType} {witness.FacetType}.{KeyMemberName} {{ get; }} =");
            WriteKeyWitnessInitializer(indent, witness);
            return;
        }

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>Gets the witness pairing this template with its contract key type and carrying the key codec; passing it to a generic method infers both type parameters from one argument.</summary>");
        }
        indent.AppendLine($"public static {witness.DescriptorType} {KeyMemberName} {{ get; }} =");
        WriteKeyWitnessInitializer(indent, witness);
    }

    private static void WriteKeyWitnessInitializer(IndentWriter indent, KeyWitness witness)
    {
        indent.Indent();
        indent.AppendLine("new()");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine($"{KeyEncoderMemberName} = {KeyEncoderParameterName} => {witness.Encoder},");
        indent.AppendLine($"{KeyDecoderMemberName} = {KeyDecoderParameterName} => {witness.Decoder},");
        indent.Dedent();
        indent.AppendLine("};");
        indent.Dedent();
        indent.AppendLine();
    }

    private void WriteContractIdClass(IndentWriter indent, string className)
    {
        indent.Require(RuntimeNamespaces.Commands);
        indent.Require(RuntimeNamespaces.Contracts);
        if (options.GenerateXmlDocs)
            indent.AppendLine($"/// <summary>Contract ID for {className}.</summary>");
        indent.AppendLine("[global::System.Text.Json.Serialization.JsonConverter(typeof(global::Daml.Runtime.Serialization.ContractIdJsonConverterFactory))]");
        indent.AppendLine($"public sealed record {NestedContractIdTypeName}(string Value) : {context.Qualifier.Qualify(RuntimeTypeNames.ContractId, context.RootNamespace)}<{className}>(Value), {context.Qualifier.Qualify(RuntimeTypeNames.IExercises, context.RootNamespace)}<{className}>");
        indent.AppendLine("{");
        indent.Indent();

        indent.AppendLine($"{context.Qualifier.Qualify(RuntimeTypeNames.ContractId, context.RootNamespace)}<{className}> {context.Qualifier.Qualify(RuntimeTypeNames.IExercises, context.RootNamespace)}<{className}>.ContractId => this;");

        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine();
    }

    private void WriteContractClass(IndentWriter indent, string className, DamlType? keyType)
    {
        indent.Require(RuntimeNamespaces.Contracts);
        if (keyType is not null)
        {
            StdlibPackages.RequireForFieldType(resolver, context.Package, indent, keyType);
        }

        var contractKeyType = keyType is null
            ? null
            : $"{context.Qualifier.Qualify(RuntimeTypeNames.ContractKey, context.RootNamespace)}<{PackageQualifiedMapper.MapType(keyType)}>";

        if (options.GenerateXmlDocs)
            indent.AppendLine($"/// <summary>Active contract for {className}.</summary>");
        indent.AppendLine($"public sealed record {NestedContractTypeName}({NestedContractIdTypeName} Id, {className} Data) : {context.Qualifier.Qualify(RuntimeTypeNames.IContract, context.RootNamespace)}<{NestedContractIdTypeName}, {className}>");
        indent.AppendLine("{");
        indent.Indent();

        if (contractKeyType is not null)
        {
            if (options.GenerateXmlDocs)
                indent.AppendLine("/// <summary>The contract key read off the created event, decoded and paired with the ledger's hash of it.</summary>");
            indent.AppendLine($"public required {contractKeyType} Key {{ get; init; }}");
            indent.AppendLine();
        }

        if (options.GenerateXmlDocs)
            indent.AppendLine($"/// <summary>Creates a {NestedContractTypeName} from a CreatedEvent.</summary>");
        indent.AppendLine($"public static {NestedContractTypeName} FromCreatedEvent({context.Qualifier.Qualify(RuntimeTypeNames.CreatedEvent, context.RootNamespace)} @event) =>");
        indent.Indent();
        if (keyType is null)
        {
            indent.AppendLine($"new(new {NestedContractIdTypeName}(@event.ContractId), {QualifyInPackage(className)}.FromRecord(@event.CreateArguments));");
        }
        else
        {
            indent.AppendLine("new(");
            indent.Indent();
            indent.AppendLine($"new {NestedContractIdTypeName}(@event.ContractId),");
            indent.AppendLine($"{QualifyInPackage(className)}.FromRecord(@event.CreateArguments))");
            indent.Dedent();
            indent.AppendLine("{");
            indent.Indent();
            indent.AppendLine($"Key = @event.ContractKey is {{ }} contractKey");
            indent.Indent();
            indent.AppendLine($"? new {contractKeyType}({PackageQualifiedMapper.FromValue(keyType, "contractKey.Value")}, contractKey.KeyHash)");
            indent.AppendLine($": throw new global::System.InvalidOperationException(\"The created event for contract '\" + @event.ContractId + \"' of keyed template {className} carried no contract key, so the contract key cannot be populated.\"),");
            indent.Dedent();
            indent.Dedent();
            indent.AppendLine("};");
        }
        indent.Dedent();

        indent.Dedent();
        indent.AppendLine("}");
    }

    /// <summary>
    /// Maps the contract-key slot and its decoder. The active contract nests <c>Contract</c>
    /// and <c>ContractId</c> records and declares <c>Id</c> / <c>Data</c> / <c>Key</c>
    /// members, any of which binds ahead of a package type the key names, so every in-package
    /// name in the key slot is resolved <c>global::</c>-qualified.
    /// </summary>
    private DamlTypeMapper PackageQualifiedMapper =>
        _packageQualifiedMapper ??= new DamlTypeMapper(context, new PackageQualifiedResolver(resolver));

    private DamlTypeMapper? _packageQualifiedMapper;

    /// <summary>
    /// Prefixes a bare in-package type name with its <c>global::</c>-qualified namespace,
    /// so the <c>Id</c> / <c>Data</c> / <c>Key</c> properties of the active contract cannot
    /// shadow a type a decoder names. A name that already carries a dot was emitted with its
    /// own qualifier by the cross-package resolver and is left alone. Mirrors the same guard
    /// the choice-result projector applies for the same reason.
    /// </summary>
    private string QualifyInPackage(string typeName) =>
        typeName.Contains('.', StringComparison.Ordinal)
            ? typeName
            : $"global::{context.RootNamespace}.{typeName}";

    [LoggerMessage(
        EventId = 1300,
        Level = LogLevel.Warning,
        Message = "Template {ModuleName}:{TemplateName} has a field {FieldName} whose C# member is named Key, so the contract-key witness is emitted as an explicit IHasKey implementation only. Reach it through a generic constraint on IHasKey, not through the template type directly.")]
    private static partial void LogKeyFieldTakesTheWitnessName(ILogger logger, string moduleName, string templateName, string fieldName);

    [LoggerMessage(
        EventId = 1301,
        Level = LogLevel.Warning,
        Message = "Template {ModuleName}:{TemplateName} maps to a C# type named Key, which a member may not share, so the contract-key witness is emitted as an explicit IHasKey implementation only. Reach it through a generic constraint on IHasKey, not through the template type directly.")]
    private static partial void LogTemplateNameTakesTheWitnessName(ILogger logger, string moduleName, string templateName);
}
