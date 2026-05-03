// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using FluentAssertions;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Daml.Runtime.Tests;

public class TransactionResultTests
{
    [Fact]
    public void Single_returns_contract_id_when_exactly_one_match()
    {
        var result = MakeTransaction(("00alice", FooBar.TemplateId));

        var id = result.Single<FooBar>();

        id.Value.Should().Be("00alice");
    }

    [Fact]
    public void Single_throws_when_no_matching_contract()
    {
        var result = MakeTransaction(("00other", new RuntimeIdentifier("pkg", "Other", "Tpl")));

        Action act = () => result.Single<FooBar>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*no contracts of type FooBar*");
    }

    [Fact]
    public void Single_throws_when_multiple_matching_contracts()
    {
        var result = MakeTransaction(
            ("00a", FooBar.TemplateId),
            ("00b", FooBar.TemplateId));

        Action act = () => result.Single<FooBar>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*2 contracts*FooBar*expected exactly 1*");
    }

    [Fact]
    public void TrySingle_returns_null_when_no_matching_contract()
    {
        var result = MakeTransaction(("00other", new RuntimeIdentifier("pkg", "Other", "Tpl")));

        var id = result.TrySingle<FooBar>();

        id.Should().BeNull();
    }

    [Fact]
    public void TrySingle_returns_contract_id_when_exactly_one_match()
    {
        var result = MakeTransaction(("00alice", FooBar.TemplateId));

        var id = result.TrySingle<FooBar>();

        id.Should().NotBeNull();
        id!.Value.Should().Be("00alice");
    }

    [Fact]
    public void TrySingle_throws_when_multiple_matching_contracts()
    {
        var result = MakeTransaction(
            ("00a", FooBar.TemplateId),
            ("00b", FooBar.TemplateId));

        Action act = () => result.TrySingle<FooBar>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*2 contracts*FooBar*expected at most 1*");
    }

    [Fact]
    public void All_returns_empty_when_no_matching_contracts()
    {
        var result = MakeTransaction(("00other", new RuntimeIdentifier("pkg", "Other", "Tpl")));

        var ids = result.All<FooBar>();

        ids.Should().BeEmpty();
    }

    [Fact]
    public void All_returns_contract_ids_in_transaction_order()
    {
        var result = MakeTransaction(
            ("00first", FooBar.TemplateId),
            ("00other", new RuntimeIdentifier("pkg", "Other", "Tpl")),
            ("00second", FooBar.TemplateId));

        var ids = result.All<FooBar>();

        ids.Should().HaveCount(2);
        ids[0].Value.Should().Be("00first");
        ids[1].Value.Should().Be("00second");
    }

    [Fact]
    public void Match_helpers_ignore_package_id_difference()
    {
        // Same module/entity but a different package id (e.g. upgrade) should still match —
        // template upgrades change the package hash but keep the qualified name stable.
        var differentPackage = new RuntimeIdentifier("pkg-v2", FooBar.TemplateId.ModuleName, FooBar.TemplateId.EntityName);
        var result = MakeTransaction(("00upgraded", differentPackage));

        result.Single<FooBar>().Value.Should().Be("00upgraded");
    }

    [Fact]
    public void ExercisedEvents_defaults_to_empty_when_not_set()
    {
        // 0.1.4 callers used the 4-arg ctor; their TransactionResult instances must
        // continue to round-trip without supplying ExercisedEvents.
        var result = new TransactionResult(
            UpdateId: "u1",
            CompletionOffset: 1L,
            CreatedContracts: [],
            ArchivedContractIds: []);

        result.ExercisedEvents.Should().NotBeNull();
        result.ExercisedEvents.Should().BeEmpty();
    }

