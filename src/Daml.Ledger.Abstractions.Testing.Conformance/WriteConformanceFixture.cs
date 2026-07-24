// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Threading.Tasks;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;

namespace Daml.Ledger.Abstractions.Testing.Conformance;

/// <summary>
/// A write-capable client and a submission it accepts, used to prove that an
/// <see cref="ILedgerWriter.SubmitAndWaitAsync"/> / <see cref="ILedgerWriter.TrySubmitAndWaitForTransactionAsync"/>
/// implementation applies the <c>submitter</c> parameter authoritatively — via
/// <see cref="CommandsSubmission.WithSubmitter(SubmitterInfo)"/>, overwriting any
/// <see cref="CommandsSubmission.ActAs"/> already set on the submission — instead of
/// dispatching whatever <see cref="CommandsSubmission.ActAs"/> the caller pre-set.
/// </summary>
/// <param name="Client">
/// A fresh client, seeded so that <paramref name="Submission"/> succeeds when dispatched
/// with <paramref name="Authorized"/> as the submitter and is rejected when dispatched
/// with <paramref name="Unauthorized"/> instead.
/// </param>
/// <param name="Submission">
/// A submission the seeded <paramref name="Client"/> accepts from <paramref name="Authorized"/>
/// and rejects from <paramref name="Unauthorized"/>. The conformance checks set its
/// <see cref="CommandsSubmission.ActAs"/> to <paramref name="Unauthorized"/> before
/// dispatching with submitter <paramref name="Authorized"/>, to prove the submitter
/// parameter wins over whatever was pre-set.
/// </param>
/// <param name="Authorized">The party the seeded client accepts as the acting party.</param>
/// <param name="Unauthorized">
/// A party distinct from <paramref name="Authorized"/> that the seeded client rejects.
/// </param>
public sealed record WriteConformanceFixture(
    ILedgerClient Client,
    CommandsSubmission Submission,
    Party Authorized,
    Party Unauthorized) : IAsyncDisposable
{
    /// <summary>Delegates to <see cref="Client"/> today; gives an adopter that seeds
    /// supplementary resources alongside the client (e.g. a pre-created contract handle)
    /// a place to add their cleanup later.</summary>
    public ValueTask DisposeAsync() => Client.DisposeAsync();
}
