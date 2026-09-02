// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

/// <summary>
/// Tests for <see cref="ViewDescriptor{TInterface, TView}"/>: the pure type witness a
/// generated interface marker exposes as its static <c>View</c> property, pairing the
/// marker with its view record so generic call sites infer both type parameters from
/// one argument.
/// </summary>
public class ViewDescriptorTests
{
    private const string TestPackageId = "witness-package-id";
    private const string TestPackageName = "witness-package";
    private const string TestModuleName = "Witness.Module";

    private interface ITestHolding : IDamlInterface, IHasView<TestHoldingView>
    {
        static Identifier IDamlInterface.InterfaceId => new(TestPackageId, TestModuleName, "TestHolding");
        static string IDamlInterface.PackageId => TestPackageId;
        static string IDamlInterface.PackageName => TestPackageName;
        static Version IDamlInterface.PackageVersion => new(1, 0, 0);
        static DamlTypeDescriptor global::Daml.Runtime.IDamlType.DamlTypeId =>
            new(new Identifier(TestPackageId, TestModuleName, "TestHolding"), DamlTypeKind.Interface, TestPackageName);

        static ViewDescriptor<ITestHolding, TestHoldingView> View { get; } = new();

        decimal Amount { get; }
    }

    private sealed record TestHoldingView(decimal Amount) : ITestHolding, IDamlRecord<TestHoldingView>
    {
        public DamlRecord ToRecord() =>
            DamlRecord.Create(DamlField.Create("amount", new DamlNumeric(Amount)));

        public static TestHoldingView FromRecord(DamlRecord record) =>
            new(record.GetRequiredField("amount").As<DamlNumeric>().Value);
    }

    private static (Type Interface, Type View) InferredPair<TInterface, TView>(ViewDescriptor<TInterface, TView> descriptor)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>
    {
        descriptor.Should().NotBeNull();
        return (typeof(TInterface), typeof(TView));
    }

    private static DamlTypeDescriptor IdentityOf<T>() where T : IDamlType => T.DamlTypeId;

    [Fact]
    public void ViewDescriptor_infers_both_type_parameters_from_the_marker_witness()
    {
        var pair = InferredPair(ITestHolding.View);

        pair.Should().Be((typeof(ITestHolding), typeof(TestHoldingView)));
    }

    [Fact]
    public void ViewDescriptor_view_record_answers_with_the_interface_identity()
    {
        var identity = IdentityOf<TestHoldingView>();

        identity.Should().Be(IdentityOf<ITestHolding>());
        identity.Kind.Should().Be(DamlTypeKind.Interface);
    }

    [Fact]
    public void ViewDescriptor_view_fields_are_readable_through_a_marker_typed_variable()
    {
        ITestHolding marker = new TestHoldingView(42.5m);

        marker.Amount.Should().Be(42.5m);
    }

    [Fact]
    public void ViewDescriptor_view_record_round_trips_through_the_record_facet()
    {
        var record = new TestHoldingView(7.25m).ToRecord();

        DamlRecordFacet.Materialize<TestHoldingView>(record).Should().Be(new TestHoldingView(7.25m));
    }
}
