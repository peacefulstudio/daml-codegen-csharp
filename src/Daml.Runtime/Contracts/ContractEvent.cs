// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;

namespace Daml.Runtime.Contracts;

/// <summary>
/// Represents a contract creation event from the ledger.
/// </summary>
/// <param name="EventId">The event's identifier within its transaction.</param>
/// <param name="ContractId">The on-ledger contract ID of the created contract.</param>
/// <param name="TemplateId">The template the contract instantiates.</param>
/// <param name="CreateArguments">The create-arguments record.</param>
/// <param name="WitnessParties">Parties notified of this event.</param>
/// <param name="Signatories">Parties that authorized the create.</param>
/// <param name="Observers">Parties the template names as observers.</param>
/// <param name="ContractKey">The contract key the event carried, or <c>null</c> when the
/// template declares none. Mandatory rather than defaulted: this is the single entry point
/// feeding every downstream key slot, so a transport must state the absence rather than
/// inherit it and populate every consumer's key with <c>null</c> while compiling clean.</param>
/// <param name="CreatedAt">The ledger-effective time of the create, when the event
/// reported one.</param>
public sealed record CreatedEvent(
    string EventId,
    string ContractId,
    Identifier TemplateId,
    DamlRecord CreateArguments,
    IReadOnlyList<Party> WitnessParties,
    IReadOnlyList<Party> Signatories,
    IReadOnlyList<Party> Observers,
    ContractKey? ContractKey,
    DateTimeOffset? CreatedAt = null)
{
    private readonly IReadOnlyList<Party> _witnessParties =
        EventCollections.Borrow(WitnessParties, nameof(WitnessParties));

    private readonly IReadOnlyList<Party> _signatories =
        EventCollections.Borrow(Signatories, nameof(Signatories));

    private readonly IReadOnlyList<Party> _observers =
        EventCollections.Borrow(Observers, nameof(Observers));

    /// <summary>
    /// Parties notified of this event. Held as the producer supplied it, not copied — an
    /// <see cref="IReadOnlyList{T}"/> is a read-only view, so a caller that retains its
    /// backing list must not mutate it after construction. Rejected at construction and on
    /// <c>init</c> when <c>null</c>.
    /// </summary>
    public IReadOnlyList<Party> WitnessParties
    {
        get => _witnessParties;
        init => _witnessParties = EventCollections.Borrow(value, nameof(WitnessParties));
    }

    /// <summary>
    /// Parties that authorized the create. Held on the same terms as
    /// <see cref="WitnessParties"/>.
    /// </summary>
    public IReadOnlyList<Party> Signatories
    {
        get => _signatories;
        init => _signatories = EventCollections.Borrow(value, nameof(Signatories));
    }

    /// <summary>
    /// Parties the template names as observers. Held on the same terms as
    /// <see cref="WitnessParties"/>.
    /// </summary>
    public IReadOnlyList<Party> Observers
    {
        get => _observers;
        init => _observers = EventCollections.Borrow(value, nameof(Observers));
    }
}

/// <summary>
/// Represents a contract key.
/// </summary>
/// <param name="Value">The key value, wire-level. Decode it with the same hop generated
/// code performs — <c>TKey.FromRecord(Value.As&lt;DamlRecord&gt;())</c> — or read it
/// directly when the template's key is a bare scalar.</param>
/// <param name="TemplateId">The template that declares the key, when the event named one;
/// <c>null</c> otherwise.</param>
public sealed record ContractKey(DamlValue Value, Identifier? TemplateId = null)
{
    /// <summary>
    /// The ledger's hash of this key — the value Canton indexes keyed contracts by. It
    /// travels beside the key on both wire formats (<c>contractKeyHash</c> on the JSON
    /// encoding, <c>contract_key_hash</c> on gRPC) and is held here as the base64 text the
    /// JSON encoding uses. A <c>null</c> there is a stated absence: the created event
    /// carried no hash, or the key was constructed by a caller rather than read off the
    /// wire. It is never computed here — the hash is the ledger's, and a transport that
    /// reads one off a created event populates it.
    /// </summary>
    /// <remarks>
    /// Deliberately excluded from <see cref="Equals(ContractKey)"/> and
    /// <see cref="GetHashCode"/>: two keys are the same key when they name the same value
    /// of the same template, and a key read off the wire must therefore equal the same key
    /// a caller constructed to exercise by it — which carries no hash. Including it would
    /// make by-key matching fail quietly depending on where the key came from.
    /// </remarks>
    public string? KeyHash { get; init; }

