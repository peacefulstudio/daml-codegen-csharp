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
/// Mints a fresh command id on every submission, discarding the caller-supplied one — the bug
/// that silently breaks deduplication across a retry that deliberately reuses an id. Used to
/// prove the verbatim checks actually fail against it. It still always records a non-empty id,
/// so it passes the mint checks: the verbatim checks are what single it out.
/// </summary>
internal sealed class SuppliedCommandIdIgnoringFakeClient : CommandIdFakeClientBase
{
    internal const string MintedPrefix = "minted-over-supplied-";

    public override Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(
        ExerciseCommand command, SubmitterInfo submitter, string? workflowId = null,
        CommandId? commandId = null,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        RecordedCommandId = Mint();
        return Task.FromResult<ExerciseOutcome<TResult>>(new ExerciseOutcome<TResult>.One(default!));
    }

    public override Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
        TTemplate payload, SubmitterInfo submitter, string? workflowId = null,
        CommandId? commandId = null,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        RecordedCommandId = Mint();
        return Task.FromResult<ExerciseOutcome<ContractId<TTemplate>>>(
            new ExerciseOutcome<ContractId<TTemplate>>.One(new ContractId<TTemplate>("created-1")));
    }

    private static string Mint() => $"{MintedPrefix}{Guid.NewGuid()}";
}
