// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;

namespace Daml.Runtime.Commands;

/// <summary>
/// Represents a submission of commands to the ledger.
/// </summary>
/// <param name="Commands">The commands to submit.</param>
/// <param name="WorkflowId">Optional workflow identifier for correlation.</param>
/// <param name="CommandId">Unique command identifier for deduplication.</param>
/// <param name="ActAs">Parties to act as when submitting.</param>
/// <param name="ReadAs">Parties whose contracts are visible.</param>
/// <param name="SynchronizerId">Optional synchronizer to pin the submission to.</param>
/// <param name="DisclosedContracts">
/// Optional contracts explicitly disclosed alongside this submission, for parties
/// that don't natively see them. <see langword="null"/> preserves today's behaviour
/// (no explicit disclosure).
/// </param>
public sealed record CommandsSubmission(
    IReadOnlyList<ICommand> Commands,
    WorkflowId? WorkflowId = null,
    CommandId? CommandId = null,
    IReadOnlyList<Party>? ActAs = null,
    IReadOnlyList<Party>? ReadAs = null,
    SynchronizerId? SynchronizerId = null,
    IReadOnlyList<DisclosedContract>? DisclosedContracts = null)
{
    /// <summary>
    /// Creates a submission with a single command.
    /// </summary>
    public static CommandsSubmission Single(ICommand command, Party? actAs = null) =>
        new([command], ActAs: actAs is not null ? [actAs.Value] : null);

    /// <summary>
    /// Creates a submission with multiple commands.
    /// </summary>
    public static CommandsSubmission Multiple(params ICommand[] commands) =>
        new(commands);

    /// <summary>
    /// Adds a workflow ID to this submission.
    /// </summary>
    public CommandsSubmission WithWorkflowId(WorkflowId workflowId) =>
        this with { WorkflowId = workflowId };

    /// <summary>
    /// Adds a command ID to this submission.
    /// </summary>
    public CommandsSubmission WithCommandId(CommandId commandId) =>
        this with { CommandId = commandId };

    /// <summary>
    /// Adds a synchronizer ID to this submission.
    /// </summary>
    public CommandsSubmission WithSynchronizerId(SynchronizerId synchronizerId) =>
        this with { SynchronizerId = synchronizerId };

    /// <summary>
    /// Sets the parties to act as.
    /// </summary>
    public CommandsSubmission WithActAs(params Party[] parties) =>
        this with { ActAs = parties };

    /// <summary>
    /// Sets the parties to read as.
    /// </summary>
    public CommandsSubmission WithReadAs(params Party[] parties) =>
        this with { ReadAs = parties };

    /// <summary>
    /// Sets the contracts to explicitly disclose alongside this submission.
    /// Passing no contracts, <see langword="null"/>, or an empty array clears
    /// the field back to <see langword="null"/>.
    /// </summary>
    public CommandsSubmission WithDisclosedContracts(params DisclosedContract[]? disclosedContracts) =>
        this with { DisclosedContracts = disclosedContracts is { Length: > 0 } ? disclosedContracts : null };

    /// <summary>
    /// Applies a <see cref="SubmitterInfo"/> — sets both <see cref="ActAs"/> and
    /// <see cref="ReadAs"/> from the submitter's party sets in a single call. The
    /// preferred way for code-generated and library callers to project a typed
    /// submitter onto a submission; preserves the property that the wire format
    /// reflects exactly the parties carried by <paramref name="submitter"/>.
    /// </summary>
    public CommandsSubmission WithSubmitter(SubmitterInfo submitter)
    {
        var withActAs = this with { ActAs = [.. submitter.ActAs] };
        // When the submitter carries no readAs parties, clear any pre-existing
        // ReadAs on the submission so the wire shape reflects exactly the
        // parties carried by the submitter — both projections fully overwritten.
        return submitter.ReadAs.Count == 0
            ? withActAs with { ReadAs = null }
            : withActAs with { ReadAs = [.. submitter.ReadAs] };
    }
}
