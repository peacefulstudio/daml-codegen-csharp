// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.Model;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Codegen for typed choice-result structs and projectors. Emits a
/// <c>&lt;Choice&gt;Result</c> nested record per choice that creates one or more
/// contracts, plus a static
/// <c>FromCreatedContracts(IEnumerable&lt;CreatedContract&gt;) -&gt; ExerciseOutcome&lt;&lt;Choice&gt;Result&gt;</c>
/// projector that walks a transaction's created contracts and validates cardinality
/// per declared template field. See issue #60.
/// </summary>
public sealed partial class CSharpCodeGenerator
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
            var (_, _, isFallback, _) = GetChoiceArgumentInfo(choice, dataTypes);
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
    ///
    /// <para>
    /// The exerciser's parameter shape is driven by the static analyzer:
    /// <list type="bullet">
    ///   <item>When every controller is a payload-field reference, one
    ///   <c>Party</c> parameter per controller (declaration order) appears on
    ///   the method, and the wrapper unions them into <c>SubmitterInfo.actAs</c>.</item>
    ///   <item>When the template-level <c>observer</c> clause and/or the
    ///   choice's <c>observer</c> clause is statically resolvable, those
    ///   parties are added to <c>SubmitterInfo.readAs</c>, deduplicated.</item>
    ///   <item>When controllers are not statically resolvable, the wrapper
    ///   falls back to a single <c>SubmitterInfo submitter</c> parameter and
    ///   passes it through unchanged — caller takes responsibility for both
    ///   <c>actAs</c> and <c>readAs</c>.</item>
    /// </list>
    /// </para>
    /// </summary>
    private void WriteChoiceAsyncExercisersClass(
        IndentWriter indent,
        DamlTemplate template,
        string templateClassName,
        IReadOnlyList<DamlField> fields,
        IReadOnlyDictionary<string, DamlDataType> dataTypes)
    {
        if (!TemplateHasEmittableAsyncExercisers(template, dataTypes))
        {
            return;
        }

        RequireAsyncExerciserNamespaces(indent);

        var partyFields = fields
            .Where(f => f.Type is DamlPrimitiveType { Primitive: DamlPrimitive.Party })
            .ToDictionary(f => f.Name, f => f, StringComparer.Ordinal);

        // Re-validate template-level observers against actual payload fields
        // — same defensive check the SubmissionExtensions emitter does.
        var templateObservers = ValidatePayloadParties(template.Observers, partyFields);

        indent.AppendLine();
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Static <c>&lt;Choice&gt;Async</c> extension methods for <see cref=\"{templateClassName}\"/>.");
            indent.AppendLine("/// One method per create-bearing choice; each delegates to");
            indent.AppendLine("/// <see cref=\"global::Daml.Ledger.Abstractions.ILedgerClient.TrySubmitAndWaitForTransactionAsync\"/>");
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

            var (_, _, isFallback, _) = GetChoiceArgumentInfo(choice, dataTypes);
            if (isFallback)
            {
                continue;
            }

            if (!first)
            {
                indent.AppendLine();
            }
            var controllers = ValidatePayloadParties(choice.Controllers, partyFields);
            var choiceObservers = ValidatePayloadParties(choice.Observers, partyFields);
            var effectiveReadAs = UnionStaticParties(templateObservers, choiceObservers);
            WriteSingleChoiceAsyncExerciser(
                indent, choice, templateClassName, dataTypes, controllers, effectiveReadAs);
            first = false;
        }

        indent.Dedent();
        indent.AppendLine("}");
    }

    /// <summary>
    /// Computes the effective <c>readAs</c> set for a choice as the union of
    /// the template-level observers and the choice-level observers, preserving
    /// declaration order and deduplicating by payload field name. Returns a
    /// <see cref="DamlPartySource.Static"/> result iff both inputs are static
    /// (a <see cref="DamlPartySource.Dynamic"/> verdict on either side is
    /// contagious for the <em>readAs</em> projection — the choice exerciser
    /// continues to emit named controller params off the controller analysis,
    /// but no <c>readAs</c> params are emitted because the dynamic component
    /// can't be synthesized at compile time).
    /// </summary>
    private static DamlPartyAnalysis UnionStaticParties(
        DamlPartyAnalysis a,
        DamlPartyAnalysis b)
    {
        if (a.Source != DamlPartySource.Static || b.Source != DamlPartySource.Static)
        {
            return DamlPartyAnalysis.Dynamic;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var union = new List<DamlPartyReference>();
        foreach (var p in a.Parties)
        {
            if (p is DamlPartyPayloadField pf && seen.Add(pf.FieldName))
            {
                union.Add(p);
            }
        }
        foreach (var p in b.Parties)
        {
            if (p is DamlPartyPayloadField pf && seen.Add(pf.FieldName))
            {
                union.Add(p);
            }
        }
        return DamlPartyAnalysis.Static(union);
    }

    private void WriteSingleChoiceAsyncExerciser(
        IndentWriter indent,
        DamlChoice choice,
        string templateClassName,
        IReadOnlyDictionary<string, DamlDataType> dataTypes,
        DamlPartyAnalysis controllers,
        DamlPartyAnalysis observers)
    {
        var choiceName = SanitizeIdentifier(choice.Name);
        var resultName = $"{choiceName}Result";
        var (argTypeName, _, _, isNestedTemplateArg) = GetChoiceArgumentInfo(choice, dataTypes);
        var hasArg = argTypeName != "DamlUnit";

        var staticControllers = controllers.Source == DamlPartySource.Static
                                 && controllers.Parties.Count > 0;

        // Observers-only (payload fields named in the observer clauses but NOT
        // also named as controllers). The choice receiver is a ContractId, so
        // we have no payload in scope — observer parties only become emittable
        // as named Party params. We surface them in declaration order on top
        // of the controller params; the body then routes controllers into
        // SubmitterInfo.actAs and observers into SubmitterInfo.readAs.
        var (controllerParams, readAsParams) = staticControllers
            ? PartitionControllersAndObservers(controllers, observers)
            : (new List<string>(), new List<string>());

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Exercises the {choice.Name} choice and projects the resulting transaction's created contracts to a typed <see cref=\"{resultName}\"/>.");
            if (staticControllers && readAsParams.Count > 0)
            {
                indent.AppendLine("/// One <c>Party</c> parameter is emitted per Daml controller (declaration order),");
                indent.AppendLine("/// followed by one parameter per Daml observer that is not also a controller.");
                indent.AppendLine("/// The wrapper builds a <see cref=\"SubmitterInfo\"/> with controllers in <c>actAs</c>");
                indent.AppendLine("/// and observers in <c>readAs</c>, so the wire format reflects Daml's stakeholder");
                indent.AppendLine("/// model exactly.");
            }
            else if (staticControllers)
            {
                indent.AppendLine("/// One <c>Party</c> parameter is emitted per Daml controller (declaration order).");
                indent.AppendLine("/// The wrapper builds a <see cref=\"SubmitterInfo\"/> from those parties before");
                indent.AppendLine("/// dispatching to <c>ILedgerClient</c>.");
            }
            else
            {
                indent.AppendLine("/// The submitter is passed explicitly via <paramref name=\"submitter\"/> — the static");
                indent.AppendLine("/// analyzer could not resolve the Daml <c>controller</c> clause to payload-field");
                indent.AppendLine("/// references. <see cref=\"SubmitterInfo\"/> implicitly converts from a");
                indent.AppendLine("/// single <c>Party</c>, so the single-party call site stays a one-liner.");
            }
            indent.AppendLine("/// </summary>");
            indent.AppendLine("/// <param name=\"contractId\">The contract on which to exercise the choice.</param>");
            indent.AppendLine("/// <param name=\"client\">The ledger client.</param>");
            if (hasArg)
            {
                indent.AppendLine("/// <param name=\"argument\">The choice argument.</param>");
            }
            if (!staticControllers)
            {
                indent.AppendLine("/// <param name=\"submitter\">The submitter party set (<c>actAs</c> + optional <c>readAs</c>).</param>");
            }
            indent.AppendLine("/// <param name=\"workflowId\">Optional workflow id; passed through to the ledger when supplied. No default — workflow IDs are correlation keys, and a per-choice default would bucket every submission of the same choice under one ID.</param>");
            indent.AppendLine("/// <param name=\"cancellationToken\">Cancellation token.</param>");
        }

        // Method signature.
        indent.AppendLine($"public static async Task<{_qualifier.Qualify("ExerciseOutcome", _currentNamespace)}<{resultName}>> {choiceName}Async(");
        indent.Indent();
        indent.AppendLine($"this {_qualifier.Qualify("ContractId", _currentNamespace)}<{templateClassName}> contractId,");
        indent.AppendLine($"{_qualifier.Qualify("ILedgerClient", _currentNamespace)} client,");
        if (hasArg)
        {
            var argParamType = isNestedTemplateArg
                ? $"{templateClassName}.{argTypeName}"
                : argTypeName;
            indent.AppendLine($"{argParamType} argument,");
        }

        if (staticControllers)
        {
            foreach (var paramName in controllerParams)
            {
                indent.AppendLine($"{_qualifier.Qualify("Party", _currentNamespace)} {paramName},");
            }
            foreach (var paramName in readAsParams)
            {
                indent.AppendLine($"{_qualifier.Qualify("Party", _currentNamespace)} {paramName},");
            }
        }
        else
        {
            indent.AppendLine($"{_qualifier.Qualify("SubmitterInfo", _currentNamespace)} submitter,");
        }
        indent.AppendLine("string? workflowId = null,");
        indent.AppendLine("CancellationToken cancellationToken = default)");
        indent.Dedent();
        indent.AppendLine("{");
        indent.Indent();

        indent.AppendLine("ArgumentNullException.ThrowIfNull(contractId);");
        indent.AppendLine("ArgumentNullException.ThrowIfNull(client);");
        if (hasArg)
        {
            indent.AppendLine("ArgumentNullException.ThrowIfNull(argument);");
        }

        // When controllers are statically resolvable, build the SubmitterInfo
        // locally — actAs from the named Party params, readAs from the
        // observer-only Party params (if any).
        if (staticControllers)
        {
            indent.AppendLine();
            if (controllerParams.Count == 1 && readAsParams.Count == 0)
            {
                // Single controller, no extra readAs — rely on Party ->
                // SubmitterInfo implicit conversion. Avoids a HashSet allocation.
                indent.AppendLine($"{_qualifier.Qualify("SubmitterInfo", _currentNamespace)} submitter = {controllerParams[0]};");
            }
            else if (readAsParams.Count == 0)
            {
                indent.Require("System.Collections.Generic");
                indent.AppendLine("// SubmitterInfo's actAs unions every named controller.");
                indent.AppendLine($"var submitter = new {_qualifier.Qualify("SubmitterInfo", _currentNamespace)}(new {_qualifier.Qualify("HashSet", _currentNamespace)}<{_qualifier.Qualify("Party", _currentNamespace)}> {{ {string.Join(", ", controllerParams)} }});");
            }
            else
            {
                indent.Require("System.Collections.Generic");
                indent.AppendLine("// actAs unions every named controller; readAs unions every observer that is");
                indent.AppendLine("// not also a controller, so the wire format reflects Daml's stakeholder model.");
                indent.AppendLine($"var submitter = new {_qualifier.Qualify("SubmitterInfo", _currentNamespace)}(");
                indent.Indent();
                indent.AppendLine($"actAs: new {_qualifier.Qualify("HashSet", _currentNamespace)}<{_qualifier.Qualify("Party", _currentNamespace)}> {{ {string.Join(", ", controllerParams)} }},");
                indent.AppendLine($"readAs: new {_qualifier.Qualify("HashSet", _currentNamespace)}<{_qualifier.Qualify("Party", _currentNamespace)}> {{ {string.Join(", ", readAsParams)} }});");
                indent.Dedent();
            }
        }

        indent.AppendLine();
        var argExpr = hasArg ? "argument.ToRecord()" : $"{_qualifier.Qualify("DamlUnit", _currentNamespace)}.Instance";
        indent.AppendLine($"var command = new {_qualifier.Qualify("ExerciseCommand", _currentNamespace)}(");
        indent.Indent();
        indent.AppendLine($"{templateClassName}.TemplateId,");
        indent.AppendLine("contractId,");
        indent.AppendLine($"new {_qualifier.Qualify("ChoiceName", _currentNamespace)}(\"{choice.Name}\"),");
        indent.AppendLine($"{argExpr});");
        indent.Dedent();

        indent.AppendLine();
        indent.AppendLine($"var submission = {_qualifier.Qualify("CommandsSubmission", _currentNamespace)}.Single(command)");
        indent.Indent();
        indent.AppendLine(".WithSubmitter(submitter)");
        indent.AppendLine($".WithCommandId(new {_qualifier.Qualify("CommandId", _currentNamespace)}(Guid.NewGuid().ToString()));");
        indent.Dedent();
        indent.AppendLine("if (!string.IsNullOrEmpty(workflowId))");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine($"submission = submission.WithWorkflowId(new {_qualifier.Qualify("WorkflowId", _currentNamespace)}(workflowId));");
        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine();
        indent.AppendLine("var outcome = await client.TrySubmitAndWaitForTransactionAsync(submission, cancellationToken).ConfigureAwait(false);");
        indent.AppendLine();
        indent.AppendLine("return outcome switch");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine($"{_qualifier.Qualify("ExerciseOutcome", _currentNamespace)}<{_qualifier.Qualify("TransactionResult", _currentNamespace)}>.One success => {resultName}.FromCreatedContracts(success.Result.CreatedContracts),");
        indent.AppendLine($"{_qualifier.Qualify("ExerciseOutcome", _currentNamespace)}<{_qualifier.Qualify("TransactionResult", _currentNamespace)}>.DamlError damlError => new {_qualifier.Qualify("ExerciseOutcome", _currentNamespace)}<{resultName}>.DamlError(damlError.Category, damlError.ErrorId, damlError.Message, damlError.Metadata),");
        indent.AppendLine($"{_qualifier.Qualify("ExerciseOutcome", _currentNamespace)}<{_qualifier.Qualify("TransactionResult", _currentNamespace)}>.InfraError infraError => new {_qualifier.Qualify("ExerciseOutcome", _currentNamespace)}<{resultName}>.InfraError(infraError.StatusCode, infraError.Message),");
        indent.AppendLine("_ => throw new InvalidOperationException($\"Unhandled outcome: {outcome.GetType().Name}\"),");
        indent.Dedent();
        indent.AppendLine("};");

        indent.Dedent();
        indent.AppendLine("}");
    }

    /// <summary>
    /// Splits the analyzed controllers and observers into two ordered lists of
    /// camelCased Party parameter names: one for controllers (which feed
    /// <c>SubmitterInfo.actAs</c>) and one for observer-only parties (which
    /// feed <c>SubmitterInfo.readAs</c>). Observer parties that are also
    /// controllers are NOT duplicated in the readAs list — the deduplication
    /// is by payload-field name, mirroring the Daml semantics.
    /// </summary>
    /// <returns>
    /// <c>(controllerParams, readAsParams)</c>, both in declaration order.
    /// When <paramref name="observers"/> is dynamic or empty, the second list
    /// is empty.
    /// </returns>
    private static (List<string> controllerParams, List<string> readAsParams)
        PartitionControllersAndObservers(DamlPartyAnalysis controllers, DamlPartyAnalysis observers)
    {
        var controllerParams = new List<string>();
        var controllerFieldNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var p in controllers.Parties)
        {
            if (p is DamlPartyPayloadField pf && controllerFieldNames.Add(pf.FieldName))
            {
                controllerParams.Add(ToCamelCaseParam(pf.FieldName));
            }
        }

        var readAsParams = new List<string>();
        if (observers.Source == DamlPartySource.Static)
        {
            var seenObservers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in observers.Parties)
            {
                if (p is DamlPartyPayloadField pf
                    && !controllerFieldNames.Contains(pf.FieldName)
                    && seenObservers.Add(pf.FieldName))
                {
                    readAsParams.Add(ToCamelCaseParam(pf.FieldName));
                }
            }
        }

        return (controllerParams, readAsParams);
    }

    private void WriteSingleChoiceResultStruct(
        IndentWriter indent,
        DamlChoice choice,
        IReadOnlyList<ChoiceCreatedSlot> slots,
        string moduleNamespace)
    {
        RequireAsyncExerciserNamespaces(indent);
        indent.Require("System.Collections.Generic");

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

    private string SlotPropertyType(ChoiceCreatedSlot slot) => slot.Cardinality switch
    {
        CreatedCardinality.Single => $"{_qualifier.Qualify("ContractId", _currentNamespace)}<{slot.CSharpTemplateType}>",
        CreatedCardinality.Optional => $"{_qualifier.Qualify("ContractId", _currentNamespace)}<{slot.CSharpTemplateType}>?",
        CreatedCardinality.List => $"{_qualifier.Qualify("IReadOnlyList", _currentNamespace)}<{_qualifier.Qualify("ContractId", _currentNamespace)}<{slot.CSharpTemplateType}>>",
        _ => $"{_qualifier.Qualify("ContractId", _currentNamespace)}<{slot.CSharpTemplateType}>",
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

        indent.AppendLine($"public static {_qualifier.Qualify("ExerciseOutcome", _currentNamespace)}<{resultName}> FromCreatedContracts(IEnumerable<{_qualifier.Qualify("CreatedContract", _currentNamespace)}> created)");
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
                    indent.AppendLine($"return new {_qualifier.Qualify("ExerciseOutcome", _currentNamespace)}<{resultName}>.None();");
                    indent.Dedent();
                    indent.AppendLine("}");
                    indent.AppendLine($"if ({local}.Count > 1)");
                    indent.AppendLine("{");
                    indent.Indent();
                    indent.AppendLine($"return new {_qualifier.Qualify("ExerciseOutcome", _currentNamespace)}<{resultName}>.Many({local}.Count, {local});");
                    indent.Dedent();
                    indent.AppendLine("}");
                    break;
                case CreatedCardinality.Optional:
                    indent.AppendLine($"if ({local}.Count > 1)");
                    indent.AppendLine("{");
                    indent.Indent();
                    indent.AppendLine($"return new {_qualifier.Qualify("ExerciseOutcome", _currentNamespace)}<{resultName}>.Many({local}.Count, {local});");
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
        indent.AppendLine($"return new {_qualifier.Qualify("ExerciseOutcome", _currentNamespace)}<{resultName}>.One(new {resultName}(");
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
                    indent.AppendLine($"{slot.FieldName}: new {_qualifier.Qualify("ContractId", _currentNamespace)}<{templateRef}>({local}[0]){separator}");
                    break;
                case CreatedCardinality.Optional:
                    indent.AppendLine($"{slot.FieldName}: {local}.Count == 1 ? new {_qualifier.Qualify("ContractId", _currentNamespace)}<{templateRef}>({local}[0]) : null{separator}");
                    break;
                case CreatedCardinality.List:
                    indent.AppendLine($"{slot.FieldName}: {local}.ConvertAll(c => new {_qualifier.Qualify("ContractId", _currentNamespace)}<{templateRef}>(c)){separator}");
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
