// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public partial class ContractTypesTests
{
    [Fact]
    public void Contract_should_store_id_and_data()
    {
        var id = new ContractId<TestTemplate>("contract-1");
        var data = new TestTemplate(new Party("Alice"), 100);

        var contract = new Contract<TestTemplate>(id, data);

        contract.Id.Should().Be(id);
        contract.Data.Should().Be(data);
    }

    [Fact]
    public void Contract_FromCreatedEvent_should_decode_contract()
    {
        var templateId = TestTemplate.TemplateId;
        var createArgs = DamlRecord.Create(
            DamlField.Create("owner", new DamlParty("Bob")),
            DamlField.Create("amount", new DamlInt64(200)));

        var createdEvent = new CreatedEvent(
            EventId: "event-1",
            ContractId: "contract-from-event",
            TemplateId: templateId,
            CreateArguments: createArgs,
            WitnessParties: [new Party("Bob")],
            Signatories: [new Party("Bob")],
            Observers: []);

        var contract = Contract<TestTemplate>.FromCreatedEvent(createdEvent, TestTemplate.FromRecord);

        contract.Id.Value.Should().Be("contract-from-event");
        contract.Data.Owner.Should().Be(new Party("Bob"));
        contract.Data.Amount.Should().Be(200);
    }

    [Fact]
    public void Contract_should_support_equality()
    {
        var id = new ContractId<TestTemplate>("contract-1");
        var data = new TestTemplate(new Party("Alice"), 100);
        var contract1 = new Contract<TestTemplate>(id, data);
        var contract2 = new Contract<TestTemplate>(id, data);

        contract1.Should().Be(contract2);
    }

    [Fact]
    public void CreatedEvent_should_store_all_properties()
    {
        var templateId = new Identifier("pkg", "Module", "Template");
        var createArgs = DamlRecord.Create();
        var witnesses = new List<Party> { new("Alice"), new("Bob") };
        var signatories = new List<Party> { new("Alice") };
        var observers = new List<Party> { new("Charlie") };
        var contractKey = new ContractKey(new DamlText("key-value"), templateId);
        var createdAt = DateTimeOffset.UtcNow;

        var @event = new CreatedEvent(
            EventId: "event-123",
            ContractId: "contract-456",
            TemplateId: templateId,
            CreateArguments: createArgs,
            WitnessParties: witnesses,
            Signatories: signatories,
            Observers: observers,
            ContractKey: contractKey,
            CreatedAt: createdAt);

        @event.EventId.Should().Be("event-123");
        @event.ContractId.Should().Be("contract-456");
        @event.TemplateId.Should().Be(templateId);
        @event.CreateArguments.Should().Be(createArgs);
        @event.WitnessParties.Should().BeEquivalentTo(witnesses);
        @event.Signatories.Should().BeEquivalentTo(signatories);
        @event.Observers.Should().BeEquivalentTo(observers);
        @event.ContractKey.Should().Be(contractKey);
        @event.CreatedAt.Should().Be(createdAt);
    }

    [Fact]
    public void CreatedEvent_should_allow_optional_properties_null()
    {
        var templateId = new Identifier("pkg", "Module", "Template");
        var createArgs = DamlRecord.Create();

        var @event = new CreatedEvent(
            EventId: "event-1",
            ContractId: "contract-1",
            TemplateId: templateId,
            CreateArguments: createArgs,
            WitnessParties: [],
            Signatories: [],
            Observers: []);

        @event.ContractKey.Should().BeNull();
        @event.CreatedAt.Should().BeNull();
    }

    [Fact]
    public void ArchivedEvent_should_store_all_properties()
    {
        var templateId = new Identifier("pkg", "Module", "Template");
        var witnesses = new List<Party> { new("Alice"), new("Bob") };

        var @event = new ArchivedEvent(
            EventId: "archive-event-1",
            ContractId: "contract-to-archive",
            TemplateId: templateId,
            WitnessParties: witnesses);

        @event.EventId.Should().Be("archive-event-1");
        @event.ContractId.Should().Be("contract-to-archive");
        @event.TemplateId.Should().Be(templateId);
        @event.WitnessParties.Should().BeEquivalentTo(witnesses);
    }

    [Fact]
    public void ArchivedEvent_should_support_equality()
    {
        var templateId = new Identifier("pkg", "Module", "Template");
        var witnesses = new List<Party> { new("Alice") };
        var event1 = new ArchivedEvent("e1", "c1", templateId, witnesses);
        var event2 = new ArchivedEvent("e1", "c1", templateId, witnesses);

        event1.Should().Be(event2);
    }

    [Fact]
    public void ContractKey_should_store_value_and_template_id()
    {
        var keyValue = new DamlText("my-key");
        var templateId = new Identifier("pkg", "Module", "Template");

        var contractKey = new ContractKey(keyValue, templateId);

        contractKey.Value.Should().Be(keyValue);
        contractKey.TemplateId.Should().Be(templateId);
    }

    [Fact]
    public void ContractKey_should_allow_null_template_id()
    {
        var keyValue = new DamlInt64(42);

        var contractKey = new ContractKey(keyValue);

        contractKey.Value.Should().Be(keyValue);
        contractKey.TemplateId.Should().BeNull();
    }

    [Fact]
    public void ContractKey_should_support_complex_key_values()
    {
        var complexKey = DamlRecord.Create(
            DamlField.Create("party", new DamlParty("Alice")),
            DamlField.Create("id", new DamlInt64(123)));
        var templateId = new Identifier("pkg", "Module", "KeyedTemplate");

        var contractKey = new ContractKey(complexKey, templateId);

        contractKey.Value.Should().BeOfType<DamlRecord>();
        var record = contractKey.Value.As<DamlRecord>();
        record.GetField("party")!.As<DamlParty>().Value.Should().Be("Alice");
        record.GetField("id")!.As<DamlInt64>().Value.Should().Be(123);
    }
}
