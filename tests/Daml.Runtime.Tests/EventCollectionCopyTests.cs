// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using AwesomeAssertions;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Daml.Runtime.Tests;

/// <summary>
/// Pins how the event records in <c>Daml.Runtime.Contracts</c> take their collection
/// members. All of them reject a <c>null</c> one with <see cref="ArgumentNullException"/>
/// naming the parameter, at the primary constructor and on <c>init</c> so <c>with</c>
/// expressions are covered too. Only the four whose equality reads the contents also copy:
/// <see cref="IReadOnlyList{T}"/> is a read-only view, not an immutable collection, so
/// without the copy a producer that keeps its backing list could mutate an
/// already-constructed value's hash code and make it unfindable in a set or dictionary that
/// already holds it. The records that keep record-synthesized equality compare that member
/// by reference, so no hash can be corrupted there and they borrow rather than copy.
/// </summary>
public class EventCollectionCopyTests
{
    private static readonly RuntimeIdentifier FooTemplateId = new("test-pkg", "Acme.Foo", "FooBar");

    [Fact]
    public void CreatedContract_copies_the_party_lists_it_is_constructed_from()
    {
        var witnesses = new List<Party> { new("alice") };
        var signatories = new List<Party> { new("alice") };
        var observers = new List<Party> { new("bob") };
        var created = MakeCreated(witnesses, signatories, observers);
        var hashBefore = created.GetHashCode();

        witnesses.Add(new Party("mallory"));
        signatories.Clear();
        observers.Add(new Party("mallory"));

        created.WitnessParties.Should().ContainSingle().Which.Id.Should().Be("alice");
        created.Signatories.Should().ContainSingle().Which.Id.Should().Be("alice");
        created.Observers.Should().ContainSingle().Which.Id.Should().Be("bob");
        created.GetHashCode().Should().Be(hashBefore);
    }

    [Fact]
    public void CreatedContract_copies_interface_ids_supplied_through_a_with_expression()
    {
        var interfaceIds = new List<RuntimeIdentifier> { new("pkg", "Acme.Foo", "IThing") };
        var created = MakeCreated() with { InterfaceIds = interfaceIds };
        var hashBefore = created.GetHashCode();

        interfaceIds.Add(new RuntimeIdentifier("pkg", "Acme.Foo", "IOther"));

        created.InterfaceIds.Should().ContainSingle();
        created.GetHashCode().Should().Be(hashBefore);
    }

    [Fact]
    public void CreatedContract_stays_findable_in_a_set_after_the_producer_mutates_its_list()
    {
        var witnesses = new List<Party> { new("alice") };
        var created = MakeCreated(witnesses);
        var set = new HashSet<CreatedContract> { created };

        witnesses.Add(new Party("mallory"));

        set.Should().Contain(created);
    }

    [Fact]
    public void TransactionResult_copies_the_lists_it_is_constructed_from()
    {
        var createdContracts = new List<CreatedContract> { MakeCreated() };
        var archived = new List<string> { "00a" };
        var result = new TransactionResult(
            UpdateId: "u1",
            CompletionOffset: LedgerOffset.At(1),
            CreatedContracts: createdContracts,
            ArchivedContractIds: archived,
            CommandId: null);
        var hashBefore = result.GetHashCode();

        createdContracts.Clear();
        archived.Add("00b");

        result.CreatedContracts.Should().ContainSingle();
        result.ArchivedContractIds.Should().ContainSingle().Which.Should().Be("00a");
        result.GetHashCode().Should().Be(hashBefore);
    }

    [Fact]
    public void TransactionResult_copies_exercised_events_supplied_through_a_with_expression()
    {
        var exercised = new List<ExercisedEvent> { MakeExercised() };
        var result = MakeResult() with { ExercisedEvents = exercised };
        var hashBefore = result.GetHashCode();

        exercised.Add(MakeExercised());

        result.ExercisedEvents.Should().ContainSingle();
        result.GetHashCode().Should().Be(hashBefore);
    }

