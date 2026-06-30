// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;

namespace Daml.Codegen.Testing.Conformance.Tests;

internal sealed class FakeLedgerClient : ILedgerClient
{
    private readonly Func<CommandsSubmission, ExerciseOutcome<TransactionResult>> _transaction;
    private readonly Func<object, ExerciseOutcome<object>>? _create;

    public CommandsSubmission? LastSubmission { get; private set; }

    public FakeLedgerClient(
        Func<CommandsSubmission, ExerciseOutcome<TransactionResult>>? transaction = null,
        Func<object, ExerciseOutcome<object>>? create = null)
    {
        _transaction = transaction ?? (_ => new ExerciseOutcome<TransactionResult>.InfraError(0, "unset"));
        _create = create;
    }

    public Task<ExerciseOutcome<TransactionResult>> TrySubmitAndWaitForTransactionAsync(
        CommandsSubmission submission,
        CancellationToken cancellationToken = default)
    {
        LastSubmission = submission;
        return Task.FromResult(_transaction(submission));
    }

    public Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
        TTemplate payload,
        SubmitterInfo submitter,
        string? workflowId = null,
        CancellationToken cancellationToken = default)
        where TTemplate : ITemplate
    {
        var projected = _create is null
            ? new ExerciseOutcome<ContractId<TTemplate>>.InfraError(0, "unset")
            : Project<TTemplate>(_create(payload!));
        return Task.FromResult(projected);
    }

    private static ExerciseOutcome<ContractId<TTemplate>> Project<TTemplate>(ExerciseOutcome<object> outcome)
        where TTemplate : ITemplate =>
        outcome switch
        {
            ExerciseOutcome<object>.One one =>
                new ExerciseOutcome<ContractId<TTemplate>>.One(new ContractId<TTemplate>((string)one.Result)),
            ExerciseOutcome<object>.DamlError e =>
                new ExerciseOutcome<ContractId<TTemplate>>.DamlError(e.Category, e.ErrorId, e.Message, e.Metadata),
            ExerciseOutcome<object>.InfraError e =>
                new ExerciseOutcome<ContractId<TTemplate>>.InfraError(e.StatusCode, e.Message),
            _ => new ExerciseOutcome<ContractId<TTemplate>>.None(),
        };

    public Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(
        ExerciseCommand command,
        SubmitterInfo submitter,
        string? workflowId = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<string> SubmitAndWaitAsync(
        CommandsSubmission submission,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ExerciseOutcome<ContractId<TTemplate>>> TryExerciseForCreatedAsync<TTemplate>(
        ExerciseCommand command,
        SubmitterInfo submitter,
        string? workflowId = null,
        CancellationToken cancellationToken = default)
        where TTemplate : IDamlType =>
        throw new NotSupportedException();

    public Task<long> GetLedgerEndAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
        SubmitterInfo submitter,
        long? fromOffset = null,
        CancellationToken cancellationToken = default)
        where T : IDamlType =>
        throw new NotSupportedException();

    public IAsyncEnumerable<ContractStreamEvent<T>.Created> SubscribeActiveAsync<T>(
        SubmitterInfo submitter,
        CancellationToken cancellationToken = default)
        where T : IDamlType =>
        throw new NotSupportedException();

    public void Dispose()
    {
    }
}
