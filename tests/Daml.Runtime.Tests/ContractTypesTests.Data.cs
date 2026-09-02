// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public partial class ContractTypesTests
{
    private const string LedgerKeyHash = "6CgQL9eNNqIjS5cB6/kK1IsqdxjcgXl/3kxSiUEkiBA=";

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
            Observers: [],
            ContractKey: null);

        var contract = Contract<TestTemplate>.FromCreatedEvent(createdEvent, TestTemplate.FromRecord);

        contract.Id.Value.Should().Be("contract-from-event");
        contract.Data.Owner.Should().Be(new Party("Bob"));
        contract.Data.Amount.Should().Be(200);
    }

    [Fact]
    public void Contract_should_declare_no_key_member_on_the_keyless_shape()
    {
        typeof(Contract<TestTemplate>).GetProperty("Key").Should().BeNull(
            "the keyless shape exists so a template that declares no contract key stops offering "
            + "a member that can never be populated; a key belongs to Contract<T, TKey>");
    }

    [Fact]
    public void Contract_FromCreatedEvent_should_read_a_keyed_event_through_the_keyless_shape()
    {
        var templateId = TestTemplate.TemplateId;
        var createdEvent = new CreatedEvent(
            EventId: "event-1",
            ContractId: "contract-with-key",
            TemplateId: templateId,
            CreateArguments: DamlRecord.Create(
                DamlField.Create("owner", new DamlParty("Bob")),
                DamlField.Create("amount", new DamlInt64(200))),
            WitnessParties: [new Party("Bob")],
            Signatories: [new Party("Bob")],
            Observers: [],
            ContractKey: new ContractKey(new DamlText("savings"), templateId));

        var contract = Contract<TestTemplate>.FromCreatedEvent(createdEvent, TestTemplate.FromRecord);

        contract.Id.Value.Should().Be("contract-with-key");
        contract.Data.Owner.Should().Be(
            new Party("Bob"),
            "reading a keyed contract through the keyless shape is the documented fallback when a "
            + "caller does not want the key, so the event's key must not make the projection fail");
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
            Observers: [],
            ContractKey: null);

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
    public void ContractKey_should_store_the_ledgers_key_hash()
    {
        var contractKey = new ContractKey(
            new DamlText("my-key"),
            new Identifier("pkg", "Module", "Template"))
        {
            KeyHash = LedgerKeyHash,
        };

        contractKey.KeyHash.Should().Be(LedgerKeyHash);
    }

    [Fact]
    public void ContractKey_should_default_the_key_hash_to_null()
    {
        var contractKey = new ContractKey(new DamlText("my-key"));

        contractKey.KeyHash.Should().BeNull();
    }

    [Fact]
    public void ContractKey_should_equal_the_same_key_constructed_without_a_hash()
    {
        var templateId = new Identifier("pkg", "Module", "Template");
        var readOffTheWire = new ContractKey(new DamlText("my-key"), templateId) { KeyHash = LedgerKeyHash };
        var builtByTheCaller = new ContractKey(new DamlText("my-key"), templateId);

        readOffTheWire.Should().Be(
            builtByTheCaller,
            "the exercise-by-key path builds a key from data the caller already holds and so has no "
            + "hash, and it names the same key the ledger reported — equality that split on the hash "
            + "would make by-key matching fail quietly depending on where the key came from");
        readOffTheWire.GetHashCode().Should().Be(
            builtByTheCaller.GetHashCode(),
            "a hash code that split on KeyHash would drop the two into different buckets of a "
            + "dictionary keyed by ContractKey, so a lookup would miss even though Equals agrees");
    }

    [Fact]
    public void ContractKey_should_not_equal_a_key_naming_a_different_value()
    {
        var templateId = new Identifier("pkg", "Module", "Template");
        var savings = new ContractKey(new DamlText("savings"), templateId) { KeyHash = LedgerKeyHash };
        var current = new ContractKey(new DamlText("current"), templateId) { KeyHash = LedgerKeyHash };

        savings.Should().NotBe(
            current,
            "equality is over the key the record names, so ignoring the hash must not collapse into "
            + "ignoring the value as well");
    }

    [Fact]
    public void ContractKey_should_not_equal_a_key_naming_a_different_template()
    {
        var value = new DamlText("my-key");
        var here = new ContractKey(value, new Identifier("pkg", "Module", "Template"));
        var elsewhere = new ContractKey(value, new Identifier("pkg", "Module", "Other"));

        here.Should().NotBe(elsewhere);
    }

    [Fact]
    public void ContractKey_should_expose_the_hash_it_was_given_despite_ignoring_it_for_equality()
    {
        var templateId = new Identifier("pkg", "Module", "Template");
        var readOffTheWire = new ContractKey(new DamlText("my-key"), templateId) { KeyHash = LedgerKeyHash };

        readOffTheWire.KeyHash.Should().Be(
            LedgerKeyHash,
            "leaving the hash out of equality must not mean discarding it — it is the value Canton "
            + "indexes keyed contracts by and the reason the slot exists");
        readOffTheWire.ToString().Should().Contain(
            LedgerKeyHash,
            "the hash stays a visible member of the record, so a diagnostic dump still shows it");
    }

    [Fact]
    public void Contract_FromCreatedEvent_should_carry_the_keys_hash()
    {
        var createdEvent = KeyedCreatedEvent(
            new ContractKey(new DamlParty("Alice"), KeyedTestTemplate.TemplateId)
            {
                KeyHash = LedgerKeyHash,
            });

        var contract = Contract<KeyedTestTemplate, Party>.FromCreatedEvent(
            createdEvent,
            KeyedTestTemplate.FromRecord);

        contract.Key.Hash.Should().Be(
            LedgerKeyHash,
            "the hash is what the ledger indexes the contract by, so a projection that rebuilds "
            + "the key without it leaves the caller unable to address the contract by key");
    }

    [Fact]
    public void Contract_FromCreatedEvent_should_decode_the_key_through_the_templates_witness()
    {
        var createdEvent = KeyedCreatedEvent(
            new ContractKey(new DamlParty("Alice"), KeyedTestTemplate.TemplateId));

        var contract = Contract<KeyedTestTemplate, Party>.FromCreatedEvent(
            createdEvent,
            KeyedTestTemplate.FromRecord);

        contract.Key.Value.Should().Be(
            new Party("Alice"),
            "the keyed shape exists to hand back the decoded key, so a caller never repeats the "
            + "cast-and-decode hop the wire-level key would force");
    }

    [Fact]
    public void Contract_FromCreatedEvent_should_reject_a_keyed_event_carrying_no_key()
    {
        var keyless = KeyedCreatedEvent(null);

        var decoding = () => Contract<KeyedTestTemplate, Party>.FromCreatedEvent(
            keyless,
            KeyedTestTemplate.FromRecord);

        decoding.Should().Throw<InvalidOperationException>(
            "the keyed shape's key is non-nullable, so an event that carries none has to fail "
            + "loudly rather than reach a caller with a key the type says cannot be absent")
            .WithMessage("*carried no contract key*");
    }

    private static CreatedEvent KeyedCreatedEvent(ContractKey? key) => new(
        EventId: "event-1",
        ContractId: "contract-with-key",
        TemplateId: KeyedTestTemplate.TemplateId,
        CreateArguments: DamlRecord.Create(DamlField.Create("owner", new DamlParty("Alice"))),
        WitnessParties: [new Party("Alice")],
        Signatories: [new Party("Alice")],
        Observers: [],
        ContractKey: key);

    private sealed record KeyedTestTemplate(Party Owner) : ITemplate, IHasKey<KeyedTestTemplate, Party>
    {
        public static Identifier TemplateId => new(TestPackageId, TestModuleName, nameof(KeyedTestTemplate));
        public static string PackageId => TestPackageId;
        public static string PackageName => TestPackageName;
        public static Version PackageVersion => TestPackageV1;
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public static KeyDescriptor<KeyedTestTemplate, Party> Key { get; } = new()
        {
            KeyEncoder = key => key.ToDamlValue(),
            KeyDecoder = value => Party.FromDamlValue(value.As<DamlParty>()),
        };

        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("owner", Owner.ToDamlValue()));

        public static KeyedTestTemplate FromRecord(DamlRecord record) =>
            new(Party.FromDamlValue(record.GetRequiredField("owner").As<DamlParty>()));
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