    [Fact]
    public void ExercisedEvent_copies_the_party_lists_it_is_constructed_from()
    {
        var acting = new List<Party> { new("alice") };
        var witnesses = new List<Party> { new("alice") };
        var exercised = MakeExercised(acting, witnesses);
        var hashBefore = exercised.GetHashCode();

        acting.Add(new Party("mallory"));
        witnesses.Clear();

        exercised.ActingParties.Should().ContainSingle();
        exercised.WitnessParties.Should().ContainSingle();
        exercised.GetHashCode().Should().Be(hashBefore);
    }

    [Fact]
    public void ExercisedEvent_copies_caught_exceptions_supplied_through_a_with_expression()
    {
        var caught = new List<CaughtException> { MakeCaught() };
        var exercised = MakeExercised() with { CaughtExceptions = caught };
        var hashBefore = exercised.GetHashCode();

        caught.Add(MakeCaught());

        exercised.CaughtExceptions.Should().ContainSingle();
        exercised.GetHashCode().Should().Be(hashBefore);
    }

    [Fact]
    public void CaughtException_copies_the_metadata_it_is_constructed_from()
    {
        var metadata = new Dictionary<string, string> { ["required"] = "100" };
        var caught = MakeCaught(metadata);
        var hashBefore = caught.GetHashCode();

        metadata["required"] = "200";
        metadata["available"] = "40";

        caught.Metadata.Should().ContainSingle();
        caught.Metadata["required"].Should().Be("100");
        caught.GetHashCode().Should().Be(hashBefore);
    }

    [Theory]
    [InlineData("WitnessParties")]
    [InlineData("Signatories")]
    [InlineData("Observers")]
    public void CreatedContract_rejects_a_null_party_list_at_the_producer(string parameterName)
    {
        Action act = () => _ = new CreatedContract(
            EventId: "evt",
            ContractId: "00c",
            TemplateId: new RuntimeIdentifier("test-pkg", "Acme.Foo", "FooBar"),
            Payload: DamlRecord.Create(),
            WitnessParties: parameterName == "WitnessParties" ? null! : [],
            Signatories: parameterName == "Signatories" ? null! : [],
            Observers: parameterName == "Observers" ? null! : []);

        act.Should().Throw<ArgumentNullException>().WithParameterName(parameterName);
    }

    [Theory]
    [InlineData("ActingParties")]
    [InlineData("WitnessParties")]
    public void ExercisedEvent_rejects_a_null_party_list_at_the_producer(string parameterName)
    {
        Action act = () => _ = new ExercisedEvent(
            ContractId: "00c",
            TemplateId: new RuntimeIdentifier("test-pkg", "Acme.Foo", "FooBar"),
            InterfaceId: null,
            ChoiceName: "DoThing",
            ChoiceArgument: DamlUnit.Instance,
            ExerciseResult: DamlUnit.Instance,
            Consuming: true,
            ActingParties: parameterName == "ActingParties" ? null! : [],
            WitnessParties: parameterName == "WitnessParties" ? null! : []);

        act.Should().Throw<ArgumentNullException>().WithParameterName(parameterName);
    }

