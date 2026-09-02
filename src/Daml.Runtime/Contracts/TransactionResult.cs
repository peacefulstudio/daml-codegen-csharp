// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using Daml.Runtime.Data;

namespace Daml.Runtime.Contracts;

/// <summary>
/// Outcome of a fire-and-wait submission: the effective <see cref="CommandId"/> the
/// participant recorded for the submission, together with the resulting transaction's
/// <see cref="UpdateId"/> and <see cref="CompletionOffset"/>. Returned by
/// <c>ILedgerClient.SubmitAndWaitAsync</c> so callers can correlate the completion with
/// the command id used for deduplication — even when the id was assigned by the client
/// rather than supplied by the caller.
/// </summary>
/// <param name="CommandId">The effective command id the participant recorded for the
/// submission, used for deduplication.</param>
/// <param name="UpdateId">Ledger-assigned update identifier of the resulting transaction.</param>
/// <param name="CompletionOffset">Offset at which the transaction was committed.</param>
public sealed record SubmitAndWaitResult(
    CommandId CommandId,
    string UpdateId,
    LedgerOffset CompletionOffset);

/// <summary>
/// Result of a submitted transaction.
/// </summary>
/// <param name="UpdateId">Ledger-assigned update identifier.</param>
/// <param name="CompletionOffset">Offset at which the transaction was committed.</param>
/// <param name="CreatedContracts">Contracts created by the transaction. Project to
/// typed <see cref="ContractId{T}"/> values via <see cref="TransactionResultExtensions"/>.</param>
/// <param name="ArchivedContractIds">Raw contract IDs archived by the transaction.</param>
/// <param name="CommandId">The effective command id the participant recorded for the
/// submission that produced this transaction, used for deduplication; <c>null</c> when
/// the participant reported none. The Ledger API omits the command id on transactions
/// this participant did not submit, so callers must treat it as optional rather than
/// assume every transaction carries one.</param>
public sealed record TransactionResult(
    string UpdateId,
    LedgerOffset CompletionOffset,
    IReadOnlyList<CreatedContract> CreatedContracts,
    IReadOnlyList<string> ArchivedContractIds,
    CommandId? CommandId)
{
    private readonly IReadOnlyList<CreatedContract> _createdContracts =
        EventCollections.Copy(CreatedContracts, nameof(CreatedContracts));

    private readonly IReadOnlyList<string> _archivedContractIds =
        EventCollections.Copy(ArchivedContractIds, nameof(ArchivedContractIds));

    private readonly IReadOnlyList<ExercisedEvent> _exercisedEvents = Array.Empty<ExercisedEvent>();

    /// <summary>
    /// Contracts created by the transaction. Copied at construction and on <c>init</c>, so a
    /// producer that retains the list it supplied cannot change this value's equality or
    /// hash code afterwards.
    /// </summary>
    public IReadOnlyList<CreatedContract> CreatedContracts
    {
        get => _createdContracts;
        init => _createdContracts = EventCollections.Copy(value, nameof(CreatedContracts));
    }

    /// <summary>
    /// Raw contract IDs archived by the transaction. Copied on the same terms as
    /// <see cref="CreatedContracts"/>.
    /// </summary>
    public IReadOnlyList<string> ArchivedContractIds
    {
        get => _archivedContractIds;
        init => _archivedContractIds = EventCollections.Copy(value, nameof(ArchivedContractIds));
    }

    /// <summary>
    /// Choice-exercise events observed in the transaction, in transaction order.
    /// Defaults to an empty list — populated by ledger-client transport
    /// implementations when the transaction was requested with
    /// ledger-effects shape. Codegen-emitted choice wrappers deserialize each
    /// <see cref="ExercisedEvent.ExerciseResult"/> through the appropriate typed
    /// projector to surface a typed <c>ExerciseOutcome&lt;TResult&gt;</c> for choices
    /// whose return type is not a contract id (e.g. <c>choice C : Decimal</c>).
    /// Copied on the same terms as <see cref="CreatedContracts"/>.
    /// </summary>
    public IReadOnlyList<ExercisedEvent> ExercisedEvents
    {
        get => _exercisedEvents;
        init => _exercisedEvents = EventCollections.Copy(value, nameof(ExercisedEvents));
    }

    /// <summary>
    /// Compares two transaction results field-by-field, comparing
    /// <see cref="CreatedContracts"/>, <see cref="ArchivedContractIds"/> and
    /// <see cref="ExercisedEvents"/> element by element rather than by list identity —
    /// each element then using its own equality. The record-synthesized equality compares
    /// the backing <see cref="IReadOnlyList{T}"/> by reference — a footgun for a
    /// value type — so we override it with structural element comparison, as
    /// <see cref="CreatedContract"/> and <see cref="DamlRecord"/> already do.
    /// </summary>
    /// <remarks>
    /// The comparison is structural all the way down: <see cref="CreatedContract"/>,
    /// <see cref="ExercisedEvent"/> and <see cref="string"/> each compare by content
    /// themselves, so two results projected from two separately-decoded trees of the same
    /// transaction compare equal.
    /// </remarks>
    public bool Equals(TransactionResult? other) =>
        other is not null
        && UpdateId == other.UpdateId
        && CompletionOffset == other.CompletionOffset
        && CommandId == other.CommandId
        && CreatedContracts.SequenceEqual(other.CreatedContracts)
        && ArchivedContractIds.SequenceEqual(other.ArchivedContractIds)
        && ExercisedEvents.SequenceEqual(other.ExercisedEvents);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(UpdateId);
        hash.Add(CompletionOffset);
        hash.Add(CommandId);
        hash.Add(CreatedContracts.Count);
        foreach (var created in CreatedContracts)
        {
            hash.Add(created);
        }
        hash.Add(ArchivedContractIds.Count);
        foreach (var archivedContractId in ArchivedContractIds)
        {
            hash.Add(archivedContractId);
        }
        hash.Add(ExercisedEvents.Count);
        foreach (var exercised in ExercisedEvents)
        {
            hash.Add(exercised);
        }
        return hash.ToHashCode();
    }
}

