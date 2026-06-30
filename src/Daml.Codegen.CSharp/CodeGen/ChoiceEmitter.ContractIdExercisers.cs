// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.Model;

namespace Daml.Codegen.CSharp.CodeGen;

public sealed partial class ChoiceEmitter
{
    /// <summary>
    /// Emits the <c>&lt;Choice&gt;Result</c> nested record (typed result struct) and a
    /// static <c>FromCreatedContracts(...)</c> projector for every choice on
    /// <paramref name="template"/> whose return type carries one or more
    /// <c>ContractId T</c>s. See <see cref="ChoiceCreatedSlots.Extract"/>.
    /// </summary>
    /// <param name="indent">Writer positioned at the emission point in the template's file.</param>
    /// <param name="template">The template whose choices are scanned for created-contract slots.</param>
    /// <param name="moduleNamespace">
    /// Fully-qualified C# namespace of the emitted template. Used to <c>global::</c>-qualify
    /// in-package template references inside the projector body so positional record
    /// properties on the result type (e.g. a slot named <c>Agreement</c>) cannot shadow
    /// the template type when looking up <c>Agreement.TemplateId</c>.
    /// </param>
    internal void WriteChoiceResultStructs(IndentWriter indent, DamlTemplate template, string moduleNamespace)
    {
        foreach (var choice in template.Choices)
        {
            var slots = ChoiceCreatedSlots.Extract(context, resolver, mapper, choice.ReturnType);
            if (slots.Count == 0)
            {
                continue;
            }

            WriteSingleChoiceResultStruct(indent, choice, slots, moduleNamespace);
        }
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="template"/> has at least one choice that
    /// (a) creates contracts (<see cref="ChoiceCreatedSlots.Extract"/> yields a non-empty list) and
    /// (b) does not go through the codegen's argument-type fallback (B3 — no
    /// <c>argument.ToRecord()</c> on a stub).
    /// </summary>
    private bool TemplateHasEmittableAsyncExercisers(
        DamlTemplate template,
        IReadOnlyDictionary<string, DamlDataType> dataTypes)
    {
        foreach (var choice in template.Choices)
        {
            var slots = ChoiceCreatedSlots.Extract(context, resolver, mapper, choice.ReturnType);
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
    internal void WriteChoiceAsyncExercisersClass(
        IndentWriter indent,
        DamlTemplate template,
        string templateClassName,
        IReadOnlyList<DamlFieldDefinition> fields,
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
        var templateObservers = party.ValidatePayloadParties(template.Observers, partyFields);

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
            var slots = ChoiceCreatedSlots.Extract(context, resolver, mapper, choice.ReturnType);
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
            var controllers = party.ValidatePayloadParties(choice.Controllers, partyFields);
            var choiceObservers = party.ValidatePayloadParties(choice.Observers, partyFields);
            var effectiveReadAs = party.UnionStaticParties(templateObservers, choiceObservers);
            WriteSingleChoiceAsyncExerciser(
                indent, choice, templateClassName, dataTypes, controllers, effectiveReadAs);

            if (controllers.Source == DamlPartySource.Static && controllers.Parties.Count > 0)
            {
                indent.AppendLine();
                WriteSingleContractChoiceAsyncExerciser(
                    indent, choice, templateClassName, dataTypes, controllers, effectiveReadAs);
            }
            first = false;
        }

        indent.Dedent();
        indent.AppendLine("}");
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
            ? party.PartitionControllersAndObservers(controllers, observers)
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
        indent.AppendLine($"public static async Task<{context.Qualifier.Qualify(RuntimeTypeNames.ExerciseOutcome, context.RootNamespace)}<{resultName}>> {choiceName}Async(");
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

        if (staticControllers)
        {
            foreach (var paramName in controllerParams)
            {
                indent.AppendLine($"{context.Qualifier.Qualify(RuntimeTypeNames.Party, context.RootNamespace)} {paramName},");
            }
            foreach (var paramName in readAsParams)
            {
                indent.AppendLine($"{context.Qualifier.Qualify(RuntimeTypeNames.Party, context.RootNamespace)} {paramName},");
            }
        }
        else
        {
            indent.AppendLine($"{context.Qualifier.Qualify(RuntimeTypeNames.SubmitterInfo, context.RootNamespace)} submitter,");
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
                indent.AppendLine($"{context.Qualifier.Qualify(RuntimeTypeNames.SubmitterInfo, context.RootNamespace)} submitter = {controllerParams[0]};");
            }
            else if (readAsParams.Count == 0)
            {
                indent.Require("System.Collections.Generic");
                indent.AppendLine("// SubmitterInfo's actAs unions every named controller.");
                indent.AppendLine($"var submitter = new {context.Qualifier.Qualify(RuntimeTypeNames.SubmitterInfo, context.RootNamespace)}(new {context.Qualifier.Qualify("HashSet", context.RootNamespace)}<{context.Qualifier.Qualify(RuntimeTypeNames.Party, context.RootNamespace)}> {{ {string.Join(", ", controllerParams)} }});");
            }
            else
            {
                indent.Require("System.Collections.Generic");
                indent.AppendLine("// actAs unions every named controller; readAs unions every observer that is");
                indent.AppendLine("// not also a controller, so the wire format reflects Daml's stakeholder model.");
                indent.AppendLine($"var submitter = new {context.Qualifier.Qualify(RuntimeTypeNames.SubmitterInfo, context.RootNamespace)}(");
                indent.Indent();
                indent.AppendLine($"actAs: new {context.Qualifier.Qualify("HashSet", context.RootNamespace)}<{context.Qualifier.Qualify(RuntimeTypeNames.Party, context.RootNamespace)}> {{ {string.Join(", ", controllerParams)} }},");
                indent.AppendLine($"readAs: new {context.Qualifier.Qualify("HashSet", context.RootNamespace)}<{context.Qualifier.Qualify(RuntimeTypeNames.Party, context.RootNamespace)}> {{ {string.Join(", ", readAsParams)} }});");
                indent.Dedent();
            }
        }

        indent.AppendLine();
        var argExpr = hasArg ? "argument.ToRecord()" : $"{context.Qualifier.Qualify(RuntimeTypeNames.DamlUnit, context.RootNamespace)}.Instance";
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
        indent.AppendLine(".WithSubmitter(submitter)");
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
        indent.AppendLine($"{context.Qualifier.Qualify(RuntimeTypeNames.ExerciseOutcome, context.RootNamespace)}<{context.Qualifier.Qualify(RuntimeTypeNames.TransactionResult, context.RootNamespace)}>.One success => {resultName}.FromCreatedContracts(success.Result.CreatedContracts),");
        indent.AppendLine($"{context.Qualifier.Qualify(RuntimeTypeNames.ExerciseOutcome, context.RootNamespace)}<{context.Qualifier.Qualify(RuntimeTypeNames.TransactionResult, context.RootNamespace)}>.DamlError damlError => new {context.Qualifier.Qualify(RuntimeTypeNames.ExerciseOutcome, context.RootNamespace)}<{resultName}>.DamlError(damlError.Category, damlError.ErrorId, damlError.Message, damlError.Metadata),");
        indent.AppendLine($"{context.Qualifier.Qualify(RuntimeTypeNames.ExerciseOutcome, context.RootNamespace)}<{context.Qualifier.Qualify(RuntimeTypeNames.TransactionResult, context.RootNamespace)}>.InfraError infraError => new {context.Qualifier.Qualify(RuntimeTypeNames.ExerciseOutcome, context.RootNamespace)}<{resultName}>.InfraError(infraError.StatusCode, infraError.Message),");
        indent.AppendLine("_ => throw new InvalidOperationException($\"Unhandled outcome: {outcome.GetType().Name}\"),");
        indent.Dedent();
        indent.AppendLine("};");

        indent.Dedent();
        indent.AppendLine("}");
    }

    /// <summary>
    /// Emits the sibling <c>&lt;Choice&gt;Async</c> overload that receives the
    /// generated nested <c>TemplateName.Contract</c> — the type
    /// <c>TemplateName.Contract.FromCreatedEvent</c> returns — instead of a bare
    /// <c>ContractId&lt;TemplateName&gt;</c>. Targeting the nested record (rather
    /// than the runtime <c>Contract&lt;T&gt;</c> base) keeps the overload reachable
    /// from a <c>FromCreatedEvent</c> result without an intermediate allocation.
    /// Because the receiver carries the payload, the wrapper reads every
    /// controller / observer party off <c>contract.Data</c> and delegates to the
    /// <c>ContractId&lt;T&gt;</c> overload — the caller passes zero parties. Emitted
    /// only when controllers are statically resolvable to payload fields; the
    /// dynamic case has no payload-derivable submitter, so no <c>Contract</c>
    /// overload is generated and callers stay on the named-parameter
    /// <c>ContractId&lt;T&gt;</c> path.
    /// </summary>
    private void WriteSingleContractChoiceAsyncExerciser(
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

        var (controllerFieldNames, readAsFieldNames) =
            party.PartitionControllerAndObserverFieldNames(controllers, observers);
        var partyArguments = controllerFieldNames
            .Concat(readAsFieldNames)
            .Select(fieldName => $"contract.Data.{MemberName(fieldName, templateClassName)}")
            .ToList();

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Exercises the {choice.Name} choice on a fetched <see cref=\"{templateClassName}\"/> contract,");
            indent.AppendLine("/// reading every controller and observer party off the contract payload so the");
            indent.AppendLine("/// caller passes no parties. Delegates to the");
            indent.AppendLine($"/// <c>ContractId&lt;{templateClassName}&gt;</c> overload.");
            indent.AppendLine("/// </summary>");
            indent.AppendLine("/// <param name=\"contract\">The fetched contract on which to exercise the choice.</param>");
            indent.AppendLine("/// <param name=\"client\">The ledger client.</param>");
            if (hasArg)
            {
                indent.AppendLine("/// <param name=\"argument\">The choice argument.</param>");
            }
            indent.AppendLine("/// <param name=\"workflowId\">Optional workflow id; passed through to the ledger when supplied. No default — workflow IDs are correlation keys, and a per-choice default would bucket every submission of the same choice under one ID.</param>");
            indent.AppendLine("/// <param name=\"cancellationToken\">Cancellation token.</param>");
        }

