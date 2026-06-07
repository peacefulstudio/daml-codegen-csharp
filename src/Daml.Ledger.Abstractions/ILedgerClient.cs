// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;

namespace Daml.Ledger.Abstractions;

/// <summary>
/// Transport-agnostic client interface for interacting with a Daml ledger.
/// Provides methods for submitting commands, exercising choices, and
/// subscribing to update streams. Implementations target a specific
/// transport (gRPC Canton, HTTP REST, in-memory test fake).
/// </summary>
public interface ILedgerClient : IDisposable
{
    /// <summary>
    /// Exercises a choice using a <see cref="SubmitterInfo"/> and returns a structured
    /// outcome distinguishing success, Daml errors, and infrastructure errors. The
    /// caller switches on the result instead of catching exceptions. This is the
    /// primary authorization-carrying overload: the <paramref name="submitter"/>
    /// carries the act-as parties and any optional read-as parties through to the
    /// implementation.
    /// Use <see cref="LedgerClientExtensions.ExerciseAsync{TResult}(ILedgerClient,ExerciseCommand,SubmitterInfo,string?,CancellationToken)"/>
    /// for the throwing convenience overload.
    /// </summary>
    /// <typeparam name="TResult">The result type of the choice.</typeparam>
    /// <param name="command">The exercise command.</param>
    /// <param name="submitter">The submitter authorization (act-as parties and optional read-as parties).</param>
    /// <param name="workflowId">Optional workflow identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A structured outcome; callers switch on the concrete subtype.</returns>
    Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(
        ExerciseCommand command,
        SubmitterInfo submitter,
        string? workflowId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Convenience wrapper that exercises a choice on behalf of a single
    /// <paramref name="actAs"/> party with no read-as parties. Forwards to the
    /// <see cref="TryExerciseAsync{TResult}(ExerciseCommand, SubmitterInfo, string?, CancellationToken)"/>
    /// primitive via the implicit <c>Party</c> to <see cref="SubmitterInfo"/> conversion.
    /// </summary>
    /// <typeparam name="TResult">The result type of the choice.</typeparam>
    /// <param name="command">The exercise command.</param>
    /// <param name="actAs">The party acting as the submitter.</param>
    /// <param name="workflowId">Optional workflow identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A structured outcome; callers switch on the concrete subtype.</returns>
    Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(
        ExerciseCommand command,
        Party actAs,
        string? workflowId = null,
        CancellationToken cancellationToken = default)
    {
        SubmitterInfo submitter = actAs;
        return TryExerciseAsync<TResult>(command, submitter, workflowId, cancellationToken);
    }

    /// <summary>
    /// Submits multiple commands as a single atomic transaction.
    /// </summary>
    /// <param name="submission">The commands submission containing multiple commands.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The update ID of the resulting transaction.</returns>
    Task<string> SubmitAsync(
        CommandsSubmission submission,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits commands and waits for the transaction result. Returns an
    /// <see cref="ExerciseOutcome{TransactionResult}"/> distinguishing success, structured
    /// Daml errors (with category, ID, metadata), and infrastructure errors —
    /// callers <c>switch</c> on the outcome instead of catching exceptions. Use the
    /// <see cref="TransactionResultExtensions"/> helpers (<c>Single&lt;T&gt;</c>,
    /// <c>TrySingle&lt;T&gt;</c>, <c>All&lt;T&gt;</c>) on the success payload to project
    /// created contracts to typed <see cref="ContractId{T}"/> values.
    /// </summary>
    /// <param name="submission">The commands submission.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The outcome of the submission.</returns>
    Task<ExerciseOutcome<TransactionResult>> TrySubmitAndWaitForTransactionAsync(
        CommandsSubmission submission,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new contract using a <see cref="SubmitterInfo"/> and projects the
    /// result to <see cref="ExerciseOutcome{T}"/> over <see cref="ContractId{T}"/>.
    /// This is the primary authorization-carrying overload: the
    /// <paramref name="submitter"/> carries the act-as parties and any optional
    /// read-as parties through to the implementation.
    /// </summary>
    /// <typeparam name="TTemplate">The template type expected to be created.</typeparam>
    /// <param name="payload">The template payload.</param>
    /// <param name="submitter">The submitter authorization (act-as parties and optional read-as parties).</param>
    /// <param name="workflowId">Optional workflow identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
        TTemplate payload,
        SubmitterInfo submitter,
        string? workflowId = null,
        CancellationToken cancellationToken = default)
        where TTemplate : ITemplate;

    /// <summary>
    /// Convenience wrapper that creates a new contract on behalf of a single
    /// <paramref name="actAs"/> party with no read-as parties. Forwards to the
    /// <see cref="TryCreateAsync{TTemplate}(TTemplate, SubmitterInfo, string?, CancellationToken)"/>
    /// primitive via the implicit <c>Party</c> to <see cref="SubmitterInfo"/> conversion.
    /// </summary>
    /// <typeparam name="TTemplate">The template type expected to be created.</typeparam>
    /// <param name="payload">The template payload.</param>
    /// <param name="actAs">The party acting as the submitter.</param>
    /// <param name="workflowId">Optional workflow identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
        TTemplate payload,
        Party actAs,
        string? workflowId = null,
        CancellationToken cancellationToken = default)
        where TTemplate : ITemplate
    {
        SubmitterInfo submitter = actAs;
        return TryCreateAsync<TTemplate>(payload, submitter, workflowId, cancellationToken);
    }

    /// <summary>
    /// Exercises a choice using a <see cref="SubmitterInfo"/> and projects the
    /// resulting transaction's created contracts to
    /// <see cref="ExerciseOutcome{T}"/> over <see cref="ContractId{T}"/>, expecting
    /// exactly one created contract of type <typeparamref name="TTemplate"/>. This is
    /// the primary authorization-carrying overload: the <paramref name="submitter"/>
    /// carries the act-as parties and any optional read-as parties through to the
    /// implementation.
    /// </summary>
    /// <typeparam name="TTemplate">The template type expected to be created by the choice.</typeparam>
    /// <param name="command">The exercise command.</param>
    /// <param name="submitter">The submitter authorization (act-as parties and optional read-as parties).</param>
    /// <param name="workflowId">Optional workflow identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ExerciseOutcome<ContractId<TTemplate>>> TryExerciseForCreatedAsync<TTemplate>(
        ExerciseCommand command,
        SubmitterInfo submitter,
        string? workflowId = null,
        CancellationToken cancellationToken = default)
        where TTemplate : ITemplate;

