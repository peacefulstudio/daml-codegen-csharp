// Copyright (c) 2026 Peaceful Studio OÜ
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
///   <item><see cref="None"/> — the operation succeeded but no <typeparamref name="T"/> was produced
///   (only meaningful when <typeparamref name="T"/> is a template payload that may be absent).</item>
///   <item><see cref="Many"/> — the operation succeeded but produced more than one
///   candidate <typeparamref name="T"/>.</item>
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

    /// <summary>The operation succeeded but produced no <typeparamref name="T"/>.</summary>
    public sealed record None : ExerciseOutcome<T>;

    /// <summary>The operation succeeded but produced more than one candidate <typeparamref name="T"/>.</summary>
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
    /// </summary>
    /// <param name="StatusCode">Transport status code from the failed call. For gRPC this is
    /// <c>(int)Grpc.Core.StatusCode</c>; consumers that want the typed enum cast back. Held as
    /// <c>int</c> so this type stays free of any transport-library dep.</param>
    /// <param name="Message">Status detail / message from the participant or transport.</param>
    public sealed record InfraError(int StatusCode, string Message) : ExerciseOutcome<T>;
}
