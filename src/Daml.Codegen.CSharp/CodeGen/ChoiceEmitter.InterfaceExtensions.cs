// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.Model;

namespace Daml.Codegen.CSharp.CodeGen;

internal sealed partial class ChoiceEmitter
{
    internal void WriteInterfaceMethod(IndentWriter indent, DamlChoice method, IReadOnlyDictionary<string, DamlDataType> dataTypes)
    {
        var methodName = SanitizeIdentifier(method.Name);
        var returnType = mapper.MapType(method.ReturnType);
        var (argTypeName, _, _, _) = GetChoiceArgumentInfo(method, dataTypes);

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine($"// Interface method {method.Name}.");
        }

        if (argTypeName == "DamlUnit")
        {
            indent.AppendLine($"// Choice {method.Name}() -> {returnType}");
        }
        else
        {
            indent.AppendLine($"// Choice {method.Name}({argTypeName}) -> {returnType}");
        }
    }

    internal void WriteInterfaceChoiceExtensions(
        IndentWriter indent,
        DamlInterface iface,
        string interfaceName)
    {
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Static <c>&lt;Choice&gt;Async</c> extension methods for the <c>{iface.Name}</c> Daml interface.");
            indent.AppendLine("/// One method per choice; each submits an interface-typed");
            indent.AppendLine($"/// <see cref=\"global::Daml.Runtime.Commands.ExerciseCommand\"/> built via");
            indent.AppendLine($"/// <see cref=\"global::Daml.Runtime.Commands.ExerciseCommand.ForInterface{{TInterface}}(global::Daml.Runtime.Contracts.ContractId{{TInterface}},global::Daml.Runtime.Commands.ChoiceName,global::Daml.Runtime.Data.DamlValue)\"/>");
            indent.AppendLine("/// through <see cref=\"global::Daml.Ledger.Abstractions.ILedgerWriter.TrySubmitAndWaitForTransactionAsync\"/>");
            indent.AppendLine($"/// and surfaces the raw <see cref=\"global::Daml.Runtime.Outcomes.ExerciseOutcome{{TransactionResult}}\"/> —");
            indent.AppendLine("/// interface choices have no typed <c>&lt;Choice&gt;Result</c> projection because the");
            indent.AppendLine("/// implementing template (and therefore the produced contracts' shapes) is unknown");
            indent.AppendLine("/// at the call site.");
            indent.AppendLine("/// </summary>");
        }

        var extensionsClassName = $"{interfaceName}Extensions";

        var emittable = iface.Choices.ToList();

        if (emittable.Count == 0)
        {
            return;
        }

        RequireAsyncExerciserNamespaces(indent);

        indent.AppendLine($"public static class {extensionsClassName}");
        indent.AppendLine("{");
        indent.Indent();

        for (var i = 0; i < emittable.Count; i++)
        {
            if (i > 0)
            {
                indent.AppendLine();
            }
            WriteInterfaceChoiceExtensionMethod(indent, emittable[i], interfaceName);
        }

        indent.Dedent();
        indent.AppendLine("}");
    }

    private void WriteInterfaceChoiceExtensionMethod(
        IndentWriter indent,
        DamlChoice choice,
        string interfaceName)
    {
        var choiceName = SanitizeIdentifier(choice.Name);
        var commandMethodName = $"{choiceName}Command";
        var methodName = $"{choiceName}Async";
        var (argTypeName, hasArg) = ResolveInterfaceChoiceArgType(choice);
        var requiresArgumentNullCheck = hasArg && choice.ArgumentType is DamlTypeRef;
        var argExpr = hasArg
            ? mapper.ToValue(choice.ArgumentType, "argument")
            : EmptyArgumentExpression(choice);

        WriteInterfaceChoiceCommandBuilder(indent, choice, interfaceName, commandMethodName, argTypeName, hasArg, requiresArgumentNullCheck, argExpr);
        indent.AppendLine();
        WriteInterfaceChoiceAsyncMethod(indent, choice, interfaceName, commandMethodName, methodName, argTypeName, hasArg);
    }

    private void WriteInterfaceChoiceCommandBuilder(
        IndentWriter indent,
        DamlChoice choice,
        string interfaceName,
        string commandMethodName,
        string argTypeName,
        bool hasArg,
        bool requiresArgumentNullCheck,
        string argExpr)
    {
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Builds the interface-typed <see cref=\"global::Daml.Runtime.Commands.ExerciseCommand\"/> for the <c>{choice.Name}</c> choice on this contract id.");
            indent.AppendLine("/// The wire-level <c>template_id</c> slot carries the interface id — Canton's");
            indent.AppendLine("/// ledger API resolves the concrete implementing template at the participant.");
            indent.AppendLine("/// </summary>");
            indent.AppendLine("/// <param name=\"contractId\">The interface-typed contract id to exercise on.</param>");
            if (hasArg)
            {
                indent.AppendLine("/// <param name=\"argument\">The choice argument.</param>");
            }
        }

        indent.AppendLine($"public static {context.Qualifier.Qualify(RuntimeTypeNames.ExerciseCommand, context.RootNamespace)} {commandMethodName}(");
        indent.Indent();
        if (hasArg)
        {
            indent.AppendLine($"this {context.Qualifier.Qualify(RuntimeTypeNames.ContractId, context.RootNamespace)}<{interfaceName}> contractId,");
            indent.AppendLine($"{argTypeName} argument)");
        }
        else
        {
            indent.AppendLine($"this {context.Qualifier.Qualify(RuntimeTypeNames.ContractId, context.RootNamespace)}<{interfaceName}> contractId)");
        }
        indent.Dedent();
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine("ArgumentNullException.ThrowIfNull(contractId);");
        if (requiresArgumentNullCheck)
        {
            indent.AppendLine("ArgumentNullException.ThrowIfNull(argument);");
        }
        indent.AppendLine($"return {context.Qualifier.Qualify(RuntimeTypeNames.ExerciseCommand, context.RootNamespace)}.ForInterface<{interfaceName}>(contractId, new {context.Qualifier.Qualify(RuntimeTypeNames.ChoiceName, context.RootNamespace)}(\"{choice.Name}\"), {argExpr});");
        indent.Dedent();
        indent.AppendLine("}");
    }

    private void WriteInterfaceChoiceAsyncMethod(
        IndentWriter indent,
        DamlChoice choice,
        string interfaceName,
        string commandMethodName,
        string methodName,
        string argTypeName,
        bool hasArg)
    {
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Exercises the <c>{choice.Name}</c> interface choice on this contract id, submitting the");
            indent.AppendLine("/// resulting <see cref=\"global::Daml.Runtime.Commands.ExerciseCommand\"/> through");
            indent.AppendLine("/// <see cref=\"global::Daml.Ledger.Abstractions.ILedgerWriter.TrySubmitAndWaitForTransactionAsync\"/> and awaiting the outcome.");
            indent.AppendLine("/// </summary>");
            indent.AppendLine("/// <param name=\"contractId\">The interface-typed contract id to exercise on.</param>");
            indent.AppendLine("/// <param name=\"client\">The ledger client.</param>");
            if (hasArg)
            {
                indent.AppendLine("/// <param name=\"argument\">The choice argument.</param>");
            }
            indent.AppendLine("/// <param name=\"actAs\">The party submitting the command.</param>");
            indent.AppendLine("/// <param name=\"workflowId\">Optional workflow id; passed through to the ledger when supplied. No default — workflow IDs are correlation keys, and a per-choice default would bucket every submission of the same choice under one ID.</param>");
            indent.AppendLine("/// <param name=\"commandId\">Optional command id for deduplication; a fresh id is minted only when omitted. Pass the same id across a retry of a lost-but-accepted submission so the ledger deduplicates the resubmission instead of re-executing the choice.</param>");
            indent.AppendLine("/// <param name=\"timeout\">Optional per-call deadline, enforced server-side; the default <c>null</c> applies no deadline. An overrun surfaces as an <c>InfraError</c> outcome.</param>");
            indent.AppendLine("/// <param name=\"cancellationToken\">Cancellation token.</param>");
        }

        // Method signature mirrors the concrete-template <Choice>Async shape,
        // but skips the typed <Choice>Result projection: interface choices do not know
        // the implementing template at the call site, so the most useful return shape
        // is the raw ExerciseOutcome<TransactionResult> the ledger client surfaces.
        indent.AppendLine($"public static async Task<{context.Qualifier.Qualify(RuntimeTypeNames.ExerciseOutcome, context.RootNamespace)}<{context.Qualifier.Qualify(RuntimeTypeNames.TransactionResult, context.RootNamespace)}>> {methodName}(");
        indent.Indent();
        indent.AppendLine($"this {context.Qualifier.Qualify(RuntimeTypeNames.ContractId, context.RootNamespace)}<{interfaceName}> contractId,");
        indent.AppendLine($"{context.Qualifier.Qualify(RuntimeTypeNames.ILedgerWriter, context.RootNamespace)} client,");
        if (hasArg)
        {
            indent.AppendLine($"{argTypeName} argument,");
        }
        indent.AppendLine($"{context.Qualifier.Qualify(RuntimeTypeNames.Party, context.RootNamespace)} actAs,");
        indent.AppendLine("string? workflowId = null,");
        indent.AppendLine($"{context.Qualifier.Qualify(RuntimeTypeNames.CommandId, context.RootNamespace)}? commandId = null,");
        indent.AppendLine("TimeSpan? timeout = null,");
        indent.AppendLine("CancellationToken cancellationToken = default)");
        indent.Dedent();
        indent.AppendLine("{");
        indent.Indent();

        indent.AppendLine("ArgumentNullException.ThrowIfNull(client);");
        indent.AppendLine(hasArg
            ? $"var command = contractId.{commandMethodName}(argument);"
            : $"var command = contractId.{commandMethodName}();");
        indent.AppendLine();
        indent.AppendLine($"var submission = {context.Qualifier.Qualify(RuntimeTypeNames.CommandsSubmission, context.RootNamespace)}.Single(command)");
        indent.Indent();
        indent.AppendLine($".WithCommandId(commandId ?? new {context.Qualifier.Qualify(RuntimeTypeNames.CommandId, context.RootNamespace)}(Guid.NewGuid().ToString()));");
        indent.Dedent();
        indent.AppendLine("if (!string.IsNullOrEmpty(workflowId))");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine($"submission = submission.WithWorkflowId(new {context.Qualifier.Qualify(RuntimeTypeNames.WorkflowId, context.RootNamespace)}(workflowId));");
        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine();
        indent.AppendLine("return await client.TrySubmitAndWaitForTransactionAsync(submission, actAs, timeout: timeout, cancellationToken: cancellationToken).ConfigureAwait(false);");

        indent.Dedent();
        indent.AppendLine("}");
    }

    private (string TypeName, bool HasArg) ResolveInterfaceChoiceArgType(DamlChoice choice)
    {
        if (choice.ArgumentType is DamlPrimitiveType { Primitive: DamlPrimitive.Unit })
        {
            return ("DamlUnit", false);
        }
        if (IsSyntheticArchive(choice))
        {
            return ("DamlUnit", false);
        }
        return (mapper.MapType(choice.ArgumentType), true);
    }
}
