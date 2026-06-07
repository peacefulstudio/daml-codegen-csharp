// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;

namespace Daml.Ledger.Abstractions;

/// <summary>
/// Throwing convenience overloads for <see cref="ILedgerClient"/>.
/// These wrap the structured <c>Try*</c> methods and throw on non-<c>One</c> outcomes.
/// </summary>
public static class LedgerClientExtensions
{
    /// <summary>
    /// Exercises a choice and returns the typed result. Throws
    /// <see cref="InvalidOperationException"/> on Daml or infrastructure errors.
    /// For structured error handling, use
    /// <see cref="ILedgerClient.TryExerciseAsync{TResult}(ExerciseCommand,Party,string?,CancellationToken)"/> instead.
    /// </summary>
    public static async Task<TResult> ExerciseAsync<TResult>(
        this ILedgerClient client,
        ExerciseCommand command,
        Party actAs,
        string? workflowId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        var outcome = await client.TryExerciseAsync<TResult>(command, actAs, workflowId, cancellationToken);
        return outcome.GetResultOrThrow();
    }

    /// <summary>
    /// Exercises a choice using a multi-party <see cref="SubmitterInfo"/> and returns
    /// the typed result. Throws <see cref="InvalidOperationException"/> on error.
    /// </summary>
    public static async Task<TResult> ExerciseAsync<TResult>(
        this ILedgerClient client,
        ExerciseCommand command,
        SubmitterInfo submitter,
        string? workflowId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        var outcome = await client.TryExerciseAsync<TResult>(command, submitter, workflowId, cancellationToken);
        return outcome.GetResultOrThrow();
    }

    /// <summary>
    /// Exercises a choice without returning a result. Throws
    /// <see cref="InvalidOperationException"/> on Daml or infrastructure errors.
    /// <c>One</c>, <c>None</c>, and <c>Many</c> outcomes are all treated as success —
    /// a void caller has discarded the result and no distinction between them is needed.
    /// Use <see cref="ILedgerClient.TryExerciseAsync{TResult}(ExerciseCommand,Party,string?,CancellationToken)"/>
    /// if you need to distinguish them.
    /// </summary>
    /// <remarks>
    /// Calls <see cref="ILedgerClient.TryExerciseAsync{TResult}(ExerciseCommand,Party,string?,CancellationToken)"/> with <c>TResult = object</c>.
    /// Implementations must ignore <c>TResult</c> for void-choice responses and return
    /// <see cref="ExerciseOutcome{T}"/> without attempting to deserialize the exercise result.
    /// </remarks>
    public static async Task ExerciseAsync(
        this ILedgerClient client,
        ExerciseCommand command,
        Party actAs,
        string? workflowId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        var outcome = await client.TryExerciseAsync<object>(command, actAs, workflowId, cancellationToken);
        outcome.ThrowIfError();
    }

    /// <summary>
    /// Exercises a choice using a multi-party <see cref="SubmitterInfo"/> without
    /// returning a result. Throws <see cref="InvalidOperationException"/> on error.
    /// <c>One</c>, <c>None</c>, and <c>Many</c> outcomes are all treated as success.
    /// </summary>
    /// <remarks>
    /// Calls <see cref="ILedgerClient.TryExerciseAsync{TResult}(ExerciseCommand,SubmitterInfo,string?,CancellationToken)"/> with <c>TResult = object</c>.
    /// Implementations must ignore <c>TResult</c> for void-choice responses and return
    /// <see cref="ExerciseOutcome{T}"/> without attempting to deserialize the exercise result.
    /// </remarks>
    public static async Task ExerciseAsync(
        this ILedgerClient client,
        ExerciseCommand command,
        SubmitterInfo submitter,
        string? workflowId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        var outcome = await client.TryExerciseAsync<object>(command, submitter, workflowId, cancellationToken);
        outcome.ThrowIfError();
    }

    private static T GetResultOrThrow<T>(this ExerciseOutcome<T> outcome) =>
        outcome switch
        {
            ExerciseOutcome<T>.One success => success.Result,
            ExerciseOutcome<T>.None => throw new InvalidOperationException(
                "TryExerciseAsync returned no result (None); expected exactly one. Use TryExerciseAsync for structured handling."),
            ExerciseOutcome<T>.Many m => throw new InvalidOperationException(
                $"TryExerciseAsync returned Many ({m.Count} results); use TryExerciseForCreatedAsync or inspect the TransactionResult directly."),
            ExerciseOutcome<T>.DamlError e => throw new InvalidOperationException(
                $"Daml error [{e.Category}/{e.ErrorId}]: {e.Message}"),
            ExerciseOutcome<T>.InfraError e => throw new InvalidOperationException(
                $"Infrastructure error [{e.StatusCode}]: {e.Message}"),
            _ => throw new InvalidOperationException(
                $"Unexpected outcome {outcome.GetType().Name} from TryExerciseAsync."),
        };

    private static void ThrowIfError<T>(this ExerciseOutcome<T> outcome)
    {
        switch (outcome)
        {
            case ExerciseOutcome<T>.DamlError e:
                throw new InvalidOperationException($"Daml error [{e.Category}/{e.ErrorId}]: {e.Message}");
            case ExerciseOutcome<T>.InfraError e:
                throw new InvalidOperationException($"Infrastructure error [{e.StatusCode}]: {e.Message}");
        }
    }
}
