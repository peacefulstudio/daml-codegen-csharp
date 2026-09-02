// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

/// <summary>
/// Closes a gap in the borrowed-collection guard: <c>Contracts/</c> event records guard every
/// collection member at both the primary constructor and the <c>init</c> accessor, but
/// the four <c>Streams/</c> event/entry unions had <c>WitnessParties</c> members with no
/// guard at all. Discovers every closed variant of the four families through reflection
/// rather than a hand-listed set, so a future variant that forgets the guard fails this
/// test instead of shipping a silent <see cref="NullReferenceException"/> downstream.
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
    public void Constructor_rejects_a_null_WitnessParties(Type recordType)
    {
        var ctor = recordType.GetConstructors().Single();
        var parameters = ctor.GetParameters();
        var witnessIndex = Array.FindIndex(parameters, p => p.Name == "WitnessParties");
        var args = parameters.Select(p => DummyValue(p.ParameterType)).ToArray();
        args[witnessIndex] = null;

        var act = () => ctor.Invoke(args);

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>()
            .Which.ParamName.Should().Be("WitnessParties");
    }

    [Theory]
    [MemberData(nameof(WitnessPartiesVariants))]
    public void Init_accessor_rejects_a_null_WitnessParties(Type recordType)
    {
        var ctor = recordType.GetConstructors().Single();
        var args = ctor.GetParameters().Select(p => DummyValue(p.ParameterType)).ToArray();
        var instance = ctor.Invoke(args);
        var setter = recordType.GetProperty("WitnessParties")!.GetSetMethod(nonPublic: true)!;

        var act = () => setter.Invoke(instance, [null]);

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>()
            .Which.ParamName.Should().Be("WitnessParties");
    }

    /// <summary>
    /// Proves the sweep is not vacuous by asserting the exact member count,
    /// rather than only "more than zero" — a discovery bug that silently dropped every
    /// variant would otherwise pass both theories above by finding nothing to test.
    /// </summary>
    [Fact]
    public void Sweep_finds_exactly_the_twelve_unguarded_members()
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

    private static object? DummyValue(Type type)
    {
        if (type == typeof(string))
        {
            return "s";
        }

        if (type == typeof(bool))
        {
            return true;
        }

        if (type == typeof(long))
        {
            return 1L;
        }

        if (type == typeof(DamlValue))
        {
            return DamlUnit.Instance;
        }

        if (type == typeof(LedgerOffset))
        {
            return LedgerOffset.At(1);
        }

        if (type == typeof(SynchronizerId))
        {
            return new SynchronizerId("sync");
        }

        if (type == typeof(IReadOnlyList<Party>))
        {
            return new List<Party> { new("alice") };
        }

        if (type == typeof(ContractKey))
        {
            return new ContractKey(DamlUnit.Instance);
        }

        if (type == typeof(TestTemplate))
        {
            return new TestTemplate();
        }

        if (type == typeof(TestView))
        {
            return new TestView();
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ContractId<>))
        {
            return Activator.CreateInstance(type, "00c");
        }

        throw new NotSupportedException(
            $"No dummy-value factory registered for parameter type '{type}'. Add one so this "
            + "sweep can keep exercising every WitnessParties-carrying variant.");
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
    }
}
