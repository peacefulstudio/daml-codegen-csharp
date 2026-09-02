// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using AwesomeAssertions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Daml.Runtime.Tests;

/// <summary>
/// Pins the transport-failure shape — a transport status code, a message, a category and the
/// source exception — that <c>AcsSnapshotEntry&lt;T&gt;.StreamError</c>,
/// <c>ContractStreamEvent&lt;T&gt;.StreamError</c>,
/// <c>InterfaceAcsSnapshotEntry&lt;TInterface, TView&gt;.StreamError</c>,
/// <c>InterfaceStreamEvent&lt;TInterface, TView&gt;.StreamError</c> and
/// <c>ExerciseOutcome&lt;T&gt;.InfraError</c> each declare for themselves. Every shape
/// compared here is read out of the declaration by reflection, so renaming, retyping,
/// reordering or adding a parameter on one declaration alone fails this class instead of
/// letting the five drift apart.
/// </summary>
public class TransportFailureShapeAgreementTests
{
    private const string VacuityReason =
        "an empty shape would let every agreement assertion in this class pass without comparing anything";

    private static readonly Type SnapshotStreamError = typeof(AcsSnapshotEntry<Probe>.StreamError);
    private static readonly Type SubscriptionStreamError = typeof(ContractStreamEvent<Probe>.StreamError);
    private static readonly Type OutcomeInfraError = typeof(ExerciseOutcome<Probe>.InfraError);
    private static readonly Type InterfaceSnapshotStreamError =
        typeof(InterfaceAcsSnapshotEntry<ProbeInterface, ProbeView>.StreamError);
    private static readonly Type InterfaceSubscriptionStreamError =
        typeof(InterfaceStreamEvent<ProbeInterface, ProbeView>.StreamError);

    [Fact]
    public void TransportFailureShapeAgreement_the_snapshot_and_subscription_variants_declare_one_shape()
    {
        DeclaredShape(SnapshotStreamError).Should().Be(
            DeclaredShape(SubscriptionStreamError),
            "the snapshot and the subscription StreamError carry the same transport failure, so a parameter " +
            "renamed, retyped, reordered or added on one of them has to be mirrored on the other");
    }

    [Fact]
    public void TransportFailureShapeAgreement_the_interface_family_variants_declare_that_same_shape()
    {
        DeclaredShape(InterfaceSnapshotStreamError).Should().Be(
            DeclaredShape(SnapshotStreamError),
            "an interface subscription reports a transport failure with the same shape a template " +
            "subscription does, so a consumer handling both families reads one vocabulary");
        DeclaredShape(InterfaceSubscriptionStreamError).Should().Be(
            DeclaredShape(SubscriptionStreamError),
            "the live interface stream carries the same transport failure shape");
    }

    [Fact]
    public void TransportFailureShapeAgreement_the_outcome_variant_opens_with_that_same_shape()
    {
        DeclaredShape(OutcomeInfraError).Should().StartWith(
            DeclaredShape(SubscriptionStreamError),
            "the infrastructure-failure outcome leads with the same transport failure ahead of its own extra " +
            "parameters, so renaming, retyping or reordering the leading ones on either side alone has to fail");
    }

    [Fact]
    public void TransportFailureShapeAgreement_every_stream_variant_ends_with_the_source_exception()
    {
        var sourceException =
            $"{typeof(Exception)}? {nameof(ExerciseOutcome<Probe>.InfraError.SourceException)}|";

        const string reason =
            "a stream consumer deciding retry policy needs the transport exception the write path " +
            "already hands it, carried last so the parameter order matches the outcome variant";

        DeclaredShape(SnapshotStreamError).Should().EndWith(sourceException, reason);
        DeclaredShape(SubscriptionStreamError).Should().EndWith(sourceException, reason);
        DeclaredShape(InterfaceSnapshotStreamError).Should().EndWith(sourceException, reason);
        DeclaredShape(InterfaceSubscriptionStreamError).Should().EndWith(sourceException, reason);
    }

    [Fact]
    public void TransportFailureShapeAgreement_a_renamed_parameter_stops_agreeing()
    {
        var renamedDeclaration = new StatusCodeRenamed(14, "unavailable").GetType();

        DeclaredShape(renamedDeclaration).Should().NotBe(
            DeclaredShape(SnapshotStreamError),
            "renaming a single parameter has to break the comparison, otherwise the agreement assertions " +
            "would hold against a one-sided edit and catch nothing");
    }

    [Fact]
    public void TransportFailureShapeAgreement_a_parameter_name_that_extends_a_pinned_one_stops_agreeing()
    {
        var extendedDeclaration = new MessageRenamedToMessageDetail(14, "unavailable").GetType();

        DeclaredShape(extendedDeclaration).Should().NotStartWith(
            DeclaredShape(SnapshotStreamError),
            "a parameter name that merely extends the pinned one has to fall outside the prefix comparison, " +
            "otherwise a one-sided rename would slip past the outcome-variant assertion");
    }

    [Fact]
    public void TransportFailureShapeAgreement_reads_a_non_empty_shape_from_every_declaration()
    {
        DeclaredShape(SnapshotStreamError).Should().NotBeEmpty(VacuityReason);
        DeclaredShape(SubscriptionStreamError).Should().NotBeEmpty(VacuityReason);
        DeclaredShape(OutcomeInfraError).Should().NotBeEmpty(VacuityReason);
    }

    private static string DeclaredShape(Type declaration)
    {
        var nullability = new NullabilityInfoContext();

        return string.Concat(declaration.GetConstructors()
            .OrderByDescending(constructor => constructor.GetParameters().Length)
            .First()
            .GetParameters()
            .Select(parameter =>
                $"{parameter.ParameterType}{NullableMarker(nullability, parameter)} {parameter.Name}|"));
    }

    private static string NullableMarker(NullabilityInfoContext nullability, ParameterInfo parameter) =>
        nullability.Create(parameter).WriteState == NullabilityState.Nullable ? "?" : string.Empty;

    private sealed record StatusCodeRenamed(int Status, string Message);

    private sealed record MessageRenamedToMessageDetail(
        int StatusCode,
        string MessageDetail,
        DamlErrorCategory? Category = null,
        Exception? SourceException = null);

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
