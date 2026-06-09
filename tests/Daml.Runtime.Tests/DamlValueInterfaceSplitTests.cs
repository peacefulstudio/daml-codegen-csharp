// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using FluentAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

/// <summary>
/// Tests for the <see cref="IDamlValue"/> split into <see cref="IDamlRecord"/> and
/// <see cref="IDamlVariant"/> (issue #271). <see cref="IDamlValue"/> becomes a bare
/// marker; record-shaped and variant-shaped values carry their own conversion member.
/// </summary>
public class DamlValueInterfaceSplitTests
{
    [Fact]
    public void IDamlValue_is_a_bare_marker_with_no_members()
    {
        typeof(IDamlValue).GetMembers().Should().BeEmpty();
    }

    [Fact]
    public void IDamlRecord_extends_IDamlValue()
    {
        typeof(IDamlValue).IsAssignableFrom(typeof(IDamlRecord)).Should().BeTrue();
    }

    [Fact]
    public void IDamlVariant_extends_IDamlValue()
    {
        typeof(IDamlValue).IsAssignableFrom(typeof(IDamlVariant)).Should().BeTrue();
    }

    [Fact]
    public void IDamlRecord_carries_ToRecord()
    {
        typeof(IDamlRecord).GetMethod(nameof(IDamlRecord.ToRecord)).Should().NotBeNull();
    }

    [Fact]
    public void IDamlVariant_carries_ToVariant()
    {
        typeof(IDamlVariant).GetMethod(nameof(IDamlVariant.ToVariant)).Should().NotBeNull();
    }

    [Fact]
    public void ITemplate_extends_IDamlRecord()
    {
        typeof(IDamlRecord).IsAssignableFrom(typeof(ITemplate)).Should().BeTrue();
    }

    [Fact]
    public void IDamlInterface_extends_IDamlRecord()
    {
        typeof(IDamlRecord).IsAssignableFrom(typeof(IDamlInterface)).Should().BeTrue();
    }
}
