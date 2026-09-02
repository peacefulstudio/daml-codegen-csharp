// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Ledger.Abstractions;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Splice.Api.Token.Allocation.V2;
using Splice.Api.Token.Holding.V2;
using Splice.Api.Token.Metadata.V1;
using Splice.Api.Token.Transfer.Instruction.V2;

Console.WriteLine("Splice Token Standard V2 - Offline Showcase");
Console.WriteLine("===========================================\n");

var alice = new Party("Alice::1220deadbeef");

var noExtraArgs = new ExtraArgs(
    new ChoiceContext(new Dictionary<string, AnyValue>()),
    new Metadata(new Dictionary<string, string>()));

Console.WriteLine("1. ACS filter over the IHolding (V2) interface:");
Console.WriteLine(
    $"   ACS query ready: {nameof(ILedgerStreamer)}.{nameof(ILedgerStreamer.SubscribeActiveAsync)}({nameof(IHolding)}.View)"
    + $" → IAsyncEnumerable<{nameof(InterfaceAcsSnapshotEntry<IHolding, HoldingView>)}<{nameof(IHolding)}, {nameof(HoldingView)}>>\n");

Console.WriteLine("2. Accepting a TransferInstruction (V2) via its typed interface choice:");
var transferId = new ContractId<ITransferInstruction>("00transferinstruction");
var acceptArg = new TransferInstruction_Accept(
    Actors: new[] { alice },
    ExtraArgs: noExtraArgs).ToRecord();
var transfer = ExerciseCommand.ForInterface<ITransferInstruction>(
    transferId,
    new ChoiceName(nameof(TransferInstruction_Accept)),
    acceptArg);
Console.WriteLine($"   interface id on the wire: {transfer.TemplateId.FullyQualifiedName}");
Console.WriteLine($"   choice: {transfer.Choice}\n");

Console.WriteLine("3. Withdrawing an Allocation (V2) via its typed interface choice:");
var allocationId = new ContractId<IAllocation>("00allocation");
var withdrawArg = new Allocation_Withdraw(
    Actors: new[] { alice },
    ExtraArgs: noExtraArgs).ToRecord();
var allocation = ExerciseCommand.ForInterface<IAllocation>(
    allocationId,
    new ChoiceName(nameof(Allocation_Withdraw)),
    withdrawArg);
Console.WriteLine($"   interface id on the wire: {allocation.TemplateId.FullyQualifiedName}");
Console.WriteLine($"   choice: {allocation.Choice}\n");

Console.WriteLine("4. Assembling a single-command submission from the transfer accept (unsent):");
var submission = CommandsSubmission.Single(transfer)
    .WithActAs(alice)
    .WithCommandId(new CommandId(Guid.NewGuid().ToString()));
Console.WriteLine($"   commands: {submission.Commands.Count}, actAs: {string.Join(", ", submission.ActAs ?? [])}\n");

Console.WriteLine("Done. Against a live ledger these commands submit through an ILedgerWriter,");
Console.WriteLine("and active IHolding contracts stream through ILedgerStreamer.SubscribeActiveAsync(IHolding.View).");
