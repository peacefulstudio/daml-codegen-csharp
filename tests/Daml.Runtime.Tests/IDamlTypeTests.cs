// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using AwesomeAssertions;
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

        typeof(IDamlType).IsAssignableFrom(template.GetType()).Should().BeTrue();
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
        // with a concrete ITemplate. This is the migration story for interface markers.
        var id = IdentityOf<SampleTemplate>();

        id.Should().Be<SampleTemplate>();
    }

    [Fact]
    public void DamlTypeId_for_template_marker_carries_template_identifier_kind_and_package_name()
    {
        var descriptor = SampleTemplate.DamlTypeId;

        descriptor.Identifier.Should().Be(new RuntimeIdentifier("test-pkg", "Sample.Module", "SampleTemplate"));
        descriptor.Kind.Should().Be(DamlTypeKind.Template);
        descriptor.PackageName.Should().Be("test-package");
    }

    [Fact]
    public void DamlTypeId_for_interface_marker_carries_interface_identifier_kind_and_package_name()
    {
        var descriptor = DescriptorOf<ISampleInterface>();

        descriptor.Identifier.Should().Be(new RuntimeIdentifier("test-pkg", "Sample.Module", "ISampleInterface"));
        descriptor.Kind.Should().Be(DamlTypeKind.Interface);
        descriptor.PackageName.Should().Be("test-package");
    }

    private static DamlTypeDescriptor DescriptorOf<T>() where T : IDamlType => T.DamlTypeId;

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
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("owner", new DamlParty(Owner)));
    }

    private interface ISampleInterface : IDamlInterface
    {
        static RuntimeIdentifier IDamlInterface.InterfaceId => new("test-pkg", "Sample.Module", "ISampleInterface");
        static string IDamlInterface.PackageId => "test-pkg";
        static string IDamlInterface.PackageName => "test-package";
        static Version IDamlInterface.PackageVersion => new(0, 1, 0);
        static DamlTypeDescriptor global::Daml.Runtime.IDamlType.DamlTypeId =>
            new(new RuntimeIdentifier("test-pkg", "Sample.Module", "ISampleInterface"), DamlTypeKind.Interface, "test-package");
    }
}
