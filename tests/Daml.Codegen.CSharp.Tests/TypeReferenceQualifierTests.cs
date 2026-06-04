// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

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

    [Theory]
    [InlineData("ExerciseCommand", "Daml.Runtime.Commands")]
    [InlineData("CommandsSubmission", "Daml.Runtime.Commands")]
    [InlineData("IDamlInterface", "Daml.Runtime.Contracts")]
    [InlineData("IHasView", "Daml.Runtime.Contracts")]
    [InlineData("CreatedEvent", "Daml.Runtime.Contracts")]
    public void qualify_global_qualifies_command_and_contract_names_when_a_namespace_segment_shadows_it(
        string simpleName, string owningNamespace)
    {
        var qualifier = new TypeReferenceQualifier([$"Acme.{simpleName}.V1"]);

        qualifier.Qualify(simpleName, $"Acme.{simpleName}.V1")
            .Should().Be($"global::{owningNamespace}.{simpleName}");
    }

    [Theory]
    [InlineData("ExerciseCommand")]
    [InlineData("CommandsSubmission")]
    [InlineData("IDamlInterface")]
    [InlineData("IHasView")]
    [InlineData("CreatedEvent")]
    public void qualify_leaves_command_and_contract_names_bare_when_no_namespace_segment_shadows_it(
        string simpleName)
    {
        var qualifier = new TypeReferenceQualifier(["Splice.Api.Token.Holding.V1"]);

        qualifier.Qualify(simpleName, "Splice.Api.Token.Holding.V1")
            .Should().Be(simpleName);
    }

    [Theory]
    [InlineData("RelTime")]
    [InlineData("Tuple2")]
    [InlineData("Tuple3")]
    [InlineData("Either")]
    [InlineData("Set")]
    [InlineData("NonEmpty")]
    [InlineData("Map")]
    [InlineData("Unit")]
    [InlineData("GenericStub")]
    public void qualify_global_qualifies_stdlib_names_when_a_namespace_segment_shadows_it(
        string simpleName)
    {
        var qualifier = new TypeReferenceQualifier([$"Acme.{simpleName}.V1"]);

        qualifier.Qualify(simpleName, $"Acme.{simpleName}.V1")
            .Should().Be($"global::Daml.Runtime.Stdlib.{simpleName}");
    }

    [Theory]
    [InlineData("RelTime")]
    [InlineData("Tuple2")]
    [InlineData("Tuple3")]
    [InlineData("Either")]
    [InlineData("Set")]
    [InlineData("NonEmpty")]
    [InlineData("Map")]
    [InlineData("Unit")]
    [InlineData("GenericStub")]
    public void qualify_leaves_stdlib_names_bare_when_no_namespace_segment_shadows_it(
        string simpleName)
    {
        var qualifier = new TypeReferenceQualifier(["My.Package.Module"]);

        qualifier.Qualify(simpleName, "My.Package.Module")
            .Should().Be(simpleName);
    }
}
