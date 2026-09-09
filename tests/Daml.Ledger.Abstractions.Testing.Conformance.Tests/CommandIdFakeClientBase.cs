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
/// The inert surface shared by the command-id fakes: every member the command-id conformance
/// checks never touch throws, and the single slot holding the <c>command_id</c> a submission
/// recorded. Each subclass writes its own <see cref="TryExerciseAsync"/> and
/// <see cref="TryCreateAsync"/> in full, so no command-id behaviour is shared between the
/// conforming fake and the deliberately-buggy ones.
/// </summary>
internal abstract class CommandIdFakeClientBase : NotSupportedLedgerClient
{
    public string? RecordedCommandId { get; protected set; }

    public abstract override Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(
        ExerciseCommand command, SubmitterInfo submitter, string? workflowId = null,
        CommandId? commandId = null,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default);

    public abstract override Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
        TTemplate payload, SubmitterInfo submitter, string? workflowId = null,
        CommandId? commandId = null,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default);
}