/// <summary>
/// Information about a contract created by a transaction. A field-for-field mirror of
/// <see cref="TreeEvent.Created"/>, so
/// <see cref="TransactionTreeExtensions.ToTransactionResult"/> flattens a create node
/// losslessly.
/// </summary>
/// <param name="EventId">The ledger-assigned event identifier.</param>
/// <param name="ContractId">The on-ledger contract ID.</param>
/// <param name="TemplateId">The template identifier (package + module + entity).</param>
/// <param name="Payload">The create-arguments record — <see cref="TreeEvent.Created.CreateArguments"/>
/// under the name the snapshot-side shapes use for the same slot (compare
/// <c>AcsSnapshotEntry.Created.Payload</c>); the mirror is field-for-field, not name-for-name
/// here. Non-nullable, so a transport
/// projecting a created event the participant sent without create arguments — the
/// interface-only case, where the contract is known only as an interface — passes an
/// empty record, which a consumer cannot distinguish from a template whose payload
/// genuinely has no fields.</param>
/// <param name="WitnessParties">Parties notified of this event. Required rather than
/// defaulted: an empty list must mean the event named no witnesses, not that a producer
/// never populated the slot. That is what separates these three from the two defaulted
/// list members — <see cref="InterfaceIds"/> and
/// <see cref="TransactionResult.ExercisedEvents"/>. Witnesses, signatories and observers
/// are always on the wire, so a silently-unpopulated one is a producer bug rather than a fact
/// about the contract — an empty list is itself meaningful, and a template that names no
/// observer clause reports empty observers on every create; interface views and exercise
/// events are populated only when the read requested that shape, so empty is both common and
/// correct for them.</param>
/// <param name="Signatories">Parties that authorized the contract's creation. Required
/// for the same reason as <paramref name="WitnessParties"/>.</param>
/// <param name="Observers">Parties the template names as observers. Required for the
/// same reason as <paramref name="WitnessParties"/>.</param>
/// <param name="ContractKey">The contract's key, when its template declares one;
/// <c>null</c> otherwise. Trailing and defaulted to mirror
/// <see cref="TreeEvent.Created.ContractKey"/> exactly. Contrast
/// <see cref="CreatedEvent.ContractKey"/>, which is mandatory: that shape is the single
/// entry point feeding the downstream key slots, whereas this one is built beside it and
/// keeps its mirror's shape.</param>
/// <param name="CreatedAt">Ledger-effective time at which the contract was created;
/// <c>null</c> when the transport does not supply it. Mirrors
/// <see cref="TreeEvent.Created.CreatedAt"/>.</param>
public sealed record CreatedContract(
    string EventId,
    string ContractId,
    Identifier TemplateId,
    DamlRecord Payload,
    IReadOnlyList<Party> WitnessParties,
    IReadOnlyList<Party> Signatories,
    IReadOnlyList<Party> Observers,
    ContractKey? ContractKey = null,
    DateTimeOffset? CreatedAt = null)
{
    private readonly IReadOnlyList<Party> _witnessParties =
        EventCollections.Copy(WitnessParties, nameof(WitnessParties));

    private readonly IReadOnlyList<Party> _signatories =
        EventCollections.Copy(Signatories, nameof(Signatories));

    private readonly IReadOnlyList<Party> _observers =
        EventCollections.Copy(Observers, nameof(Observers));

    private readonly IReadOnlyList<Identifier> _interfaceIds = Array.Empty<Identifier>();

    /// <summary>
    /// Parties notified of this event. Copied at construction and on <c>init</c>, so a
    /// producer that retains the list it supplied cannot change this value's equality or
    /// hash code afterwards.
    /// </summary>
    public IReadOnlyList<Party> WitnessParties
    {
        get => _witnessParties;
        init => _witnessParties = EventCollections.Copy(value, nameof(WitnessParties));
    }

    /// <summary>
    /// Parties that authorized the contract's creation. Copied on the same terms as
    /// <see cref="WitnessParties"/>.
    /// </summary>
    public IReadOnlyList<Party> Signatories
    {
        get => _signatories;
        init => _signatories = EventCollections.Copy(value, nameof(Signatories));
    }

    /// <summary>
    /// Parties the template names as observers. Copied on the same terms as
    /// <see cref="WitnessParties"/>.
    /// </summary>
    public IReadOnlyList<Party> Observers
    {
        get => _observers;
        init => _observers = EventCollections.Copy(value, nameof(Observers));
    }

    /// <summary>
    /// Interface ids the participant computed for this created event
    /// (Canton gRPC <c>CreatedEvent.interface_views[].interface_id</c>).
    /// Defaults to an empty list — populated by ledger-client transport
    /// implementations for interface-only consumption, where a contract is
    /// known only as an interface and must be dispatched at runtime.
    /// Copied on the same terms as <see cref="WitnessParties"/>.
    /// </summary>
    public IReadOnlyList<Identifier> InterfaceIds
    {
        get => _interfaceIds;
        init => _interfaceIds = EventCollections.Copy(value, nameof(InterfaceIds));
    }

    /// <summary>
    /// Compares two created contracts field-by-field, including element-wise
    /// <see cref="WitnessParties"/>, <see cref="Signatories"/>, <see cref="Observers"/>
    /// and <see cref="InterfaceIds"/> content. The record-synthesized equality compares
    /// the backing <see cref="IReadOnlyList{T}"/> by reference — a footgun for a
    /// value type — so we override it with structural element comparison.
    /// </summary>
    /// <remarks>
    /// One corner is not literally field-by-field: <see cref="ContractKey"/> compares by the
    /// key it names and deliberately ignores <see cref="Contracts.ContractKey.KeyHash"/>, so
    /// a create read off the wire equals the same create rebuilt by a caller. That is the
    /// intended propagation of that type's own rule, not an oversight here.
    /// </remarks>
    public bool Equals(CreatedContract? other) =>
        other is not null
        && EventId == other.EventId
        && ContractId == other.ContractId
        && TemplateId == other.TemplateId
        && Equals(Payload, other.Payload)
        && ContractKey == other.ContractKey
        && CreatedAt == other.CreatedAt
        && WitnessParties.SequenceEqual(other.WitnessParties)
        && Signatories.SequenceEqual(other.Signatories)
        && Observers.SequenceEqual(other.Observers)
        && InterfaceIds.SequenceEqual(other.InterfaceIds);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(EventId);
        hash.Add(ContractId);
        hash.Add(TemplateId);
        hash.Add(Payload);
        hash.Add(ContractKey);
        hash.Add(CreatedAt);
        hash.Add(WitnessParties.Count);
        foreach (var witness in WitnessParties)
        {
            hash.Add(witness);
        }
        hash.Add(Signatories.Count);
        foreach (var signatory in Signatories)
        {
            hash.Add(signatory);
        }
        hash.Add(Observers.Count);
        foreach (var observer in Observers)
        {
            hash.Add(observer);
        }
        hash.Add(InterfaceIds.Count);
        foreach (var interfaceId in InterfaceIds)
        {
            hash.Add(interfaceId);
        }
        return hash.ToHashCode();
    }
}