    [Fact]
    public void CaughtException_rejects_null_metadata_at_the_producer()
    {
        Action act = () => _ = new CaughtException(
            ErrorId: "Acme.Errors:InsufficientFunds",
            Message: "not enough funds",
            Metadata: null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("Metadata");
    }

    [Theory]
    [InlineData("WitnessParties")]
    [InlineData("Signatories")]
    [InlineData("Observers")]
    public void CreatedEvent_rejects_a_null_party_list_at_the_producer(string parameterName)
    {
        Action act = () => _ = new CreatedEvent(
            EventId: "evt",
            ContractId: "00c",
            TemplateId: FooTemplateId,
            CreateArguments: DamlRecord.Create(),
            WitnessParties: parameterName == "WitnessParties" ? null! : [],
            Signatories: parameterName == "Signatories" ? null! : [],
            Observers: parameterName == "Observers" ? null! : [],
            ContractKey: null);

        act.Should().Throw<ArgumentNullException>().WithParameterName(parameterName);
    }

    [Theory]
    [InlineData("WitnessParties")]
    [InlineData("Signatories")]
    [InlineData("Observers")]
    public void CreatedEvent_rejects_a_null_party_list_from_a_with_expression(string parameterName)
    {
        var created = MakeCreatedEvent();

        Action act = () => _ = parameterName switch
        {
            "WitnessParties" => created with { WitnessParties = null! },
            "Signatories" => created with { Signatories = null! },
            _ => created with { Observers = null! },
        };

        act.Should().Throw<ArgumentNullException>().WithParameterName(parameterName);
    }

    [Fact]
    public void ArchivedEvent_rejects_null_witness_parties_at_the_producer()
    {
        Action act = () => _ = new ArchivedEvent(
            EventId: "evt",
            ContractId: "00c",
            TemplateId: FooTemplateId,
            WitnessParties: null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("WitnessParties");
    }

    [Fact]
    public void ArchivedEvent_rejects_null_witness_parties_from_a_with_expression()
    {
        var archived = MakeArchivedEvent();

        Action act = () => _ = archived with { WitnessParties = null! };

        act.Should().Throw<ArgumentNullException>().WithParameterName("WitnessParties");
    }

    [Theory]
    [InlineData("WitnessParties")]
    [InlineData("Signatories")]
    [InlineData("Observers")]
    public void TreeEventCreated_rejects_a_null_party_list_at_the_producer(string parameterName)
    {
        Action act = () => _ = new TreeEvent.Created(
            EventId: "evt",
            ContractId: "00c",
            TemplateId: FooTemplateId,
            CreateArguments: DamlRecord.Create(),
            WitnessParties: parameterName == "WitnessParties" ? null! : [],
            Signatories: parameterName == "Signatories" ? null! : [],
            Observers: parameterName == "Observers" ? null! : []);

        act.Should().Throw<ArgumentNullException>().WithParameterName(parameterName);
    }

    [Theory]
    [InlineData("WitnessParties")]
    [InlineData("Signatories")]
    [InlineData("Observers")]
    [InlineData("InterfaceIds")]
    public void TreeEventCreated_rejects_a_null_list_from_a_with_expression(string parameterName)
    {
        var created = MakeTreeCreated();

        Action act = () => _ = parameterName switch
        {
            "WitnessParties" => created with { WitnessParties = null! },
            "Signatories" => created with { Signatories = null! },
            "Observers" => created with { Observers = null! },
            _ => created with { InterfaceIds = null! },
        };

        act.Should().Throw<ArgumentNullException>().WithParameterName(parameterName);
    }

    [Fact]
    public void TreeEventCreated_defaults_interface_ids_to_an_empty_list()
    {
        MakeTreeCreated().InterfaceIds.Should().BeEmpty();
    }

    [Theory]
    [InlineData("ActingParties")]
    [InlineData("WitnessParties")]
    [InlineData("ChildEvents")]
    public void TreeEventExercised_rejects_a_null_list_at_the_producer(string parameterName)
    {
        Action act = () => _ = new TreeEvent.Exercised(
            EventId: "evt",
            ContractId: "00c",
            TemplateId: FooTemplateId,
            InterfaceId: null,
            ChoiceName: "DoThing",
            ChoiceArgument: DamlUnit.Instance,
            ExerciseResult: DamlUnit.Instance,
            Consuming: true,
            ActingParties: parameterName == "ActingParties" ? null! : [],
            WitnessParties: parameterName == "WitnessParties" ? null! : [],
            ChildEvents: parameterName == "ChildEvents" ? null! : []);

        act.Should().Throw<ArgumentNullException>().WithParameterName(parameterName);
    }

    [Theory]
    [InlineData("ActingParties")]
    [InlineData("WitnessParties")]
    [InlineData("ChildEvents")]
    public void TreeEventExercised_rejects_a_null_list_from_a_with_expression(string parameterName)
    {
        var exercised = MakeTreeExercised();

        Action act = () => _ = parameterName switch
        {
            "ActingParties" => exercised with { ActingParties = null! },
            "WitnessParties" => exercised with { WitnessParties = null! },
            _ => exercised with { ChildEvents = null! },
        };

        act.Should().Throw<ArgumentNullException>().WithParameterName(parameterName);
    }

    [Fact]
    public void TransactionTree_rejects_null_root_events_at_the_producer()
    {
        Action act = () => _ = new TransactionTree(
            UpdateId: "u1",
            CompletionOffset: LedgerOffset.At(1),
            RootEvents: null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("RootEvents");
    }

    [Fact]
    public void TransactionTree_rejects_null_root_events_from_a_with_expression()
    {
        var tree = MakeTree();

        Action act = () => _ = tree with { RootEvents = null! };

        act.Should().Throw<ArgumentNullException>().WithParameterName("RootEvents");
    }

    [Fact]
    public void CreatedEvent_borrows_the_party_lists_it_is_constructed_from()
    {
        var witnesses = new List<Party> { new("alice") };
        var signatories = new List<Party> { new("alice") };
        var observers = new List<Party> { new("bob") };

        var created = new CreatedEvent(
            EventId: "evt-00c",
            ContractId: "00c",
            TemplateId: FooTemplateId,
            CreateArguments: DamlRecord.Create(),
            WitnessParties: witnesses,
            Signatories: signatories,
            Observers: observers,
            ContractKey: null);

        created.WitnessParties.Should().BeSameAs(witnesses);
        created.Signatories.Should().BeSameAs(signatories);
        created.Observers.Should().BeSameAs(observers);
    }

    [Fact]
    public void ArchivedEvent_borrows_the_witness_parties_it_is_constructed_from()
    {
        var witnesses = new List<Party> { new("alice") };

        var archived = new ArchivedEvent(
            EventId: "evt-00c",
            ContractId: "00c",
            TemplateId: FooTemplateId,
            WitnessParties: witnesses);

        archived.WitnessParties.Should().BeSameAs(witnesses);
    }

    [Fact]
    public void TreeEventCreated_borrows_the_lists_it_is_constructed_from()
    {
        var witnesses = new List<Party> { new("alice") };
        var signatories = new List<Party> { new("alice") };
        var observers = new List<Party> { new("bob") };
        var interfaceIds = new List<RuntimeIdentifier> { new("pkg", "Acme.Foo", "IThing") };

        var created = new TreeEvent.Created(
            EventId: "evt-00c",
            ContractId: "00c",
            TemplateId: FooTemplateId,
            CreateArguments: DamlRecord.Create(),
            WitnessParties: witnesses,
            Signatories: signatories,
            Observers: observers) with
        {
            InterfaceIds = interfaceIds,
        };

        created.WitnessParties.Should().BeSameAs(witnesses);
        created.Signatories.Should().BeSameAs(signatories);
        created.Observers.Should().BeSameAs(observers);
        created.InterfaceIds.Should().BeSameAs(interfaceIds);
    }

    [Fact]
    public void TreeEventExercised_borrows_the_lists_it_is_constructed_from()
    {
        var acting = new List<Party> { new("alice") };
        var witnesses = new List<Party> { new("alice") };
        var children = new List<TreeEvent> { MakeTreeCreated() };

        var exercised = new TreeEvent.Exercised(
            EventId: "evt-00c",
            ContractId: "00c",
            TemplateId: FooTemplateId,
            InterfaceId: null,
            ChoiceName: "DoThing",
            ChoiceArgument: DamlUnit.Instance,
            ExerciseResult: DamlUnit.Instance,
            Consuming: true,
            ActingParties: acting,
            WitnessParties: witnesses,
            ChildEvents: children);

        exercised.ActingParties.Should().BeSameAs(acting);
        exercised.WitnessParties.Should().BeSameAs(witnesses);
        exercised.ChildEvents.Should().BeSameAs(children);
    }

    [Fact]
    public void TransactionTree_borrows_the_root_events_it_is_constructed_from()
    {
        var roots = new List<TreeEvent> { MakeTreeCreated() };
        var replacement = new List<TreeEvent> { MakeTreeCreated() };

        var tree = new TransactionTree(
            UpdateId: "u1",
            CompletionOffset: LedgerOffset.At(1),
            RootEvents: roots);

        tree.RootEvents.Should().BeSameAs(roots);
        (tree with { RootEvents = replacement }).RootEvents.Should().BeSameAs(replacement);
    }

    private static CreatedContract MakeCreated(
        IReadOnlyList<Party>? witnesses = null,
        IReadOnlyList<Party>? signatories = null,
        IReadOnlyList<Party>? observers = null) =>
        new(
            EventId: "evt-00c",
            ContractId: "00c",
            TemplateId: new RuntimeIdentifier("test-pkg", "Acme.Foo", "FooBar"),
            Payload: DamlRecord.Create(),
            WitnessParties: witnesses ?? [new Party("alice")],
            Signatories: signatories ?? [new Party("alice")],
            Observers: observers ?? []);

    private static TransactionResult MakeResult() =>
        new(
            UpdateId: "u1",
            CompletionOffset: LedgerOffset.At(1),
            CreatedContracts: [],
            ArchivedContractIds: [],
            CommandId: null);

    private static ExercisedEvent MakeExercised(
        IReadOnlyList<Party>? acting = null,
        IReadOnlyList<Party>? witnesses = null) =>
        new(
            ContractId: "00c",
            TemplateId: new RuntimeIdentifier("test-pkg", "Acme.Foo", "FooBar"),
            InterfaceId: null,
            ChoiceName: "DoThing",
            ChoiceArgument: DamlUnit.Instance,
            ExerciseResult: DamlUnit.Instance,
            Consuming: true,
            ActingParties: acting ?? [new Party("alice")],
            WitnessParties: witnesses ?? [new Party("alice")]);

    private static CaughtException MakeCaught(IReadOnlyDictionary<string, string>? metadata = null) =>
        new(
            ErrorId: "Acme.Errors:InsufficientFunds",
            Message: "not enough funds",
            Metadata: metadata ?? new Dictionary<string, string> { ["required"] = "100" });

    private static CreatedEvent MakeCreatedEvent() =>
        new(
            EventId: "evt-00c",
            ContractId: "00c",
            TemplateId: FooTemplateId,
            CreateArguments: DamlRecord.Create(),
            WitnessParties: [new Party("alice")],
            Signatories: [new Party("alice")],
            Observers: [],
            ContractKey: null);

    private static ArchivedEvent MakeArchivedEvent() =>
        new(
            EventId: "evt-00c",
            ContractId: "00c",
            TemplateId: FooTemplateId,
            WitnessParties: [new Party("alice")]);

    private static TreeEvent.Created MakeTreeCreated() =>
        new(
            EventId: "evt-00c",
            ContractId: "00c",
            TemplateId: FooTemplateId,
            CreateArguments: DamlRecord.Create(),
            WitnessParties: [new Party("alice")],
            Signatories: [new Party("alice")],
            Observers: []);

    private static TreeEvent.Exercised MakeTreeExercised() =>
        new(
            EventId: "evt-00c",
            ContractId: "00c",
            TemplateId: FooTemplateId,
            InterfaceId: null,
            ChoiceName: "DoThing",
            ChoiceArgument: DamlUnit.Instance,
            ExerciseResult: DamlUnit.Instance,
            Consuming: true,
            ActingParties: [new Party("alice")],
            WitnessParties: [new Party("alice")],
            ChildEvents: []);

    private static TransactionTree MakeTree() =>
        new(
            UpdateId: "u1",
            CompletionOffset: LedgerOffset.At(1),
            RootEvents: []);
}
