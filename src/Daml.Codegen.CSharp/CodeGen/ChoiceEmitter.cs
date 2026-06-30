// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.Model;
using LedgerNamespaces = Daml.Ledger.Abstractions.LedgerNamespaces;
using RuntimeNamespaces = Daml.Runtime.RuntimeNamespaces;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Emits the C# that <em>exercises</em> a choice: the nested <c>&lt;Choice&gt;Arg</c>
/// fallback type, the <c>Choice&lt;Template, Arg, Result&gt;</c> descriptor with its
/// result decoder, the typed <c>&lt;Choice&gt;Async</c> exercisers (both the
/// contract-id-returning and the value-returning flavour), and the interface-choice
/// extensions. Constructed once per package over the package's
/// <see cref="PackageEmitContext"/>, the DAR-scoped <see cref="ICrossPackageResolver"/>,
/// the package's <see cref="DamlTypeMapper"/>, and the shared <see cref="PartyAnalysis"/>
/// module. Calls the mapper for every type fragment and reads — but does not own — the
/// resolved choice-argument metadata. Distinct from the create/submission path: creating
/// a contract is not exercising a choice.
/// </summary>
public sealed partial class ChoiceEmitter(
    PackageEmitContext context,
    ICrossPackageResolver resolver,
    CodeGenOptions options,
    DamlTypeMapper mapper,
    PartyAnalysis party)
{
    /// <summary>
    /// Emits the choice descriptor surface nested inside the template record: the
    /// <c>&lt;Choice&gt;Arg</c> fallback type and the <c>Choice&lt;...&gt;</c> property
    /// (with its argument encoder and result decoder) for every choice on
    /// <paramref name="template"/>.
    /// </summary>
    internal void WriteChoiceDescriptors(IndentWriter indent, DamlTemplate template)
    {
        foreach (var choice in template.Choices)
        {
            WriteChoiceArgumentType(indent, choice);
            WriteChoiceMethod(indent, choice);
        }
    }

    internal (string TypeName, IReadOnlyList<DamlFieldDefinition>? Fields, bool IsFallback, bool IsNestedTemplateArg) GetChoiceArgumentInfo(
        DamlChoice choice,
        IReadOnlyDictionary<string, DamlDataType> dataTypes)
    {
        if (choice.ArgumentType is DamlTypeRef typeRef
            && context.IsLocalRef(typeRef)
            && dataTypes.TryGetValue(typeRef.Name, out var dataType))
        {
            var fields = dataType.Definition is DamlRecordDefinition recordDef ? recordDef.Fields : null;
            return (SanitizeIdentifier(choice.Name), fields, false, true);
        }

        if (choice.ArgumentType is DamlPrimitiveType { Primitive: DamlPrimitive.Unit })
        {
            return ("DamlUnit", null, false, false);
        }

        if (choice.ArgumentType is DamlTypeRef externalRef)
        {
            if (externalRef is { Name: "Archive", Module: "DA.Internal.Template" }
                && !string.IsNullOrEmpty(externalRef.PackageId)
                && resolver.LookupPackage(externalRef.PackageId) is { } archivePkg
                && (IsStdlibPackage(archivePkg.Name) || IsPlaceholderPackageName(archivePkg.Name)))
            {
                return ("DamlUnit", null, false, false);
            }
            return (resolver.Resolve(externalRef, context), null, false, false);
        }

        return ($"{SanitizeIdentifier(choice.Name)}Arg", null, true, true);
    }

    private void WriteChoiceArgumentType(IndentWriter indent, DamlChoice choice)
    {
        var (_, _, isFallback, _) = GetChoiceArgumentInfo(choice, context.DataTypes);

        if (!isFallback)
        {
            return;
        }

        var choiceName = SanitizeIdentifier(choice.Name);
        indent.AppendLine($"/// <summary>Arguments for the {choice.Name} choice.</summary>");
        indent.AppendLine($"public sealed record {choiceName}Arg");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine("// TODO: Extract fields from argument type");
        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine();
    }

    private void WriteChoiceMethod(IndentWriter indent, DamlChoice choice)
    {
        var dataTypes = context.DataTypes;
        var choiceName = SanitizeIdentifier(choice.Name);
        var returnType = mapper.MapType(choice.ReturnType);
        var (argTypeName, _, isFallback, _) = GetChoiceArgumentInfo(choice, dataTypes);

        if (isFallback)
        {
            return;
        }

        indent.Require(RuntimeNamespaces.Commands);
        StdlibPackages.RequireForFieldType(resolver, indent, choice.ReturnType);

        indent.AppendLine("/// <summary>");
        indent.AppendLine($"/// Exercise the {choice.Name} choice.");
        if (choice.Consuming)
        {
            indent.AppendLine("/// This choice is consuming and will archive the contract.");
        }
        indent.AppendLine("/// </summary>");

        var argTypeRef = argTypeName == "DamlUnit"
            ? context.Qualifier.Qualify(RuntimeTypeNames.DamlUnit, context.RootNamespace)
            : argTypeName;
        indent.AppendLine($"public static {context.Qualifier.Qualify(RuntimeTypeNames.Choice, context.RootNamespace)}<{indent.CurrentTypeName}, {argTypeRef}, {returnType}> Choice{choiceName} {{ get; }} = new()");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine($"Name = new {context.Qualifier.Qualify(RuntimeTypeNames.ChoiceName, context.RootNamespace)}(\"{choice.Name}\"),");
        indent.AppendLine($"Consuming = {(choice.Consuming ? "true" : "false")},");

        if (argTypeName == "DamlUnit")
        {
            indent.AppendLine($"ArgumentEncoder = _ => {context.Qualifier.Qualify(RuntimeTypeNames.DamlUnit, context.RootNamespace)}.Instance,");
        }
        else
        {
            indent.AppendLine("ArgumentEncoder = arg => arg.ToRecord(),");
        }

        WriteResultDecoder(indent, choice.ReturnType, returnType);

        indent.Dedent();
        indent.AppendLine("};");
        indent.AppendLine();
    }

    private void WriteResultDecoder(IndentWriter indent, DamlType returnType, string csharpReturnType)
    {
        // Keep the canonical short forms for trivial cases (Unit and the primitive
        // ContractId form) where the call-site reads more naturally than the helper's
        // output. Every other case — including type-refs (record/variant/enum) —
        // delegates to GetFromValueConversion so the result decoder picks up the same
        // module-qualified enum dispatch and TextMap/GenMap/Optional/List handling
        // that field deserialization uses. Earlier hand-rolled paths here used a
        // simple-name enum check that diverged from the module-qualified version,
        // and would silently route an enum return through DamlRecord.As<>() when a
        // same-named record existed in another module of the same package.
        switch (returnType)
        {
            case DamlPrimitiveType { Primitive: DamlPrimitive.Unit }:
                indent.AppendLine($"ResultDecoder = _ => {context.Qualifier.Qualify(RuntimeTypeNames.DamlUnit, context.RootNamespace)}.Instance");
                return;
            case DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.ContractId }, Arguments: [var arg] }:
                var contractType = mapper.MapType(arg);
                indent.AppendLine($"ResultDecoder = val => new {context.Qualifier.Qualify(RuntimeTypeNames.ContractId, context.RootNamespace)}<{contractType}>(val.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlContractId, context.RootNamespace)}>().Value)");
                return;
        }

        var expr = mapper.FromValue(returnType, "val");
        indent.AppendLine($"ResultDecoder = val => {expr}");
    }

    private static void RequireAsyncExerciserNamespaces(IndentWriter indent)
    {
        indent.Require("System");
        indent.Require("System.Threading");
        indent.Require("System.Threading.Tasks");
        indent.Require(LedgerNamespaces.Abstractions);
        indent.Require(RuntimeNamespaces.Commands);
        indent.Require(RuntimeNamespaces.Contracts);
        indent.Require(RuntimeNamespaces.Outcomes);
    }

    private static bool IsStdlibPackage(string packageName) => StdlibPackages.IsStdlibPackage(packageName);

    private static bool IsPlaceholderPackageName(string packageName) => StdlibPackages.IsPlaceholderPackageName(packageName);

    private static string SanitizeIdentifier(string name) => Identifiers.Sanitize(name);

    private static string MemberName(string damlFieldName, string enclosingTypeName) =>
        Identifiers.MemberName(damlFieldName, enclosingTypeName);
}
