// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;

namespace Daml.Ledger.Abstractions.Testing.Conformance.Tests;

/// <summary>
/// A write-only fake whose <see cref="SubmitAndWaitAsync"/> and
/// <see cref="TrySubmitAndWaitForTransactionAsync"/> accept a submission only when the
/// effective ActAs — after <see cref="CommandsSubmission.WithSubmitter"/> is applied —
/// contains <paramref name="authorized"/>. Proves the conformance kit's submitter-authority
/// checks pass against a client that honors the contract correctly. Read methods throw:
/// <see cref="WriteConformanceFixture"/>-driven tests never call them.
/// </summary>
internal sealed class AuthorizingFakeClient(Party authorized) : ILedgerClient
{
    public Task<SubmitAndWaitResult> SubmitAndWaitAsync(
        CommandsSubmission submission, SubmitterInfo submitter, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        RequireAuthorized(submission, submitter);
        return Task.FromResult(new SubmitAndWaitResult(new CommandId("cmd-1"), "update-1", LedgerOffset.At(1)));
    }

    public Task<ExerciseOutcome<TransactionResult>> TrySubmitAndWaitForTransactionAsync(
        CommandsSubmission submission, SubmitterInfo submitter, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsAuthorized(submission, submitter))
        {
            return Task.FromResult<ExerciseOutcome<TransactionResult>>(new ExerciseOutcome<TransactionResult>.DamlError(
                DamlErrorCategory.AuthorizationChecksFailed, "UNAUTHORIZED",
                $"party {authorized.Id} did not authorize this submission", new Dictionary<string, string>()));
        }

        return Task.FromResult<ExerciseOutcome<TransactionResult>>(new ExerciseOutcome<TransactionResult>.One(
            new TransactionResult("update-1", LedgerOffset.At(1), [], [], new CommandId("cmd-1"))));
    }

    private bool IsAuthorized(CommandsSubmission submission, SubmitterInfo submitter) =>
        (submission.WithSubmitter(submitter).ActAs ?? []).Contains(authorized);

    private void RequireAuthorized(CommandsSubmission submission, SubmitterInfo submitter)
    {
        if (!IsAuthorized(submission, submitter))
        {
            throw new LedgerOperationException(
                $"party {authorized.Id} did not authorize this submission",
                DamlErrorCategory.AuthorizationChecksFailed, "UNAUTHORIZED", new Dictionary<string, string>());
        }
    }

    public Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(
        ExerciseCommand command, SubmitterInfo submitter, string? workflowId = null,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
        TTemplate payload, SubmitterInfo submitter, string? workflowId = null,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        where TTemplate : ITemplate =>
        throw new NotSupportedException();

    public IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
        SubmitterInfo submitter, LedgerOffset? fromOffset = null, LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default)
        where T : IDamlType =>
        throw new NotSupportedException();

    public IAsyncEnumerable<ContractStreamEvent<T>> SubscribeLedgerEffectsAsync<T>(
        SubmitterInfo submitter, LedgerOffset? fromOffset = null, LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default)
        where T : IDamlType =>
        throw new NotSupportedException();

    public IAsyncEnumerable<AcsSnapshotEntry<T>> SubscribeActiveAsync<T>(
        SubmitterInfo submitter, LedgerOffset? activeAtOffset = null,
        CancellationToken cancellationToken = default)
        where T : IDamlType =>
        throw new NotSupportedException();

    public Task<LedgerOffset> GetLedgerEndAsync(
        TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public void Dispose()
    {
    }
}