    /// <summary>
    /// Compares two keys by the key they name — <see cref="Value"/> and
    /// <see cref="TemplateId"/> — ignoring <see cref="KeyHash"/>.
    /// </summary>
    /// <param name="other">The key to compare against.</param>
    /// <returns><c>true</c> when both name the same key.</returns>
    public bool Equals(ContractKey? other) =>
        other is not null
        && EqualityComparer<DamlValue>.Default.Equals(Value, other.Value)
        && EqualityComparer<Identifier?>.Default.Equals(TemplateId, other.TemplateId);

    /// <summary>
    /// Hashes the key by the key it names, consistently with
    /// <see cref="Equals(ContractKey)"/>.
    /// </summary>
    /// <returns>A hash code over <see cref="Value"/> and <see cref="TemplateId"/>.</returns>
    public override int GetHashCode() => HashCode.Combine(Value, TemplateId);
}

/// <summary>
/// Represents an archived (consumed) contract event.
/// </summary>
public sealed record ArchivedEvent(
    string EventId,
    string ContractId,
    Identifier TemplateId,
    IReadOnlyList<Party> WitnessParties)
{
    private readonly IReadOnlyList<Party> _witnessParties =
        EventCollections.Borrow(WitnessParties, nameof(WitnessParties));

    /// <summary>
    /// Parties notified of this event. Held as the producer supplied it, not copied — an
    /// <see cref="IReadOnlyList{T}"/> is a read-only view, so a caller that retains its
    /// backing list must not mutate it after construction. Rejected at construction and on
    /// <c>init</c> when <c>null</c>.
    /// </summary>
    public IReadOnlyList<Party> WitnessParties
    {
        get => _witnessParties;
        init => _witnessParties = EventCollections.Borrow(value, nameof(WitnessParties));
    }
}

/// <summary>
/// Represents a Daml exception caught by a <c>try</c>/<c>catch</c> block during
/// choice interpretation. Transport-neutral and wire-format-agnostic — the
/// Canton ledger client owns translating the gRPC exception representation
/// into this shape.
/// </summary>
/// <param name="ErrorId">The identifier of the caught exception (e.g. its
/// qualified Daml type name or the ledger's error code).</param>
/// <param name="Message">The human-readable message carried by the exception.</param>
/// <param name="Metadata">Additional key-value context associated with the
/// exception, as provided by the ledger.</param>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "This is the projected shape of a Daml exception caught by the ledger, not a throwable; the name matches the Daml vocabulary consumers already read in transaction trees.")]
public sealed record CaughtException(
    string ErrorId,
    string Message,
    IReadOnlyDictionary<string, string> Metadata)
{
    private readonly IReadOnlyDictionary<string, string> _metadata =
        EventCollections.Copy(Metadata, nameof(Metadata));

    /// <summary>
    /// Additional key-value context associated with the exception, as provided by the
    /// ledger. Copied at construction and on <c>init</c>, so a producer that retains the
    /// dictionary it supplied cannot change this value's equality or hash code afterwards.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata
    {
        get => _metadata;
        init => _metadata = EventCollections.Copy(value, nameof(Metadata));
    }

    /// <summary>
    /// Compares two caught exceptions by content, comparing <see cref="Metadata"/> key by
    /// key and independently of insertion order. The record-synthesized equality compares
    /// the backing <see cref="IReadOnlyDictionary{TKey,TValue}"/> by reference — a footgun
    /// for a value type — so we override it, as <see cref="Data.DamlTextMap"/> already does
    /// for the same shape.
    /// </summary>
    /// <param name="other">The caught exception to compare against.</param>
    /// <returns><c>true</c> when both describe the same caught exception.</returns>
    public bool Equals(CaughtException? other)
    {
        if (other is null
            || ErrorId != other.ErrorId
            || Message != other.Message
            || Metadata.Count != other.Metadata.Count)
        {
            return false;
        }
        foreach (var (key, value) in Metadata)
        {
            if (!other.Metadata.TryGetValue(key, out var otherValue) || value != otherValue)
            {
                return false;
            }
        }
        return true;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = HashCode.Combine(ErrorId, Message, Metadata.Count);
        foreach (var (key, value) in Metadata)
        {
            hash ^= HashCode.Combine(key, value);
        }
        return hash;
    }
}

