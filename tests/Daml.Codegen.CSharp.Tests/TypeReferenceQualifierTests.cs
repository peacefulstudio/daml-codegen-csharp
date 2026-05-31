// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Daml.Codegen.CSharp.CodeGen;
using FluentAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class TypeReferenceQualifierTests
{
    [Fact]
    public void all_namespaces_expands_a_leaf_into_every_ancestor_prefix()
    {
        var qualifier = new TypeReferenceQualifier(["Canton.Party.Replication"]);

        qualifier.AllNamespaces.Should().BeEquivalentTo(
            "Canton", "Canton.Party", "Canton.Party.Replication");
    }

    [Fact]
    public void qualify_returns_the_bare_name_when_no_namespace_collides()
    {
        var qualifier = new TypeReferenceQualifier(["Splice.Api.Token.Holding.V1"]);

        qualifier.Qualify("ContractId", "Splice.Api.Token.Holding.V1")
            .Should().Be("ContractId");
    }

    [Fact]
    public void qualify_global_qualifies_party_when_a_namespace_segment_shadows_it()
    {
        var qualifier = new TypeReferenceQualifier(["Canton.Party.Replication"]);

        qualifier.Qualify("Party", "Canton.Party.Replication")
            .Should().Be("global::Daml.Runtime.Data.Party");
    }

    [Fact]
    public void qualify_leaves_party_bare_when_no_namespace_segment_shadows_it()
    {
        var qualifier = new TypeReferenceQualifier(["Splice.Api.Token.Holding.V1"]);

        qualifier.Qualify("Party", "Splice.Api.Token.Holding.V1")
            .Should().Be("Party");
    }

    [Fact]
    public void qualify_does_not_double_prefix_an_already_global_qualified_name()
    {
        var qualifier = new TypeReferenceQualifier(["Canton.Party.Replication"]);

        qualifier.Qualify("global::Daml.Runtime.Data.Party", "Canton.Party.Replication")
            .Should().Be("global::Daml.Runtime.Data.Party");
    }

    [Fact]
    public void qualify_leaves_unregistered_local_names_unchanged()
    {
        var qualifier = new TypeReferenceQualifier(["Canton.Party.Replication"]);

        qualifier.Qualify("Replication", "Canton.Party.Replication")
            .Should().Be("Replication");
    }
}
