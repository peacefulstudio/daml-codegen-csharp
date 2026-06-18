// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.Model;

namespace Daml.Codegen.CSharp.CodeGen;

public sealed partial class ChoiceEmitter
{
    /// <summary>
    /// Returns <c>true</c> when the choice is the synthetic <c>Archive</c>
    /// imported from <c>DA.Internal.Template</c> in a Daml stdlib package.
    /// Archive's choice machinery is already exposed via the existing
    /// <c>Choice&lt;Choice&gt;</c> property; a typed exerciser would add nothing.
    /// Gating on the stdlib package id prevents a user-defined choice named
    /// <c>Archive</c> with the same module path from being falsely suppressed.
    /// </summary>
    private bool IsArchiveChoice(DamlChoice choice)
    {
        if (!string.Equals(choice.Name, "Archive", StringComparison.Ordinal))
        {
            return false;
        }
        if (choice.ArgumentType is not DamlTypeRef { Module: "DA.Internal.Template", Name: "Archive" } archiveTypeRef)
        {
            return false;
        }
        if (string.IsNullOrEmpty(archiveTypeRef.PackageId))
        {
            return false;
        }
        var pkg = resolver.LookupPackage(archiveTypeRef.PackageId);
        return pkg is not null && IsStdlibPackage(pkg.Name);
    }

