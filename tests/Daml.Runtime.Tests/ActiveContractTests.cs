// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Daml.Runtime.Streams;
using Xunit;

namespace Daml.Runtime.Tests;

/// <summary>
/// The carrier's own contract: an unconstrained payload type, and provenance that sits beside
/// the contract rather than inside it.
/// </summary>
public sealed class ActiveContractTests
{
    [Fact]
    public void ActiveContract_wraps_an_interface_view_pairing_this_repo_does_not_own()
    {
        var row = new InterfaceAcsSnapshotEntry<ProbeInterface, ProbeView>.Created(
            new ContractId<ProbeInterface>("cid-1"),
            new ProbeView(new Party("alice")),
            null,
            LedgerOffset.At(5),
            new SynchronizerId("sync-a"),
            [new Party("alice")]);

        var active = new ActiveContract<InterfaceViewPairing>(
            new InterfaceViewPairing(row.ContractId, row.Payload),
            row.Offset,
            row.SynchronizerId);

        active.Contract.View.Owner.Should().Be(
            new Party("alice"),
            "TContract is unconstrained so a consumer's own interface-contract shape wraps here, "
            + "which is why this repo does not need to define one");
        active.LastUpdateOffset.Should().Be(LedgerOffset.At(5));
        active.SynchronizerId.Should().Be(new SynchronizerId("sync-a"));
    }

    [Fact]
    public void ActiveContract_separates_contract_equality_from_observation_equality()
    {
        var contract = new Contract<ProbeTemplate>(
            new ContractId<ProbeTemplate>("cid-1"),
            new ProbeTemplate(new Party("alice")));
        var synchronizer = new SynchronizerId("sync-a");

        var earlier = new ActiveContract<Contract<ProbeTemplate>>(contract, LedgerOffset.At(5), synchronizer);
        var later = new ActiveContract<Contract<ProbeTemplate>>(contract, LedgerOffset.At(9), synchronizer);

        later.Should().NotBe(
            earlier,
            "two snapshots of the same contract at different offsets are two observations");
        later.Contract.Should().Be(
            earlier.Contract,
            "keeping provenance out of the contract shape is what stops contract equality from "
            + "silently becoming observation equality");
    }

    private interface ProbeInterface : IDamlInterface, IHasView<ProbeView>
    {
        static Identifier IDamlInterface.InterfaceId => new("pkg", "M", "ProbeInterface");
        static string IDamlInterface.PackageId => "pkg";
        static string IDamlInterface.PackageName => "test";
        static Version IDamlInterface.PackageVersion => new(0, 1, 0);
        static DamlTypeDescriptor IDamlType.DamlTypeId =>
            new(new Identifier("pkg", "M", "ProbeInterface"), DamlTypeKind.Interface, "test");
    }

    private sealed record ProbeView(Party Owner) : IDamlRecord<ProbeView>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(new DamlField("owner", Owner.ToDamlValue()));

        public static ProbeView FromRecord(DamlRecord record) =>
            new(Party.FromDamlValue(record.GetRequiredField("owner").As<DamlParty>()));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            throw new NotSupportedException();
    }

    private sealed record InterfaceViewPairing(ContractId<ProbeInterface> Id, ProbeView View);

    private sealed record ProbeTemplate(Party Owner) : ITemplate, IDamlRecord<ProbeTemplate>
    {
        public static Identifier TemplateId { get; } = new("pkg", "M", "ProbeTemplate");
        public static string PackageId => "pkg";
        public static string PackageName => "test";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create(new DamlField("owner", Owner.ToDamlValue()));

        public static ProbeTemplate FromRecord(DamlRecord record) =>
            new(Party.FromDamlValue(record.GetRequiredField("owner").As<DamlParty>()));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            throw new NotSupportedException();
    }
}
