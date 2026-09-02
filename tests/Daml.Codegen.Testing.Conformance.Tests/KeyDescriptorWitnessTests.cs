// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.Contractkeys;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

/// <summary>
/// The emitted <c>Key</c> witness is what lets generic code reach a template's key codec from
/// the template type alone, with no reflection and no constraint on the key type. These tests
/// drive it through a generic constraint over both corpus key shapes: <c>Account</c>'s record
/// key and <c>Steward</c>'s bare <c>Party</c> key.
/// </summary>
public class KeyDescriptorWitnessTests
{
    private static readonly Party Custodian = new("custodian::1220");

    private static TKey RoundTrip<TTemplate, TKey>(TKey key)
        where TTemplate : ITemplate, IHasKey<TTemplate, TKey>
    {
        return TTemplate.Key.KeyDecoder(TTemplate.Key.KeyEncoder(key));
    }

    private static ExerciseByKeyCommand ArchiveByKey<TTemplate, TKey>(TKey key)
        where TTemplate : ITemplate, IHasKey<TTemplate, TKey>
    {
        return new(
            TTemplate.TemplateId,
            TTemplate.Key.KeyEncoder(key),
            new ChoiceName("Archive"),
            DamlRecord.Create());
    }

    [Fact]
    public void KeyDescriptorWitness_round_trips_a_record_key()
    {
        var key = new AccountKey(Custodian, "savings");

        Account.Key.KeyEncoder(key).Should().Be(key.ToRecord());
        RoundTrip<Account, AccountKey>(key).Should().Be(key);
    }

    [Fact]
    public void KeyDescriptorWitness_round_trips_a_bare_party_key()
    {
        Steward.Key.KeyEncoder(Custodian).Should().Be(Custodian.ToDamlValue());
        RoundTrip<Steward, Party>(Custodian).Should().Be(Custodian);
    }

    [Fact]
    public void KeyDescriptorWitness_builds_the_by_key_command_the_emitted_builder_builds()
    {
        var key = new AccountKey(Custodian, "savings");

        ArchiveByKey<Account, AccountKey>(key).Should().Be(Account.ArchiveByKeyCommand(key));
        ArchiveByKey<Steward, Party>(Custodian).Should().Be(Steward.ArchiveByKeyCommand(Custodian));
    }
}
