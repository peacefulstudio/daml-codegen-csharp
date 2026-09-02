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
/// <param name="MinLedgerTime">
/// Optional earliest ledger effective time for this submission — the participant must not
/// commit it before the bound. <see langword="null"/> preserves today's behaviour (no bound:
/// the participant assigns the ledger time itself).
/// </param>
public sealed record CommandsSubmission(
    IReadOnlyList<ICommand> Commands,
    WorkflowId? WorkflowId = null,
    CommandId? CommandId = null,
    IReadOnlyList<Party>? ActAs = null,
    IReadOnlyList<Party>? ReadAs = null,
    SynchronizerId? SynchronizerId = null,
    IReadOnlyList<DisclosedContract>? DisclosedContracts = null,
    MinLedgerTime? MinLedgerTime = null)
{
    /// <summary>
    /// Creates a submission with a single command.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Naming",
        "CA1720:Identifier contains type name",
        Justification = "Single and Multiple name the command cardinality of the submission, paired as a factory vocabulary; neither refers to System.Single.")]
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
    /// Adds a workflow ID to this submission when <paramref name="workflowId"/> carries one,
    /// and returns the submission unchanged when it is <c>null</c>, empty or whitespace-only.
    /// A blank workflow id is treated as absent rather than stored, because <c>workflow_id</c>
    /// is a correlation key and a blank one correlates nothing — which is as true of a
    /// whitespace-only value as it is of an empty one.
    /// </summary>
    /// <remarks>
    /// Only this convenience overload decides that blank means absent.
    /// <see cref="WorkflowId"/> itself stays permissive — its constructor accepts empty and
    /// whitespace because the Ledger API puts no non-empty constraint on <c>workflow_id</c> —
    /// so a caller who genuinely wants to send a blank one still can, by saying
    /// <c>WithWorkflowId(new WorkflowId(" "))</c> explicitly.
    /// </remarks>
    /// <param name="workflowId">The workflow id, or <c>null</c>/blank to leave the submission unchanged.</param>
    public CommandsSubmission WithOptionalWorkflowId(string? workflowId) =>
        string.IsNullOrWhiteSpace(workflowId) ? this : WithWorkflowId(new WorkflowId(workflowId));

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
    /// Sets the earliest ledger effective time this submission may be assigned. Passing
    /// <see langword="null"/> clears the bound, leaving the ledger time to the participant.
    /// </summary>
    /// <param name="minLedgerTime">
    /// The bound — <see cref="Commands.MinLedgerTime.Absolute"/> or
    /// <see cref="Commands.MinLedgerTime.Relative"/> — or <see langword="null"/> to impose none.
    /// </param>
    public CommandsSubmission WithMinLedgerTime(MinLedgerTime? minLedgerTime) =>
        this with { MinLedgerTime = minLedgerTime };

    /// <summary>
    /// Applies a <see cref="SubmitterInfo"/> — sets both <see cref="ActAs"/> and
    /// <see cref="ReadAs"/> from the submitter's party sets in a single call. The
    /// preferred way for code-generated and library callers to project a typed
    /// submitter onto a submission; preserves the property that the wire format
    /// reflects exactly the parties carried by <paramref name="submitter"/>.
    /// </summary>
    /// <remarks>
    /// Both projections are overwritten, so a submitter carrying no <c>readAs</c> parties clears
    /// any <see cref="ReadAs"/> already on the submission.
    /// </remarks>
    public CommandsSubmission WithSubmitter(SubmitterInfo submitter)
    {
        var withActAs = this with { ActAs = [.. submitter.ActAs] };
        return submitter.ReadAs.Count == 0
            ? withActAs with { ReadAs = null }
            : withActAs with { ReadAs = [.. submitter.ReadAs] };
    }
}
