// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Threading;
using System.Threading.Tasks;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;

namespace Daml.Ledger.Abstractions.Extensions;

/// <summary>
/// Throwing convenience overloads over <see cref="ILedgerWriter"/>.
/// These wrap the structured <c>Try*</c> primitives and throw on error outcomes.
/// </summary>
public static class ThrowingExercise
{
    /// <summary>
    /// Exercises a choice and returns the typed result. Throws
    /// <see cref="LedgerOperationException"/> on Daml or infrastructure errors.
    /// For structured error handling or a per-call deadline, use
    /// <see cref="PartyOverloads.TryExerciseAsync{TResult}(ILedgerWriter,ExerciseCommand,Party,string?,TimeSpan?,CancellationToken)"/> instead.
    /// </summary>
    public static async Task<TResult> ExerciseAsync<TResult>(
        this ILedgerWriter writer,
        ExerciseCommand command,
        Party actAs,
        string? workflowId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var outcome = await writer.TryExerciseAsync<TResult>(command, actAs, workflowId, cancellationToken: cancellationToken).ConfigureAwait(false);
        return outcome.GetResultOrThrow(cancellationToken);
    }

    /// <summary>
    /// Exercises a choice using a multi-party <see cref="SubmitterInfo"/> and returns
    /// the typed result. Throws <see cref="LedgerOperationException"/> on error.
    /// For structured error handling or a per-call deadline, use
    /// <see cref="ILedgerWriter.TryExerciseAsync{TResult}(ExerciseCommand,SubmitterInfo,string?,TimeSpan?,CancellationToken)"/> instead.
    /// </summary>
    public static async Task<TResult> ExerciseAsync<TResult>(
        this ILedgerWriter writer,
        ExerciseCommand command,
        SubmitterInfo submitter,
        string? workflowId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var outcome = await writer.TryExerciseAsync<TResult>(command, submitter, workflowId, cancellationToken: cancellationToken).ConfigureAwait(false);
        return outcome.GetResultOrThrow(cancellationToken);
    }

    /// <summary>
    /// Exercises a void choice acting as a single party, throwing on failure.
    /// Submits the exercise as a single-command transaction and discards the result:
    /// <c>One</c>, <c>None</c>, and <c>Many</c> outcomes are all treated as success —
    /// a void caller has discarded the result and no distinction between them is needed.
    /// Only structured Daml errors and infrastructure errors throw
    /// <see cref="LedgerOperationException"/>.
    /// </summary>
    public static async Task ExerciseAsync(
        this ILedgerWriter writer,
        ExerciseCommand command,
        Party actAs,
        string? workflowId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var submission = CommandsSubmission.Single(command, actAs).WithOptionalWorkflowId(workflowId);
        var outcome = await writer
            .TrySubmitAndWaitForTransactionAsync(submission, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        outcome.ThrowIfError(cancellationToken);
    }

    /// <summary>
    /// Exercises a void choice with an explicit submitter, throwing on failure.
    /// Submits the exercise as a single-command transaction and discards the result:
    /// <c>One</c>, <c>None</c>, and <c>Many</c> outcomes are all treated as success.
    /// Only structured Daml errors and infrastructure errors throw
    /// <see cref="LedgerOperationException"/>.
    /// </summary>
    public static async Task ExerciseAsync(
        this ILedgerWriter writer,
        ExerciseCommand command,
        SubmitterInfo submitter,
        string? workflowId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var submission = CommandsSubmission.Single(command).WithSubmitter(submitter).WithOptionalWorkflowId(workflowId);
        var outcome = await writer
            .TrySubmitAndWaitForTransactionAsync(submission, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        outcome.ThrowIfError(cancellationToken);
    }

    private static T GetResultOrThrow<T>(this ExerciseOutcome<T> outcome, CancellationToken cancellationToken) =>
        outcome switch
        {
            ExerciseOutcome<T>.One success => success.Result,
            ExerciseOutcome<T>.None => throw new LedgerOperationException(
                "TryExerciseAsync returned no result (None); expected exactly one. Use TryExerciseAsync for structured handling."),
            ExerciseOutcome<T>.Many m => throw new LedgerOperationException(
                $"TryExerciseAsync returned Many ({m.Count} results); use TryCreateManyByExerciseAsync to collect created contract ids, or inspect the TransactionResult directly."),
            ExerciseOutcome<T>.DamlError e => throw e.ToException(),
            ExerciseOutcome<T>.InfraError e => e.ThrowAsCancellationOrException(cancellationToken),
            _ => throw new LedgerOperationException(
                $"Unexpected outcome {outcome.GetType().Name} from TryExerciseAsync."),
        };

    private static void ThrowIfError<T>(this ExerciseOutcome<T> outcome, CancellationToken cancellationToken)
    {
        switch (outcome)
        {
            case ExerciseOutcome<T>.DamlError e:
                throw e.ToException();
            case ExerciseOutcome<T>.InfraError e:
                e.ThrowAsCancellationOrException(cancellationToken);
                break;
        }
    }
}