        indent.AppendLine($"public static Task<{context.Qualifier.Qualify(RuntimeTypeNames.ExerciseOutcome, context.RootNamespace)}<{resultName}>> {choiceName}Async(");
        indent.Indent();
        indent.AppendLine($"this {templateClassName}.Contract contract,");
        indent.AppendLine($"{context.Qualifier.Qualify(RuntimeTypeNames.ILedgerClient, context.RootNamespace)} client,");
        if (hasArg)
        {
            var argParamType = isNestedTemplateArg
                ? $"{templateClassName}.{argTypeName}"
                : argTypeName;
            indent.AppendLine($"{argParamType} argument,");
        }
        indent.AppendLine("string? workflowId = null,");
        indent.AppendLine("CancellationToken cancellationToken = default)");
        indent.Dedent();
        indent.AppendLine("{");
        indent.Indent();

        indent.AppendLine("ArgumentNullException.ThrowIfNull(contract);");
        indent.AppendLine("ArgumentNullException.ThrowIfNull(client);");
        if (hasArg)
        {
            indent.AppendLine("ArgumentNullException.ThrowIfNull(argument);");
        }

        indent.AppendLine();
        indent.AppendLine($"return contract.Id.{choiceName}Async(");
        indent.Indent();
        indent.AppendLine("client,");
        if (hasArg)
        {
            indent.AppendLine("argument,");
        }
        foreach (var partyArgument in partyArguments)
        {
            indent.AppendLine($"{partyArgument},");
        }
        indent.AppendLine("workflowId,");
        indent.AppendLine("cancellationToken);");
        indent.Dedent();

