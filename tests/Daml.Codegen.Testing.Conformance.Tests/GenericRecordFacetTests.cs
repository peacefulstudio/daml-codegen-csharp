// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.Richtypes;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

/// <summary>
/// Generated types satisfy <see cref="IDamlRecord{TSelf}"/>, so callers can materialize
/// them generically through the static abstract factory instead of reflection. The two
/// materialization cases are not duplicates: <c>Asset</c> exercises the template
/// emitter's stamp and <c>HoldingView</c> the record emitter's.
/// </summary>
public class GenericRecordFacetTests
{
    private static T Materialize<T>(DamlRecord record) where T : IDamlRecord<T> =>
        T.FromRecord(record);

    [Fact]
    public void Asset_should_materialize_generically_through_IDamlRecord_TSelf_facet()
    {
        var original = new Asset(new Party("alice"), 42m);

        Materialize<Asset>(original.ToRecord()).Should().Be(original);
    }

    [Fact]
    public void HoldingView_should_materialize_generically_through_IDamlRecord_TSelf_facet()
    {
        var original = new HoldingView(42m);

        Materialize<HoldingView>(original.ToRecord()).Should().Be(original);
    }

    [Fact]
    public void GenericRecordFacet_every_generated_IDamlRecord_TSelf_implementor_should_name_itself()
    {
        var facets = (
            from type in typeof(Asset).Assembly.GetTypes()
            from facet in type.GetInterfaces()
            where facet.IsGenericType && facet.GetGenericTypeDefinition() == typeof(IDamlRecord<>)
            select (Implementor: type, Self: facet.GetGenericArguments()[0])).ToList();

        facets.Should().NotBeEmpty();
        facets.Should().OnlyContain(f => f.Implementor == f.Self);
    }
}
