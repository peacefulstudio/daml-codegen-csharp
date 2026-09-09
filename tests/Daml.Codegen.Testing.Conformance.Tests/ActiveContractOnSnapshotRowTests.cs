// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.ContractKeys;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

/// <summary>
/// The provenance-carrying projection of a snapshot row, run against the corpus's own
/// generated key codecs rather than a hand-written test template: a record key and a bare
/// <c>Party</c> key both have to reach the caller decoded, with the row's last-update offset
/// and synchronizer id intact.
/// </summary>
public class ActiveContractOnSnapshotRowTests
{
    private static readonly Party Custodian = new("custodian::1220");

    private const string LedgerKeyHash = "6CgQL9eNNqIjS5cB6/kK1IsqdxjcgXl/3kxSiUEkiBA=";

    [Fact]
    public void ToActiveContract_decodes_a_generated_record_key_and_keeps_the_rows_provenance()
    {
        var key = new AccountKey(Custodian, "savings");
        var created = new AcsSnapshotEntry<Account>.Created(
            new ContractId<Account>("contract-1"),
            new Account(Custodian, "savings", 42),
            new ContractKey(key.ToRecord(), Account.TemplateId) { KeyHash = LedgerKeyHash },
            LedgerOffset.At(9),
            new SynchronizerId("synchronizer::1220"),
            [Custodian]);

        var active = created.ToActiveContract<Account, AccountKey>();

        active.Contract.Key.Value.Should().Be(key);
        active.Contract.Key.Hash.Should().Be(LedgerKeyHash);
        active.Contract.Data.Balance.Should().Be(42);
        active.LastUpdateOffset.Should().Be(LedgerOffset.At(9));
        active.SynchronizerId.Should().Be(new SynchronizerId("synchronizer::1220"));
    }

    [Fact]
    public void ToActiveContract_decodes_a_generated_bare_party_key_and_keeps_the_rows_provenance()
    {
        var created = new AcsSnapshotEntry<Steward>.Created(
            new ContractId<Steward>("contract-2"),
            new Steward(Custodian, "charter"),
            new ContractKey(Custodian.ToDamlValue(), Steward.TemplateId),
            LedgerOffset.At(11),
            new SynchronizerId("synchronizer::1220"),
            [Custodian]);

        var active = created.ToActiveContract<Steward, Party>();

        active.Contract.Key.Value.Should().Be(
            Custodian,
            "a bare-key template's key is not a record, so the projection must decode through the "
            + "template's own key witness rather than assume a record shape");
        active.LastUpdateOffset.Should().Be(LedgerOffset.At(11));
        active.SynchronizerId.Should().Be(new SynchronizerId("synchronizer::1220"));
    }
}
