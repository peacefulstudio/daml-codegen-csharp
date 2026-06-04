// Copyright (c) 2026 Peaceful Studio OÜ
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
/// The Daml template (matched by <c>TemplateId</c>) the stream is filtered
/// to. When Daml interface markers are integrated, this constraint will
/// broaden to <c>IDamlType</c> so interface IDs can be matched too.
/// </typeparam>
/// <remarks>
/// <list type="bullet">
///   <item><see cref="Created"/> — a contract of type <typeparamref name="T"/>
///   was created on the ledger; full payload is available.</item>
///   <item><see cref="Archived"/> — a contract of type <typeparamref name="T"/>
///   was archived; payload is not available (Canton does not re-emit it).</item>
///   <item><see cref="Exercised"/> — a choice was exercised on a contract of
///   type <typeparamref name="T"/>; choice argument and result are available
///   when the stream was opened with ledger-effects shape.</item>
///   <item><see cref="Assigned"/>/<see cref="Unassigned"/> — a contract of
///   type <typeparamref name="T"/> was reassigned across synchronizers.</item>
///   <item><see cref="Checkpoint"/> — a participant-emitted offset checkpoint.
///   Carries no contract payload; consumers persist <see cref="Checkpoint.Offset"/>
///   to advance the resume offset during quiet periods (no template-matching
///   transactions arriving), avoiding the re-process-from-stale-offset failure
///   mode after a crash.</item>
///   <item><see cref="StreamError"/> — the transport stream failed mid-flight.
///   Surfaced as a value rather than thrown so the consuming
///   <c>await foreach</c> loop can decide whether to retry, log, or stop.</item>
/// </list>
/// </remarks>
public abstract record ContractStreamEvent<T>
    where T : ITemplate
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
    /// <param name="WitnessParties">Parties that witnessed the create event.</param>
    public sealed record Created(
        ContractId<T> ContractId,
        DamlRecord Payload,
        long Offset,
        IReadOnlyList<Party> WitnessParties) : ContractStreamEvent<T>;

    /// <summary>
    /// A contract of type <typeparamref name="T"/> was archived.
    /// </summary>
    /// <param name="ContractId">The on-ledger contract ID.</param>
    /// <param name="Offset">The ledger offset at which the contract was archived.</param>
    /// <param name="WitnessParties">Parties that witnessed the archive event.</param>
    public sealed record Archived(
        ContractId<T> ContractId,
        long Offset,
        IReadOnlyList<Party> WitnessParties) : ContractStreamEvent<T>;

    /// <summary>
    /// A choice was exercised on a contract of type <typeparamref name="T"/>.
    /// Only emitted when the stream is opened with ledger-effects shape;
    /// ACS-delta streams emit only <see cref="Created"/> and <see cref="Archived"/>.
    /// </summary>
    /// <param name="ContractId">The on-ledger contract ID the choice was exercised on.</param>
    /// <param name="ChoiceName">The choice name.</param>
    /// <param name="ChoiceArgument">The argument value passed to the choice.</param>
    /// <param name="ExerciseResult">The result returned by the choice.</param>
    /// <param name="Consuming">Whether the exercise consumed (archived) the contract.</param>
    /// <param name="Offset">The ledger offset of the exercise.</param>
    /// <param name="WitnessParties">Parties that witnessed the exercise event.</param>
    public sealed record Exercised(
        ContractId<T> ContractId,
        string ChoiceName,
        DamlValue ChoiceArgument,
        DamlValue ExerciseResult,
        bool Consuming,
        long Offset,
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
    /// <param name="WitnessParties">Parties that witnessed the assignment.</param>
    public sealed record Assigned(
        ContractId<T> ContractId,
        DamlRecord Payload,
        long Offset,
        SynchronizerId Source,
        SynchronizerId Target,
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
    /// <param name="WitnessParties">Parties that witnessed the unassignment.</param>
    public sealed record Unassigned(
        ContractId<T> ContractId,
        long Offset,
        SynchronizerId Source,
        SynchronizerId Target,
        IReadOnlyList<Party> WitnessParties) : ContractStreamEvent<T>;

    /// <summary>
    /// A participant-emitted offset checkpoint with no template-matching
    /// activity to surface. Canton emits these on a participant-configured
    /// cadence (<c>max_offset_checkpoint_emission_delay</c>) regardless of
    /// the active filter, so consumers can advance their persisted resume
    /// offset during quiet periods.
    /// </summary>
    /// <remarks>
    /// Without this signal a low-traffic subscription that crashes during a
    /// quiet period would resume from a stale <c>Created</c>/<c>Archived</c>/
    /// <c>Exercised</c> offset and re-process every transaction the
    /// participant has retained between then and now.
    /// </remarks>
    /// <param name="Offset">The participant's current ledger offset.</param>
    public sealed record Checkpoint(long Offset) : ContractStreamEvent<T>;

    /// <summary>
    /// The transport stream failed mid-flight. Surfaced in-band rather than
    /// thrown so callers can decide policy — log and continue with a fresh
    /// stream from the last good offset, terminate, etc.
    /// </summary>
    /// <param name="StatusCode">Transport status code from the failed call.
    /// For gRPC streams this is <c>(int)Grpc.Core.StatusCode</c>; consumers
    /// that want the typed enum cast back. Held as <c>int</c> so this type
    /// stays free of any transport-library dep.</param>
    /// <param name="Message">Status detail / message from the participant or transport.</param>
    public sealed record StreamError(
        int StatusCode,
        string Message) : ContractStreamEvent<T>;
}
