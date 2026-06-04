// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using FluentAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class SynchronizerIdFieldTypingTests
{
    private static readonly SynchronizerId Source = new("global_sync::abc::35-0");
    private static readonly SynchronizerId Target = new("local_sync::def::35-0");
    private static readonly Party Alice = new("alice");

    [Fact]
    public void Assigned_Source_should_be_typed_as_SynchronizerId()
    {
        var ev = new ContractStreamEvent<TestTemplate>.Assigned(
            new ContractId<TestTemplate>("c1"),
            DamlRecord.Create(),
            4L,
            Source,
            Target,
            [Alice]);

        ev.Source.Should().BeOfType<SynchronizerId>().And.Be(Source);
    }

    [Fact]
    public void Assigned_Target_should_be_typed_as_SynchronizerId()
    {
        var ev = new ContractStreamEvent<TestTemplate>.Assigned(
            new ContractId<TestTemplate>("c1"),
            DamlRecord.Create(),
            4L,
            Source,
            Target,
            [Alice]);

        ev.Target.Should().BeOfType<SynchronizerId>().And.Be(Target);
    }

    [Fact]
    public void Unassigned_Source_should_be_typed_as_SynchronizerId()
    {
        var ev = new ContractStreamEvent<TestTemplate>.Unassigned(
            new ContractId<TestTemplate>("c1"),
            5L,
            Source,
            Target,
            [Alice]);

        ev.Source.Should().BeOfType<SynchronizerId>().And.Be(Source);
    }

    [Fact]
    public void Unassigned_Target_should_be_typed_as_SynchronizerId()
    {
        var ev = new ContractStreamEvent<TestTemplate>.Unassigned(
            new ContractId<TestTemplate>("c1"),
            5L,
            Source,
            Target,
            [Alice]);

        ev.Target.Should().BeOfType<SynchronizerId>().And.Be(Target);
    }

    private sealed record TestTemplate(string Owner) : ITemplate
    {
        public static Identifier TemplateId { get; } = new("pkg", "M", "TestTemplate");
        public static string PackageId => "pkg";
        public static string PackageName => "test";
        public static Version PackageVersion { get; } = new(0, 1, 0);

        public DamlRecord ToRecord() => DamlRecord.Create();
    }
}
