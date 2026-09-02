// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Outcomes;
using AwesomeAssertions;
using NSubstitute;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;
using Party = Daml.Runtime.Data.Party;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Invokes the emitted interface-choice exercisers for real, through reflection over a
/// compiled assembly, to pin where their argument-validation failures surface. An
/// interface choice has no typed result to project, so the exercisers hand back the
/// submission task instead of awaiting it — which moves a null-argument failure from
/// the returned task to the call site. The <c>Func&lt;Task&gt;</c> + <c>ThrowAsync</c>
/// idiom used elsewhere in the suite passes either way; <c>MethodInfo.Invoke</c> does
/// not, because it only wraps an exception the invoked method threw before returning.
/// </summary>
public class EmittedInterfaceChoiceExerciserInvocationTests
{
    private const string ChoiceMethodName = "TransferAsync";

    private static readonly Assembly Emitted = EmitToAssembly(GenerateCustodyInterfacePackage());

    [Fact]
    public void EmittedInterfaceChoiceExerciser_rejects_a_null_client_before_returning_a_task()
    {
        var exerciser = ChoiceExerciser();

        Action act = () => exerciser.Invoke(null, Arguments(null));

        var thrown = act.Should().Throw<TargetInvocationException>(
            "an exerciser that awaited its submission would be an async method, whose argument checks surface on the returned task rather than at the call site")
            .Which;
        thrown.InnerException.Should().BeOfType<ArgumentNullException>();
    }

    [Fact]
    public void EmittedInterfaceChoiceExerciser_hands_back_the_submission_task_for_valid_arguments()
    {
        var exerciser = ChoiceExerciser();
        var client = Substitute.For<ILedgerWriter>();
        client.TrySubmitAndWaitForTransactionAsync(
                Arg.Any<CommandsSubmission>(),
                Arg.Any<SubmitterInfo>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ExerciseOutcome<TransactionResult>>(
                new ExerciseOutcome<TransactionResult>.None()));

        var returned = exerciser.Invoke(null, Arguments(client));

        returned.Should().BeAssignableTo<Task<ExerciseOutcome<TransactionResult>>>(
            "only the argument checks run at the call site — a valid call still has to hand the caller the submission task, so an exerciser that threw unconditionally would satisfy the null-client pin while being useless");
    }

    private static MethodInfo ChoiceExerciser() =>
        EmittedType("ICustodyExtensions")
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == ChoiceMethodName
                         && m.GetParameters().Any(p => p.ParameterType == typeof(SubmitterInfo)));

    private static object?[] Arguments(ILedgerWriter? client) =>
    [
        TypedContractId(),
        client,
        Submitter(),
        null,
        null,
        null,
        CancellationToken.None,
    ];

    private static SubmitterInfo Submitter() => new Party("alice");

    private static object TypedContractId() =>
        Activator.CreateInstance(
            typeof(ContractId<>).MakeGenericType(EmittedType("ICustody")),
            "cid-1")!;

    private static Type EmittedType(string name) => Emitted.GetTypes().Single(t => t.Name == name);

    private static IReadOnlyList<GeneratedFile> GenerateCustodyInterfacePackage()
    {
        var fields = new[]
        {
            new DamlFieldDefinition("platform", new DamlPrimitiveType(DamlPrimitive.Party)),
            new DamlFieldDefinition("counterparty", new DamlPrimitiveType(DamlPrimitive.Party)),
        };

        var module = new DamlModule
        {
            Name = "Acme.Offers",
            Templates =
            [
                new DamlTemplate
                {
                    Name = "Offer",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Accept",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlTypeApp(
                                new DamlPrimitiveType(DamlPrimitive.ContractId),
                                [new DamlTypeRef("test-pkg", "Acme.Offers", "Offer")]),
                            Controllers = DamlPartyAnalysis.Static([new DamlPartyPayloadField("counterparty")]),
                            Observers = DamlPartyAnalysis.Static([]),
                        },
                    ],
                    Signatories = DamlPartyAnalysis.Static([new DamlPartyPayloadField("platform")]),
                    Observers = DamlPartyAnalysis.Static([]),
                }
            ],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Offer",
                    Definition = new DamlRecordDefinition(fields),
                }
            ],
            Interfaces =
            [
                new DamlInterface
                {
                    Name = "Custody",
                    Choices =
                    [
                        new DamlChoice
                        {
                            Name = "Transfer",
                            Consuming = true,
                            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            ReturnType = new DamlPrimitiveType(DamlPrimitive.Unit),
                            Controllers = DamlPartyAnalysis.Dynamic,
                            Observers = DamlPartyAnalysis.Dynamic,
                        },
                    ],
                }
            ],
        };

        var dar = new DarModel
        {
            MainPackage = new DamlPackage
            {
                PackageId = "test-pkg",
                Name = "test-package",
                Version = new Version(1, 0, 0),
                LfVersion = "2.1",
                Modules = [module],
                DependencyReferences = [],
            },
            Dependencies = [],
        };

        return CreateGenerator().Generate(dar);
    }
}
