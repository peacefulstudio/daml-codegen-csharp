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
/// Records the caller-supplied <c>commandId</c> verbatim and mints one only when the caller
/// omits it — the contract as documented. Proves the command-id conformance checks pass against
/// a client that honors it, so a failure elsewhere is the fake's bug and not the check's.
/// </summary>
internal sealed class CommandIdHonoringFakeClient : CommandIdFakeClientBase
{
    public override Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(
        ExerciseCommand command, SubmitterInfo submitter, string? workflowId = null,
        CommandId? commandId = null,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        RecordedCommandId = commandId?.Value ?? Mint();
        return Task.FromResult<ExerciseOutcome<TResult>>(new ExerciseOutcome<TResult>.One(default!));
    }

    public override Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
        TTemplate payload, SubmitterInfo submitter, string? workflowId = null,
        CommandId? commandId = null,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        RecordedCommandId = commandId?.Value ?? Mint();
        return Task.FromResult<ExerciseOutcome<ContractId<TTemplate>>>(
            new ExerciseOutcome<ContractId<TTemplate>>.One(new ContractId<TTemplate>("created-1")));
    }

    private static string Mint() => $"minted-{Guid.NewGuid()}";
}
