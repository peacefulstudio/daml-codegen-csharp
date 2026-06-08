// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

// Example demonstrating how generated Daml C# code would be used.
// This shows what the codegen produces and how to interact with contracts.

using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;

namespace QuickstartExample;

// This is an example of what the codegen would generate for a simple Iou template:
//
// template Iou
//   with
//     issuer: Party
//     owner: Party
//     currency: Text
//     amount: Decimal
//   where
//     signatory issuer
//     choice Transfer: ContractId Iou
//       with
//         newOwner: Party
//       controller owner
//       do create this with owner = newOwner

/// <summary>
/// Generated from Daml template Main:Iou
/// </summary>
public sealed record Iou(
    Party Issuer,
    Party Owner,
    string Currency,
    decimal Amount) : ITemplate
{
    /// <summary>Gets the template identifier.</summary>
    public static Identifier TemplateId { get; } = new("abc123", "Main", "Iou");

    /// <summary>Gets the package ID.</summary>
    public static string PackageId => "abc123";

    /// <summary>Gets the package name.</summary>
    public static string PackageName => "quickstart";

    /// <summary>Gets the package version.</summary>
    public static Version PackageVersion { get; } = new(0, 0, 1);

    /// <summary>Converts this template to a DamlRecord.</summary>
    public DamlRecord ToRecord()
    {
        return DamlRecord.Create(
            DamlField.Create("issuer", Issuer.ToDamlValue()),
            DamlField.Create("owner", Owner.ToDamlValue()),
            DamlField.Create("currency", new DamlText(Currency)),
            DamlField.Create("amount", new DamlNumeric(Amount))
        );
    }

    /// <summary>Creates an instance from a DamlRecord.</summary>
    public static Iou FromRecord(DamlRecord record)
    {
        return new Iou(
            Issuer: Party.FromDamlValue(record.GetRequiredField("issuer").As<DamlParty>()),
            Owner: Party.FromDamlValue(record.GetRequiredField("owner").As<DamlParty>()),
            Currency: record.GetRequiredField("currency").As<DamlText>().Value,
            Amount: record.GetRequiredField("amount").As<DamlNumeric>().Value
        );
    }

    /// <summary>Arguments for the Transfer choice.</summary>
    public sealed record TransferArgument(Party NewOwner)
    {
        public DamlValue ToValue() => DamlRecord.Create(
            DamlField.Create("newOwner", NewOwner.ToDamlValue())
        );
    }

    /// <summary>Contract ID for Iou.</summary>
    public sealed record IouContractId(string Value) : ContractId<Iou>(Value), IExercises<Iou>
    {
        ContractId<Iou> IExercises<Iou>.ContractId => this;

        /// <summary>Exercise the Transfer choice.</summary>
        public ExerciseCommand ExerciseTransfer(TransferArgument arg) =>
            ExerciseCommand.For(this, new ChoiceName("Transfer"), arg.ToValue());

        /// <summary>Exercise the Transfer choice.</summary>
        public ExerciseCommand ExerciseTransfer(Party newOwner) =>
            ExerciseTransfer(new TransferArgument(newOwner));
    }

    /// <summary>Active contract helpers for Iou.</summary>
    public static class IouContract
    {
        /// <summary>Creates a Contract from a CreatedEvent.</summary>
        public static Contract<Iou> FromCreatedEvent(CreatedEvent @event) =>
            Contract<Iou>.FromCreatedEvent(@event, Iou.FromRecord);
    }
}

class Program
{
    static void Main()
    {
        Console.WriteLine("Daml C# Codegen - Quickstart Example");
        Console.WriteLine("=====================================\n");

        // Example 1: Create an Iou contract
        Console.WriteLine("1. Creating an Iou contract:");
        var iou = new Iou(
            Issuer: new Party("Alice"),
            Owner: new Party("Bob"),
            Currency: "USD",
            Amount: 1000.00m
        );
        Console.WriteLine($"   Issuer: {iou.Issuer}");
        Console.WriteLine($"   Owner: {iou.Owner}");
        Console.WriteLine($"   Currency: {iou.Currency}");
        Console.WriteLine($"   Amount: {iou.Amount:C}\n");

        // Example 2: Convert to DamlRecord for Ledger API
        Console.WriteLine("2. Converting to DamlRecord:");
        var record = iou.ToRecord();
        Console.WriteLine($"   Fields: {record.Fields.Count}");
        foreach (var field in record.Fields)
        {
            Console.WriteLine($"   - {field.Label}: {field.Value}");
        }
        Console.WriteLine();

        // Example 3: Serialize to JSON
        Console.WriteLine("3. Serializing to JSON:");
        var json = DamlJsonSerializer.Serialize(record);
        Console.WriteLine($"   {json}\n");

        // Example 4: Create a CreateCommand
        Console.WriteLine("4. Building a CreateCommand:");
        var createCmd = CreateCommand.For(iou);
        Console.WriteLine($"   Template: {createCmd.TemplateId.FullyQualifiedName}");
        Console.WriteLine($"   Type: {createCmd.CommandType}\n");

        // Example 5: Create an ExerciseCommand (Transfer)
        Console.WriteLine("5. Building an ExerciseCommand (Transfer):");
        var contractId = new Iou.IouContractId("00abc123");
        var exerciseCmd = contractId.ExerciseTransfer(newOwner: new Party("Charlie"));
        Console.WriteLine($"   Contract: {exerciseCmd.ContractId}");
        Console.WriteLine($"   Choice: {exerciseCmd.Choice}");
        Console.WriteLine($"   Type: {exerciseCmd.CommandType}\n");

        // Example 6: Build a command submission
        Console.WriteLine("6. Building a command submission:");
        var submission = CommandsSubmission.Single(createCmd)
            .WithActAs(new Party("Alice"))
            .WithWorkflowId(new WorkflowId("iou-issuance"))
            .WithCommandId(new CommandId(Guid.NewGuid().ToString()));
        Console.WriteLine($"   Commands: {submission.Commands.Count}");
        Console.WriteLine($"   ActAs: {string.Join(", ", submission.ActAs ?? [])}");
        Console.WriteLine($"   WorkflowId: {submission.WorkflowId}");
        Console.WriteLine($"   CommandId: {submission.CommandId}\n");

        // Example 7: Reconstruct from record (simulating Ledger API response)
        Console.WriteLine("7. Reconstructing from DamlRecord:");
        var reconstructed = Iou.FromRecord(record);
        Console.WriteLine($"   Issuer: {reconstructed.Issuer}");
        Console.WriteLine($"   Owner: {reconstructed.Owner}");
        Console.WriteLine($"   Match: {iou == reconstructed}\n");

        Console.WriteLine("Done!");
    }
}
