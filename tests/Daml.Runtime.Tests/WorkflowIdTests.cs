// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class WorkflowIdTests
{
    [Fact]
    public void Construct_should_store_value_verbatim()
    {
        var id = new WorkflowId("wf-1");
        id.Value.Should().Be("wf-1");
    }

    [Theory]
    [InlineData(null)]
    public void Construct_should_throw_when_value_is_null(string? value)
    {
        Action act = () => _ = new WorkflowId(value!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Construct_should_accept_empty_or_whitespace_verbatim(string value)
    {
        var id = new WorkflowId(value);
        id.Value.Should().Be(value);
    }

    [Fact]
    public void Default_uninitialized_value_should_throw_on_Value_access()
    {
        var defaulted = default(WorkflowId);
        Action act = () => _ = defaulted.Value;
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Explicit_conversion_to_string_should_return_value()
    {
        var id = new WorkflowId("wf-1");
        var s = (string)id;
        s.Should().Be("wf-1");
    }

    [Fact]
    public void Explicit_conversion_from_string_should_construct()
    {
        var id = (WorkflowId)"wf-1";
        id.Value.Should().Be("wf-1");
    }
}
