// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public sealed class ContractIdToDamlValueInvariantTests
{
    private const string FixturePackageId = "invariant-package-id";
    private const string FixturePackageName = "invariant-package-name";
    private const string FixtureModuleName = "Invariant.Module";
    private static readonly Version FixturePackageVersion = new(1, 0, 0);

    private interface IInvariantInterfaceMarker : IDamlInterface
    {
        static Identifier IDamlInterface.InterfaceId =>
            new(FixturePackageId, FixtureModuleName, nameof(IInvariantInterfaceMarker));
        static string IDamlInterface.PackageId => FixturePackageId;
        static string IDamlInterface.PackageName => FixturePackageName;
        static Version IDamlInterface.PackageVersion => FixturePackageVersion;
        static DamlTypeDescriptor global::Daml.Runtime.IDamlType.DamlTypeId =>
            new(new Identifier(FixturePackageId, FixtureModuleName, nameof(IInvariantInterfaceMarker)), DamlTypeKind.Interface, FixturePackageName);
    }

    private const string PlaceholderThrowMessage =
        "'InvariantInterfacePlaceholder' is the C# placeholder for a Daml interface "
        + "and carries no template metadata.";

    private sealed record InvariantInterfacePlaceholder : ITemplate
    {
        public static Identifier TemplateId =>
            throw new InvalidOperationException(PlaceholderThrowMessage);
        public static string PackageId =>
            throw new InvalidOperationException(PlaceholderThrowMessage);
        public static string PackageName =>
            throw new InvalidOperationException(PlaceholderThrowMessage);
        public static Version PackageVersion =>
            throw new InvalidOperationException(PlaceholderThrowMessage);
        public static DamlTypeDescriptor DamlTypeId =>
            throw new InvalidOperationException(PlaceholderThrowMessage);

        public DamlRecord ToRecord() => DamlRecord.Create();
        public static InvariantInterfacePlaceholder FromRecord(DamlRecord record) => new();
    }

    private sealed record InvariantTemplate : ITemplate
    {
        public static Identifier TemplateId =>
            new(FixturePackageId, FixtureModuleName, nameof(InvariantTemplate));
        public static string PackageId => FixturePackageId;
        public static string PackageName => FixturePackageName;
        public static Version PackageVersion => FixturePackageVersion;
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create();
        public static InvariantTemplate FromRecord(DamlRecord record) => new();
    }

    [Fact]
    public void Interface_marker_ToDamlValue_does_not_throw_and_carries_value_and_InterfaceId()
    {
        var contractId = new ContractId<IInvariantInterfaceMarker>("marker-cid");

        DamlContractId? damlValue = null;
        var act = () => damlValue = contractId.ToDamlValue();

        act.Should().NotThrow();
        damlValue!.Value.Should().Be("marker-cid");
        damlValue.TemplateId.Should().Be(
            new Identifier(FixturePackageId, FixtureModuleName, nameof(IInvariantInterfaceMarker)));
    }

    [Fact]
    public void Interface_placeholder_ToDamlValue_throws_InvalidOperationException()
    {
        var contractId = new ContractId<InvariantInterfacePlaceholder>("placeholder-cid");

        var act = () => contractId.ToDamlValue();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Template_ToDamlValue_does_not_throw_and_carries_value_and_TemplateId()
    {
        var contractId = new ContractId<InvariantTemplate>("template-cid");

        DamlContractId? damlValue = null;
        var act = () => damlValue = contractId.ToDamlValue();

        act.Should().NotThrow();
        damlValue!.Value.Should().Be("template-cid");
        damlValue.TemplateId.Should().Be(
            new Identifier(FixturePackageId, FixtureModuleName, nameof(InvariantTemplate)));
    }
}
