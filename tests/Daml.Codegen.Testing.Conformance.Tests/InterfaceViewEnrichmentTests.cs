// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.Richtypes;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

/// <summary>
/// The generated interface marker is enriched for the typed interface/view family:
/// it exposes the static <see cref="ViewDescriptor{TInterface, TView}"/> witness, mirrors
/// the view's fields as instance properties, and the view record implements the marker so
/// it answers with the interface's identity in identity-keyed generic code.
/// </summary>
public class InterfaceViewEnrichmentTests
{
    private static (Type Interface, Type View) InferredPair<TInterface, TView>(ViewDescriptor<TInterface, TView> descriptor)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView>
    {
        descriptor.Should().NotBeNull();
        return (typeof(TInterface), typeof(TView));
    }

    private static DamlTypeDescriptor IdentityOf<T>() where T : IDamlType => T.DamlTypeId;

    [Fact]
    public void IHolding_View_witness_infers_the_marker_and_view_pair()
    {
        var pair = InferredPair(IHolding.View);

        pair.Should().Be((typeof(IHolding), typeof(HoldingView)));
    }

    [Fact]
    public void HoldingView_answers_with_the_interface_identity()
    {
        var identity = IdentityOf<HoldingView>();

        identity.Should().Be(IdentityOf<IHolding>());
        identity.Kind.Should().Be(DamlTypeKind.Interface);
        identity.Identifier.EntityName.Should().Be("Holding");
    }

    [Fact]
    public void HoldingView_amount_is_readable_through_a_marker_typed_variable()
    {
        IHolding marker = new HoldingView(42.5m);

        marker.Amount.Should().Be(42.5m);
    }
}
