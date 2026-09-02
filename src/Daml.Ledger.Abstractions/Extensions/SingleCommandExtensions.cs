// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Threading;
using System.Threading.Tasks;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Outcomes;

namespace Daml.Ledger.Abstractions.Extensions;

/// <summary>
/// The single-command submission path shared by generated choice exercisers and by the
/// hand-written write-path extensions, so the submission a single command is wrapped in
/// is built in one place rather than at every call site.
/// </summary>
public static class SingleCommandExtensions
{
    /// <summary>
    /// Submits <paramref name="command"/> as a single-command transaction and returns the
    /// submission task, minting a command id when the caller supplies none so every
    /// submission carries one rather than leaving <c>command_id</c> unset, where
    /// deduplicability would depend on the transport.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// Thrown synchronously when <paramref name="writer"/> or <paramref name="command"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown synchronously when <paramref name="commandId"/> is a default (uninitialized) value.
    /// </exception>
    /// <param name="writer">The ledger writer.</param>
    /// <param name="command">The single command to submit.</param>
    /// <param name="submitter">The submitter party set (<c>actAs</c> + optional <c>readAs</c>).</param>
    /// <param name="workflowId">
    /// Optional workflow id; passed through to the ledger when non-empty. A <c>null</c> or empty
    /// value leaves <c>workflow_id</c> unset, because it is a correlation key and an empty one
    /// correlates nothing.
    /// </param>
    /// <param name="commandId">
    /// Optional command id for deduplication; a fresh id is minted only when omitted. Pass the
    /// same id across a retry of a lost-but-accepted submission so the ledger deduplicates the
    /// resubmission instead of re-executing the command. A minted id is not returned on a failed
    /// submission, so only a caller-supplied id makes an application-level retry deduplicable.
    /// </param>
    /// <param name="timeout">
    /// Optional per-call deadline, applied best-effort by the transport; transports without a
    /// server-side deadline apply a client-side bound only. <c>null</c> applies no deadline.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The outcome of the submission, carrying the resulting transaction on success.</returns>
    public static Task<ExerciseOutcome<TransactionResult>> TrySubmitSingleAsync(
        this ILedgerWriter writer,
        ICommand command,
        SubmitterInfo submitter,
        string? workflowId = null,
        CommandId? commandId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(command);
        WriterExtensionHelpers.ThrowIfDefault(commandId);

        var submission = CommandsSubmission.Single(command)
            .WithCommandId(commandId ?? new CommandId(Guid.NewGuid().ToString()))
            .WithOptionalWorkflowId(workflowId);

        return writer.TrySubmitAndWaitForTransactionAsync(submission, submitter, timeout, cancellationToken);
    }
}
