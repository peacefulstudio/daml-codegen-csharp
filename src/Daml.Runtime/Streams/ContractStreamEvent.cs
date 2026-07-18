// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Daml.Runtime.Streams;

/// <summary>
/// A typed event observed on a subscription stream over <typeparamref name="T"/>.
/// Discriminated union: callers <c>switch</c> on the concrete subtype rather than
/// catching exceptions for stream errors. Transport-agnostic — lives in
/// <c>Daml.Runtime</c> so any ledger client (gRPC, JSON, in-memory) can yield
/// these without dragging the consumer into a specific transport dependency.
/// </summary>
/// <typeparam name="T">
/// The Daml marker the stream is filtered to: a template (matched by
/// <c>TemplateId</c>) or a Daml interface marker (matched by interface id).
/// </typeparam>
/// <remarks>
/// <list type="bullet">
///   <item><see cref="Created"/> — a contract of type <typeparamref name="T"/>
///   was created on the ledger; full payload is available.</item>
///   <item><see cref="Archived"/> — a contract of type <typeparamref name="T"/>
///   was archived; payload is not available (Canton does not re-emit it).
///   Emitted only on ACS-delta-shaped streams (the shape the live
///   <c>ILedgerStreamer.SubscribeAsync</c> stream uses); the ledger-effects
///   subscription (<c>ILedgerStreamer.SubscribeLedgerEffectsAsync</c>) never
///   yields this variant — an archive there arrives as a consuming
///   <see cref="Exercised"/> (<see cref="Exercised.Consuming"/> is <c>true</c>).</item>
///   <item><see cref="Exercised"/> — a choice was exercised on a contract of
///   type <typeparamref name="T"/>; choice argument and result are available.
///   Emitted only on the ledger-effects shape (the shape the live
///   <c>ILedgerStreamer.SubscribeLedgerEffectsAsync</c> stream uses); a
///   consuming exercise (<see cref="Exercised.Consuming"/> is <c>true</c>) is
///   that shape's archival signal.</item>
///   <item><see cref="Assigned"/>/<see cref="Unassigned"/> — a contract of
///   type <typeparamref name="T"/> was reassigned across synchronizers.</item>
///   <item><see cref="Checkpoint"/> — an offset checkpoint carrying no
///   contract payload: a participant-emitted marker on a live subscription that
///   consumers persist via <see cref="Checkpoint.Offset"/> to advance the
///   resume offset during quiet periods (no template-matching transactions
///   arriving), avoiding the re-process-from-stale-offset failure mode after
///   a crash. Active-contract-set snapshots stream
///   <see cref="AcsSnapshotEntry{T}"/> instead of this type; their terminal
///   marker is <see cref="AcsSnapshotEntry{T}.Checkpoint"/>.</item>
///   <item><see cref="StreamError"/> — the transport stream failed mid-flight.
///   Surfaced as a value rather than thrown so the consuming
///   <c>await foreach</c> loop can decide whether to retry, log, or stop.</item>
///   <item><see cref="Unclassified"/> — an event the transport delivered but
///   this layer could not map to any other variant; surfaced as a value so
///   consumers can implement a no-silent-drop policy for themselves.</item>
/// </list>
/// <para>
/// Which variants a stream yields is a property of its transaction shape, not
/// of this type: the union structurally admits every variant, but a given
/// stream emits only the subset its shape produces.
/// </para>
/// <list type="table">
///   <listheader>
///     <term>Stream</term>
///     <description>Variants emitted</description>
///   </listheader>
///   <item>
///     <term>Ledger-effects live update stream
///     (<c>ILedgerStreamer.SubscribeLedgerEffectsAsync</c>)</term>
///     <description><see cref="Created"/>, <see cref="Exercised"/> (a consuming
///     exercise is the archival signal), <see cref="Assigned"/>,
///     <see cref="Unassigned"/>, <see cref="Checkpoint"/>,
///     <see cref="StreamError"/>, <see cref="Unclassified"/>. Never
///     <see cref="Archived"/>.</description>
///   </item>
///   <item>
///     <term>ACS-delta live update stream
///     (<c>ILedgerStreamer.SubscribeAsync</c>)</term>
///     <description><see cref="Created"/>, <see cref="Archived"/>,
///     <see cref="Assigned"/>, <see cref="Unassigned"/>,
///     <see cref="Checkpoint"/>, <see cref="StreamError"/>,
///     <see cref="Unclassified"/>. Never <see cref="Exercised"/>.</description>
///   </item>
///   <item>
///     <term>Active-contract-set snapshot
///     (<c>ILedgerStreamer.SubscribeActiveAsync</c>)</term>
///     <description><see cref="Created"/> and <see cref="Unclassified"/>
///     entries followed by a single terminal <see cref="Checkpoint"/>.
///     In-flight reassignments surface as <see cref="Created"/>, not
///     <see cref="Assigned"/>/<see cref="Unassigned"/>.</description>
///   </item>
/// </list>
/// </remarks>
public abstract record ContractStreamEvent<T>
    where T : IDamlType
{
    /// <summary>Sealed; new variants live alongside the existing ones.</summary>
    private protected ContractStreamEvent() { }

    /// <summary>
    /// A contract of type <typeparamref name="T"/> was created.
    /// </summary>
    /// <param name="ContractId">The on-ledger contract ID.</param>
    /// <param name="Payload">The deserialized create-arguments record.</param>
    /// <param name="Offset">The ledger offset at which the contract was
    /// created. Strictly increasing per synchronizer; suitable for use as
    /// the resume offset on a subsequent subscription (exclusive).</param>
    /// <param name="SynchronizerId">The synchronizer the contract was created on.</param>
    /// <param name="WitnessParties">Parties that witnessed the create event.</param>
    public sealed record Created(
        ContractId<T> ContractId,
        DamlRecord Payload,
        LedgerOffset Offset,
        SynchronizerId SynchronizerId,
        IReadOnlyList<Party> WitnessParties) : ContractStreamEvent<T>;

    /// <summary>
    /// A contract of type <typeparamref name="T"/> was archived. Emitted only on
    /// ACS-delta-shaped streams (the shape the live
    /// <c>ILedgerStreamer.SubscribeAsync</c> stream uses); the ledger-effects
    /// subscription (<c>ILedgerStreamer.SubscribeLedgerEffectsAsync</c>) never
    /// yields this variant — an archive there arrives as a consuming
    /// <see cref="Exercised"/> (<see cref="Exercised.Consuming"/> is <c>true</c>).
    /// </summary>
    /// <param name="ContractId">The on-ledger contract ID.</param>
    /// <param name="Offset">The ledger offset at which the contract was archived.</param>
    /// <param name="SynchronizerId">The synchronizer the contract was archived on.</param>
    /// <param name="WitnessParties">Parties that witnessed the archive event.</param>
    public sealed record Archived(
        ContractId<T> ContractId,
        LedgerOffset Offset,
        SynchronizerId SynchronizerId,
        IReadOnlyList<Party> WitnessParties) : ContractStreamEvent<T>;

    /// <summary>
    /// A choice was exercised on a contract of type <typeparamref name="T"/>.
    /// Only emitted when the stream is opened with ledger-effects shape (the
    /// shape the live <c>ILedgerStreamer.SubscribeLedgerEffectsAsync</c> update
    /// stream uses); ACS-delta streams emit only <see cref="Created"/> and
    /// <see cref="Archived"/>. On the ledger-effects shape a consuming exercise
    /// (<see cref="Consuming"/> is <c>true</c>) is the contract's archival
    /// signal — there is no separate <see cref="Archived"/> event on that shape.
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
        ContractId<T> ContractId,
        string ChoiceName,
        DamlValue ChoiceArgument,
        DamlValue ExerciseResult,
        bool Consuming,
        LedgerOffset Offset,
        SynchronizerId SynchronizerId,
        IReadOnlyList<Party> WitnessParties) : ContractStreamEvent<T>;

    /// <summary>
    /// A contract of type <typeparamref name="T"/> was assigned to a
    /// synchronizer (typically completing a reassignment from another
    /// synchronizer). The contract becomes active on the target synchronizer
    /// at this offset; the create-arguments are re-emitted so consumers
    /// rebuilding state from a single stream stay correct.
    /// </summary>
    /// <param name="ContractId">The on-ledger contract ID.</param>
    /// <param name="Payload">The contract's create-arguments, re-emitted on assignment.</param>
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
        ContractId<T> ContractId,
        DamlRecord Payload,
        LedgerOffset Offset,
        SynchronizerId Source,
        SynchronizerId Target,
        string ReassignmentId,
        long ReassignmentCounter,
        IReadOnlyList<Party> WitnessParties) : ContractStreamEvent<T>;

    /// <summary>
    /// A contract of type <typeparamref name="T"/> was unassigned from a
    /// synchronizer (the start of a reassignment). The contract is no longer
    /// active on the source synchronizer at this offset.
    /// </summary>
    /// <param name="ContractId">The on-ledger contract ID.</param>
    /// <param name="Offset">The ledger offset of the unassignment.</param>
    /// <param name="Source">The synchronizer the contract is leaving.</param>
    /// <param name="Target">The synchronizer the contract is moving to.</param>
    /// <param name="ReassignmentId">The reassignment's unique id — the same value on the
    /// paired assignment, and the input to the assign command that completes the move.</param>
    /// <param name="ReassignmentCounter">The reassignment counter shared by the paired
    /// assignment; consumers pair the two events (and dedup replays) by matching this value.</param>
    /// <param name="WitnessParties">Parties that witnessed the unassignment.</param>
    public sealed record Unassigned(
        ContractId<T> ContractId,
        LedgerOffset Offset,
        SynchronizerId Source,
        SynchronizerId Target,
        string ReassignmentId,
        long ReassignmentCounter,
        IReadOnlyList<Party> WitnessParties) : ContractStreamEvent<T>;

    /// <summary>
    /// An offset checkpoint with no contract payload: on a live update
    /// subscription, a participant-emitted marker with no template-matching
    /// activity to surface — Canton emits these on a participant-configured
    /// cadence (<c>max_offset_checkpoint_emission_delay</c>) regardless of
    /// the active filter, so consumers can advance their persisted resume
    /// offset during quiet periods. Active-contract-set snapshots stream
    /// <see cref="AcsSnapshotEntry{T}"/> and carry their own terminal
    /// <see cref="AcsSnapshotEntry{T}.Checkpoint"/>.
    /// </summary>
    /// <remarks>
    /// Without the quiet-period signal a low-traffic subscription that
    /// crashes during a quiet period would resume from a stale
    /// <c>Created</c>/<c>Exercised</c> offset and re-process
    /// every transaction the participant has retained between then and now.
    /// </remarks>
    /// <param name="Offset">The participant's current ledger offset; persist it
    /// as the resume offset for a subsequent subscription. That subscription
    /// treats its lower bound as exclusive, so resuming from this offset does
    /// not re-deliver any event already seen up to it.</param>
    public sealed record Checkpoint(LedgerOffset Offset) : ContractStreamEvent<T>;

    /// <summary>
    /// The transport stream failed mid-flight. Surfaced in-band rather than
    /// thrown so callers can decide policy — log and continue with a fresh
    /// stream from the last good offset, terminate, etc.
    /// </summary>
    /// <remarks>
    /// Emitted only by the live update subscriptions
    /// (<c>ILedgerStreamer.SubscribeAsync</c> and
    /// <c>ILedgerStreamer.SubscribeLedgerEffectsAsync</c>). Active-contract-set
    /// snapshots stream <see cref="AcsSnapshotEntry{T}"/> and surface a
    /// mid-snapshot transport fault in-band as their own terminal
    /// <see cref="AcsSnapshotEntry{T}.StreamError"/> variant instead.
    /// </remarks>
    /// <param name="StatusCode">Transport status code from the failed call.
    /// For gRPC streams this is <c>(int)Grpc.Core.StatusCode</c>; consumers
    /// that want the typed enum cast back. Held as <c>int</c> so this type
    /// stays free of any transport-library dep.</param>
    /// <param name="Message">Status detail / message from the participant or transport.</param>
    public sealed record StreamError(
        int StatusCode,
        string Message) : ContractStreamEvent<T>;

    /// <summary>
    /// An event the transport delivered but this layer could not map to any
    /// of the other variants. Surfaced rather than silently dropped so
    /// consumers can honour a no-silent-drop invariant — this is the
    /// transport-agnostic <c>Daml.Runtime</c> layer, so no raw wire bytes
    /// are available to attach here.
    /// </summary>
    /// <param name="Offset">The ledger offset at which the unrecognized event occurred.</param>
    /// <param name="Kind">Why the event could not be mapped to a typed variant, as a
    /// strongly-typed discriminator consumers <c>switch</c> on. <see cref="UnclassifiedKind.Unknown"/>
    /// means the transport delivered a variant this layer does not recognise; the raw
    /// descriptor is then on <paramref name="RawKind"/>.</param>
    /// <param name="RawKind">The transport's raw descriptor for the unrecognized event. The
    /// constructor guarantees the invariant that <paramref name="RawKind"/> is non-<c>null</c>
    /// exactly when <paramref name="Kind"/> is <see cref="UnclassifiedKind.Unknown"/>, and
    /// <c>null</c> for every enumerated reason — so a consumer never sees a stale descriptor
    /// attached to a named kind. Preserves forward-compatibility with server event variants
    /// added after <see cref="UnclassifiedKind"/> was published, so such an event is surfaced
    /// as data rather than dropped.</param>
    /// <exception cref="ArgumentException"><paramref name="Kind"/> is
    /// <see cref="UnclassifiedKind.Unknown"/> with a <c>null</c> <paramref name="RawKind"/>, or an
    /// enumerated <paramref name="Kind"/> with a non-<c>null</c> <paramref name="RawKind"/>.</exception>
    public sealed record Unclassified(
        LedgerOffset Offset,
        UnclassifiedKind Kind,
        string? RawKind = null) : ContractStreamEvent<T>
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
