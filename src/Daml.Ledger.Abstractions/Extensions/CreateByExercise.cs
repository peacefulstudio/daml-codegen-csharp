// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;

namespace Daml.Ledger.Abstractions.Extensions;

/// <summary>
/// Create-by-exercise convenience: exercise a choice and lift the contracts it created
/// into typed <see cref="ContractId{TTemplate}"/> values. The single-result
/// (<c>*One*</c>) and multi-result (<c>*Many*</c>) shapes are split so callers name up
/// front how many contracts of the target template they expect, and no outcome is ever
/// silently collapsed into a success shape.
/// </summary>
public static class CreateByExercise
{
    /// <summary>
    /// Exercises <paramref name="choice"/> and lifts the single created
    /// <typeparamref name="TTemplate"/> into an <see cref="ExerciseOutcome{T}"/> over its
    /// <see cref="ContractId{T}"/>. Exactly one created <typeparamref name="TTemplate"/>
    /// yields <see cref="ExerciseOutcome{T}.One"/>; none yields
    /// <see cref="ExerciseOutcome{T}.None"/>; more than one yields
    /// <see cref="ExerciseOutcome{T}.Many"/> carrying every created contract id.
    /// A writer-level <see cref="ExerciseOutcome{T}.Many"/> (multiple root transactions)
    /// is propagated faithfully rather than discarded. Use
    /// <see cref="TryCreateManyByExerciseAsync{TTemplate}(ILedgerWriter,ExerciseCommand,SubmitterInfo,string?,TimeSpan?,CancellationToken)"/>
    /// when the choice is expected to create any number of <typeparamref name="TTemplate"/> contracts.
    /// </summary>
    public static async Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateOneByExerciseAsync<TTemplate>(
        this ILedgerWriter writer,
        ExerciseCommand choice,
        SubmitterInfo submitter,
        string? workflowId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TTemplate : IDamlType
    {
        ArgumentNullException.ThrowIfNull(writer);
        var submission = CommandsSubmission.Single(choice).WithSubmitter(submitter).WithOptionalWorkflowId(workflowId);

        var outcome = await writer
            .TrySubmitAndWaitForTransactionAsync(submission, timeout, cancellationToken)
            .ConfigureAwait(false);

        return outcome switch
        {
            ExerciseOutcome<TransactionResult>.One one => ProjectSingleCreated<TTemplate>(one.Result),
            ExerciseOutcome<TransactionResult>.None => new ExerciseOutcome<ContractId<TTemplate>>.None(),
            ExerciseOutcome<TransactionResult>.Many many => new ExerciseOutcome<ContractId<TTemplate>>.Many(many.Count, many.ContractIds),
            ExerciseOutcome<TransactionResult>.DamlError e => new ExerciseOutcome<ContractId<TTemplate>>.DamlError(e.Category, e.ErrorId, e.Message, e.Metadata),
            ExerciseOutcome<TransactionResult>.InfraError e => new ExerciseOutcome<ContractId<TTemplate>>.InfraError(e.StatusCode, e.Message, e.SourceException),
            _ => throw new UnreachableException($"Unexpected outcome {outcome.GetType().Name} from TrySubmitAndWaitForTransactionAsync."),
        };
    }

    /// <summary>
    /// Exercises <paramref name="choice"/> as a single party and lifts the single created
    /// <typeparamref name="TTemplate"/>. See
    /// <see cref="TryCreateOneByExerciseAsync{TTemplate}(ILedgerWriter,ExerciseCommand,SubmitterInfo,string?,TimeSpan?,CancellationToken)"/>.
    /// </summary>
    public static Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateOneByExerciseAsync<TTemplate>(
        this ILedgerWriter writer,
        ExerciseCommand choice,
        Party actAs,
        string? workflowId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TTemplate : IDamlType
    {
        SubmitterInfo submitter = actAs;
        return writer.TryCreateOneByExerciseAsync<TTemplate>(choice, submitter, workflowId, timeout, cancellationToken);
    }

    /// <summary>
    /// Exercises <paramref name="choice"/> and lifts every created
    /// <typeparamref name="TTemplate"/> into an <see cref="ExerciseOutcome{T}"/> over a
    /// read-only list of <see cref="ContractId{T}"/>. A conforming writer yields only
    /// <see cref="ExerciseOutcome{T}.One"/> (whose list holds all created
    /// <typeparamref name="TTemplate"/> contracts, in transaction order — it may be empty
    /// when the choice created none), <see cref="ExerciseOutcome{T}.DamlError"/>, or
    /// <see cref="ExerciseOutcome{T}.InfraError"/>. A writer-level
    /// <see cref="ExerciseOutcome{T}.None"/> or <see cref="ExerciseOutcome{T}.Many"/> is
    /// only reachable from a non-conforming writer and is propagated faithfully rather
    /// than collapsed.
    /// </summary>
    public static async Task<ExerciseOutcome<IReadOnlyList<ContractId<TTemplate>>>> TryCreateManyByExerciseAsync<TTemplate>(
        this ILedgerWriter writer,
        ExerciseCommand choice,
        SubmitterInfo submitter,
        string? workflowId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TTemplate : IDamlType
    {
        ArgumentNullException.ThrowIfNull(writer);
        var submission = CommandsSubmission.Single(choice).WithSubmitter(submitter).WithOptionalWorkflowId(workflowId);

        var outcome = await writer
            .TrySubmitAndWaitForTransactionAsync(submission, timeout, cancellationToken)
            .ConfigureAwait(false);

        return outcome switch
        {
            ExerciseOutcome<TransactionResult>.One one => new ExerciseOutcome<IReadOnlyList<ContractId<TTemplate>>>.One(one.Result.All<TTemplate>()),
            ExerciseOutcome<TransactionResult>.None => new ExerciseOutcome<IReadOnlyList<ContractId<TTemplate>>>.None(),
            ExerciseOutcome<TransactionResult>.Many many => new ExerciseOutcome<IReadOnlyList<ContractId<TTemplate>>>.Many(many.Count, many.ContractIds),
            ExerciseOutcome<TransactionResult>.DamlError e => new ExerciseOutcome<IReadOnlyList<ContractId<TTemplate>>>.DamlError(e.Category, e.ErrorId, e.Message, e.Metadata),
            ExerciseOutcome<TransactionResult>.InfraError e => new ExerciseOutcome<IReadOnlyList<ContractId<TTemplate>>>.InfraError(e.StatusCode, e.Message, e.SourceException),
            _ => throw new UnreachableException($"Unexpected outcome {outcome.GetType().Name} from TrySubmitAndWaitForTransactionAsync."),
        };
    }

    /// <summary>
    /// Exercises <paramref name="choice"/> as a single party and lifts every created
    /// <typeparamref name="TTemplate"/>. See
    /// <see cref="TryCreateManyByExerciseAsync{TTemplate}(ILedgerWriter,ExerciseCommand,SubmitterInfo,string?,TimeSpan?,CancellationToken)"/>.
    /// </summary>
    public static Task<ExerciseOutcome<IReadOnlyList<ContractId<TTemplate>>>> TryCreateManyByExerciseAsync<TTemplate>(
        this ILedgerWriter writer,
        ExerciseCommand choice,
        Party actAs,
        string? workflowId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TTemplate : IDamlType
    {
        SubmitterInfo submitter = actAs;
        return writer.TryCreateManyByExerciseAsync<TTemplate>(choice, submitter, workflowId, timeout, cancellationToken);
    }

    /// <summary>
    /// Exercises <paramref name="choice"/> and returns the single created
    /// <typeparamref name="TTemplate"/> contract id, throwing on any other outcome.
    /// Throws <see cref="LedgerOperationException"/> when the choice created no
    /// <typeparamref name="TTemplate"/> (expected exactly one), when it created more than
    /// one (use
    /// <see cref="CreateManyByExerciseAsync{TTemplate}(ILedgerWriter,ExerciseCommand,SubmitterInfo,string?,TimeSpan?,CancellationToken)"/>
    /// instead), or on a Daml or infrastructure error. For structured handling or a
    /// per-call deadline, use
    /// <see cref="TryCreateOneByExerciseAsync{TTemplate}(ILedgerWriter,ExerciseCommand,SubmitterInfo,string?,TimeSpan?,CancellationToken)"/>.
    /// </summary>
    public static async Task<ContractId<TTemplate>> CreateOneByExerciseAsync<TTemplate>(
        this ILedgerWriter writer,
        ExerciseCommand choice,
        SubmitterInfo submitter,
        string? workflowId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TTemplate : IDamlType
    {
        var outcome = await writer
            .TryCreateOneByExerciseAsync<TTemplate>(choice, submitter, workflowId, timeout, cancellationToken)
            .ConfigureAwait(false);

        return outcome switch
        {
            ExerciseOutcome<ContractId<TTemplate>>.One one => one.Result,
            ExerciseOutcome<ContractId<TTemplate>>.None => throw new LedgerOperationException(
                $"Exercising the choice created no {typeof(TTemplate).Name}; expected exactly one."),
            ExerciseOutcome<ContractId<TTemplate>>.Many many => throw new LedgerOperationException(
                $"Exercising the choice created {many.Count} {typeof(TTemplate).Name} contracts; expected exactly one. Use CreateManyByExerciseAsync to collect them."),
            ExerciseOutcome<ContractId<TTemplate>>.DamlError e => throw e.ToException(),
            ExerciseOutcome<ContractId<TTemplate>>.InfraError e => e.ThrowAsCancellationOrException(cancellationToken),
            _ => throw new UnreachableException($"Unexpected outcome {outcome.GetType().Name} from TryCreateOneByExerciseAsync."),
        };
    }

    /// <summary>
    /// Exercises <paramref name="choice"/> as a single party and returns the single
    /// created <typeparamref name="TTemplate"/> contract id, throwing on any other
    /// outcome. See
    /// <see cref="CreateOneByExerciseAsync{TTemplate}(ILedgerWriter,ExerciseCommand,SubmitterInfo,string?,TimeSpan?,CancellationToken)"/>.
    /// </summary>
    public static Task<ContractId<TTemplate>> CreateOneByExerciseAsync<TTemplate>(
        this ILedgerWriter writer,
        ExerciseCommand choice,
        Party actAs,
        string? workflowId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TTemplate : IDamlType
    {
        SubmitterInfo submitter = actAs;
        return writer.CreateOneByExerciseAsync<TTemplate>(choice, submitter, workflowId, timeout, cancellationToken);
    }

    /// <summary>
    /// Exercises <paramref name="choice"/> and returns every created
    /// <typeparamref name="TTemplate"/> contract id in transaction order, throwing on
    /// error. The returned list may be empty when the choice created no
    /// <typeparamref name="TTemplate"/>. Throws <see cref="LedgerOperationException"/> on
    /// a Daml or infrastructure error, or when a non-conforming writer yields a
    /// <c>None</c> / <c>Many</c> outcome. For structured handling or a per-call deadline,
    /// use
    /// <see cref="TryCreateManyByExerciseAsync{TTemplate}(ILedgerWriter,ExerciseCommand,SubmitterInfo,string?,TimeSpan?,CancellationToken)"/>.
    /// </summary>
    public static async Task<IReadOnlyList<ContractId<TTemplate>>> CreateManyByExerciseAsync<TTemplate>(
        this ILedgerWriter writer,
        ExerciseCommand choice,
        SubmitterInfo submitter,
        string? workflowId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TTemplate : IDamlType
    {
        var outcome = await writer
            .TryCreateManyByExerciseAsync<TTemplate>(choice, submitter, workflowId, timeout, cancellationToken)
            .ConfigureAwait(false);

        return outcome switch
        {
            ExerciseOutcome<IReadOnlyList<ContractId<TTemplate>>>.One one => one.Result,
            ExerciseOutcome<IReadOnlyList<ContractId<TTemplate>>>.None => throw new LedgerOperationException(
                $"Exercising the choice yielded no result (None) for {typeof(TTemplate).Name}; a conforming writer returns One carrying every created contract."),
            ExerciseOutcome<IReadOnlyList<ContractId<TTemplate>>>.Many many => throw new LedgerOperationException(
                $"Exercising the choice yielded Many ({many.Count}) for {typeof(TTemplate).Name}; a conforming writer returns One carrying every created contract."),
            ExerciseOutcome<IReadOnlyList<ContractId<TTemplate>>>.DamlError e => throw e.ToException(),
            ExerciseOutcome<IReadOnlyList<ContractId<TTemplate>>>.InfraError e => e.ThrowAsCancellationOrException(cancellationToken),
            _ => throw new UnreachableException($"Unexpected outcome {outcome.GetType().Name} from TryCreateManyByExerciseAsync."),
        };
    }

    /// <summary>
    /// Exercises <paramref name="choice"/> as a single party and returns every created
    /// <typeparamref name="TTemplate"/> contract id, throwing on error. See
    /// <see cref="CreateManyByExerciseAsync{TTemplate}(ILedgerWriter,ExerciseCommand,SubmitterInfo,string?,TimeSpan?,CancellationToken)"/>.
    /// </summary>
    public static Task<IReadOnlyList<ContractId<TTemplate>>> CreateManyByExerciseAsync<TTemplate>(
        this ILedgerWriter writer,
        ExerciseCommand choice,
        Party actAs,
        string? workflowId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TTemplate : IDamlType
    {
        SubmitterInfo submitter = actAs;
        return writer.CreateManyByExerciseAsync<TTemplate>(choice, submitter, workflowId, timeout, cancellationToken);
    }

    private static ExerciseOutcome<ContractId<TTemplate>> ProjectSingleCreated<TTemplate>(TransactionResult result)
        where TTemplate : IDamlType
    {
        var created = result.All<TTemplate>();
        return created.Count switch
        {
            1 => new ExerciseOutcome<ContractId<TTemplate>>.One(created[0]),
            0 => new ExerciseOutcome<ContractId<TTemplate>>.None(),
            _ => new ExerciseOutcome<ContractId<TTemplate>>.Many(created.Count, [.. created.Select(id => id.Value)]),
        };
    }
}
