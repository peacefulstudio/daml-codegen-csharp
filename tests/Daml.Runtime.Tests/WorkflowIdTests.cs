// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
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

    [Fact]
    public void Json_should_serialize_as_a_bare_string()
    {
        JsonSerializer.Serialize(new WorkflowId("wf-1")).Should().Be("\"wf-1\"");
    }

    [Fact]
    public void Json_should_read_back_the_id_it_wrote()
    {
        var restored = JsonSerializer.Deserialize<WorkflowId>(JsonSerializer.Serialize(new WorkflowId("wf-1")));

        restored.Value.Should().Be("wf-1");
    }

    [Fact]
    public void Json_should_read_back_the_empty_id_the_Ledger_API_permits()
    {
        var restored = JsonSerializer.Deserialize<WorkflowId>(JsonSerializer.Serialize(new WorkflowId("")));

        restored.Value.Should().BeEmpty();
    }

    [Fact]
    public void Json_should_read_back_a_padded_id_verbatim()
    {
        var restored = JsonSerializer.Deserialize<WorkflowId>(
            JsonSerializer.Serialize(new WorkflowId(" padded ")));

        restored.Value.Should().Be(" padded ");
    }

    [Fact]
    public void Json_should_throw_on_non_string_token()
    {
        var act = () => JsonSerializer.Deserialize<WorkflowId>("1");

        act.Should().Throw<JsonException>().WithMessage("*WorkflowId*Number*");
    }

    [Fact]
    public void Json_should_throw_on_null_for_a_non_nullable_member()
    {
        var act = () => JsonSerializer.Deserialize<Holder>("{\"Id\":null}");

        act.Should().Throw<JsonException>().WithMessage("*WorkflowId*Null*");
    }

    [Fact]
    public void Json_should_read_an_optional_member_back_with_the_id_it_correlates_on()
    {
        var submission = new CommandsSubmission([]).WithWorkflowId(new WorkflowId("wf-1"));

        var restored = JsonSerializer.Deserialize<CommandsSubmission>(
            JsonSerializer.Serialize(submission));

        restored!.WorkflowId.Should().Be(new WorkflowId("wf-1"));
    }

    private sealed record Holder(WorkflowId Id);
}
