// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.CSharp.CodeGen;

internal sealed partial class ChoiceEmitter
{
    private string MapNonContractReturnType(DamlType returnType) => returnType switch
    {
        DamlPrimitiveType { Primitive: DamlPrimitive.Unit } => context.Qualifier.Qualify(RuntimeTypeNames.Unit, context.RootNamespace),
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional },
                      Arguments: [DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional } } or DamlTypeVar] } =>
            mapper.MapType(returnType),
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional }, Arguments: [var arg] } =>
            $"{MapNonContractReturnType(arg)}?",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.List }, Arguments: [var arg] } =>
            $"{context.Qualifier.Qualify("IReadOnlyList", context.RootNamespace)}<{MapNonContractReturnType(arg)}>",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.TextMap }, Arguments: [var arg] } =>
            $"{context.Qualifier.Qualify("IReadOnlyDictionary", context.RootNamespace)}<string, {MapNonContractReturnType(arg)}>",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.GenMap }, Arguments: [var keyArg, var valueArg] } =>
            $"{context.Qualifier.Qualify("IReadOnlyDictionary", context.RootNamespace)}<{MapNonContractReturnType(keyArg)}, {MapNonContractReturnType(valueArg)}>",
        _ => mapper.MapType(returnType),
    };

    private static bool ReturnTypeNeedsStdlibUnitDecoder(DamlType type) => type switch
    {
        DamlPrimitiveType { Primitive: DamlPrimitive.Unit } => true,
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional }, Arguments: [var arg] } =>
            ReturnTypeNeedsStdlibUnitDecoder(arg),
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.List }, Arguments: [var arg] } =>
            ReturnTypeNeedsStdlibUnitDecoder(arg),
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.TextMap }, Arguments: [var arg] } =>
            ReturnTypeNeedsStdlibUnitDecoder(arg),
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.GenMap }, Arguments: [var keyArg, var valueArg] } =>
            ReturnTypeNeedsStdlibUnitDecoder(keyArg) || ReturnTypeNeedsStdlibUnitDecoder(valueArg),
        _ => false,
    };

    private string RenderNonContractReturnDecoder(
        DamlType returnType,
        string valueExpr) => returnType switch
    {
        DamlPrimitiveType { Primitive: DamlPrimitive.Unit } => context.Qualifier.Qualify(RuntimeTypeNames.Unit, context.RootNamespace) + ".Value",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional },
                      Arguments: [DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional } } or DamlTypeVar] } =>
            mapper.FromValue(returnType, valueExpr),
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional }, Arguments: [var arg] } =>
            $"{valueExpr}.AsOptional().HasValue ? {RenderNonContractReturnDecoder(arg, $"{valueExpr}.AsOptional().Value!")} : null",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.List }, Arguments: [var arg] } =>
            $"{valueExpr}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlList, context.RootNamespace)}>().Values.Select(x => {RenderNonContractReturnDecoder(arg, "x")}).ToList()",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.TextMap }, Arguments: [var arg] } =>
            $"{valueExpr}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlTextMap, context.RootNamespace)}>().Values.ToDictionary(kv => kv.Key, kv => {RenderNonContractReturnDecoder(arg, "kv.Value")})",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.GenMap }, Arguments: [var keyArg, var valueArg] } =>
            $"{valueExpr}.As<{context.Qualifier.Qualify(RuntimeTypeNames.DamlGenMap, context.RootNamespace)}>().Entries.ToDictionary(kv => {RenderNonContractReturnDecoder(keyArg, "kv.Key")}, kv => {RenderNonContractReturnDecoder(valueArg, "kv.Value")})",
        _ => mapper.FromValue(returnType, valueExpr),
    };

    /// <summary>
    /// Emits a static <c>&lt;TemplateName&gt;NonContractExtensions</c> class
    /// with one <c>&lt;Choice&gt;Async</c> extension per non-CID-returning
    /// choice on <paramref name="template"/>, plus a private projector helper
    /// per choice that walks <c>tx.ExercisedEvents</c> and runs the choice's
    /// <c>ResultDecoder</c>. Returns <c>true</c> when at least one extension
    /// was emitted (so the caller can decide whether the per-template
    /// extensions class is needed at all).
    /// </summary>
    internal bool TryWriteNonContractChoiceExtensions(
        IndentWriter indent,
        DamlTemplate template,
        IReadOnlyDictionary<string, DamlDataType> dataTypes)
    {
        var className = SanitizeIdentifier(template.Name);

        var emittable = template.Choices
            .Where(c => ChoiceCreatedSlots.Extract(context, resolver, mapper, c.ReturnType).Count == 0)
            .ToList();

        if (emittable.Count == 0)
        {
            return false;
        }

        EmittedUsings.RequireAsyncExerciserNamespaces(indent);

        indent.AppendLine();
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Async exerciser extensions for <see cref=\"{className}\"/> contract IDs whose choices");
            indent.AppendLine("/// return a non-contract-id payload (Decimal, records, lists, Unit, etc.).");
            indent.AppendLine("/// Each method submits the choice via");
            indent.AppendLine("/// <c>SingleCommandExtensions.TrySubmitSingleAsync</c> and lifts the typed result");
            indent.AppendLine("/// into <c>ExerciseOutcome&lt;TReturn&gt;</c>.");
            indent.AppendLine("/// </summary>");
        }
        indent.AppendLine($"public static class {className}NonContractExtensions");
        indent.AppendLine("{");
        indent.Indent();

        for (var i = 0; i < emittable.Count; i++)
        {
            if (i > 0)
            {
                indent.AppendLine();
            }
            WriteChoiceCommandBuilder(indent, emittable[i], className, dataTypes);
            indent.AppendLine();
            WriteSingleNonContractChoiceAsyncExerciser(
                indent, emittable[i], className, dataTypes, SubmitterInfoParameter());
        }

        foreach (var choice in emittable)
        {
            indent.AppendLine();
            WriteExerciseProjector(indent, choice, className);
        }

        indent.Dedent();
        indent.AppendLine("}");

        return true;
    }

    private void WriteSingleNonContractChoiceAsyncExerciser(
        IndentWriter indent,
        DamlChoice choice,
        string templateClassName,
        IReadOnlyDictionary<string, DamlDataType> dataTypes,
        ChoiceSubmitterParameter submitter)
    {
        var choiceName = SanitizeIdentifier(choice.Name);
        var returnTypeName = MapNonContractReturnType(choice.ReturnType);
        var argument = GetChoiceArgumentInfo(choice, dataTypes);
        var hasArg = argument.HasArgument;

        StdlibPackages.RequireForFieldType(resolver, context.Package, indent, choice.ReturnType);

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Exercises the {choice.Name} choice and lifts the choice's exercise result to");
            indent.AppendLine($"/// <see cref=\"ExerciseOutcome{{T}}\"/> over <c>{returnTypeName}</c>. Structured Canton/Daml errors");
            indent.AppendLine("/// and infrastructure/transport errors pass through unchanged.");
            indent.AppendLine("/// </summary>");
            indent.AppendLine("/// <param name=\"contractId\">The contract on which to exercise the choice.</param>");
            indent.AppendLine("/// <param name=\"client\">The ledger client.</param>");
            if (hasArg)
            {
                indent.AppendLine("/// <param name=\"argument\">The choice argument.</param>");
            }
            indent.AppendLine($"/// <param name=\"{submitter.Name}\">{submitter.DocSummary}</param>");
            WriteSubmissionParameterDocs(indent);
        }

        indent.AppendLine($"public static async Task<{context.Qualifier.Qualify(RuntimeTypeNames.ExerciseOutcome, context.RootNamespace)}<{returnTypeName}>> {choiceName}Async(");
        indent.Indent();
        indent.AppendLine($"this {context.Qualifier.Qualify(RuntimeTypeNames.ContractId, context.RootNamespace)}<{templateClassName}> contractId,");
        indent.AppendLine($"{context.Qualifier.Qualify(RuntimeTypeNames.ILedgerWriter, context.RootNamespace)} client,");
        if (hasArg)
        {
            indent.AppendLine($"{argument.ParameterType(templateClassName)} argument,");
        }
        indent.AppendLine($"{submitter.TypeName} {submitter.Name},");
        WriteSubmissionParametersAndCloseSignature(indent);
        indent.Dedent();
        indent.AppendLine("{");
        indent.Indent();

        indent.AppendLine("ArgumentNullException.ThrowIfNull(client);");
        indent.AppendLine();

        indent.AppendLine(hasArg
            ? $"var command = contractId.{choiceName}Command(argument);"
            : $"var command = contractId.{choiceName}Command();");

        indent.AppendLine();
        indent.AppendLine($"var outcome = await client.TrySubmitSingleAsync(command, {submitter.Name}, workflowId, commandId, timeout, cancellationToken).ConfigureAwait(false);");
        indent.AppendLine();
        indent.AppendLine($"return outcome.ProjectCommitted(tx => Project{choiceName}Result(tx, contractId.Value));");

        indent.Dedent();
        indent.AppendLine("}");
    }

    /// <summary>
    /// Emits a private static helper that locates the matching
    /// <see cref="Daml.Runtime.Data.DamlValue"/> in <c>tx.ExercisedEvents</c>,
    /// runs the choice's <c>ResultDecoder</c> over it, and returns
    /// <c>ExerciseOutcome&lt;TReturn&gt;.One(...)</c>. Throws
    /// <see cref="InvalidOperationException"/> when no matching exercise is
    /// present (mirrors the cardinality semantics of upstream's
    /// <c>tx.ExerciseResult&lt;T&gt;(choiceName)</c>).
    /// </summary>
    /// <remarks>
    /// The emitted filter keys on contract id, template id and choice name together, so a nested
    /// exercise of the same choice on another contract in the same transaction is not returned by
    /// mistake. The template id is compared on its module and entity names only, never the full
    /// identifier, so package-id drift from an upgrade does not break projection — the same
    /// drift-tolerant comparison the created-contract projector uses.
    /// </remarks>
    private void WriteExerciseProjector(
        IndentWriter indent,
        DamlChoice choice,
        string templateClassName)
    {
        var choiceName = SanitizeIdentifier(choice.Name);
        var returnTypeName = MapNonContractReturnType(choice.ReturnType);
        var needsStdlibUnitDecoder = ReturnTypeNeedsStdlibUnitDecoder(choice.ReturnType);

        indent.AppendLine($"private static {context.Qualifier.Qualify(RuntimeTypeNames.ExerciseOutcome, context.RootNamespace)}<{returnTypeName}> Project{choiceName}Result({context.Qualifier.Qualify(RuntimeTypeNames.TransactionResult, context.RootNamespace)} tx, string contractId)");
        indent.AppendLine("{");
        indent.Indent();

        indent.AppendLine("foreach (var exercised in tx.ExercisedEvents)");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine($"if (string.Equals(exercised.ContractId, contractId, StringComparison.Ordinal)");
        indent.AppendLine($"    && string.Equals(exercised.TemplateId.ModuleName, {templateClassName}.TemplateId.ModuleName, StringComparison.Ordinal)");
        indent.AppendLine($"    && string.Equals(exercised.TemplateId.EntityName, {templateClassName}.TemplateId.EntityName, StringComparison.Ordinal)");
        indent.AppendLine($"    && string.Equals(exercised.ChoiceName, \"{choice.Name}\", StringComparison.Ordinal))");
        indent.AppendLine("{");
        indent.Indent();

        if (needsStdlibUnitDecoder)
        {
            var decoderExpr = RenderNonContractReturnDecoder(
                choice.ReturnType,
                "exercised.ExerciseResult");
            indent.AppendLine($"return new {context.Qualifier.Qualify(RuntimeTypeNames.ExerciseOutcome, context.RootNamespace)}<{returnTypeName}>.One({decoderExpr});");
        }
        else
        {
            indent.AppendLine($"var decoded = {templateClassName}.Choice{choiceName}.ResultDecoder!(exercised.ExerciseResult);");
            indent.AppendLine($"return new {context.Qualifier.Qualify(RuntimeTypeNames.ExerciseOutcome, context.RootNamespace)}<{returnTypeName}>.One(decoded);");
        }

        indent.Dedent();
        indent.AppendLine("}");
        indent.Dedent();
        indent.AppendLine("}");

        indent.AppendLine();
        indent.AppendLine("throw new InvalidOperationException(");
        indent.Indent();
        indent.AppendLine($"$\"Submission succeeded but no '{choice.Name}' exercise on contract '{{contractId}}' was recorded on transaction {{tx.UpdateId}}. \" +");
        indent.AppendLine("\"This is most often caused by the ILedgerWriter implementation not populating TransactionResult.ExercisedEvents — \" +");
        indent.AppendLine("\"your ILedgerWriter implementation must project the transaction's exercised events into TransactionResult.ExercisedEvents. \" +");
        indent.AppendLine("\"If your implementation does populate ExercisedEvents, ensure the participant is configured to return \" +");
        indent.AppendLine("\"LedgerEffects with verbose events so the exercise event survives projection.\");");
        indent.Dedent();

        indent.Dedent();
        indent.AppendLine("}");
    }
}
