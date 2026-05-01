// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Daml.Codegen.CSharp.DarReader;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Codegen for typed choice-result structs and projectors. Emits a
/// <c>&lt;Choice&gt;Result</c> nested record per choice that creates one or more
/// contracts, plus a static
/// <c>FromCreatedContracts(IEnumerable&lt;CreatedContract&gt;) -&gt; ExerciseOutcome&lt;&lt;Choice&gt;Result&gt;</c>
/// projector that walks a transaction's created contracts and validates cardinality
/// per declared template field. See issue #60.
/// </summary>
internal sealed partial class CSharpCodeGenerator
{
    /// <summary>
    /// Cardinality of an expected created contract slot in a choice's return type.
    /// </summary>
    private enum CreatedCardinality
    {
        /// <summary>Single <c>ContractId T</c> — exactly one created contract of <c>T</c> is expected.</summary>
        Single,
        /// <summary>Optional <c>ContractId T</c> — zero or one created contracts of <c>T</c> is expected.</summary>
        Optional,
        /// <summary>List <c>[ContractId T]</c> — any number of created contracts of <c>T</c> is expected.</summary>
        List,
    }

    /// <summary>
    /// One declared <c>ContractId T</c>-bearing slot in a choice's return type.
    /// </summary>
    /// <param name="FieldName">PascalCase C# field name on the emitted <c>&lt;Choice&gt;Result</c> record.</param>
    /// <param name="CSharpTemplateType">C# name of the template type (e.g. <c>Agreement</c>, <c>SwapRecord</c>).</param>
    /// <param name="Cardinality">How many created contracts of this template the choice should produce.</param>
    private sealed record ChoiceCreatedSlot(
        string FieldName,
        string CSharpTemplateType,
        CreatedCardinality Cardinality);