        indent.Dedent();
        indent.AppendLine("}");
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
        CreatedCardinality.Single => $"{context.Qualifier.Qualify(RuntimeTypeNames.ContractId, context.RootNamespace)}<{slot.CSharpTemplateType}>",
        CreatedCardinality.Optional => $"{context.Qualifier.Qualify(RuntimeTypeNames.ContractId, context.RootNamespace)}<{slot.CSharpTemplateType}>?",
        CreatedCardinality.List => $"{context.Qualifier.Qualify("IReadOnlyList", context.RootNamespace)}<{context.Qualifier.Qualify(RuntimeTypeNames.ContractId, context.RootNamespace)}<{slot.CSharpTemplateType}>>",
        _ => $"{context.Qualifier.Qualify(RuntimeTypeNames.ContractId, context.RootNamespace)}<{slot.CSharpTemplateType}>",
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

        indent.AppendLine($"public static {context.Qualifier.Qualify(RuntimeTypeNames.ExerciseOutcome, context.RootNamespace)}<{resultName}> FromCreatedContracts(IEnumerable<{context.Qualifier.Qualify(RuntimeTypeNames.CreatedContract, context.RootNamespace)}> created)");
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
                    indent.AppendLine($"return new {context.Qualifier.Qualify(RuntimeTypeNames.ExerciseOutcome, context.RootNamespace)}<{resultName}>.None();");
                    indent.Dedent();
                    indent.AppendLine("}");
                    indent.AppendLine($"if ({local}.Count > 1)");
                    indent.AppendLine("{");
                    indent.Indent();
                    indent.AppendLine($"return new {context.Qualifier.Qualify(RuntimeTypeNames.ExerciseOutcome, context.RootNamespace)}<{resultName}>.Many({local}.Count, {local});");
                    indent.Dedent();
                    indent.AppendLine("}");
                    break;
                case CreatedCardinality.Optional:
                    indent.AppendLine($"if ({local}.Count > 1)");
                    indent.AppendLine("{");
                    indent.Indent();
                    indent.AppendLine($"return new {context.Qualifier.Qualify(RuntimeTypeNames.ExerciseOutcome, context.RootNamespace)}<{resultName}>.Many({local}.Count, {local});");
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
        indent.AppendLine($"return new {context.Qualifier.Qualify(RuntimeTypeNames.ExerciseOutcome, context.RootNamespace)}<{resultName}>.One(new {resultName}(");
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
                    indent.AppendLine($"{slot.FieldName}: new {context.Qualifier.Qualify(RuntimeTypeNames.ContractId, context.RootNamespace)}<{templateRef}>({local}[0]){separator}");
                    break;
                case CreatedCardinality.Optional:
                    indent.AppendLine($"{slot.FieldName}: {local}.Count == 1 ? new {context.Qualifier.Qualify(RuntimeTypeNames.ContractId, context.RootNamespace)}<{templateRef}>({local}[0]) : null{separator}");
                    break;
                case CreatedCardinality.List:
                    indent.AppendLine($"{slot.FieldName}: {local}.ConvertAll(c => new {context.Qualifier.Qualify(RuntimeTypeNames.ContractId, context.RootNamespace)}<{templateRef}>(c)){separator}");
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
