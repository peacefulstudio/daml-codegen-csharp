// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
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
    /// Exercises a choice and returns a structured outcome distinguishing success,
    /// Daml errors, and infrastructure errors. The caller switches on the result
    /// instead of catching exceptions.
    /// Use <see cref="LedgerClientExtensions.ExerciseAsync{TResult}(ILedgerClient,ExerciseCommand,string,string?,CancellationToken)"/>
    /// for the throwing convenience overload.
    /// </summary>
    /// <typeparam name="TResult">The result type of the choice.</typeparam>
    /// <param name="command">The exercise command.</param>
    /// <param name="actAs">The party acting as the submitter.</param>
    /// <param name="workflowId">Optional workflow identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A structured outcome; callers switch on the concrete subtype.</returns>
    Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(
        ExerciseCommand command,
        string actAs,
        string? workflowId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exercises a choice using a multi-party <see cref="SubmitterInfo"/> and returns
    /// a structured outcome. The default implementation forwards single-party
    /// submissions to the <c>string actAs</c> overload; multi-party submissions
    /// throw <see cref="NotSupportedException"/> — implementations override to support them.
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
        CancellationToken cancellationToken = default)
    {
        if (TryGetSinglePartyActAs(submitter, out var actAs))
        {
            return TryExerciseAsync<TResult>(command, actAs, workflowId, cancellationToken);
        }

        throw NewMultiPartyNotSupported(nameof(TryExerciseAsync));
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
    /// Creates a new contract and projects the result to
    /// <see cref="ExerciseOutcome{T}"/> over <see cref="ContractId{T}"/>.
    /// </summary>
    /// <typeparam name="TTemplate">The template type expected to be created.</typeparam>
    Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
        TTemplate payload,
        string actAs,
        string? workflowId = null,
        CancellationToken cancellationToken = default)
        where TTemplate : ITemplate;

    /// <summary>
    /// Creates a new contract using a multi-party <see cref="SubmitterInfo"/>
    /// and projects the result to <see cref="ExerciseOutcome{T}"/> over
    /// <see cref="ContractId{T}"/>.
    /// </summary>
    /// <typeparam name="TTemplate">The template type expected to be created.</typeparam>
    /// <param name="payload">The template payload.</param>
    /// <param name="submitter">The submitter authorization (act-as parties and optional read-as parties).</param>
    /// <param name="workflowId">Optional workflow identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// The default implementation forwards single-party submissions
    /// (<c>ActAs.Count == 1</c> and <c>ReadAs.Count == 0</c>) to the
    /// <see cref="TryCreateAsync{TTemplate}(TTemplate, string, string, CancellationToken)"/>
    /// overload. Multi-party submissions throw <see cref="NotSupportedException"/> —
    /// implementations must override this method to support true multi-party authorization.
    /// </remarks>
    Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
        TTemplate payload,
        SubmitterInfo submitter,
        string? workflowId = null,
        CancellationToken cancellationToken = default)
        where TTemplate : ITemplate
    {
        if (TryGetSinglePartyActAs(submitter, out var actAs))
        {
            return TryCreateAsync<TTemplate>(payload, actAs, workflowId, cancellationToken);
        }

        throw NewMultiPartyNotSupported(nameof(TryCreateAsync));
    }

    /// <summary>
    /// Exercises a choice and projects the resulting transaction's created contracts to
    /// <see cref="ExerciseOutcome{T}"/> over <see cref="ContractId{T}"/>, expecting
    /// exactly one created contract of type <typeparamref name="TTemplate"/>.
    /// </summary>
    /// <typeparam name="TTemplate">The template type expected to be created by the choice.</typeparam>
    Task<ExerciseOutcome<ContractId<TTemplate>>> TryExerciseForCreatedAsync<TTemplate>(
        ExerciseCommand command,
        string actAs,
        string? workflowId = null,
        CancellationToken cancellationToken = default)
        where TTemplate : ITemplate;

    /// <summary>
    /// Exercises a choice using a multi-party <see cref="SubmitterInfo"/> and
    /// projects the resulting transaction's created contracts to
    /// <see cref="ExerciseOutcome{T}"/> over <see cref="ContractId{T}"/>, expecting
    /// exactly one created contract of type <typeparamref name="TTemplate"/>.
    /// </summary>
    /// <typeparam name="TTemplate">The template type expected to be created by the choice.</typeparam>
    /// <param name="command">The exercise command.</param>
    /// <param name="submitter">The submitter authorization (act-as parties and optional read-as parties).</param>
    /// <param name="workflowId">Optional workflow identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// The default implementation forwards single-party submissions
    /// (<c>ActAs.Count == 1</c> and <c>ReadAs.Count == 0</c>) to the
    /// <see cref="TryExerciseForCreatedAsync{TTemplate}(ExerciseCommand, string, string, CancellationToken)"/>
    /// overload. Multi-party submissions throw <see cref="NotSupportedException"/> —
    /// implementations must override this method to support true multi-party authorization.
    /// </remarks>
    Task<ExerciseOutcome<ContractId<TTemplate>>> TryExerciseForCreatedAsync<TTemplate>(
        ExerciseCommand command,
        SubmitterInfo submitter,
        string? workflowId = null,
        CancellationToken cancellationToken = default)
        where TTemplate : ITemplate
    {
        if (TryGetSinglePartyActAs(submitter, out var actAs))
        {
            return TryExerciseForCreatedAsync<TTemplate>(command, actAs, workflowId, cancellationToken);
        }

        throw NewMultiPartyNotSupported(nameof(TryExerciseForCreatedAsync));
    }

    /// <summary>
    /// Subscribes to the ledger update stream for a single template, projected to
    /// strongly-typed <see cref="ContractStreamEvent{T}"/> values. Implementations
    /// filter by <typeparamref name="T"/>'s <c>TemplateId</c>; events for unrelated
    /// templates are dropped at the source.
    /// </summary>
    /// <typeparam name="T">
    /// The Daml template to filter by. When Daml interface markers are
    /// integrated upstream (TODO: broaden to <c>IDamlType</c> once
    /// the interface-marker work lands), this will accept
    /// interface markers as well.
    /// </typeparam>
    /// <param name="actAs">
    /// The party whose visibility scopes the subscription. For multi-party
    /// visibility, use the
    /// <see cref="SubscribeAsync{T}(SubmitterInfo, long?, CancellationToken)"/>
    /// overload.
    /// </param>
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
        string actAs,
        long? fromOffset = null,
        CancellationToken cancellationToken = default)
        where T : ITemplate;

    /// <summary>
    /// Subscribes to the ledger update stream using a multi-party
    /// <see cref="SubmitterInfo"/>. The combined <c>ActAs ∪ ReadAs</c> set
    /// scopes visibility for the subscription.
    /// </summary>
    /// <typeparam name="T">The Daml template to filter by.</typeparam>
    /// <param name="submitter">The submitter authorization whose combined parties scope visibility.</param>
    /// <param name="fromOffset">
    /// Resume the stream after this absolute offset (exclusive). <c>null</c>
    /// or <c>0</c> begins from the start of the ledger.
    /// </param>
    /// <param name="cancellationToken">Cancels the underlying stream cleanly.</param>
    /// <remarks>
    /// The default implementation forwards single-party subscriptions
    /// (<c>ActAs.Count == 1</c> and <c>ReadAs.Count == 0</c>) to the
    /// <see cref="SubscribeAsync{T}(string, long?, CancellationToken)"/>
    /// overload. Multi-party subscriptions throw <see cref="NotSupportedException"/> —
    /// implementations must override this method to support true multi-party visibility.
    /// </remarks>
    IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
        SubmitterInfo submitter,
        long? fromOffset = null,
        CancellationToken cancellationToken = default)
        where T : ITemplate
    {
        if (TryGetSinglePartyActAs(submitter, out var actAs))
        {
            return SubscribeAsync<T>(actAs, fromOffset, cancellationToken);
        }

        throw NewMultiPartyNotSupported(nameof(SubscribeAsync));
    }

    /// <summary>
    /// Subscribes to the active-contract-set snapshot for a single template
    /// at the current ledger end, projected to strongly-typed
    /// <see cref="ContractStreamEvent{T}.Created"/> values. Implementations
    /// filter by <typeparamref name="T"/>'s <c>TemplateId</c>.
    /// </summary>
    /// <typeparam name="T">
    /// The Daml template to filter by. (TODO: broaden to <c>IDamlType</c>
    /// when interface markers ship — see <see cref="SubscribeAsync{T}(string, long?, CancellationToken)"/>.)
    /// </typeparam>
    /// <param name="actAs">
    /// The party whose visibility scopes the snapshot. For multi-party
    /// visibility, use the
    /// <see cref="SubscribeActiveAsync{T}(SubmitterInfo, CancellationToken)"/>
    /// overload.
    /// </param>
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
    /// <see cref="SubscribeAsync{T}(string, long?, CancellationToken)"/> using the offset returned by
    /// <see cref="GetLedgerEndAsync"/> at the time of the snapshot.
    /// Mid-stream transport failures throw rather than surfacing in-band as
    /// the typed <see cref="ContractStreamEvent{T}.StreamError"/> variant —
    /// the public return type
    /// (<see cref="IAsyncEnumerable{T}"/> of <see cref="ContractStreamEvent{T}.Created"/>) can't carry the variant.
    /// Wrap in <c>try</c>/<c>catch</c> for in-band tolerance, or use
    /// <see cref="SubscribeAsync{T}(string, long?, CancellationToken)"/> directly.
    /// </remarks>
    IAsyncEnumerable<ContractStreamEvent<T>.Created> SubscribeActiveAsync<T>(
        string actAs,
        CancellationToken cancellationToken = default)
        where T : ITemplate;

    /// <summary>
    /// Subscribes to the active-contract-set snapshot using a multi-party
    /// <see cref="SubmitterInfo"/>. The combined <c>ActAs ∪ ReadAs</c> set
    /// scopes visibility for the snapshot.
    /// </summary>
    /// <typeparam name="T">The Daml template to filter by.</typeparam>
    /// <param name="submitter">The submitter authorization whose combined parties scope visibility.</param>
    /// <param name="cancellationToken">Cancels the underlying stream cleanly.</param>
    /// <remarks>
    /// The default implementation forwards single-party subscriptions
    /// (<c>ActAs.Count == 1</c> and <c>ReadAs.Count == 0</c>) to the
    /// <see cref="SubscribeActiveAsync{T}(string, CancellationToken)"/>
    /// overload. Multi-party subscriptions throw <see cref="NotSupportedException"/> —
    /// implementations must override this method to support true multi-party visibility.
    /// </remarks>
    IAsyncEnumerable<ContractStreamEvent<T>.Created> SubscribeActiveAsync<T>(
        SubmitterInfo submitter,
        CancellationToken cancellationToken = default)
        where T : ITemplate
    {
        if (TryGetSinglePartyActAs(submitter, out var actAs))
        {
            return SubscribeActiveAsync<T>(actAs, cancellationToken);
        }

        throw NewMultiPartyNotSupported(nameof(SubscribeActiveAsync));
    }

    /// <summary>
    /// Returns the participant's current ledger-end offset. Compose with
    /// <see cref="SubscribeActiveAsync{T}(string, CancellationToken)"/> for a snapshot-then-subscribe
    /// pattern: capture an offset, drain the snapshot, then resume from
    /// the captured offset via <see cref="SubscribeAsync{T}(string, long?, CancellationToken)"/>. Note that
    /// the snapshot's exact end offset is not surfaced by this
    /// abstraction, so some duplication near the snapshot boundary is
    /// possible — consumers should handle creates idempotently (by
    /// contract id) to absorb any redundant events.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The current ledger-end offset on the participant.</returns>
    Task<long> GetLedgerEndAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true and the single act-as party when <paramref name="submitter"/>
    /// represents a single-party submission (one <c>ActAs</c> party, no <c>ReadAs</c>
    /// parties); false otherwise.
    /// </summary>
    private static bool TryGetSinglePartyActAs(SubmitterInfo submitter, out string actAs)
    {
        if (submitter.ActAs.Count == 1 && submitter.ReadAs.Count == 0)
        {
            actAs = submitter.ActAs.First().Id;
            return true;
        }

        actAs = string.Empty;
        return false;
    }

    /// <summary>
    /// Constructs the canonical <see cref="NotSupportedException"/> for callers
    /// of multi-party <see cref="SubmitterInfo"/> overloads that haven't been
    /// overridden by the implementation.
    /// </summary>
    private static NotSupportedException NewMultiPartyNotSupported(string methodName) =>
        new($"Multi-party SubmitterInfo requires the ILedgerClient implementation to override the SubmitterInfo overload of {methodName}. " +
            "The default implementation only supports a single ActAs party with no ReadAs parties.");
}
