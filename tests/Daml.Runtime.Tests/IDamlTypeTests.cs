// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using FluentAssertions;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Daml.Runtime.Tests;

/// <summary>
/// Tests for <see cref="IDamlType"/>, the marker interface that <see cref="ITemplate"/>
/// and <see cref="IDamlInterface"/> both extend so that generic helpers which do not
/// dispatch on template-specific static metadata can constrain on the broader marker.
/// </summary>
public class IDamlTypeTests
{
    [Fact]
    public void ITemplate_is_assignable_to_IDamlType()
    {
        // The whole point of the marker is to be a common base; ITemplate must extend it.
        typeof(IDamlType).IsAssignableFrom(typeof(ITemplate)).Should().BeTrue();
    }

    [Fact]
    public void IDamlInterface_is_assignable_to_IDamlType()
    {
        // Sibling marker: future interface-typed exercise helpers will rely on this.
        typeof(IDamlType).IsAssignableFrom(typeof(IDamlInterface)).Should().BeTrue();
    }

    [Fact]
    public void Concrete_template_instance_is_an_IDamlType()
    {
        // A generated `record Foo : ITemplate` flows through `IDamlType`-constrained APIs
        // because ITemplate : IDamlType.
        ITemplate template = new SampleTemplate("alice");

        template.Should().BeAssignableTo<IDamlType>();
    }

    [Fact]
    public void ExerciseOutcome_accepts_arbitrary_IDamlType_payload()
    {
        // ExerciseOutcome<T> imposes no constraint on T, so any IDamlType (template or
        // interface marker) flows through unchanged. Compiles == passes.
        var template = new SampleTemplate("alice");
        var outcome = new ExerciseOutcome<SampleTemplate>.One(template);

        outcome.Result.Should().BeSameAs(template);
        AcceptDamlType(template);
    }

    [Fact]
    public void IDamlType_constrained_helper_compiles_for_concrete_template()
    {
        // Demonstrates that a generic helper widened to `where T : IDamlType` is callable
        // with a concrete ITemplate. This is the migration story for #67's interface markers.
        var id = IdentityOf<SampleTemplate>();

        id.Should().Be<SampleTemplate>();
    }

    /// <summary>Static helper standing in for a future <c>IDamlType</c>-constrained helper.</summary>
    private static Type IdentityOf<T>() where T : IDamlType => typeof(T);

    /// <summary>Pass-through that forces an instance through an <c>IDamlType</c> reference.</summary>
    private static void AcceptDamlType(IDamlType value)
    {
        value.Should().NotBeNull();
    }

    private sealed record SampleTemplate(string Owner) : ITemplate
    {
        public static RuntimeIdentifier TemplateId { get; } = new("test-pkg", "Sample.Module", "SampleTemplate");
        public static string PackageId => "test-pkg";
        public static string PackageName => "test-package";
        public static Version PackageVersion { get; } = new(0, 1, 0);

        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("owner", new DamlParty(Owner)));
    }
}
