// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.Richtypes;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

public class RichRecordChoiceTests
{
    [Fact]
    public void relabel_argument_round_trips_through_its_record()
    {
        var argument = new RichRecord.Relabel("renamed");

        var restored = RichRecord.Relabel.FromRecord(argument.ToRecord());

        restored.Should().Be(argument);
        argument.ToRecord().GetRequiredField("newLabel").As<DamlText>().Value.Should().Be("renamed");
    }

    [Fact]
    public void relabel_choice_is_non_consuming_and_named_relabel()
    {
        RichRecord.ChoiceRelabel.Name.Value.Should().Be("Relabel");
        RichRecord.ChoiceRelabel.Consuming.Should().BeFalse();
    }

    [Fact]
    public void archive_choice_is_consuming_and_named_archive()
    {
        RichRecord.ChoiceArchive.Name.Value.Should().Be("Archive");
        RichRecord.ChoiceArchive.Consuming.Should().BeTrue();
    }

    [Fact]
    public void contract_from_created_event_pairs_typed_id_with_decoded_payload()
    {
        var payload = new RichRecord(
            Owner: new Party("alice"),
            Count: 1,
            Amount: 1m,
            Label: "l",
            Active: false,
            AsOf: new DateOnly(2026, 1, 1),
            ObservedAt: DateTimeOffset.UnixEpoch,
            Note: null,
            Tags: new List<string>(),
            Attributes: new Dictionary<string, string>(),
            Marker: new ContractId<Marker>("m"),
            HoldingCid: new ContractId<IHolding>("00h"),
            HoldingCids: new List<ContractId<IHolding>>(),
            Profile: new Profile("n", 0),
            Outcome: new Outcome.Pending(),
            Fee: 0m);
        var @event = new CreatedEvent(
            EventId: "ev-1",
            ContractId: "rich-cid",
            TemplateId: RichRecord.TemplateId,
            CreateArguments: payload.ToRecord(),
            WitnessParties: Array.Empty<Party>(),
            Signatories: Array.Empty<Party>(),
            Observers: Array.Empty<Party>());

        var contract = RichRecord.Contract.FromCreatedEvent(@event);

        contract.Id.Value.Should().Be("rich-cid");
        contract.Data.Label.Should().Be("l");
    }

    [Fact]
    public void contract_identifiers_expose_template_ids_for_pqs_queries()
    {
        ContractIdentifiers.Marker.Should().Contain("RichTypes:Marker");
        ContractIdentifiers.RichRecord.Should().Contain("RichTypes:RichRecord");
    }
}