    [Fact]
    public void ExercisedEvents_round_trips_when_initialized()
    {
        var exerciseResult = new DamlNumeric(42.5m);
        var argument = new DamlText("ping");
        var exercised = new ExercisedEvent(
            ContractId: "00alice",
            TemplateId: FooBar.TemplateId,
            InterfaceId: null,
            ChoiceName: "GetTrailingTwap",
            ChoiceArgument: argument,
            ExerciseResult: exerciseResult,
            Consuming: false,
            ActingParties: [new Party("alice")],
            WitnessParties: [new Party("alice"), new Party("bob")]);

        var result = new TransactionResult(
            UpdateId: "u1",
            CompletionOffset: 1L,
            CreatedContracts: [],
            ArchivedContractIds: [])
        {
            ExercisedEvents = [exercised],
        };

        result.ExercisedEvents.Should().HaveCount(1);
        result.ExercisedEvents[0].ChoiceName.Should().Be("GetTrailingTwap");
        result.ExercisedEvents[0].ExerciseResult.Should().BeSameAs(exerciseResult);
        result.ExercisedEvents[0].ChoiceArgument.Should().BeSameAs(argument);
        result.ExercisedEvents[0].Consuming.Should().BeFalse();
        result.ExercisedEvents[0].InterfaceId.Should().BeNull();
        result.ExercisedEvents[0].ActingParties.Should().ContainSingle().Which.Id.Should().Be("alice");
    }

    [Fact]
    public void ExercisedEvents_with_expression_preserves_other_fields()
    {
        var original = new TransactionResult(
            UpdateId: "u1",
            CompletionOffset: 5L,
            CreatedContracts: [new CreatedContract("00a", FooBar.TemplateId, "{}")],
            ArchivedContractIds: ["00b"]);

        var exercised = new ExercisedEvent(
            ContractId: "00c",
            TemplateId: FooBar.TemplateId,
            InterfaceId: null,
            ChoiceName: "DoThing",
            ChoiceArgument: DamlUnit.Instance,
            ExerciseResult: DamlUnit.Instance,
            Consuming: true,
            ActingParties: [new Party("alice")],
            WitnessParties: [new Party("alice")]);

        var withEvents = original with { ExercisedEvents = [exercised] };

        withEvents.UpdateId.Should().Be("u1");
        withEvents.CompletionOffset.Should().Be(5L);
        withEvents.CreatedContracts.Should().HaveCount(1);
        withEvents.ArchivedContractIds.Should().ContainSingle().Which.Should().Be("00b");
        withEvents.ExercisedEvents.Should().ContainSingle();
        // Original is unmodified.
        original.ExercisedEvents.Should().BeEmpty();
    }

    [Fact]
    public void ExercisedEvent_supports_inherited_interface_choice()
    {
        var interfaceId = new RuntimeIdentifier("pkg", "Sample.Iface", "IThing");
        var exercised = new ExercisedEvent(
            ContractId: "00a",
            TemplateId: FooBar.TemplateId,
            InterfaceId: interfaceId,
            ChoiceName: "Inherited",
            ChoiceArgument: DamlUnit.Instance,
            ExerciseResult: DamlUnit.Instance,
            Consuming: false,
            ActingParties: [new Party("alice")],
            WitnessParties: [new Party("alice")]);

        exercised.InterfaceId.Should().Be(interfaceId);
        exercised.TemplateId.Should().NotBe(interfaceId);
    }

    private static TransactionResult MakeTransaction(params (string ContractId, RuntimeIdentifier TemplateId)[] created)
    {
        var contracts = new List<CreatedContract>();
        foreach (var (cid, tid) in created)
        {
            contracts.Add(new CreatedContract(cid, tid, "{}"));
        }
        return new TransactionResult(
            UpdateId: "u1",
            CompletionOffset: 1L,
            CreatedContracts: contracts,
            ArchivedContractIds: []);
    }

    private sealed record FooBar(string Owner) : ITemplate
    {
        public static RuntimeIdentifier TemplateId { get; } = new("test-pkg", "Sample.Foo", "FooBar");
        public static string PackageId => "test-pkg";
        public static string PackageName => "test-package";
        public static Version PackageVersion { get; } = new(0, 1, 0);

        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("owner", new DamlParty(Owner)));
    }
}
