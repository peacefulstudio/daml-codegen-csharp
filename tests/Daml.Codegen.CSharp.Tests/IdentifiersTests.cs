// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class IdentifiersTests
{
    [Theory]
    [InlineData("Period", "Period", "Period_")]
    [InlineData("Other", "Period", "Other")]
    [InlineData("Period", "Other", "Period")]
    [InlineData("Period", "", "Period")]
    public void Disambiguate_appends_underscore_only_when_identifier_equals_enclosing_type(
        string identifier, string enclosingTypeName, string expected)
    {
        Identifiers.Disambiguate(identifier, enclosingTypeName).Should().Be(expected);
    }

    [Theory]
    [InlineData("period", "Period", "Period_")]
    [InlineData("other", "Period", "Other")]
    [InlineData("period", "", "Period")]
    public void MemberName_pascal_cases_then_disambiguates_against_enclosing_type(
        string damlFieldName, string enclosingTypeName, string expected)
    {
        Identifiers.MemberName(damlFieldName, enclosingTypeName).Should().Be(expected);
    }

    [Fact]
    public void MemberName_known_limitation_field_named_period_underscore_collides_with_disambiguated_Period()
    {
        Identifiers.MemberName("period_", "Period").Should().Be("Period_");
    }
}