    /// <summary>
    /// Walks the choice's return type for embedded <c>ContractId T</c> references and
    /// returns one slot per reference (preserving declaration order). Returns an empty
    /// list when the return type carries no contract IDs — those choices don't get a
    /// <c>&lt;Choice&gt;Result</c> emitted.
    /// </summary>
    /// <remarks>
    /// <para>
    ///   Recognised return-type shapes:
    ///   <list type="bullet">
    ///     <item><c>ContractId T</c> — single-create.</item>
    ///     <item><c>Optional (ContractId T)</c> — optional-create.</item>
    ///     <item><c>[ContractId T]</c> — list-create.</item>
    ///     <item><c>(ContractId A, ContractId B, ...)</c> — Daml tuples (LF: <c>DA.Types:Tuple{N}</c>) — flattened across components.</item>
    ///   </list>
    /// </para>
    /// <para>
    ///   Anything else (records, primitives, plain <c>Unit</c>) yields an empty list —
    ///   the choice is treated as non-creating from the codegen's perspective. This
    ///   intentionally undershoots: a choice whose body creates contracts but returns
    ///   <c>Unit</c> won't get a typed projector. Consumers can fall back to walking
    ///   <c>tx.CreatedContracts</c> manually for those cases.
    /// </para>
    /// </remarks>
    private IReadOnlyList<ChoiceCreatedSlot> ExtractCreatedSlots(DamlType returnType)
    {
        var slots = new List<ChoiceCreatedSlot>();
        WalkForCreatedSlots(returnType, slots, parentCardinality: CreatedCardinality.Single);
        // Disambiguate field names when the same template appears more than once in the
        // same choice's return type. The first occurrence keeps the bare template name;
        // subsequent occurrences get a numeric suffix matching their position. Without
        // this, a choice returning `(ContractId Foo, ContractId Foo)` would emit two
        // record fields named `Foo` and fail to compile.
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (seen.TryGetValue(slot.FieldName, out var count))
            {
                seen[slot.FieldName] = count + 1;
                slots[i] = slot with { FieldName = $"{slot.FieldName}{count + 1}" };
            }
            else
            {
                seen[slot.FieldName] = 1;
            }
        }
        return slots;
    }

    private void WalkForCreatedSlots(
        DamlType type,
        List<ChoiceCreatedSlot> slots,
        CreatedCardinality parentCardinality)
    {
        switch (type)
        {
            // ContractId T — single-create slot. Inherit `parentCardinality` from any
            // wrapping Optional/List so a `[ContractId T]` becomes a List slot rather
            // than a Single one.
            case DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.ContractId }, Arguments: [var arg] }:
            {
                var (templateName, csharpName) = ResolveContractIdTarget(arg);
                slots.Add(new ChoiceCreatedSlot(
                    FieldName: templateName,
                    CSharpTemplateType: csharpName,
                    Cardinality: parentCardinality));
                return;
            }
            // Optional (ContractId T) — recurse with Optional cardinality.
            case DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional }, Arguments: [var inner] }:
                WalkForCreatedSlots(inner, slots, CreatedCardinality.Optional);
                return;
            // [ContractId T] — recurse with List cardinality.
            case DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.List }, Arguments: [var inner] }:
                WalkForCreatedSlots(inner, slots, CreatedCardinality.List);
                return;
            // Daml tuple (DA.Types:Tuple2/3/.../N) — flatten over the components.
            case DamlTypeApp { Base: DamlTypeRef { Module: "DA.Types", Name: var tupleName } } app
                when tupleName.StartsWith("Tuple", StringComparison.Ordinal):
                for (var i = 0; i < app.Arguments.Count; i++)
                {
                    WalkForCreatedSlots(app.Arguments[i], slots, parentCardinality);
                }
                return;
            default:
                // Records, variants, primitives, type vars, plain Unit — no created-slot
                // contribution. Codegen treats these as non-creating return types.
                return;
        }
    }

    /// <summary>
    /// Resolves a <c>ContractId T</c>'s argument to (PascalCaseFieldName, FullyQualifiedCSharpName).
    /// The field name is the unqualified template name; the C# type is the resolved name (which
    /// may be cross-package qualified).
    /// </summary>
    private (string FieldName, string CSharpTemplateType) ResolveContractIdTarget(DamlType arg)
    {
        switch (arg)
        {
            case DamlTypeRef typeRef:
            {
                var fieldName = SanitizeIdentifier(typeRef.Name);
                var csharpName = ResolveTypeRefName(typeRef);
                return (fieldName, csharpName);
            }
            case DamlTypeApp { Base: DamlTypeRef typeRef }:
            {
                var fieldName = SanitizeIdentifier(typeRef.Name);
                var csharpName = MapDamlTypeToCSharp(arg);
                return (fieldName, csharpName);
            }
            default:
                // Type variable or otherwise opaque target — fall back to the mapped C#
                // name and a synthetic field name. Generated code may not compile in this
                // case; callers will see a clear loud failure at consumer build time.
                var mapped = MapDamlTypeToCSharp(arg);
                return ("Created", mapped);
        }
    }

    /// <summary>
    /// Emits the <c>&lt;Choice&gt;Result</c> nested record (typed result struct) and a
    /// static <c>FromCreatedContracts(...)</c> projector for every choice on
    /// <paramref name="template"/> whose return type carries one or more
    /// <c>ContractId T</c>s. See <see cref="ExtractCreatedSlots"/>.
    /// </summary>
    /// <param name="moduleNamespace">
    /// Fully-qualified C# namespace of the emitted template. Used to <c>global::</c>-qualify
    /// in-package template references inside the projector body so positional record
    /// properties on the result type (e.g. a slot named <c>Agreement</c>) cannot shadow
    /// the template type when looking up <c>Agreement.TemplateId</c>.
    /// </param>
    private void WriteChoiceResultStructs(IndentWriter indent, DamlTemplate template, string moduleNamespace)
    {
        foreach (var choice in template.Choices)
        {
            var slots = ExtractCreatedSlots(choice.ReturnType);
            if (slots.Count == 0)
            {
                continue;
            }

            WriteSingleChoiceResultStruct(indent, choice, slots, moduleNamespace);
        }
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="template"/> has at least one choice that
    /// (a) creates contracts (<see cref="ExtractCreatedSlots"/> yields a non-empty list) and
    /// (b) does not go through the codegen's argument-type fallback (B3 — no
    /// <c>argument.ToRecord()</c> on a stub).
    /// </summary>
    private bool TemplateHasEmittableAsyncExercisers(
        DamlTemplate template,
        IReadOnlyDictionary<string, DamlDataType> dataTypes)
    {
        foreach (var choice in template.Choices)
        {
            var slots = ExtractCreatedSlots(choice.ReturnType);
            if (slots.Count == 0)
            {
                continue;
            }
            var (_, _, _, isFallback) = GetChoiceArgumentInfo(choice, dataTypes);
            if (isFallback)
            {
                continue;
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// Emits the static <c>&lt;TemplateName&gt;Extensions</c> class containing one
    /// <c>&lt;Choice&gt;Async(...)</c> exerciser per create-bearing choice. Lives at the
    /// namespace level so the methods extend <c>ContractId&lt;TemplateName&gt;</c> in
    /// every consumer that imports the module's namespace. Skips emission entirely
    /// when no choice qualifies (avoids stranded empty classes).
    /// </summary>
    private void WriteChoiceAsyncExercisersClass(
        IndentWriter indent,
        DamlTemplate template,
        string templateClassName,
        IReadOnlyDictionary<string, DamlDataType> dataTypes)
    {
        if (!TemplateHasEmittableAsyncExercisers(template, dataTypes))
        {
            return;
        }

        indent.AppendLine();
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Static <c>&lt;Choice&gt;Async</c> extension methods for <see cref=\"{templateClassName}\"/>.");
            indent.AppendLine("/// One method per create-bearing choice; each delegates to");
            indent.AppendLine("/// <see cref=\"Daml.Ledger.Abstractions.ILedgerClient.TrySubmitAndWaitForTransactionAsync\"/>");
            indent.AppendLine($"/// and projects success via <c>&lt;Choice&gt;Result.FromCreatedContracts</c>.");
            indent.AppendLine("/// </summary>");
        }
        indent.AppendLine($"public static class {templateClassName}Extensions");
        indent.AppendLine("{");
        indent.Indent();

        var first = true;
        foreach (var choice in template.Choices)
        {
            var slots = ExtractCreatedSlots(choice.ReturnType);
            if (slots.Count == 0)
            {
                continue;
            }

            // B3: skip choices whose argument type went through the codegen fallback
            // path (no recognised record / Unit / external ref). The fallback emits a
            // field-less <Choice>Arg stub with no ToRecord(), so the Async exerciser's
            // `argument.ToRecord()` would not compile in consumer output.
            var (_, _, _, isFallback) = GetChoiceArgumentInfo(choice, dataTypes);
            if (isFallback)
            {
                continue;
            }

            if (!first)
            {
                indent.AppendLine();
            }
            WriteSingleChoiceAsyncExerciser(indent, choice, templateClassName, dataTypes);
            first = false;
        }

        indent.Dedent();
        indent.AppendLine("}");
    }

    private void WriteSingleChoiceAsyncExerciser(
        IndentWriter indent,
        DamlChoice choice,
        string templateClassName,
        IReadOnlyDictionary<string, DamlDataType> dataTypes)
    {
        var choiceName = SanitizeIdentifier(choice.Name);
        var resultName = $"{choiceName}Result";
        var (argTypeName, _, isExternalRef, _) = GetChoiceArgumentInfo(choice, dataTypes);
        var hasArg = argTypeName != "DamlUnit" && !isExternalRef;

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Exercises the {choice.Name} choice and projects the resulting transaction's created contracts to a typed <see cref=\"{resultName}\"/>.");
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

        // Method signature.
        indent.AppendLine($"public static async Task<ExerciseOutcome<{resultName}>> {choiceName}Async(");
        indent.Indent();
        indent.AppendLine($"this ContractId<{templateClassName}> contractId,");
        indent.AppendLine("ILedgerClient client,");
        if (hasArg)
        {
            // Argument types remain nested inside the template class (e.g.
            // `Agreement.Renew`) — qualify with the template name so the
            // extension class at namespace level resolves the type.
            indent.AppendLine($"{templateClassName}.{argTypeName} argument,");
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
        indent.AppendLine($"ExerciseOutcome<TransactionResult>.One success => {resultName}.FromCreatedContracts(success.Result.CreatedContracts),");
        indent.AppendLine($"ExerciseOutcome<TransactionResult>.DamlError damlError => new ExerciseOutcome<{resultName}>.DamlError(damlError.Category, damlError.ErrorId, damlError.Message, damlError.Metadata),");
        indent.AppendLine($"ExerciseOutcome<TransactionResult>.InfraError infraError => new ExerciseOutcome<{resultName}>.InfraError(infraError.StatusCode, infraError.Message),");
        indent.AppendLine("_ => throw new InvalidOperationException($\"Unhandled outcome: {outcome.GetType().Name}\"),");
        indent.Dedent();
        indent.AppendLine("};");

        indent.Dedent();
        indent.AppendLine("}");
    }

    private void WriteSingleChoiceResultStruct(
        IndentWriter indent,
        DamlChoice choice,
        IReadOnlyList<ChoiceCreatedSlot> slots,
        string moduleNamespace)
    {
        var choiceName = SanitizeIdentifier(choice.Name);
        var resultName = $"{choiceName}Result";

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Typed projection of the contracts created by the {choice.Name} choice.");
            indent.AppendLine("/// One field per template the choice creates; cardinality follows the choice's");
            indent.AppendLine("/// return type (single, optional, list).");
            indent.AppendLine("/// </summary>");
        }

        // Record header — primary constructor with one parameter per slot.
        var parameters = string.Join(", ", slots.Select(s => $"{SlotPropertyType(s)} {s.FieldName}"));
        indent.AppendLine($"public sealed record {resultName}({parameters})");
        indent.AppendLine("{");
        indent.Indent();

        WriteFromCreatedContractsProjector(indent, resultName, slots, moduleNamespace);

        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine();
    }

    private static string SlotPropertyType(ChoiceCreatedSlot slot) => slot.Cardinality switch
    {
        CreatedCardinality.Single => $"ContractId<{slot.CSharpTemplateType}>",
        CreatedCardinality.Optional => $"ContractId<{slot.CSharpTemplateType}>?",
        CreatedCardinality.List => $"IReadOnlyList<ContractId<{slot.CSharpTemplateType}>>",
        _ => $"ContractId<{slot.CSharpTemplateType}>",
    };

    private void WriteFromCreatedContractsProjector(
        IndentWriter indent,
        string resultName,
        IReadOnlyList<ChoiceCreatedSlot> slots,
        string moduleNamespace)
    {
        // Helper: qualify in-package template references with the module namespace
        // (and `global::`) so positional record properties on the result type cannot
        // shadow the template type. Cross-package types are emitted by codegen with
        // their own qualifier already, so we only prefix bare names — heuristically
        // detected by checking for an embedded dot.
        string Q(string templateName) =>
            templateName.Contains('.', StringComparison.Ordinal)
                ? templateName
                : $"global::{moduleNamespace}.{templateName}";

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Projects an upstream transaction's created contracts to a typed <see cref=\"{resultName}\"/>.");
            indent.AppendLine("/// Returns:");
            indent.AppendLine("/// <list type=\"bullet\">");
            indent.AppendLine("///   <item><see cref=\"ExerciseOutcome{T}.One\"/> when every expected template's cardinality matches.</item>");
            indent.AppendLine("///   <item><see cref=\"ExerciseOutcome{T}.None\"/> when at least one required slot's template is missing from the transaction.</item>");
            indent.AppendLine("///   <item><see cref=\"ExerciseOutcome{T}.Many\"/> when a single-cardinality slot has more than one created contract of its template, or an optional-cardinality slot has more than one.</item>");
            indent.AppendLine("/// </list>");
            indent.AppendLine("/// Cardinality is matched by template ID's <c>(module, entity)</c> pair only — package upgrades that share the same logical template name match cleanly.");
            indent.AppendLine("/// </summary>");
        }

        indent.AppendLine($"public static ExerciseOutcome<{resultName}> FromCreatedContracts(IEnumerable<CreatedContract> created)");
        indent.AppendLine("{");
        indent.Indent();

        indent.AppendLine("ArgumentNullException.ThrowIfNull(created);");

        // Group slots by template ref. When the same template appears more than once
        // in the choice's return type (e.g. `(ContractId Half, ContractId Half)`),
        // every slot pointing at the same template shares one bucket of created
        // contracts; we then distribute that bucket across the slots in declaration
        // order. The earlier `if/else if`-per-slot shape silently dropped duplicates
        // on the floor: the second slot's branch was unreachable, so its bucket
        // stayed empty and the projector returned `.None` for any caller using the
        // duplicate-template feature this PR otherwise advertises.
        var templateGroups = new List<(string TemplateRef, List<int> SlotIndexes)>();
        for (var i = 0; i < slots.Count; i++)
        {
            var templateRef = Q(slots[i].CSharpTemplateType);
            var groupIndex = -1;
            for (var g = 0; g < templateGroups.Count; g++)
            {
                if (string.Equals(templateGroups[g].TemplateRef, templateRef, StringComparison.Ordinal))
                {
                    groupIndex = g;
                    break;
                }
            }

            if (groupIndex < 0)
            {
                templateGroups.Add((templateRef, new List<int> { i }));
            }
            else
            {
                templateGroups[groupIndex].SlotIndexes.Add(i);
            }
        }

        // One bucket per unique template. Slots fan out from these buckets below.
        for (var g = 0; g < templateGroups.Count; g++)
        {
            indent.AppendLine($"var templateMatches{g} = new List<string>();");
        }

        indent.AppendLine("foreach (var item in created)");
        indent.AppendLine("{");
        indent.Indent();
        for (var g = 0; g < templateGroups.Count; g++)
        {
            var prefix = g == 0 ? "if" : "else if";
            var templateRef = templateGroups[g].TemplateRef;
            indent.AppendLine($"{prefix} (string.Equals(item.TemplateId.ModuleName, {templateRef}.TemplateId.ModuleName, StringComparison.Ordinal)");
            indent.Indent();
            indent.AppendLine($"&& string.Equals(item.TemplateId.EntityName, {templateRef}.TemplateId.EntityName, StringComparison.Ordinal))");
            indent.Dedent();
            indent.AppendLine("{");
            indent.Indent();
            indent.AppendLine($"templateMatches{g}.Add(item.ContractId);");
            indent.Dedent();
            indent.AppendLine("}");
        }
        indent.Dedent();
        indent.AppendLine("}");

        // Distribute each template bucket across its slots in declaration order.
        // Single/Optional slots take one match each; List slots drain the remainder.
        // Validation against the per-slot cardinality runs in the next loop, against
        // these `matches{i}` locals — so the existing None/Many semantics are preserved.
        for (var i = 0; i < slots.Count; i++)
        {
            indent.AppendLine($"var matches{i} = new List<string>();");
        }
        for (var g = 0; g < templateGroups.Count; g++)
        {
            var slotIndexes = templateGroups[g].SlotIndexes;
            indent.AppendLine($"var templateMatchIndex{g} = 0;");
            for (var k = 0; k < slotIndexes.Count; k++)
            {
                var slotIndex = slotIndexes[k];
                var slot = slots[slotIndex];
                switch (slot.Cardinality)
                {
                    case CreatedCardinality.Single:
                    case CreatedCardinality.Optional:
                        indent.AppendLine($"if (templateMatchIndex{g} < templateMatches{g}.Count)");
                        indent.AppendLine("{");
                        indent.Indent();
                        indent.AppendLine($"matches{slotIndex}.Add(templateMatches{g}[templateMatchIndex{g}]);");
                        indent.AppendLine($"templateMatchIndex{g}++;");
                        indent.Dedent();
                        indent.AppendLine("}");
                        break;

                    case CreatedCardinality.List:
                        // List slots accept any cardinality. When a List slot appears
                        // alongside Single/Optional slots for the same template, the
                        // List slot drains whatever is left after the upstream slots
                        // claim their share — declaration order wins.
                        indent.AppendLine($"while (templateMatchIndex{g} < templateMatches{g}.Count)");
                        indent.AppendLine("{");
                        indent.Indent();
                        indent.AppendLine($"matches{slotIndex}.Add(templateMatches{g}[templateMatchIndex{g}]);");
                        indent.AppendLine($"templateMatchIndex{g}++;");
                        indent.Dedent();
                        indent.AppendLine("}");
                        break;
                }
            }

            // Whatever is left after distribution counts as "extra of this template".
            // For Single/Optional-only slot groups, those leftovers are what trigger
            // the existing `Many` outcome below (each Single/Optional slot's local
            // bucket has at most 1, so the leftover never becomes part of `matchesN`
            // — we have to surface it back into one of the slot buckets so the
            // cardinality validator sees the total population). Strategy: append the
            // leftover into the *last* slot for this group, which is then counted by
            // the validator. If the last slot is Single/Optional, this trips its
            // count > 1 branch (correct: the consumer asked for one, ledger created
            // many). If the last slot is List, the leftover was already drained by
            // the while loop above.
            indent.AppendLine($"if (templateMatchIndex{g} < templateMatches{g}.Count)");
            indent.AppendLine("{");
            indent.Indent();
            var lastSlotIndex = slotIndexes[^1];
            indent.AppendLine($"while (templateMatchIndex{g} < templateMatches{g}.Count)");
            indent.AppendLine("{");
            indent.Indent();
            indent.AppendLine($"matches{lastSlotIndex}.Add(templateMatches{g}[templateMatchIndex{g}]);");
            indent.AppendLine($"templateMatchIndex{g}++;");
            indent.Dedent();
            indent.AppendLine("}");
            indent.Dedent();
            indent.AppendLine("}");
        }

        // Cardinality validation per slot.
        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            var local = $"matches{i}";
            indent.AppendLine();
            switch (slot.Cardinality)
            {
                case CreatedCardinality.Single:
                    indent.AppendLine($"if ({local}.Count == 0)");
                    indent.AppendLine("{");
                    indent.Indent();
                    indent.AppendLine($"return new ExerciseOutcome<{resultName}>.None();");
                    indent.Dedent();
                    indent.AppendLine("}");
                    indent.AppendLine($"if ({local}.Count > 1)");
                    indent.AppendLine("{");
                    indent.Indent();
                    indent.AppendLine($"return new ExerciseOutcome<{resultName}>.Many({local}.Count, {local});");
                    indent.Dedent();
                    indent.AppendLine("}");
                    break;
                case CreatedCardinality.Optional:
                    indent.AppendLine($"if ({local}.Count > 1)");
                    indent.AppendLine("{");
                    indent.Indent();
                    indent.AppendLine($"return new ExerciseOutcome<{resultName}>.Many({local}.Count, {local});");
                    indent.Dedent();
                    indent.AppendLine("}");
                    break;
                case CreatedCardinality.List:
                    // No upper-bound check — list slots accept any cardinality including zero.
                    break;
            }
        }

        // Build the result.
        indent.AppendLine();
        indent.AppendLine($"return new ExerciseOutcome<{resultName}>.One(new {resultName}(");
        indent.Indent();
        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            var local = $"matches{i}";
            var separator = i == slots.Count - 1 ? "" : ",";
            // Same shadowing-avoidance rationale as the match loop above:
            // the result type's positional property `<TemplateName>` must not
            // shadow the template type when constructing the typed ContractId.
            var templateRef = Q(slot.CSharpTemplateType);
            switch (slot.Cardinality)
            {
                case CreatedCardinality.Single:
                    indent.AppendLine($"{slot.FieldName}: new ContractId<{templateRef}>({local}[0]){separator}");
                    break;
                case CreatedCardinality.Optional:
                    indent.AppendLine($"{slot.FieldName}: {local}.Count == 1 ? new ContractId<{templateRef}>({local}[0]) : null{separator}");
                    break;
                case CreatedCardinality.List:
                    indent.AppendLine($"{slot.FieldName}: {local}.ConvertAll(c => new ContractId<{templateRef}>(c)){separator}");
                    break;
            }
        }
        indent.Dedent();
        indent.AppendLine("));");

        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine();
    }
}
