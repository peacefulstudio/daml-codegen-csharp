// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Daml.Runtime.Commands;
using Daml.Runtime.Outcomes;

namespace Daml.Ledger.Abstractions.Extensions;

internal static class WriterExtensionHelpers
{
    internal static CommandsSubmission WithOptionalWorkflowId(this CommandsSubmission submission, string? workflowId) =>
        workflowId is null ? submission : submission.WithWorkflowId(new WorkflowId(workflowId));

    internal static LedgerOperationException ToException<T>(this ExerciseOutcome<T>.DamlError error) =>
        new($"Daml error [{error.Category}/{error.ErrorId}]: {error.Message}",
            error.Category, error.ErrorId, error.Metadata);

    internal static LedgerOperationException ToException<T>(this ExerciseOutcome<T>.InfraError error) =>
        new($"Infrastructure error [{error.StatusCode}]: {error.Message}", error.StatusCode, error.SourceException);

    [DoesNotReturn]
    internal static T ThrowAsCancellationOrException<T>(this ExerciseOutcome<T>.InfraError error, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw error.ToException();
    }
}
