// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;

namespace Daml.Runtime.Outcomes;

/// <summary>
/// Outcome of an exercise/create whose success carries a value of type <typeparamref name="T"/>.
/// Discriminated union: callers <c>switch</c> on the concrete subtype instead of catching
/// exceptions. Transport-agnostic — lives in <c>Daml.Runtime</c> so any ledger client
/// (gRPC, JSON, in-memory) can yield these without dragging consumers into a specific
/// transport dependency.
/// </summary>
/// <typeparam name="T">
/// The success payload type. Common shapes:
/// <list type="bullet">
///   <item><see cref="TransactionResult"/> — the raw transaction (use
///   <see cref="TransactionResultExtensions.Single{T}"/> et al. to project).</item>
///   <item><see cref="ContractId{T}"/> — the typed contract ID of a single created template.</item>
///   <item>A choice result record — composite Daml choice results.</item>
///   <item>Any record / scalar — choice results that aren't template-typed.</item>
/// </list>
/// No constraint is imposed on <typeparamref name="T"/>: the outcome describes
/// success/failure, not the shape of the success.
/// </typeparam>
/// <remarks>
/// <list type="bullet">
///   <item><see cref="One"/> — the operation succeeded and produced a <typeparamref name="T"/>.</item>
///   <item><see cref="None"/> — the operation committed, but the expected single result was
///   absent (at the writer level, no transaction).</item>
///   <item><see cref="Many"/> — the operation committed, but more than one candidate filled a
///   slot that expected exactly one (at the writer level, more than one transaction);
///   <see cref="Many.ContractIds"/> holds the candidates' raw contract ids, not
///   <typeparamref name="T"/> values.</item>
///   <item><see cref="DamlError"/> — structured Canton/Daml error decoded from a transport-level trailer
///   (gRPC <c>grpc-status-details-bin</c>, JSON error body, etc.).</item>
///   <item><see cref="InfraError"/> — transport-level failure with no structured Canton error attached.</item>
/// </list>
/// </remarks>
public abstract record ExerciseOutcome<T>
{
    /// <summary>Sealed; new variants live alongside the existing ones.</summary>
    private protected ExerciseOutcome() { }

    /// <summary>The operation succeeded and produced a <typeparamref name="T"/>.</summary>
    public sealed record One(T Result) : ExerciseOutcome<T>;

    /// <summary>
    /// The operation committed, but the expected single result was absent — no created contract
    /// filled the slot where exactly one was expected. At the writer level (<typeparamref name="T"/>
    /// is <see cref="TransactionResult"/>), this means the submission produced no transaction.
    /// </summary>
    public sealed record None : ExerciseOutcome<T>;

    /// <summary>
    /// The operation committed, but more than one candidate filled a slot that expected exactly
    /// one. At the writer level (<typeparamref name="T"/> is <see cref="TransactionResult"/>),
    /// this means the submission produced more than one transaction.
    /// </summary>
    /// <param name="Count">The number of competing candidates.</param>
    /// <param name="ContractIds">Raw contract ids of the competing candidates. Not
    /// <typeparamref name="T"/> values — the created contracts' template is deliberately not part
    /// of <typeparamref name="T"/>, so the ids are carried untyped to survive re-wrapping across
    /// generic instantiations.</param>
    public sealed record Many(int Count, IReadOnlyList<string> ContractIds) : ExerciseOutcome<T>;

    /// <summary>
    /// Structured Canton/Daml error returned by the participant
    /// (e.g. <c>CONTRACT_NOT_FOUND</c>, <c>INCONSISTENT</c>, or a Daml-defined
    /// <c>failWithStatus</c> error ID).
    /// </summary>
    /// <param name="Category">Canton error category — closed set; falls back to
    /// <see cref="DamlErrorCategory.Unknown"/> when the transport trailer is missing or unparseable.</param>
    /// <param name="ErrorId">Open string — Canton built-in or Daml-defined.</param>
    /// <param name="Message">Status message from the participant.</param>
    /// <param name="Metadata">Structured detail from <c>ErrorInfo.metadata</c>.</param>
    public sealed record DamlError(
        DamlErrorCategory Category,
        string ErrorId,
        string Message,
        IReadOnlyDictionary<string, string> Metadata) : ExerciseOutcome<T>;

    /// <summary>
    /// Infrastructure-level failure (no structured Canton error attached).
    /// Must not represent cancellation of the caller's own <c>CancellationToken</c> —
    /// a <c>Try*</c> method must let <see cref="OperationCanceledException"/> (or a
    /// subtype, e.g. <c>TaskCanceledException</c>) propagate for that case instead of
    /// mapping it to this outcome, even where the underlying transport reports caller
    /// cancellation via the same channel as a genuine infrastructure failure (e.g. a
    /// gRPC <c>Cancelled</c> status).
    /// </summary>
    /// <param name="StatusCode">Transport status code from the failed call. For gRPC this is
    /// <c>(int)Grpc.Core.StatusCode</c>; consumers that want the typed enum cast back. Held as
    /// <c>int</c> so this type stays free of any transport-library dep.</param>
    /// <param name="Message">Status detail / message from the participant or transport.</param>
    /// <param name="SourceException">Transport exception that caused the infrastructure failure, when available.</param>
    public sealed record InfraError(int StatusCode, string Message, Exception? SourceException = null) : ExerciseOutcome<T>;
}