    /// <summary>
    /// Convenience wrapper that exercises a choice on behalf of a single
    /// <paramref name="actAs"/> party with no read-as parties, projecting the
    /// resulting created contracts as described on the
    /// <see cref="TryExerciseForCreatedAsync{TTemplate}(ExerciseCommand, SubmitterInfo, string?, CancellationToken)"/>
    /// primitive it forwards to via the implicit <c>Party</c> to
    /// <see cref="SubmitterInfo"/> conversion.
    /// </summary>
    /// <typeparam name="TTemplate">The template type expected to be created by the choice.</typeparam>
    /// <param name="command">The exercise command.</param>
    /// <param name="actAs">The party acting as the submitter.</param>
    /// <param name="workflowId">Optional workflow identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ExerciseOutcome<ContractId<TTemplate>>> TryExerciseForCreatedAsync<TTemplate>(
        ExerciseCommand command,
        Party actAs,
        string? workflowId = null,
        CancellationToken cancellationToken = default)
        where TTemplate : ITemplate
    {
        SubmitterInfo submitter = actAs;
        return TryExerciseForCreatedAsync<TTemplate>(command, submitter, workflowId, cancellationToken);
    }

    /// <summary>
    /// Subscribes to the ledger update stream for a single template using a
    /// <see cref="SubmitterInfo"/>, projected to strongly-typed
    /// <see cref="ContractStreamEvent{T}"/> values. The combined
    /// <c>ActAs ∪ ReadAs</c> set scopes visibility for the subscription.
    /// Implementations filter by <typeparamref name="T"/>'s <c>TemplateId</c>;
    /// events for unrelated templates are dropped at the source. This is the
    /// primary authorization-carrying overload.
    /// </summary>
    /// <typeparam name="T">
    /// The Daml template to filter by. When Daml interface markers are
    /// integrated upstream (TODO: broaden to <c>IDamlType</c> once
    /// the interface-marker work lands), this will accept
    /// interface markers as well.
    /// </typeparam>
    /// <param name="submitter">The submitter authorization whose combined parties scope visibility.</param>
    /// <param name="fromOffset">
    /// Resume the stream after this absolute offset (exclusive). <c>null</c>
    /// or <c>0</c> begins from the start of the ledger. The library does
    /// not persist offsets; the caller is responsible for checkpointing
    /// (e.g. by recording the last <c>Offset</c> observed on a
    /// <see cref="ContractStreamEvent{T}.Created"/>/<see cref="ContractStreamEvent{T}.Archived"/>/
    /// <see cref="ContractStreamEvent{T}.Exercised"/> event).
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the underlying stream cleanly.
    /// <c>OperationCanceledException</c> propagates to the consumer; any other
    /// transport failure surfaces in-band as
    /// <see cref="ContractStreamEvent{T}.StreamError"/>.
    /// </param>
    /// <remarks>
    /// Uses ledger-effects transaction shape so
    /// <see cref="ContractStreamEvent{T}.Exercised"/> events are emitted alongside
    /// creates and archives. Backpressure is honoured by
    /// <see cref="IAsyncEnumerable{T}"/> of <see cref="ContractStreamEvent{T}"/> — the underlying stream is only read as
    /// fast as the consumer iterates.
    /// </remarks>
    IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
        SubmitterInfo submitter,
        long? fromOffset = null,
        CancellationToken cancellationToken = default)
        where T : ITemplate;

    /// <summary>
    /// Convenience wrapper that subscribes to the ledger update stream scoped to a
    /// single <paramref name="actAs"/> party with no read-as parties. Forwards to the
    /// <see cref="SubscribeAsync{T}(SubmitterInfo, long?, CancellationToken)"/>
    /// primitive via the implicit <c>Party</c> to <see cref="SubmitterInfo"/> conversion.
    /// </summary>
    /// <typeparam name="T">The Daml template to filter by.</typeparam>
    /// <param name="actAs">
    /// The party whose visibility scopes the subscription. For multi-party
    /// visibility, use the
    /// <see cref="SubscribeAsync{T}(SubmitterInfo, long?, CancellationToken)"/>
    /// overload.
    /// </param>
    /// <param name="fromOffset">
    /// Resume the stream after this absolute offset (exclusive). <c>null</c>
    /// or <c>0</c> begins from the start of the ledger.
    /// </param>
    /// <param name="cancellationToken">Cancels the underlying stream cleanly.</param>
    IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
        Party actAs,
        long? fromOffset = null,
        CancellationToken cancellationToken = default)
        where T : ITemplate
    {
        SubmitterInfo submitter = actAs;
        return SubscribeAsync<T>(submitter, fromOffset, cancellationToken);
    }

    /// <summary>
    /// Subscribes to the active-contract-set snapshot for a single template
    /// at the current ledger end using a <see cref="SubmitterInfo"/>, projected
    /// to strongly-typed <see cref="ContractStreamEvent{T}.Created"/> values. The
    /// combined <c>ActAs ∪ ReadAs</c> set scopes visibility for the snapshot.
    /// Implementations filter by <typeparamref name="T"/>'s <c>TemplateId</c>.
    /// This is the primary authorization-carrying overload.
    /// </summary>
    /// <typeparam name="T">
    /// The Daml template to filter by. (TODO: broaden to <c>IDamlType</c>
    /// when interface markers ship — see <see cref="SubscribeAsync{T}(SubmitterInfo, long?, CancellationToken)"/>.)
    /// </typeparam>
    /// <param name="submitter">The submitter authorization whose combined parties scope visibility.</param>
    /// <param name="cancellationToken">
    /// Cancels the underlying stream cleanly.
    /// <c>OperationCanceledException</c> propagates to the consumer.
    /// </param>
    /// <remarks>
    /// The snapshot is taken at the current ledger end and terminates when
    /// the source has streamed all active contracts. Where the underlying
    /// transport supports multi-synchronizer reassignment, the snapshot
    /// includes in-flight reassignment entries — these carry
    /// create-arguments for contracts that are mid-reassignment and are
    /// required for a complete ACS view in those deployments.
    /// To stay current after the snapshot completes, follow up with
    /// <see cref="SubscribeAsync{T}(SubmitterInfo, long?, CancellationToken)"/> using the offset returned by
    /// <see cref="GetLedgerEndAsync"/> at the time of the snapshot.
    /// Mid-stream transport failures throw rather than surfacing in-band as
    /// the typed <see cref="ContractStreamEvent{T}.StreamError"/> variant —
    /// the public return type
    /// (<see cref="IAsyncEnumerable{T}"/> of <see cref="ContractStreamEvent{T}.Created"/>) can't carry the variant.
    /// Wrap in <c>try</c>/<c>catch</c> for in-band tolerance, or use
    /// <see cref="SubscribeAsync{T}(SubmitterInfo, long?, CancellationToken)"/> directly.
    /// </remarks>
    IAsyncEnumerable<ContractStreamEvent<T>.Created> SubscribeActiveAsync<T>(
        SubmitterInfo submitter,
        CancellationToken cancellationToken = default)
        where T : ITemplate;

    /// <summary>
    /// Convenience wrapper that subscribes to the active-contract-set snapshot
    /// scoped to a single <paramref name="actAs"/> party with no read-as parties.
    /// Forwards to the
    /// <see cref="SubscribeActiveAsync{T}(SubmitterInfo, CancellationToken)"/>
    /// primitive via the implicit <c>Party</c> to <see cref="SubmitterInfo"/> conversion.
    /// </summary>
    /// <typeparam name="T">The Daml template to filter by.</typeparam>
    /// <param name="actAs">
    /// The party whose visibility scopes the snapshot. For multi-party
    /// visibility, use the
    /// <see cref="SubscribeActiveAsync{T}(SubmitterInfo, CancellationToken)"/>
    /// overload.
    /// </param>
    /// <param name="cancellationToken">Cancels the underlying stream cleanly.</param>
    IAsyncEnumerable<ContractStreamEvent<T>.Created> SubscribeActiveAsync<T>(
        Party actAs,
        CancellationToken cancellationToken = default)
        where T : ITemplate
    {
        SubmitterInfo submitter = actAs;
        return SubscribeActiveAsync<T>(submitter, cancellationToken);
    }

    /// <summary>
    /// Returns the participant's current ledger-end offset. Compose with
    /// <see cref="SubscribeActiveAsync{T}(SubmitterInfo, CancellationToken)"/> for a snapshot-then-subscribe
    /// pattern: capture an offset, drain the snapshot, then resume from
    /// the captured offset via <see cref="SubscribeAsync{T}(SubmitterInfo, long?, CancellationToken)"/>. Note that
    /// the snapshot's exact end offset is not surfaced by this
    /// abstraction, so some duplication near the snapshot boundary is
    /// possible — consumers should handle creates idempotently (by
    /// contract id) to absorb any redundant events.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The current ledger-end offset on the participant.</returns>
    Task<long> GetLedgerEndAsync(CancellationToken cancellationToken = default);
}