/// <summary>
/// Represents a choice-exercise event observed in a transaction. Carries the
/// wire-level <see cref="ExerciseResult"/> so codegen-emitted choice wrappers
/// can deserialize the choice's typed return value (e.g. project a
/// <c>choice GetTrailingTwap : Decimal</c> result to <c>ExerciseOutcome&lt;decimal&gt;</c>).
/// </summary>
/// <param name="ContractId">The on-ledger contract ID the choice was exercised on.</param>
/// <param name="TemplateId">The template that defines the executed choice. The package
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
public sealed record ExercisedEvent(
    string ContractId,
    Identifier TemplateId,
    Identifier? InterfaceId,
    string ChoiceName,
    DamlValue ChoiceArgument,
    DamlValue ExerciseResult,
    bool Consuming,
    IReadOnlyList<Party> ActingParties,
    IReadOnlyList<Party> WitnessParties)
{
    private readonly IReadOnlyList<Party> _actingParties =
        EventCollections.Copy(ActingParties, nameof(ActingParties));

    private readonly IReadOnlyList<Party> _witnessParties =
        EventCollections.Copy(WitnessParties, nameof(WitnessParties));

    private readonly IReadOnlyList<CaughtException> _caughtExceptions = Array.Empty<CaughtException>();

    /// <summary>
    /// Parties that exercised the choice. Copied at construction and on <c>init</c>, so a
    /// producer that retains the list it supplied cannot change this value's equality or
    /// hash code afterwards.
    /// </summary>
    public IReadOnlyList<Party> ActingParties
    {
        get => _actingParties;
        init => _actingParties = EventCollections.Copy(value, nameof(ActingParties));
    }

    /// <summary>
    /// Parties notified of this event. Copied on the same terms as
    /// <see cref="ActingParties"/>.
    /// </summary>
    public IReadOnlyList<Party> WitnessParties
    {
        get => _witnessParties;
        init => _witnessParties = EventCollections.Copy(value, nameof(WitnessParties));
    }

    /// <summary>
    /// Daml exceptions caught by a <c>try</c>/<c>catch</c> block during this
    /// choice's interpretation. Defaults to an empty list — populated by
    /// ledger-client transport implementations from the gRPC exception
    /// status on the exercise node. Copied on the same terms as
    /// <see cref="ActingParties"/>.
    /// </summary>
    public IReadOnlyList<CaughtException> CaughtExceptions
    {
        get => _caughtExceptions;
        init => _caughtExceptions = EventCollections.Copy(value, nameof(CaughtExceptions));
    }

    /// <summary>
    /// Compares two exercise events field-by-field, comparing <see cref="ActingParties"/>,
    /// <see cref="WitnessParties"/> and <see cref="CaughtExceptions"/> element by element
    /// rather than by list identity. The record-synthesized equality compares the backing
    /// <see cref="IReadOnlyList{T}"/> by reference — a footgun for a value type — so we
    /// override it with structural element comparison, as <see cref="CreatedContract"/> and
    /// <see cref="TransactionResult"/> already do.
    /// </summary>
    /// <param name="other">The exercise event to compare against.</param>
    /// <returns><c>true</c> when both describe the same exercise.</returns>
    public bool Equals(ExercisedEvent? other) =>
        other is not null
        && ContractId == other.ContractId
        && TemplateId == other.TemplateId
        && InterfaceId == other.InterfaceId
        && ChoiceName == other.ChoiceName
        && Equals(ChoiceArgument, other.ChoiceArgument)
        && Equals(ExerciseResult, other.ExerciseResult)
        && Consuming == other.Consuming
        && ActingParties.SequenceEqual(other.ActingParties)
        && WitnessParties.SequenceEqual(other.WitnessParties)
        && CaughtExceptions.SequenceEqual(other.CaughtExceptions);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ContractId);
        hash.Add(TemplateId);
        hash.Add(InterfaceId);
        hash.Add(ChoiceName);
        hash.Add(ChoiceArgument);
        hash.Add(ExerciseResult);
        hash.Add(Consuming);
        hash.Add(ActingParties.Count);
        foreach (var actingParty in ActingParties)
        {
            hash.Add(actingParty);
        }
        hash.Add(WitnessParties.Count);
        foreach (var witness in WitnessParties)
        {
            hash.Add(witness);
        }
        hash.Add(CaughtExceptions.Count);
        foreach (var caught in CaughtExceptions)
        {
            hash.Add(caught);
        }
        return hash.ToHashCode();
    }
}
