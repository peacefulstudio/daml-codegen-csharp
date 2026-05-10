// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Daml.Codegen.CSharp.DarReader;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Codegen for typed exerciser wrappers covering choices whose return type
/// carries <em>no</em> <c>ContractId</c> slots: <c>Decimal</c>, <c>()</c>,
/// plain records, lists of primitives, optionals of primitives, etc. Each such
/// choice gets a static <c>&lt;Choice&gt;Async</c> extension on
/// <c>ContractId&lt;TemplateName&gt;</c> that calls
/// <c>ILedgerClient.TrySubmitAndWaitForTransactionAsync</c>, walks the resulting
/// transaction's <c>ExercisedEvents</c> for the matching choice, runs the
/// already-emitted <c>Choice&lt;Choice&gt;.ResultDecoder</c> over the choice's
/// <see cref="Daml.Runtime.Data.DamlValue"/> exercise result, and lifts to
/// <c>ExerciseOutcome&lt;TReturn&gt;</c>.
///
/// <para>
/// Returns whose top-level shape exposes a <c>ContractId T</c> directly to
/// <c>ExtractCreatedSlots</c> — bare <c>ContractId T</c>, <c>Optional (ContractId T)</c>,
/// <c>[ContractId T]</c>, and tuples-with-<c>ContractId</c> components — are
/// intentionally not handled here. They flow through issue #60's slot-based
/// projector (<c>&lt;Choice&gt;Result.FromCreatedContracts</c> emitted into
/// <c>&lt;Tpl&gt;Extensions</c>). Splitting on <c>ExtractCreatedSlots</c>
/// keeps the two emission paths from producing duplicate
/// <c>&lt;Choice&gt;Async</c> extension methods on the same target.
/// </para>
///
/// <para>
/// Records-via-<see cref="DamlTypeRef"/> whose fields happen to contain
/// <c>ContractId</c>s do <em>not</em> trigger the slot-based projector
/// (<c>ExtractCreatedSlots</c> intentionally does not unfold record types),
/// so those returns stay on this path and round-trip through the choice's
/// <c>ResultDecoder</c>. They produce a typed result that consumers can use
/// even though the contained <c>ContractId</c>s aren't projected separately.
/// </para>
///
/// <para>
/// This emitter is the implementation of issue #63 (typed choice wrappers for
/// non-contract-id returns). It depends on
/// <see cref="Daml.Runtime.Contracts.TransactionResult.ExercisedEvents"/>
/// (added in PR #80) so the runtime can locate the matching exercise and pull
/// its typed result.
/// </para>
/// </summary>
internal sealed partial class CSharpCodeGenerator
{
    /// <summary>
    /// Returns <c>true</c> when the choice is the synthetic <c>Archive</c>
    /// imported from <c>DA.Internal.Template</c>. Archive's choice machinery is
    /// already exposed via the existing <c>Choice&lt;Choice&gt;</c> property; a
    /// typed exerciser would add nothing.
    /// </summary>
    private static bool IsArchiveChoice(DamlChoice choice) =>
        string.Equals(choice.Name, "Archive", StringComparison.Ordinal)
        && choice.ArgumentType is DamlTypeRef { Module: "DA.Internal.Template", Name: "Archive" };

