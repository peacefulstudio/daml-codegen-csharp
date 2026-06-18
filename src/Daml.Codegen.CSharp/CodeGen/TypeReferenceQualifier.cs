// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using LedgerNamespaces = Daml.Ledger.Abstractions.LedgerNamespaces;
using RuntimeNamespaces = Daml.Runtime.RuntimeNamespaces;

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
            [RuntimeTypeNames.Party] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlRecord] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlField] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlValue] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.IDamlValue] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.IDamlRecord] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.IDamlVariant] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlVariant] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlEnum] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlOptional] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlList] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlTextMap] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlGenMap] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlInt64] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlNumeric] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlText] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlBool] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlUnit] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlDate] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlTimestamp] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlParty] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.Identifier] = RuntimeNamespaces.Data,
            [RuntimeTypeNames.DamlContractId] = RuntimeNamespaces.Contracts,
            [RuntimeTypeNames.ContractId] = RuntimeNamespaces.Contracts,
            [RuntimeTypeNames.ITemplate] = RuntimeNamespaces.Contracts,
            [RuntimeTypeNames.IHasKey] = RuntimeNamespaces.Contracts,
            [RuntimeTypeNames.IUpgradeable] = RuntimeNamespaces.Contracts,
            [RuntimeTypeNames.IContract] = RuntimeNamespaces.Contracts,
            [RuntimeTypeNames.TransactionResult] = RuntimeNamespaces.Contracts,
            [RuntimeTypeNames.CreatedContract] = RuntimeNamespaces.Contracts,
            [RuntimeTypeNames.Choice] = RuntimeNamespaces.Commands,
            [RuntimeTypeNames.SubmitterInfo] = RuntimeNamespaces.Commands,
            [RuntimeTypeNames.IExercises] = RuntimeNamespaces.Commands,
            [RuntimeTypeNames.ExerciseCommand] = RuntimeNamespaces.Commands,
            [RuntimeTypeNames.CommandsSubmission] = RuntimeNamespaces.Commands,
            [RuntimeTypeNames.WorkflowId] = RuntimeNamespaces.Commands,
            [RuntimeTypeNames.CommandId] = RuntimeNamespaces.Commands,
            [RuntimeTypeNames.ChoiceName] = RuntimeNamespaces.Commands,
            [RuntimeTypeNames.IDamlInterface] = RuntimeNamespaces.Contracts,
            [RuntimeTypeNames.IHasView] = RuntimeNamespaces.Contracts,
            [RuntimeTypeNames.CreatedEvent] = RuntimeNamespaces.Contracts,
            [RuntimeTypeNames.ExerciseOutcome] = RuntimeNamespaces.Outcomes,
            [RuntimeTypeNames.RelTime] = RuntimeNamespaces.Stdlib,
            [RuntimeTypeNames.Tuple2] = RuntimeNamespaces.Stdlib,
            [RuntimeTypeNames.Tuple3] = RuntimeNamespaces.Stdlib,
            [RuntimeTypeNames.Either] = RuntimeNamespaces.Stdlib,
            [RuntimeTypeNames.Set] = RuntimeNamespaces.Stdlib,
            [RuntimeTypeNames.NonEmpty] = RuntimeNamespaces.Stdlib,
            [RuntimeTypeNames.Map] = RuntimeNamespaces.Stdlib,
            [RuntimeTypeNames.Unit] = RuntimeNamespaces.Stdlib,
            [RuntimeTypeNames.GenericStub] = RuntimeNamespaces.Stdlib,
            [RuntimeTypeNames.ILedgerClient] = LedgerNamespaces.Abstractions,
            ["IReadOnlyList"] = "System.Collections.Generic",
            ["IReadOnlyDictionary"] = "System.Collections.Generic",
            ["HashSet"] = "System.Collections.Generic",
        };

    /// <summary>Every generated namespace plus all its ancestor prefixes, used for shadowing checks.</summary>
    public IReadOnlySet<string> AllNamespaces { get; }

    /// <summary>Creates a qualifier scoped to the given generated namespaces (ancestors are derived automatically).</summary>
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
