// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;

namespace Daml.Runtime.Contracts;

/// <summary>
/// Result of a submitted transaction, preserving the parent/child hierarchy
/// of its events. A tree-aware sibling of <see cref="TransactionResult"/>,
/// which flattens the same information into separate created/archived/exercised
/// lists. Use <see cref="TransactionTreeExtensions.ToTransactionResult"/> to
/// project a <see cref="TransactionTree"/> to that flattened shape.
/// </summary>
/// <param name="UpdateId">Ledger-assigned update identifier.</param>
/// <param name="CompletionOffset">Offset at which the transaction was committed.</param>
/// <param name="RootEvents">The transaction's top-level events, in transaction
/// order. Events caused by an exercise (its sub-creates and sub-exercises) are
/// not repeated here — they nest under that exercise's
/// <see cref="TreeEvent.Exercised.ChildEvents"/>.</param>
public sealed record TransactionTree(
    string UpdateId,
    LedgerOffset CompletionOffset,
    IReadOnlyList<TreeEvent> RootEvents)
{
    private readonly IReadOnlyList<TreeEvent> _rootEvents =
        EventCollections.Borrow(RootEvents, nameof(RootEvents));

    /// <summary>
    /// The transaction's top-level events, in transaction order. Events caused by an
    /// exercise (its sub-creates and sub-exercises) are not repeated here — they nest under
    /// that exercise's <see cref="TreeEvent.Exercised.ChildEvents"/>. Held as the producer
    /// supplied it, not copied — an <see cref="IReadOnlyList{T}"/> is a read-only view, so a
    /// caller that retains its backing list must not mutate it after construction. Rejected
    /// at construction and on <c>init</c> when <c>null</c>.
    /// </summary>
    public IReadOnlyList<TreeEvent> RootEvents
    {
        get => _rootEvents;
        init => _rootEvents = EventCollections.Borrow(value, nameof(RootEvents));
    }
}

/// <summary>
/// A single node in a <see cref="TransactionTree"/>: either a contract
/// creation (<see cref="Created"/>) or a choice exercise (<see cref="Exercised"/>).
/// Exercise nodes carry the events they directly caused, preserving the
/// ledger's causal hierarchy.
/// </summary>
public abstract record TreeEvent
{
    private TreeEvent()
    {
    }

