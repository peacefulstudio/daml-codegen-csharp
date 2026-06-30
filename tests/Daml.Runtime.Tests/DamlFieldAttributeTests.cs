// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Reflection;
using Daml.Runtime.Data;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

/// <summary>
/// Tests for <see cref="DamlFieldAttribute"/>, the codegen-owned metadata that
/// captures the original Daml field name on each generated property.
/// </summary>
public class DamlFieldAttributeTests
{
    [Fact]
    public void Ctor_sets_Name()
    {
        new DamlFieldAttribute("owner").Name.Should().Be("owner");
    }

    [Fact]
    public void Is_an_attribute()
    {
        typeof(Attribute).IsAssignableFrom(typeof(DamlFieldAttribute)).Should().BeTrue();
    }

    [Fact]
    public void Targets_only_properties_without_multiple_or_inheritance()
    {
        var usage = typeof(DamlFieldAttribute).GetCustomAttribute<AttributeUsageAttribute>();

        usage.Should().NotBeNull();
        usage!.ValidOn.Should().Be(AttributeTargets.Property);
        usage.AllowMultiple.Should().BeFalse();
        usage.Inherited.Should().BeFalse();
    }
}
