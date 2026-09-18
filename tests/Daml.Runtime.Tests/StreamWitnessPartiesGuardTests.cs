// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Text.Json;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Daml.Runtime.Streams;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

/// <summary>
/// The four <c>Streams/</c> event/entry unions carry a <c>WitnessParties</c> member on every
/// contract-bearing arm. Its guarantee — never <c>null</c>, compared by content — is the
/// member's declared type, <see cref="EquatableArray{T}"/>, so a future arm keeps it by
/// declaring that type and loses it by declaring anything else. Discovers every closed
/// variant of the four families through reflection rather than a hand-listed set, so a
/// variant that regresses to an <see cref="IReadOnlyList{T}"/> member fails here instead of
/// shipping a member that can be nulled again.
/// </summary>
public class StreamWitnessPartiesGuardTests
{
    public static TheoryData<Type> WitnessPartiesVariants()
    {
        var data = new TheoryData<Type>();
        foreach (var variant in DiscoverVariants())
        {
            data.Add(variant);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(WitnessPartiesVariants))]
    public void WitnessParties_is_declared_as_an_EquatableArray_of_Party(Type recordType)
    {
        var parameter = recordType.GetConstructors().Single()
            .GetParameters()
            .Single(p => p.Name == "WitnessParties");

        parameter.ParameterType.Should().Be<EquatableArray<Party>>();
        recordType.GetProperty("WitnessParties")!.PropertyType.Should().Be<EquatableArray<Party>>();
    }

    /// <summary>
    /// Proves the sweep is not vacuous by asserting the exact member count,
    /// rather than only "more than zero" — a discovery bug that silently dropped every
    /// variant would otherwise pass the theory above by finding nothing to test.
    /// </summary>
    [Fact]
    public void Sweep_finds_exactly_the_twelve_witness_party_members()
    {
        DiscoverVariants().Should().HaveCount(12);
    }

    private static IEnumerable<Type> DiscoverVariants()
    {
        Type[] families =
        [
            typeof(ContractStreamEvent<TestTemplate>),
            typeof(InterfaceStreamEvent<TestInterface, TestView>),
            typeof(AcsSnapshotEntry<TestTemplate>),
            typeof(InterfaceAcsSnapshotEntry<TestInterface, TestView>),
        ];

        foreach (var family in families)
        {
            foreach (var nested in family.GetNestedTypes(BindingFlags.Public))
            {
                if (!nested.IsSealed)
                {
                    continue;
                }

                var closed = nested.IsGenericTypeDefinition
                    ? nested.MakeGenericType(family.GetGenericArguments())
                    : nested;

                var ctor = closed.GetConstructors().SingleOrDefault();
                if (ctor is not null && ctor.GetParameters().Any(p => p.Name == "WitnessParties"))
                {
                    yield return closed;
                }
            }
        }
    }

    private sealed record TestTemplate : ITemplate, IDamlRecord<TestTemplate>
    {
        public static Identifier TemplateId { get; } = new("pkg", "M", "TestTemplate");
        public static string PackageId => "pkg";
        public static string PackageName => "test";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create();

        public static TestTemplate FromRecord(DamlRecord record) => new();

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            throw new NotSupportedException();
    }

    private interface TestInterface : IDamlInterface, IHasView<TestView>
    {
        static Identifier IDamlInterface.InterfaceId => new("pkg", "M", "TestInterface");
        static string IDamlInterface.PackageId => "pkg";
        static string IDamlInterface.PackageName => "test";
        static Version IDamlInterface.PackageVersion => new(0, 1, 0);
        static DamlTypeDescriptor IDamlType.DamlTypeId =>
            new(new Identifier("pkg", "M", "TestInterface"), DamlTypeKind.Interface, "test");
    }

    private sealed record TestView : IDamlRecord<TestView>
    {
        public DamlRecord ToRecord() => DamlRecord.Create();

        public static TestView FromRecord(DamlRecord record) => new();

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            throw new NotSupportedException();
    }
}