    private string MapNonContractReturnType(DamlType returnType) => returnType switch
    {
        DamlPrimitiveType { Primitive: DamlPrimitive.Unit } => "Daml.Runtime.Stdlib.Unit",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional },
                      Arguments: [DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional } }] } =>
            throw new NotSupportedException("Codegen does not support nested Optional types (Optional (Optional t)). C# nullable syntax cannot represent the Some Nothing / Nothing distinction without a wrapper type. Refactor the Daml signature, or open a feature request to introduce a representable CLR model."),
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional }, Arguments: [var arg] } =>
            $"{MapNonContractReturnType(arg)}?",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.List }, Arguments: [var arg] } =>
            $"IReadOnlyList<{MapNonContractReturnType(arg)}>",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.TextMap }, Arguments: [var arg] } =>
            $"IReadOnlyDictionary<string, {MapNonContractReturnType(arg)}>",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.GenMap }, Arguments: [var keyArg, var valueArg] } =>
            $"IReadOnlyDictionary<{MapNonContractReturnType(keyArg)}, {MapNonContractReturnType(valueArg)}>",
        _ => MapDamlTypeToCSharp(returnType),
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
        string valueExpr,
        IReadOnlyDictionary<string, DamlDataType>? dataTypes) => returnType switch
    {
        DamlPrimitiveType { Primitive: DamlPrimitive.Unit } => "Daml.Runtime.Stdlib.Unit.Value",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional }, Arguments: [var arg] } =>
            $"{valueExpr}.As<DamlOptional>().HasValue ? {RenderNonContractReturnDecoder(arg, $"{valueExpr}.As<DamlOptional>().Value!", dataTypes)} : null",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.List }, Arguments: [var arg] } =>
            $"{valueExpr}.As<DamlList>().Values.Select(x => {RenderNonContractReturnDecoder(arg, "x", dataTypes)}).ToList()",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.TextMap }, Arguments: [var arg] } =>
            $"{valueExpr}.As<DamlTextMap>().Values.ToDictionary(kv => kv.Key, kv => {RenderNonContractReturnDecoder(arg, "kv.Value", dataTypes)})",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.GenMap }, Arguments: [var keyArg, var valueArg] } =>
            $"{valueExpr}.As<DamlGenMap>().Entries.ToDictionary(kv => {RenderNonContractReturnDecoder(keyArg, "kv.Key", dataTypes)}, kv => {RenderNonContractReturnDecoder(valueArg, "kv.Value", dataTypes)})",
        _ => GetFromValueConversion(returnType, valueExpr, dataTypes),
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
    private bool TryWriteNonContractChoiceExtensions(
        IndentWriter indent,
        DamlTemplate template,
        IReadOnlyDictionary<string, DamlDataType> dataTypes)
    {
        var className = SanitizeIdentifier(template.Name);

        // Mirror the gate applied to <Choice>Async emission for CID-returning
        // choices (#77/#78): skip choices whose argument type went through the
        // codegen fallback path. The fallback emits a field-less <Choice>Arg
        // stub with no ToRecord(), so `argument.ToRecord()` would not compile
        // in consumer output.
        var emittable = template.Choices
            .Where(c =>
            {
                if (ExtractCreatedSlots(c.ReturnType).Count > 0
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
            WriteExerciseProjector(indent, choice, className, dataTypes);
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

        RequireForFieldType(indent, choice.ReturnType);

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

        indent.AppendLine($"public static async Task<ExerciseOutcome<{returnTypeName}>> {choiceName}Async(");
        indent.Indent();
        indent.AppendLine($"this ContractId<{templateClassName}> contractId,");
        indent.AppendLine("ILedgerClient client,");
        if (hasArg)
        {
            var argParamType = isNestedTemplateArg
                ? $"{templateClassName}.{argTypeName}"
                : argTypeName;
            indent.AppendLine($"{argParamType} argument,");
        }
        indent.AppendLine("Party actAs,");
        indent.AppendLine("string? workflowId = null,");
        indent.AppendLine("CancellationToken cancellationToken = default)");
        indent.Dedent();
        indent.AppendLine("{");
        indent.Indent();

        indent.AppendLine("ArgumentNullException.ThrowIfNull(contractId);");
        indent.AppendLine("ArgumentNullException.ThrowIfNull(client);");

        var argExpr = hasArg ? "argument.ToRecord()" : "DamlUnit.Instance";
        indent.AppendLine();
        indent.AppendLine("var command = new ExerciseCommand(");
        indent.Indent();
        indent.AppendLine($"{templateClassName}.TemplateId,");
        indent.AppendLine("contractId.Value,");
        indent.AppendLine($"\"{choice.Name}\",");
        indent.AppendLine($"{argExpr});");
        indent.Dedent();

        indent.AppendLine();
        indent.AppendLine("var submission = CommandsSubmission.Single(command)");
        indent.Indent();
        indent.AppendLine(".WithActAs(actAs)");
        indent.AppendLine(".WithCommandId(Guid.NewGuid().ToString());");
        indent.Dedent();
        indent.AppendLine("if (workflowId is not null)");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine("submission = submission.WithWorkflowId(workflowId);");
        indent.Dedent();
        indent.AppendLine("}");

        indent.AppendLine();
        indent.AppendLine("var outcome = await client.TrySubmitAndWaitForTransactionAsync(submission, cancellationToken).ConfigureAwait(false);");
        indent.AppendLine();
        indent.AppendLine("return outcome switch");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine($"ExerciseOutcome<TransactionResult>.One success => Project{choiceName}Result(success.Result, contractId.Value),");
        indent.AppendLine($"ExerciseOutcome<TransactionResult>.DamlError damlError => new ExerciseOutcome<{returnTypeName}>.DamlError(damlError.Category, damlError.ErrorId, damlError.Message, damlError.Metadata),");
        indent.AppendLine($"ExerciseOutcome<TransactionResult>.InfraError infraError => new ExerciseOutcome<{returnTypeName}>.InfraError(infraError.StatusCode, infraError.Message),");
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
        string templateClassName,
        IReadOnlyDictionary<string, DamlDataType> dataTypes)
    {
        var choiceName = SanitizeIdentifier(choice.Name);
        var returnTypeName = MapNonContractReturnType(choice.ReturnType);
        var needsStdlibUnitDecoder = ReturnTypeNeedsStdlibUnitDecoder(choice.ReturnType);

        indent.AppendLine($"private static ExerciseOutcome<{returnTypeName}> Project{choiceName}Result(TransactionResult tx, string contractId)");
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
                "exercised.ExerciseResult",
                dataTypes);
            indent.AppendLine($"return new ExerciseOutcome<{returnTypeName}>.One({decoderExpr});");
        }
        else
        {
            indent.AppendLine($"var decoded = {templateClassName}.Choice{choiceName}.ResultDecoder!(exercised.ExerciseResult);");
            indent.AppendLine($"return new ExerciseOutcome<{returnTypeName}>.One(decoded);");
        }

        indent.Dedent();
        indent.AppendLine("}");
        indent.Dedent();
        indent.AppendLine("}");

        indent.AppendLine();
        indent.AppendLine("throw new InvalidOperationException(");
        indent.Indent();
        indent.AppendLine($"$\"Submission succeeded but no '{choice.Name}' exercise on contract '{{contractId}}' was recorded on transaction {{tx.UpdateId}}. \" +");
        indent.AppendLine("\"This is most often caused by the ILedgerClient bridge not populating TransactionResult.ExercisedEvents — \" +");
        indent.AppendLine("\"the gRPC bridge in canton-ledger-api-csharp is the canonical example and is being updated to project exercised_events. \" +");
        indent.AppendLine("\"If you have wired up a bridge that does populate ExercisedEvents, ensure the participant is configured to return \" +");
        indent.AppendLine("\"LedgerEffects with verbose events so the exercise event survives projection.\");");
        indent.Dedent();

        indent.Dedent();
        indent.AppendLine("}");
    }
}
