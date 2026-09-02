// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;

namespace Daml.Runtime.Streams;

/// <summary>
/// A typed event observed on a subscription stream over the Daml interface
/// <typeparamref name="TInterface"/>. The interface-family counterpart of
/// <see cref="ContractStreamEvent{T}"/>: identical variants and fields, but the payload
/// is the interface's server-computed view record <typeparamref name="TView"/> rather
/// than the implementing template's own record, which an interface subscription never
/// sees.
/// </summary>
/// <typeparam name="TInterface">The Daml interface marker the stream is filtered to,
/// matched by interface id.</typeparam>
/// <typeparam name="TView">The interface's view record type, carried as the payload.</typeparam>
/// <remarks>
/// <para>
/// Call sites do not name the pair: they pass the marker's generated
/// <see cref="ViewDescriptor{TInterface, TView}"/> witness to
/// <c>ILedgerStreamer.SubscribeAsync</c> and both type parameters are inferred from it.
/// </para>
/// <para>
/// A matching event that arrives without an interface view — the view is computed by the
/// participant, so it can be absent — is surfaced as
/// <see cref="Unclassified"/> with
/// <see cref="UnclassifiedKind.InterfaceViewUnavailable"/> rather than dropped, since
/// there is no view record to decode into <typeparamref name="TView"/>.
/// </para>
/// <para>
/// Which variants a stream yields is a property of its transaction shape, exactly as for
/// <see cref="ContractStreamEvent{T}"/>: the ACS-delta shape emits
/// <see cref="Archived"/> and never <see cref="Exercised"/>, the ledger-effects shape
/// emits <see cref="Exercised"/> (a consuming exercise being the archival signal) and
/// never <see cref="Archived"/>, and an active-contract-set snapshot streams
/// <see cref="InterfaceAcsSnapshotEntry{TInterface, TView}"/> instead of this type.
/// </para>
/// </remarks>
public abstract record InterfaceStreamEvent<TInterface, TView>
    where TInterface : IDamlInterface, IHasView<TView>
    where TView : IDamlRecord<TView>
{
    /// <summary>Sealed; new variants live alongside the existing ones.</summary>
    private protected InterfaceStreamEvent() { }

    /// <summary>
    /// A contract implementing <typeparamref name="TInterface"/> was created.
    /// </summary>
    /// <param name="ContractId">The on-ledger contract ID, interface-typed.</param>
    /// <param name="Payload">The interface view, decoded into <typeparamref name="TView"/>.</param>
    /// <param name="Key">The contract key read off the created event, or <c>null</c> when the
    /// event carried none. Separate from <paramref name="Payload"/> because the key is its own
    /// wire field, not a projection of the view, and its type is the implementing template's
    /// key type rather than <typeparamref name="TView"/>.</param>
    /// <param name="Offset">The ledger offset at which the contract was created. Strictly
    /// increasing per synchronizer; suitable for use as the resume offset on a subsequent
    /// subscription (exclusive).</param>
    /// <param name="SynchronizerId">The synchronizer the contract was created on.</param>
    /// <param name="WitnessParties">Parties that witnessed the create event.</param>
    public sealed record Created(
        ContractId<TInterface> ContractId,
        TView Payload,
        ContractKey? Key,
        LedgerOffset Offset,
        SynchronizerId SynchronizerId,
        IReadOnlyList<Party> WitnessParties) : InterfaceStreamEvent<TInterface, TView>
    {
        private readonly IReadOnlyList<Party> _witnessParties =
            EventCollections.Borrow(WitnessParties, nameof(WitnessParties));

        /// <summary>
        /// Parties that witnessed the create event. Held as the producer supplied it, not
        /// copied — an <see cref="IReadOnlyList{T}"/> is a read-only view, so a caller that
        /// retains its backing list must not mutate it after construction. Rejected at
        /// construction and on <c>init</c> when <c>null</c>.
        /// </summary>
        public IReadOnlyList<Party> WitnessParties
        {
            get => _witnessParties;
            init => _witnessParties = EventCollections.Borrow(value, nameof(WitnessParties));
        }
    }

    /// <summary>
    /// A contract implementing <typeparamref name="TInterface"/> was archived. Emitted only
    /// on ACS-delta-shaped streams; the ledger-effects subscription yields a consuming
    /// <see cref="Exercised"/> instead.
    /// </summary>
    /// <param name="ContractId">The on-ledger contract ID, interface-typed.</param>
    /// <param name="Offset">The ledger offset at which the contract was archived.</param>
    /// <param name="SynchronizerId">The synchronizer the contract was archived on.</param>
    /// <param name="WitnessParties">Parties that witnessed the archive event.</param>
    public sealed record Archived(
        ContractId<TInterface> ContractId,
        LedgerOffset Offset,
        SynchronizerId SynchronizerId,
        IReadOnlyList<Party> WitnessParties) : InterfaceStreamEvent<TInterface, TView>
    {
        private readonly IReadOnlyList<Party> _witnessParties =
            EventCollections.Borrow(WitnessParties, nameof(WitnessParties));

        /// <summary>
        /// Parties that witnessed the archive event. Held on the same terms as
        /// <see cref="Created.WitnessParties"/>.
        /// </summary>
        public IReadOnlyList<Party> WitnessParties
        {
            get => _witnessParties;
            init => _witnessParties = EventCollections.Borrow(value, nameof(WitnessParties));
        }
    }

    /// <summary>
    /// A choice was exercised on a contract implementing <typeparamref name="TInterface"/>.
    /// Emitted only on the ledger-effects shape, where a consuming exercise
    /// (<see cref="Consuming"/> is <c>true</c>) is the contract's archival signal.
    /// </summary>
    /// <param name="ContractId">The on-ledger contract ID the choice was exercised on.</param>
    /// <param name="ChoiceName">The choice name.</param>
    /// <param name="ChoiceArgument">The argument value passed to the choice.</param>
    /// <param name="ExerciseResult">The result returned by the choice.</param>
    /// <param name="Consuming">Whether the exercise consumed (archived) the contract.</param>
    /// <param name="Offset">The ledger offset of the exercise.</param>
    /// <param name="SynchronizerId">The synchronizer the exercise occurred on.</param>
    /// <param name="WitnessParties">Parties that witnessed the exercise event.</param>
    public sealed record Exercised(
        ContractId<TInterface> ContractId,
        string ChoiceName,
        DamlValue ChoiceArgument,
        DamlValue ExerciseResult,
        bool Consuming,
        LedgerOffset Offset,
        SynchronizerId SynchronizerId,
        IReadOnlyList<Party> WitnessParties) : InterfaceStreamEvent<TInterface, TView>
    {
        private readonly IReadOnlyList<Party> _witnessParties =
            EventCollections.Borrow(WitnessParties, nameof(WitnessParties));

        /// <summary>
        /// Parties that witnessed the exercise event. Held on the same terms as
        /// <see cref="Created.WitnessParties"/>.
        /// </summary>
        public IReadOnlyList<Party> WitnessParties
        {
            get => _witnessParties;
            init => _witnessParties = EventCollections.Borrow(value, nameof(WitnessParties));
        }
    }

    /// <summary>
    /// A contract implementing <typeparamref name="TInterface"/> was assigned to a
    /// synchronizer. The interface view is re-emitted so consumers rebuilding state from a
    /// single stream stay correct.
    /// </summary>
    /// <param name="ContractId">The on-ledger contract ID, interface-typed.</param>
    /// <param name="Payload">The interface view, re-emitted on assignment and decoded into
    /// <typeparamref name="TView"/>.</param>
    /// <param name="Key">The contract key read off the assigned event's created contract, or
    /// <c>null</c> when it carried none.</param>
    /// <param name="Offset">The ledger offset of the assignment.</param>
    /// <param name="Source">The synchronizer the contract was reassigned from.</param>
    /// <param name="Target">The synchronizer the contract was reassigned to.</param>
    /// <param name="ReassignmentId">The reassignment's unique id — the same value on the
    /// paired unassignment and assignment, and the input to the completing assign command.</param>
    /// <param name="ReassignmentCounter">The reassignment counter shared by the paired
    /// unassignment and assignment; consumers pair the two events (and dedup replays) by
    /// matching this value.</param>
    /// <param name="WitnessParties">Parties that witnessed the assignment.</param>
    public sealed record Assigned(
        ContractId<TInterface> ContractId,
        TView Payload,
        ContractKey? Key,
        LedgerOffset Offset,
        SynchronizerId Source,
        SynchronizerId Target,
        string ReassignmentId,
        long ReassignmentCounter,
        IReadOnlyList<Party> WitnessParties) : InterfaceStreamEvent<TInterface, TView>
    {
        private readonly IReadOnlyList<Party> _witnessParties =
            EventCollections.Borrow(WitnessParties, nameof(WitnessParties));

        /// <summary>
        /// Parties that witnessed the assignment. Held on the same terms as
        /// <see cref="Created.WitnessParties"/>.
        /// </summary>
        public IReadOnlyList<Party> WitnessParties
        {
            get => _witnessParties;
            init => _witnessParties = EventCollections.Borrow(value, nameof(WitnessParties));
        }
    }

    /// <summary>
    /// A contract implementing <typeparamref name="TInterface"/> was unassigned from a
    /// synchronizer (the start of a reassignment).
    /// </summary>
    /// <param name="ContractId">The on-ledger contract ID, interface-typed.</param>
    /// <param name="Offset">The ledger offset of the unassignment.</param>
    /// <param name="Source">The synchronizer the contract is leaving.</param>
    /// <param name="Target">The synchronizer the contract is moving to.</param>
    /// <param name="ReassignmentId">The reassignment's unique id — the same value on the
    /// paired assignment, and the input to the assign command that completes the move.</param>
    /// <param name="ReassignmentCounter">The reassignment counter shared by the paired
    /// assignment; consumers pair the two events (and dedup replays) by matching this value.</param>
    /// <param name="WitnessParties">Parties that witnessed the unassignment.</param>
    public sealed record Unassigned(
        ContractId<TInterface> ContractId,
        LedgerOffset Offset,
        SynchronizerId Source,
        SynchronizerId Target,
        string ReassignmentId,
        long ReassignmentCounter,
        IReadOnlyList<Party> WitnessParties) : InterfaceStreamEvent<TInterface, TView>
    {
        private readonly IReadOnlyList<Party> _witnessParties =
            EventCollections.Borrow(WitnessParties, nameof(WitnessParties));

        /// <summary>
        /// Parties that witnessed the unassignment. Held on the same terms as
        /// <see cref="Created.WitnessParties"/>.
        /// </summary>
        public IReadOnlyList<Party> WitnessParties
        {
            get => _witnessParties;
            init => _witnessParties = EventCollections.Borrow(value, nameof(WitnessParties));
        }
    }

    /// <summary>
    /// An offset checkpoint with no contract payload: a participant-emitted marker with no
    /// matching activity to surface, so consumers can advance their persisted resume offset
    /// during quiet periods.
    /// </summary>
    /// <param name="Offset">The participant's current ledger offset; persist it as the resume
    /// offset for a subsequent subscription. That subscription treats its lower bound as
    /// exclusive, so resuming from this offset does not re-deliver any event already seen up
    /// to it.</param>
    public sealed record Checkpoint(LedgerOffset Offset) : InterfaceStreamEvent<TInterface, TView>;

    /// <summary>
    /// The transport stream failed mid-flight. Surfaced in-band rather than thrown so callers
    /// can decide policy — log and continue with a fresh stream from the last good offset,
    /// terminate, etc.
    /// </summary>
    /// <param name="StatusCode">Transport status code from the failed call. For gRPC streams
    /// this is <c>(int)Grpc.Core.StatusCode</c>; consumers that want the typed enum cast back.
    /// Held as <c>int</c> so this type stays free of any transport-library dep.</param>
    /// <param name="Message">Status detail / message from the participant or transport.</param>
    /// <param name="Category">Classification of the transport failure when the transport could
    /// determine one without a structured Canton error attached; <c>null</c> when the failure
    /// was not classified.</param>
    /// <param name="SourceException">Transport exception that caused the stream failure, when
    /// available.</param>
    public sealed record StreamError(
        int StatusCode,
        string Message,
        DamlErrorCategory? Category = null,
        Exception? SourceException = null) : InterfaceStreamEvent<TInterface, TView>;

    /// <summary>
    /// An event the transport delivered but this layer could not map to any of the other
    /// variants. Surfaced rather than silently dropped so consumers can honour a
    /// no-silent-drop invariant. A matching event carrying no interface view lands here as
    /// <see cref="UnclassifiedKind.InterfaceViewUnavailable"/>.
    /// </summary>
    /// <param name="Offset">The ledger offset at which the unrecognized event occurred, or
    /// <c>null</c> when the event could not be placed on the ledger at all. A consumer
    /// persisting resume state must not checkpoint a <c>null</c> offset:
    /// <see cref="LedgerOffset"/> has no absent value and its <c>default</c> is
    /// <see cref="LedgerOffset.Begin"/>, a genuine ledger position, so substituting one
    /// resumes from the beginning of the ledger and re-reads the whole stream. Skip the event
    /// and keep the last offset that was real.</param>
    /// <param name="Kind">Why the event could not be mapped to a typed variant, as a
    /// strongly-typed discriminator consumers <c>switch</c> on.</param>
    /// <param name="RawKind">The transport's raw descriptor for the unrecognized event —
    /// non-<c>null</c> exactly when <paramref name="Kind"/> is
    /// <see cref="UnclassifiedKind.Unknown"/>, and <c>null</c> for every enumerated reason.</param>
    /// <exception cref="ArgumentException"><paramref name="Kind"/> is
    /// <see cref="UnclassifiedKind.Unknown"/> with a <c>null</c> <paramref name="RawKind"/>, or an
    /// enumerated <paramref name="Kind"/> with a non-<c>null</c> <paramref name="RawKind"/>.</exception>
    public sealed record Unclassified(
        LedgerOffset? Offset,
        UnclassifiedKind Kind,
        string? RawKind = null) : InterfaceStreamEvent<TInterface, TView>
    {
        /// <summary>
        /// Why the event could not be mapped to a typed variant, as a strongly-typed
        /// discriminator consumers <c>switch</c> on. Get-only, so a <c>with</c> expression
        /// cannot reassign it independently of <see cref="RawKind"/>.
        /// </summary>
        public UnclassifiedKind Kind { get; } = Kind;

        /// <summary>
        /// The transport's raw descriptor for the unrecognized event — non-<c>null</c> exactly
        /// when <see cref="Kind"/> is <see cref="UnclassifiedKind.Unknown"/>, and <c>null</c>
        /// otherwise. Get-only, so the invariant validated at construction cannot be bypassed by
        /// a <c>with</c> expression.
        /// </summary>
        public string? RawKind { get; } = (Kind, RawKind) switch
        {
            (UnclassifiedKind.Unknown, null) => throw new ArgumentException(
                "An Unclassified event with Kind Unknown must carry the transport's raw descriptor in RawKind.",
                nameof(RawKind)),
            (not UnclassifiedKind.Unknown, not null) => throw new ArgumentException(
                $"An Unclassified event with the enumerated Kind '{Kind}' must not carry a RawKind; RawKind is populated only for Unknown.",
                nameof(RawKind)),
            _ => RawKind,
        };
    }
}
