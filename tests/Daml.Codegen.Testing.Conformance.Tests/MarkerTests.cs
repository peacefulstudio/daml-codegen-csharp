// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.Richtypes;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

public class MarkerTests
{
    private const string PackageHash = "1e0f96e54a2b32b2e081b86edb35567a3ec6f087804f416583d54527e2b52e38";

    [Fact]
    public void ToRecord_then_from_record_round_trips_the_owner()
    {
        var original = new Marker(new Party("alice"));

        var restored = Marker.FromRecord(original.ToRecord());

        restored.Should().Be(original);
    }

    [Fact]
    public void Marker_template_id_carries_package_module_and_entity()
    {
        Marker.TemplateId.PackageId.Should().Be(PackageHash);
        Marker.TemplateId.ModuleName.Should().Be("RichTypes");
        Marker.TemplateId.EntityName.Should().Be("Marker");
    }

    [Fact]
    public void Marker_package_metadata_matches_the_corpus_dar()
    {
        Marker.PackageId.Should().Be(PackageHash);
        Marker.PackageName.Should().Be("richtypes");
        Marker.PackageVersion.Should().Be(new Version(0, 0, 1));
    }

    [Fact]
    public void Marker_archive_choice_is_consuming_and_named_archive()
    {
        Marker.ChoiceArchive.Name.Value.Should().Be("Archive");
        Marker.ChoiceArchive.Consuming.Should().BeTrue();
    }

    [Fact]
    public void Marker_contract_from_created_event_pairs_typed_id_with_decoded_payload()
    {
        var payload = new Marker(new Party("alice"));
        var @event = new CreatedEvent(
            EventId: "ev-1",
            ContractId: "cid-1",
            TemplateId: Marker.TemplateId,
            CreateArguments: payload.ToRecord(),
            WitnessParties: Array.Empty<Party>(),
            Signatories: Array.Empty<Party>(),
            Observers: Array.Empty<Party>(),
            ContractKey: null);

        var contract = Marker.Contract.FromCreatedEvent(@event);

        contract.Id.Value.Should().Be("cid-1");
        contract.Data.Should().Be(payload);
    }
}
