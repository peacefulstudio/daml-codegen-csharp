// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.Contractkeys;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

/// <summary>
/// The key travels on the created event, so the generated active contract — not the
/// template payload a caller constructs locally — is where it lands. These tests run
/// against the corpus's own generated types for a record key, a record key built by a
/// helper in another module, and a bare <c>Party</c> key.
/// </summary>
public class ContractKeyOnActiveContractTests
{
    private static readonly Party Custodian = new("custodian::1220");

    private const string LedgerKeyHash = "6CgQL9eNNqIjS5cB6/kK1IsqdxjcgXl/3kxSiUEkiBA=";

    private static CreatedEvent CreatedEvent(Identifier templateId, DamlRecord createArguments, DamlValue? key) =>
        new(
            EventId: "event-1",
            ContractId: "contract-1",
            TemplateId: templateId,
            CreateArguments: createArguments,
            WitnessParties: [],
            Signatories: [Custodian],
            Observers: [],
            ContractKey: key is null ? null : new ContractKey(key));

    [Fact]
    public void FromCreatedEvent_reads_a_record_key_off_the_created_event()
    {
        var payload = new Account(Custodian, "savings", 42);
        var key = new AccountKey(Custodian, "savings");

        var contract = Account.Contract.FromCreatedEvent(
            CreatedEvent(Account.TemplateId, payload.ToRecord(), key.ToRecord()));

        contract.Key.Value.Should().Be(key);
        contract.Data.Should().Be(payload);
        contract.Id.Value.Should().Be("contract-1");
    }

    [Fact]
    public void FromCreatedEvent_reads_a_bare_party_key_off_the_created_event()
    {
        var payload = new Steward(Custodian, "charter");

        var contract = Steward.Contract.FromCreatedEvent(
            CreatedEvent(Steward.TemplateId, payload.ToRecord(), Custodian.ToDamlValue()));

        contract.Key.Value.Should().Be(Custodian);
    }

    [Fact]
    public void FromCreatedEvent_reads_a_key_whose_fields_share_no_name_with_the_payload()
    {
        var payload = new Schedule(new ScheduleView(Custodian, "2026-Q1"));
        var key = new ScheduleKey(Custodian, "2026-Q1");

        var contract = Schedule.Contract.FromCreatedEvent(
            CreatedEvent(Schedule.TemplateId, payload.ToRecord(), key.ToRecord()));

        contract.Key.Value.Should().Be(key);
    }

    [Fact]
    public void FromCreatedEvent_carries_the_ledgers_hash_of_the_key()
    {
        var payload = new Account(Custodian, "savings", 42);
        var key = new AccountKey(Custodian, "savings");
        var createdEvent = CreatedEvent(Account.TemplateId, payload.ToRecord(), key.ToRecord()) with
        {
            ContractKey = new ContractKey(key.ToRecord()) { KeyHash = LedgerKeyHash },
        };

        var contract = Account.Contract.FromCreatedEvent(createdEvent);

        contract.Key.Hash.Should().Be(
            LedgerKeyHash,
            "the hash is Canton-computed over the key and the template id, so a generated contract "
            + "that drops it leaves the caller unable to address the contract by key");
    }

    [Fact]
    public void FromCreatedEvent_rejects_a_keyed_event_carrying_no_key()
    {
        var payload = new Account(Custodian, "savings", 42);

        var decoding = () => Account.Contract.FromCreatedEvent(
            CreatedEvent(Account.TemplateId, payload.ToRecord(), key: null));

        decoding.Should().Throw<InvalidOperationException>(
            "a keyed template's generated contract declares a non-nullable key, so an event that "
            + "carries none has to fail loudly rather than reach a caller through that shape")
            .WithMessage("*carried no contract key*");
    }

    [Fact]
    public void Constructing_a_contract_has_to_name_the_key_slot()
    {
        var key = new AccountKey(Custodian, "savings");

        var contract = new Account.Contract(new Account.ContractId("contract-1"), new Account(Custodian, "savings", 42))
        {
            Key = new ContractKey<AccountKey>(key, null),
        };

        contract.Key.Value.Should().Be(
            key,
            "the slot is required, so the key is a choice the call site states rather than a default it inherits");
    }

    [Fact]
    public void Deconstructing_a_contract_stays_source_compatible()
    {
        var contract = new Account.Contract(new Account.ContractId("contract-1"), new Account(Custodian, "savings", 42))
        {
            Key = new ContractKey<AccountKey>(new AccountKey(Custodian, "savings"), null),
        };

        var (id, data) = contract;

        id.Value.Should().Be("contract-1");
        data.Should().Be(new Account(Custodian, "savings", 42),
            "moving the key off the positional list leaves Deconstruct at two parameters, so var (id, data) = contract still compiles");
    }
}
