// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Threading;
using System.Threading.Tasks;
using Daml.Runtime;

namespace Daml.Ledger.Abstractions;

/// <summary>The read capability: query participant ledger state.</summary>
public interface ILedgerReader
{
    /// <summary>Gets the current end of the participant's ledger.</summary>
    /// <param name="timeout">
    /// Optional per-call deadline, applied best-effort by the transport — see
    /// <see cref="ILedgerWriter.TryExerciseAsync{TResult}(Daml.Runtime.Commands.ExerciseCommand, Daml.Runtime.Commands.SubmitterInfo, string?, Daml.Runtime.Commands.CommandId?, TimeSpan?, CancellationToken)"/>
    /// for the deadline contract.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The current ledger-end offset on the participant.</returns>
    Task<LedgerOffset> GetLedgerEndAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}