    private string MapNonContractReturnType(DamlType returnType) => returnType switch
    {
        DamlPrimitiveType { Primitive: DamlPrimitive.Unit } => context.Qualifier.Qualify(RuntimeTypeNames.Unit, context.RootNamespace),
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional },
                      Arguments: [DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional } }] } =>
            throw new NotSupportedException("Codegen does not support nested Optional types (Optional (Optional t)). C# nullable syntax cannot represent the Some Nothing / Nothing distinction without a wrapper type. Refactor the Daml signature, or open a feature request to introduce a representable CLR model."),
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

        // Mirror the gate applied to <Choice>Async emission for CID-returning
        // choices: skip choices whose argument type went through the
        // codegen fallback path. The fallback emits a field-less <Choice>Arg
        // stub with no ToRecord(), so `argument.ToRecord()` would not compile
        // in consumer output.
        var emittable = template.Choices
            .Where(c =>
            {
                if (ChoiceCreatedSlots.Extract(context, resolver, mapper, c.ReturnType).Count > 0
                    || IsArchiveChoice(c))
                {
                    return false;
                }
                var (_, _, isFallback, _) = GetChoiceArgumentInfo(c, dataTypes);
                return !isFallback;
            })
            .ToList();

        if (emittable.Count == 0)
        {
            return false;
        }

        RequireAsyncExerciserNamespaces(indent);

        indent.AppendLine();
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Async exerciser extensions for <see cref=\"{className}\"/> contract IDs whose choices");
            indent.AppendLine("/// return a non-contract-id payload (Decimal, records, lists, Unit, etc.).");
            indent.AppendLine("/// Each method submits the choice via");
            indent.AppendLine("/// <c>ILedgerClient.TrySubmitAndWaitForTransactionAsync</c> and lifts the typed result");
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
            WriteSingleNonContractChoiceAsyncExerciser(indent, emittable[i], className, dataTypes);
        }

        // Per-choice projector helpers, emitted after the public methods so they
        // visually follow the call sites that reference them.
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
        IReadOnlyDictionary<string, DamlDataType> dataTypes)
    {
        var choiceName = SanitizeIdentifier(choice.Name);
        var returnTypeName = MapNonContractReturnType(choice.ReturnType);
        var (argTypeName, _, _, isNestedTemplateArg) = GetChoiceArgumentInfo(choice, dataTypes);
        var hasArg = argTypeName != "DamlUnit";

        StdlibPackages.RequireForFieldType(resolver, indent, choice.ReturnType);

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
            indent.AppendLine("/// <param name=\"actAs\">The party submitting the command.</param>");
            indent.AppendLine("/// <param name=\"workflowId\">Optional workflow id; passed through to the ledger when supplied. No default — workflow IDs are correlation keys, and a per-choice default would bucket every submission of the same choice under one ID.</param>");
            indent.AppendLine("/// <param name=\"cancellationToken\">Cancellation token.</param>");
        }

        indent.AppendLine($"public static async Task<{context.Qualifier.Qualify(RuntimeTypeNames.ExerciseOutcome, context.RootNamespace)}<{returnTypeName}>> {choiceName}Async(");
        indent.Indent();
        indent.AppendLine($"this {context.Qualifier.Qualify(RuntimeTypeNames.ContractId, context.RootNamespace)}<{templateClassName}> contractId,");
        indent.AppendLine($"{context.Qualifier.Qualify(RuntimeTypeNames.ILedgerClient, context.RootNamespace)} client,");
        if (hasArg)
        {
            var argParamType = isNestedTemplateArg
                ? $"{templateClassName}.{argTypeName}"
                : argTypeName;
            indent.AppendLine($"{argParamType} argument,");
        }
        indent.AppendLine($"{context.Qualifier.Qualify(RuntimeTypeNames.Party, context.RootNamespace)} actAs,");
        indent.AppendLine("string? workflowId = null,");
        indent.AppendLine("CancellationToken cancellationToken = default)");
        indent.Dedent();
        indent.AppendLine("{");
        indent.Indent();

        indent.AppendLine("ArgumentNullException.ThrowIfNull(contractId);");
        indent.AppendLine("ArgumentNullException.ThrowIfNull(client);");

        var argExpr = hasArg ? "argument.ToRecord()" : $"{context.Qualifier.Qualify(RuntimeTypeNames.DamlUnit, context.RootNamespace)}.Instance";
        indent.AppendLine();
        indent.AppendLine($"var command = new {context.Qualifier.Qualify(RuntimeTypeNames.ExerciseCommand, context.RootNamespace)}(");
        indent.Indent();
        indent.AppendLine($"{templateClassName}.TemplateId,");
        indent.AppendLine("contractId,");
        indent.AppendLine($"new {context.Qualifier.Qualify(RuntimeTypeNames.ChoiceName, context.RootNamespace)}(\"{choice.Name}\"),");
        indent.AppendLine($"{argExpr});");
        indent.Dedent();

        indent.AppendLine();
        indent.AppendLine($"var submission = {context.Qualifier.Qualify(RuntimeTypeNames.CommandsSubmission, context.RootNamespace)}.Single(command)");
        indent.Indent();
        indent.AppendLine(".WithActAs(actAs)");
        indent.AppendLine($".WithCommandId(new {context.Qualifier.Qualify(RuntimeTypeNames.CommandId, context.RootNamespace)}(Guid.NewGuid().ToString()));");
        indent.Dedent();
        indent.AppendLine("if (!string.IsNullOrEmpty(workflowId))");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine($"submission = submission.WithWorkflowId(new {context.Qualifier.Qualify(RuntimeTypeNames.WorkflowId, context.RootNamespace)}(workflowId));");
        indent.Dedent();
        indent.AppendLine("}");

        indent.AppendLine();
        indent.AppendLine("var outcome = await client.TrySubmitAndWaitForTransactionAsync(submission, cancellationToken).ConfigureAwait(false);");
        indent.AppendLine();
        indent.AppendLine("return outcome switch");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine($"{context.Qualifier.Qualify(RuntimeTypeNames.ExerciseOutcome, context.RootNamespace)}<{context.Qualifier.Qualify(RuntimeTypeNames.TransactionResult, context.RootNamespace)}>.One success => Project{choiceName}Result(success.Result, contractId.Value),");
        indent.AppendLine($"{context.Qualifier.Qualify(RuntimeTypeNames.ExerciseOutcome, context.RootNamespace)}<{context.Qualifier.Qualify(RuntimeTypeNames.TransactionResult, context.RootNamespace)}>.DamlError damlError => new {context.Qualifier.Qualify(RuntimeTypeNames.ExerciseOutcome, context.RootNamespace)}<{returnTypeName}>.DamlError(damlError.Category, damlError.ErrorId, damlError.Message, damlError.Metadata),");
        indent.AppendLine($"{context.Qualifier.Qualify(RuntimeTypeNames.ExerciseOutcome, context.RootNamespace)}<{context.Qualifier.Qualify(RuntimeTypeNames.TransactionResult, context.RootNamespace)}>.InfraError infraError => new {context.Qualifier.Qualify(RuntimeTypeNames.ExerciseOutcome, context.RootNamespace)}<{returnTypeName}>.InfraError(infraError.StatusCode, infraError.Message),");
        indent.AppendLine("_ => throw new InvalidOperationException($\"Unhandled outcome: {outcome.GetType().Name}\"),");
        indent.Dedent();
        indent.AppendLine("};");

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

        // Filter on (ContractId, TemplateId by (ModuleName, EntityName), ChoiceName)
        // so nested exercises of the same choice on other contracts within the same
        // transaction don't get returned by mistake. TemplateId is matched on
        // (ModuleName, EntityName) only — not the full Identifier — so package-id
        // drift from upgrades doesn't break projection. Mirrors the same
        // drift-tolerant comparison used by the created-contract projector.
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
        indent.AppendLine("\"This is most often caused by the ILedgerClient implementation not populating TransactionResult.ExercisedEvents — \" +");
        indent.AppendLine("\"your ILedgerClient implementation must project the transaction's exercised events into TransactionResult.ExercisedEvents. \" +");
        indent.AppendLine("\"If your implementation does populate ExercisedEvents, ensure the participant is configured to return \" +");
        indent.AppendLine("\"LedgerEffects with verbose events so the exercise event survives projection.\");");
        indent.Dedent();

        indent.Dedent();
        indent.AppendLine("}");
    }
}