    /// <summary>
    /// Enumerates every event nested under this one, in depth-first pre-order.
    /// Safe on arbitrarily deep trees — traversal does not use call-stack
    /// recursion, so it cannot overflow the stack. Empty for
    /// <see cref="Created"/> events and for <see cref="Exercised"/> events
    /// with no <see cref="Exercised.ChildEvents"/>.
    /// </summary>
    public IEnumerable<TreeEvent> DescendantEvents()
    {
        if (this is not Exercised exercised)
        {
            yield break;
        }

        var stack = new Stack<TreeEvent>();
        PushChildrenInPreOrder(stack, exercised.ChildEvents);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;

            if (current is Exercised currentExercised && currentExercised.ChildEvents.Count > 0)
            {
                PushChildrenInPreOrder(stack, currentExercised.ChildEvents);
            }
        }
    }

    private static void PushChildrenInPreOrder(Stack<TreeEvent> stack, IReadOnlyList<TreeEvent> children)
    {
        for (var i = children.Count - 1; i >= 0; i--)
        {
            stack.Push(children[i]);
        }
    }

    /// <summary>
    /// A contract creation node in a <see cref="TransactionTree"/>.
    /// </summary>
    /// <param name="EventId">The ledger-assigned event identifier.</param>
    /// <param name="ContractId">The on-ledger contract ID of the created contract.</param>
    /// <param name="TemplateId">The template identifier (package + module + entity).</param>
    /// <param name="CreateArguments">Wire-level create-argument payload.</param>
    /// <param name="WitnessParties">Parties notified of this event.</param>
    /// <param name="Signatories">Parties that authorized the contract's creation.</param>
    /// <param name="Observers">Parties with read access to the contract.</param>
    /// <param name="ContractKey">The contract's key, when its template declares one;
    /// <c>null</c> otherwise. Mirrors <see cref="CreatedEvent.ContractKey"/>.</param>
    /// <param name="CreatedAt">Ledger-effective time at which the contract was created;
    /// <c>null</c> when the transport does not supply it. Mirrors
    /// <see cref="CreatedEvent.CreatedAt"/>.</param>
    public sealed record Created(
        string EventId,
        string ContractId,
        Identifier TemplateId,
        DamlRecord CreateArguments,
        IReadOnlyList<Party> WitnessParties,
        IReadOnlyList<Party> Signatories,
        IReadOnlyList<Party> Observers,
        ContractKey? ContractKey = null,
        DateTimeOffset? CreatedAt = null) : TreeEvent
    {
        private readonly IReadOnlyList<Party> _witnessParties =
            EventCollections.Borrow(WitnessParties, nameof(WitnessParties));

        private readonly IReadOnlyList<Party> _signatories =
            EventCollections.Borrow(Signatories, nameof(Signatories));

        private readonly IReadOnlyList<Party> _observers =
            EventCollections.Borrow(Observers, nameof(Observers));

        private readonly IReadOnlyList<Identifier> _interfaceIds = Array.Empty<Identifier>();

        /// <summary>
        /// Parties notified of this event. Held as the producer supplied it, not copied — an
        /// <see cref="IReadOnlyList{T}"/> is a read-only view, so a caller that retains its
        /// backing list must not mutate it after construction. Rejected at construction and
        /// on <c>init</c> when <c>null</c>.
        /// </summary>
        public IReadOnlyList<Party> WitnessParties
        {
            get => _witnessParties;
            init => _witnessParties = EventCollections.Borrow(value, nameof(WitnessParties));
        }

        /// <summary>
        /// Parties that authorized the contract's creation. Held on the same terms as
        /// <see cref="WitnessParties"/>.
        /// </summary>
        public IReadOnlyList<Party> Signatories
        {
            get => _signatories;
            init => _signatories = EventCollections.Borrow(value, nameof(Signatories));
        }

        /// <summary>
        /// Parties with read access to the contract. Held on the same terms as
        /// <see cref="WitnessParties"/>.
        /// </summary>
        public IReadOnlyList<Party> Observers
        {
            get => _observers;
            init => _observers = EventCollections.Borrow(value, nameof(Observers));
        }

        /// <summary>
        /// Interface ids the participant computed for this created event
        /// (Canton gRPC <c>CreatedEvent.interface_views[].interface_id</c>).
        /// Defaults to an empty list — populated by ledger-client transport
        /// implementations for interface-only consumption, where a contract is
        /// known only as an interface and must be dispatched at runtime. Flattened
        /// through to <see cref="CreatedContract.InterfaceIds"/> by
        /// <see cref="TransactionTreeExtensions.ToTransactionResult"/>.
        /// Held on the same terms as <see cref="WitnessParties"/>.
        /// </summary>
        public IReadOnlyList<Identifier> InterfaceIds
        {
            get => _interfaceIds;
            init => _interfaceIds = EventCollections.Borrow(value, nameof(InterfaceIds));
        }
    }

    /// <summary>
    /// A choice-exercise node in a <see cref="TransactionTree"/>. Carries the
    /// wire-level <see cref="ChoiceArgument"/> and <see cref="ExerciseResult"/>,
    /// consistent with <see cref="ExercisedEvent"/>, plus the events this
    /// exercise directly caused as <see cref="ChildEvents"/>.
    /// </summary>
    /// <param name="EventId">The ledger-assigned event identifier.</param>
    /// <param name="ContractId">The on-ledger contract ID the choice was exercised on.</param>
    /// <param name="TemplateId">The template that defines the exercised choice. The package
    /// id may differ from the target contract's package id when the contract has been
    /// upgraded or downgraded.</param>
    /// <param name="InterfaceId">When the choice is inherited from an interface, the
    /// interface identifier; <c>null</c> for choices defined directly on the template.</param>
    /// <param name="ChoiceName">The choice that was exercised on the target contract.</param>
    /// <param name="ChoiceArgument">The argument value passed to the choice. Wire-level
    /// <see cref="DamlValue"/>; codegen-emitted wrappers deserialize to the typed argument.</param>
    /// <param name="ExerciseResult">The result returned by the choice. Wire-level
    /// <see cref="DamlValue"/>; codegen-emitted wrappers deserialize to the typed return.</param>
    /// <param name="Consuming">Whether the exercise consumed (archived) the target contract.</param>
    /// <param name="ActingParties">Parties that exercised the choice.</param>
    /// <param name="WitnessParties">Parties notified of this event.</param>
    /// <param name="ChildEvents">The events this exercise directly caused — its
    /// sub-creates and sub-exercises — in transaction order.</param>
    public sealed record Exercised(
        string EventId,
        string ContractId,
        Identifier TemplateId,
        Identifier? InterfaceId,
        string ChoiceName,
        DamlValue ChoiceArgument,
        DamlValue ExerciseResult,
        bool Consuming,
        IReadOnlyList<Party> ActingParties,
        IReadOnlyList<Party> WitnessParties,
        IReadOnlyList<TreeEvent> ChildEvents) : TreeEvent
    {
        private readonly IReadOnlyList<Party> _actingParties =
            EventCollections.Borrow(ActingParties, nameof(ActingParties));

        private readonly IReadOnlyList<Party> _witnessParties =
            EventCollections.Borrow(WitnessParties, nameof(WitnessParties));

        private readonly IReadOnlyList<TreeEvent> _childEvents =
            EventCollections.Borrow(ChildEvents, nameof(ChildEvents));

        /// <summary>
        /// Parties that exercised the choice. Held as the producer supplied it, not copied —
        /// an <see cref="IReadOnlyList{T}"/> is a read-only view, so a caller that retains
        /// its backing list must not mutate it after construction. Rejected at construction
        /// and on <c>init</c> when <c>null</c>.
        /// </summary>
        public IReadOnlyList<Party> ActingParties
        {
            get => _actingParties;
            init => _actingParties = EventCollections.Borrow(value, nameof(ActingParties));
        }

        /// <summary>
        /// Parties notified of this event. Held on the same terms as
        /// <see cref="ActingParties"/>.
        /// </summary>
        public IReadOnlyList<Party> WitnessParties
        {
            get => _witnessParties;
            init => _witnessParties = EventCollections.Borrow(value, nameof(WitnessParties));
        }

        /// <summary>
        /// The events this exercise directly caused — its sub-creates and sub-exercises — in
        /// transaction order. Held on the same terms as <see cref="ActingParties"/>.
        /// </summary>
        public IReadOnlyList<TreeEvent> ChildEvents
        {
            get => _childEvents;
            init => _childEvents = EventCollections.Borrow(value, nameof(ChildEvents));
        }
    }
}
