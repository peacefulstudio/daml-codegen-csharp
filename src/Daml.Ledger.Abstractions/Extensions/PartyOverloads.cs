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

namespace Daml.Ledger.Abstractions.Extensions;

/// <summary>Single-<see cref="Party"/> convenience overloads over the ledger capabilities.</summary>
public static class PartyOverloads
{
    /// <summary>Exercises a choice acting as a single party.</summary>
    public static Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(
        this ILedgerWriter writer,
        ExerciseCommand command,
        Party actAs,
        string? workflowId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(writer);
        SubmitterInfo submitter = actAs;
        return writer.TryExerciseAsync<TResult>(command, submitter, workflowId, timeout, cancellationToken);
    }

    /// <summary>Creates a contract acting as a single party.</summary>
    public static Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
        this ILedgerWriter writer,
        TTemplate payload,
        Party actAs,
        string? workflowId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TTemplate : ITemplate
    {
        ArgumentNullException.ThrowIfNull(writer);
        SubmitterInfo submitter = actAs;
        return writer.TryCreateAsync<TTemplate>(payload, submitter, workflowId, timeout, cancellationToken);
    }

    /// <summary>Subscribes to contract events acting as a single party.</summary>
    public static IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
        this ILedgerStreamer streamer,
        Party actAs,
        LedgerOffset? fromOffset = null,
        LedgerOffset? toOffset = null,
        CancellationToken cancellationToken = default)
        where T : IDamlType
    {
        ArgumentNullException.ThrowIfNull(streamer);
        SubmitterInfo submitter = actAs;
        return streamer.SubscribeAsync<T>(submitter, fromOffset, toOffset, cancellationToken);
    }

    /// <summary>Subscribes to the ACS snapshot acting as a single party.</summary>
    public static IAsyncEnumerable<AcsSnapshotEntry<T>> SubscribeActiveAsync<T>(
        this ILedgerStreamer streamer,
        Party actAs,
        LedgerOffset? activeAtOffset = null,
        CancellationToken cancellationToken = default)
        where T : IDamlType
    {
        ArgumentNullException.ThrowIfNull(streamer);
        SubmitterInfo submitter = actAs;
        return streamer.SubscribeActiveAsync<T>(submitter, activeAtOffset, cancellationToken);
    }
}
