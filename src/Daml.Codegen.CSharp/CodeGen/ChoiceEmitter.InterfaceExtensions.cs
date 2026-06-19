// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.Model;

namespace Daml.Codegen.CSharp.CodeGen;

public sealed partial class ChoiceEmitter
{
    internal void WriteInterfaceMethod(IndentWriter indent, DamlChoice method, IReadOnlyDictionary<string, DamlDataType> dataTypes)
    {
        var methodName = SanitizeIdentifier(method.Name);
        var returnType = mapper.MapType(method.ReturnType);
        var (argTypeName, _, _, _) = GetChoiceArgumentInfo(method, dataTypes);

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Interface method {method.Name}.");
            indent.AppendLine("/// </summary>");
        }

        // Generate method signature
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
            indent.AppendLine("/// through <see cref=\"global::Daml.Ledger.Abstractions.ILedgerClient.TrySubmitAndWaitForTransactionAsync\"/>");
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
        var methodName = $"{SanitizeIdentifier(choice.Name)}Async";
        var (argTypeName, hasArg) = ResolveInterfaceChoiceArgType(choice);

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Exercises the <c>{choice.Name}</c> interface choice on this contract id.");
            indent.AppendLine("/// The wire-level <c>template_id</c> slot carries the interface id — Canton's");
            indent.AppendLine("/// ledger API resolves the concrete implementing template at the participant.");
            indent.AppendLine("/// </summary>");
            indent.AppendLine("/// <param name=\"contractId\">The interface-typed contract id to exercise on.</param>");
            indent.AppendLine("/// <param name=\"client\">The ledger client.</param>");
            if (hasArg)
            {
                indent.AppendLine("/// <param name=\"argument\">The choice argument.</param>");
            }
            indent.AppendLine("/// <param name=\"actAs\">The party submitting the command.</param>");
            indent.AppendLine("/// <param name=\"workflowId\">Optional workflow id; passed through to the ledger when supplied. No default — workflow IDs are correlation keys, and a per-choice default would bucket every submission of the same choice under one ID.</param>");
            indent.AppendLine("/// <param name=\"cancellationToken\">Cancellation token.</param>");
        }

        // Method signature mirrors the concrete-template <Choice>Async shape,
        // but skips the typed <Choice>Result projection: interface choices do not know
        // the implementing template at the call site, so the most useful return shape
        // is the raw ExerciseOutcome<TransactionResult> the ledger client surfaces.
        indent.AppendLine($"public static async Task<{context.Qualifier.Qualify(RuntimeTypeNames.ExerciseOutcome, context.RootNamespace)}<{context.Qualifier.Qualify(RuntimeTypeNames.TransactionResult, context.RootNamespace)}>> {methodName}(");
        indent.Indent();
        indent.AppendLine($"this {context.Qualifier.Qualify(RuntimeTypeNames.ContractId, context.RootNamespace)}<{interfaceName}> contractId,");
        indent.AppendLine($"{context.Qualifier.Qualify(RuntimeTypeNames.ILedgerClient, context.RootNamespace)} client,");
        if (hasArg)
        {
            indent.AppendLine($"{argTypeName} argument,");
        }
        indent.AppendLine($"{context.Qualifier.Qualify(RuntimeTypeNames.Party, context.RootNamespace)} actAs,");
        indent.AppendLine("string? workflowId = null,");
        indent.AppendLine("CancellationToken cancellationToken = default)");
        indent.Dedent();
        indent.AppendLine("{");
        indent.Indent();

        indent.AppendLine("ArgumentNullException.ThrowIfNull(contractId);");
        indent.AppendLine("ArgumentNullException.ThrowIfNull(client);");
        if (hasArg && choice.ArgumentType is DamlTypeRef)
        {
            indent.AppendLine("ArgumentNullException.ThrowIfNull(argument);");
        }

        var argExpr = hasArg
            ? mapper.ToValue(choice.ArgumentType, "argument")
            : $"{context.Qualifier.Qualify(RuntimeTypeNames.DamlUnit, context.RootNamespace)}.Instance";
        indent.AppendLine($"var command = {context.Qualifier.Qualify(RuntimeTypeNames.ExerciseCommand, context.RootNamespace)}.ForInterface<{interfaceName}>(contractId, new {context.Qualifier.Qualify(RuntimeTypeNames.ChoiceName, context.RootNamespace)}(\"{choice.Name}\"), {argExpr});");
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
        indent.AppendLine("return await client.TrySubmitAndWaitForTransactionAsync(submission, cancellationToken).ConfigureAwait(false);");

        indent.Dedent();
        indent.AppendLine("}");
    }

    private (string TypeName, bool HasArg) ResolveInterfaceChoiceArgType(DamlChoice choice)
    {
        if (choice.ArgumentType is DamlPrimitiveType { Primitive: DamlPrimitive.Unit })
        {
            return ("DamlUnit", false);
        }
        if (choice.ArgumentType is DamlTypeRef { Name: "Archive", Module: "DA.Internal.Template" } archiveRef
            && !string.IsNullOrEmpty(archiveRef.PackageId)
            && resolver.LookupPackage(archiveRef.PackageId) is { } archivePkg
            && (IsStdlibPackage(archivePkg.Name) || IsPlaceholderPackageName(archivePkg.Name)))
        {
            return ("DamlUnit", false);
        }
        return (mapper.MapType(choice.ArgumentType), true);
    }
}
