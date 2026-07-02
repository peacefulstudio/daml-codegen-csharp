// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;

namespace Daml.Runtime.Contracts;

/// <summary>
/// Tree-walking and compatibility helpers for <see cref="TransactionTree"/>.
/// </summary>
public static class TransactionTreeExtensions
{
    /// <summary>
    /// Enumerates every event in the tree — <see cref="TransactionTree.RootEvents"/>
    /// followed by each root's <see cref="TreeEvent.DescendantEvents"/> — in
    /// depth-first pre-order.
    /// </summary>
    public static IEnumerable<TreeEvent> AllEvents(this TransactionTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        return AllEventsCore(tree);
    }

    private static IEnumerable<TreeEvent> AllEventsCore(TransactionTree tree)
    {
        foreach (var root in tree.RootEvents)
        {
            yield return root;
            foreach (var descendant in root.DescendantEvents())
            {
                yield return descendant;
            }
        }
    }

    /// <summary>
    /// Projects this <see cref="TransactionTree"/> to the flattened
    /// <see cref="TransactionResult"/> shape, for callers that don't need
    /// hierarchy. <see cref="TreeEvent.Created"/> nodes become
    /// <see cref="CreatedContract"/> entries (with a JSON-serialized payload);
    /// <see cref="TreeEvent.Exercised"/> nodes become
    /// <see cref="TransactionResult.ExercisedEvents"/> entries, and consuming
    /// exercises additionally contribute their target contract id to
    /// <see cref="TransactionResult.ArchivedContractIds"/>.
    /// </summary>
    /// <remarks>
    /// This projection is lossy: <see cref="CreatedContract"/> has no slot for
    /// <see cref="TreeEvent.Created.EventId"/>, <see cref="TreeEvent.Created.WitnessParties"/>,
    /// <see cref="TreeEvent.Created.Signatories"/>, <see cref="TreeEvent.Created.Observers"/>,
    /// <see cref="TreeEvent.Created.ContractKey"/>, or <see cref="TreeEvent.Created.CreatedAt"/>,
    /// and <see cref="ExercisedEvent"/> has no slot for <see cref="TreeEvent.Exercised.EventId"/>;
    /// its <see cref="ExercisedEvent.CaughtExceptions"/> is always empty here, since
    /// <see cref="TreeEvent.Exercised"/> doesn't carry that data. Callers that need those
    /// fields must walk <see cref="TransactionTree.RootEvents"/> directly instead.
    /// </remarks>
    public static TransactionResult ToTransactionResult(this TransactionTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);

        var createdContracts = new List<CreatedContract>();
        var archivedContractIds = new List<string>();
        var exercisedEvents = new List<ExercisedEvent>();

        foreach (var treeEvent in tree.AllEvents())
        {
            switch (treeEvent)
            {
                case TreeEvent.Created created:
                    createdContracts.Add(new CreatedContract(
                        created.ContractId,
                        created.TemplateId,
                        DamlJsonSerializer.Serialize(created.CreateArguments))
                    {
                        InterfaceIds = created.InterfaceIds,
                    });
                    break;
                case TreeEvent.Exercised exercised:
                    exercisedEvents.Add(new ExercisedEvent(
                        exercised.ContractId,
                        exercised.TemplateId,
                        exercised.InterfaceId,
                        exercised.ChoiceName,
                        exercised.ChoiceArgument,
                        exercised.ExerciseResult,
                        exercised.Consuming,
                        exercised.ActingParties,
                        exercised.WitnessParties));
                    if (exercised.Consuming)
                    {
                        archivedContractIds.Add(exercised.ContractId);
                    }
                    break;
                default:
                    throw new UnreachableException(
                        $"Unhandled {nameof(TreeEvent)} case: {treeEvent.GetType().Name}");
            }
        }

        return new TransactionResult(
            tree.UpdateId,
            tree.CompletionOffset,
            createdContracts,
            archivedContractIds)
        {
            ExercisedEvents = exercisedEvents,
        };
    }
}
