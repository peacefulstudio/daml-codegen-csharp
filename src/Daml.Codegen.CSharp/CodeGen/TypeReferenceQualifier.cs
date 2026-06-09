// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Resolves whether an imported runtime/BCL simple type name is shadowed by a
/// generated namespace segment in a given file, and qualifies it with
/// <c>global::</c> when it is. C# binds an unqualified simple name by walking
/// enclosing namespace scopes before consulting <c>using</c> directives, so a
/// generated namespace segment equal to an imported type name produces CS0118.
/// </summary>
public sealed class TypeReferenceQualifier
{
    private const string GlobalPrefix = "global::";

    private static readonly IReadOnlyDictionary<string, string> ImportedSimpleNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Party"] = "Daml.Runtime.Data",
            ["DamlRecord"] = "Daml.Runtime.Data",
            ["DamlField"] = "Daml.Runtime.Data",
            ["DamlValue"] = "Daml.Runtime.Data",
            ["IDamlValue"] = "Daml.Runtime.Data",
            ["IDamlRecord"] = "Daml.Runtime.Data",
            ["IDamlVariant"] = "Daml.Runtime.Data",
            ["DamlVariant"] = "Daml.Runtime.Data",
            ["DamlEnum"] = "Daml.Runtime.Data",
            ["DamlOptional"] = "Daml.Runtime.Data",
            ["DamlList"] = "Daml.Runtime.Data",
            ["DamlTextMap"] = "Daml.Runtime.Data",
            ["DamlGenMap"] = "Daml.Runtime.Data",
            ["DamlInt64"] = "Daml.Runtime.Data",
            ["DamlNumeric"] = "Daml.Runtime.Data",
            ["DamlText"] = "Daml.Runtime.Data",
            ["DamlBool"] = "Daml.Runtime.Data",
            ["DamlUnit"] = "Daml.Runtime.Data",
            ["DamlDate"] = "Daml.Runtime.Data",
            ["DamlTimestamp"] = "Daml.Runtime.Data",
            ["DamlParty"] = "Daml.Runtime.Data",
            ["Identifier"] = "Daml.Runtime.Data",
            ["DamlContractId"] = "Daml.Runtime.Contracts",
            ["ContractId"] = "Daml.Runtime.Contracts",
            ["ITemplate"] = "Daml.Runtime.Contracts",
            ["IHasKey"] = "Daml.Runtime.Contracts",
            ["IUpgradeable"] = "Daml.Runtime.Contracts",
            ["IContract"] = "Daml.Runtime.Contracts",
            ["TransactionResult"] = "Daml.Runtime.Contracts",
            ["CreatedContract"] = "Daml.Runtime.Contracts",
            ["Choice"] = "Daml.Runtime.Commands",
            ["SubmitterInfo"] = "Daml.Runtime.Commands",
            ["IExercises"] = "Daml.Runtime.Commands",
            ["ExerciseCommand"] = "Daml.Runtime.Commands",
            ["CommandsSubmission"] = "Daml.Runtime.Commands",
            ["WorkflowId"] = "Daml.Runtime.Commands",
            ["CommandId"] = "Daml.Runtime.Commands",
            ["ChoiceName"] = "Daml.Runtime.Commands",
            ["IDamlInterface"] = "Daml.Runtime.Contracts",
            ["IHasView"] = "Daml.Runtime.Contracts",
            ["CreatedEvent"] = "Daml.Runtime.Contracts",
            ["ExerciseOutcome"] = "Daml.Runtime.Outcomes",
            ["RelTime"] = "Daml.Runtime.Stdlib",
            ["Tuple2"] = "Daml.Runtime.Stdlib",
            ["Tuple3"] = "Daml.Runtime.Stdlib",
            ["Either"] = "Daml.Runtime.Stdlib",
            ["Set"] = "Daml.Runtime.Stdlib",
            ["NonEmpty"] = "Daml.Runtime.Stdlib",
            ["Map"] = "Daml.Runtime.Stdlib",
            ["Unit"] = "Daml.Runtime.Stdlib",
            ["GenericStub"] = "Daml.Runtime.Stdlib",
            ["ILedgerClient"] = "Daml.Ledger.Abstractions",
            ["IReadOnlyList"] = "System.Collections.Generic",
            ["IReadOnlyDictionary"] = "System.Collections.Generic",
            ["HashSet"] = "System.Collections.Generic",
        };

    public IReadOnlySet<string> AllNamespaces { get; }

    public TypeReferenceQualifier(IEnumerable<string> generatedNamespaces)
    {
        var all = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ns in generatedNamespaces)
        {
            AddWithAncestors(all, ns);
        }

        AllNamespaces = all;
    }

    /// <summary>
    /// Qualifies the head symbol of a C# type reference. Returns
    /// <c>global::Owning.Namespace.<paramref name="simpleName"/></c> when
    /// <paramref name="simpleName"/> is an imported runtime/BCL type shadowed by a
    /// generated namespace segment reachable from <paramref name="currentNamespace"/>;
    /// returns the name unchanged otherwise. Generic arguments are composed by the
    /// caller, e.g. <c>$"{Qualify("ContractId", ns)}&lt;{inner}&gt;"</c>. Names already
    /// <c>global::</c>-qualified or namespace-qualified are returned unchanged.
    /// </summary>
    public string Qualify(string simpleName, string currentNamespace) =>
        QualifyHead(simpleName, currentNamespace);

    private string QualifyHead(string simpleName, string currentNamespace)
    {
        if (simpleName.StartsWith(GlobalPrefix, StringComparison.Ordinal)
            || simpleName.Contains('.')
            || !ImportedSimpleNames.TryGetValue(simpleName, out var owningNamespace)
            || !IsShadowed(simpleName, currentNamespace))
        {
            return simpleName;
        }

        return $"{GlobalPrefix}{owningNamespace}.{simpleName}";
    }

    private bool IsShadowed(string simpleName, string currentNamespace)
    {
        if (AllNamespaces.Contains(simpleName))
        {
            return true;
        }

        var segments = currentNamespace.Split('.');
        for (var length = 1; length <= segments.Length; length++)
        {
            var prefix = string.Join('.', segments[..length]);
            if (AllNamespaces.Contains($"{prefix}.{simpleName}"))
            {
                return true;
            }
        }

        return false;
    }

    private static void AddWithAncestors(HashSet<string> target, string ns)
    {
        var segments = ns.Split('.');
        for (var length = 1; length <= segments.Length; length++)
        {
            target.Add(string.Join('.', segments[..length]));
        }
    }
}
