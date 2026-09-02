// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using AwesomeAssertions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Daml.Runtime.Tests;

/// <summary>
/// Pins the created-contract shape that <c>AcsSnapshotEntry&lt;T&gt;.Created</c>,
/// <c>ContractStreamEvent&lt;T&gt;.Created</c> and their interface-family counterparts each
/// declare for themselves. All four carry the same created event — snapshot or live stream,
/// template or interface — so a consumer handling any of them switches on one vocabulary. Every shape compared here is read out of the
/// declaration by reflection, so renaming, retyping, reordering, adding or omitting a
/// parameter on one declaration alone fails this class instead of letting a fact reach one
/// path and not the other.
/// </summary>
public class CreatedShapeAgreementTests
{
    private const string VacuityReason =
        "an empty shape would let every agreement assertion in this class pass without comparing anything";

    private static readonly Type SnapshotCreated = typeof(AcsSnapshotEntry<Probe>.Created);
    private static readonly Type SubscriptionCreated = typeof(ContractStreamEvent<Probe>.Created);
    private static readonly Type InterfaceSnapshotCreated =
        typeof(InterfaceAcsSnapshotEntry<ProbeInterface, ProbeView>.Created);
    private static readonly Type InterfaceSubscriptionCreated =
        typeof(InterfaceStreamEvent<ProbeInterface, ProbeView>.Created);

    [Fact]
    public void CreatedShapeAgreement_the_snapshot_and_subscription_variants_declare_one_shape()
    {
        DeclaredShape(SnapshotCreated).Should().Be(
            DeclaredShape(SubscriptionCreated),
            "the snapshot and the subscription Created carry the same created contract, so a parameter " +
            "renamed, retyped, reordered, added or dropped on one of them has to be mirrored on the other");
    }

    [Fact]
    public void CreatedShapeAgreement_both_variants_carry_the_contract_key()
    {
        DeclaredShape(SnapshotCreated).Should().Contain(
            "ContractKey? Key|",
            "the key is a separate wire field the payload record cannot hold, so a created row that omits " +
            "the slot leaves the key unreadable on that path no matter what the emitted contract declares");
        DeclaredShape(SubscriptionCreated).Should().Contain("ContractKey? Key|", "the live stream carries the same field");
    }

    [Fact]
    public void CreatedShapeAgreement_a_variant_missing_the_key_stops_agreeing()
    {
        var keyless = typeof(PayloadWithoutKey);

        DeclaredShape(keyless).Should().NotBe(
            DeclaredShape(SnapshotCreated),
            "a declaration that drops the key has to break the comparison, otherwise the agreement " +
            "assertions would hold against the very omission this class exists to catch");
    }

    [Fact]
    public void CreatedShapeAgreement_the_interface_family_variants_declare_one_shape()
    {
        DeclaredShape(InterfaceSnapshotCreated).Should().Be(
            DeclaredShape(InterfaceSubscriptionCreated),
            "the interface-family snapshot and subscription Created carry the same created contract, " +
            "so a parameter renamed, retyped, reordered, added or dropped on one of them has to be " +
            "mirrored on the other");
    }

    [Fact]
    public void CreatedShapeAgreement_the_interface_family_mirrors_the_template_family_field_for_field()
    {
        DeclaredShape(InterfaceSubscriptionCreated).Should().Be(
            DeclaredShape(SubscriptionCreated).Replace("Probe Payload|", "ProbeView Payload|", StringComparison.Ordinal),
            "the interface family is the template family with the view record in the payload slot, so " +
            "the two differ in the payload type alone — any other divergence makes a consumer that " +
            "handles both families switch on two vocabularies");
    }

    [Fact]
    public void CreatedShapeAgreement_reads_a_non_empty_shape_from_every_declaration()
    {
        DeclaredShape(SnapshotCreated).Should().NotBeEmpty(VacuityReason);
        DeclaredShape(SubscriptionCreated).Should().NotBeEmpty(VacuityReason);
        DeclaredShape(InterfaceSnapshotCreated).Should().NotBeEmpty(VacuityReason);
        DeclaredShape(InterfaceSubscriptionCreated).Should().NotBeEmpty(VacuityReason);
    }

    private static string DeclaredShape(Type declaration)
    {
        var nullability = new NullabilityInfoContext();

        return string.Concat(declaration.GetConstructors()
            .OrderByDescending(constructor => constructor.GetParameters().Length)
            .First()
            .GetParameters()
            .Select(parameter =>
                $"{parameter.ParameterType.Name}{NullableMarker(nullability, parameter)} {parameter.Name}|"));
    }

    private static string NullableMarker(NullabilityInfoContext nullability, ParameterInfo parameter) =>
        nullability.Create(parameter).WriteState == NullabilityState.Nullable ? "?" : string.Empty;

    private sealed record PayloadWithoutKey(DamlRecord Payload);

    private sealed record Probe : ITemplate, IDamlRecord<Probe>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("pkg", "M", "Probe");
        public static string PackageId => "pkg";
        public static string PackageName => "probe";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } =
            new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create();
        public static Probe FromRecord(DamlRecord record) => new();
    }

    private sealed record ProbeInterface : IDamlInterface, IHasView<ProbeView>
    {
        public static RuntimeIdentifier InterfaceId { get; } = new("pkg", "M", "ProbeInterface");
        public static string PackageId => "pkg";
        public static string PackageName => "probe";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } =
            new(InterfaceId, DamlTypeKind.Interface, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create();
    }

    private sealed record ProbeView : IDamlRecord<ProbeView>
    {
        public DamlRecord ToRecord() => DamlRecord.Create();
        public static ProbeView FromRecord(DamlRecord record) => new();
    }
}
