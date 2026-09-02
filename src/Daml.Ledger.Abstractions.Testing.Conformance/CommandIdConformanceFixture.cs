// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Threading;
using System.Threading.Tasks;
using Daml.Runtime.Commands;

namespace Daml.Ledger.Abstractions.Testing.Conformance;

/// <summary>
/// A write-capable client and the two single-command submissions it accepts, used to prove that an
/// <see cref="ILedgerWriter.TryExerciseAsync{TResult}(ExerciseCommand, SubmitterInfo, string?, CommandId?, TimeSpan?, CancellationToken)"/> /
/// <see cref="ILedgerWriter.TryCreateAsync{TTemplate}(TTemplate, SubmitterInfo, string?, CommandId?, TimeSpan?, CancellationToken)"/>
/// implementation honors the deduplication contract both document: the <c>commandId</c> argument
/// reaches the participant verbatim, and a fresh id is minted only when the caller omits it —
/// never leaving the participant's <c>command_id</c> unset.
/// </summary>
/// <param name="Client">
/// A fresh client that accepts both submissions below. Each conformance check takes its own
/// fixture and submits exactly once against it, so the id read back by
/// <paramref name="ReadRecordedCommandId"/> can only have come from that one submission — a
/// leftover id from an earlier check cannot masquerade as a minted one.
/// </param>
/// <param name="Exercise">
/// Submits one exercise the <paramref name="Client"/> accepts, forwarding the supplied
/// <see cref="CommandId"/> to the <c>commandId</c> parameter of
/// <see cref="ILedgerWriter.TryExerciseAsync{TResult}(ExerciseCommand, SubmitterInfo, string?, CommandId?, TimeSpan?, CancellationToken)"/>
/// verbatim — <c>null</c> included, since passing it through unchanged is what asks the
/// implementation to mint. Adopters bind the choice's result type here, so the kit need not guess
/// a <c>TResult</c> the transport can decode.
/// </param>
/// <param name="Create">
/// Submits one create the <paramref name="Client"/> accepts, forwarding the supplied
/// <see cref="CommandId"/> to the <c>commandId</c> parameter of
/// <see cref="ILedgerWriter.TryCreateAsync{TTemplate}(TTemplate, SubmitterInfo, string?, CommandId?, TimeSpan?, CancellationToken)"/>
/// verbatim, <c>null</c> included. Adopters bind the template type here.
/// </param>
/// <param name="ReadRecordedCommandId">
/// Reads back the <c>command_id</c> the participant recorded for the submission just dispatched,
/// or <c>null</c> when it recorded none. A raw string rather than a <see cref="CommandId"/>: an
/// implementation that leaves the field unset is precisely what the mint check must catch, and
/// <see cref="CommandId"/> cannot represent that state — it rejects empty input at construction,
/// and a <c>default</c> instance still reads as present through a <see cref="Nullable{T}"/> while
/// throwing on <see cref="CommandId.Value"/>.
/// </param>
public sealed record CommandIdConformanceFixture(
    ILedgerClient Client,
    Func<ILedgerWriter, CommandId?, Task> Exercise,
    Func<ILedgerWriter, CommandId?, Task> Create,
    Func<ValueTask<string?>> ReadRecordedCommandId) : IAsyncDisposable
{
    /// <summary>Delegates to <see cref="Client"/> today; gives an adopter that seeds
    /// supplementary resources alongside the client (e.g. a pre-created contract handle)
    /// a place to add their cleanup later.</summary>
    public ValueTask DisposeAsync() => Client.DisposeAsync();
}
