// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Threading;
using System.Threading.Tasks;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Outcomes;

namespace Daml.Ledger.Abstractions.Testing.Conformance.Tests;

/// <summary>
/// Forwards the caller-supplied command id but mints nothing when the caller omits one, leaving
/// the participant's <c>command_id</c> unset — the bug that yields a submission which can neither
/// be deduplicated nor correlated to its completion. Used to prove the mint checks actually fail
/// against it. It records the supplied id verbatim, so it passes the verbatim checks: the mint
/// checks are what single it out.
/// </summary>
internal sealed class UnsetCommandIdFakeClient : CommandIdFakeClientBase
{
    public override Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(
        ExerciseCommand command, SubmitterInfo submitter, string? workflowId = null,
        CommandId? commandId = null,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        RecordedCommandId = commandId?.Value;
        return Task.FromResult<ExerciseOutcome<TResult>>(new ExerciseOutcome<TResult>.One(default!));
    }

    public override Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
        TTemplate payload, SubmitterInfo submitter, string? workflowId = null,
        CommandId? commandId = null,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        RecordedCommandId = commandId?.Value;
        return Task.FromResult<ExerciseOutcome<ContractId<TTemplate>>>(
            new ExerciseOutcome<ContractId<TTemplate>>.One(new ContractId<TTemplate>("created-1")));
    }
}
