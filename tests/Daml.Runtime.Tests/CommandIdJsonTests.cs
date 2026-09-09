// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Xunit;

namespace Daml.Runtime.Tests;

/// <summary>
/// Pins the wire shape of <see cref="CommandId"/> — the bare JSON string the Ledger API puts
/// on <c>command_id</c> — and the read that recovers it rather than yielding a value whose
/// <see cref="CommandId.Value"/> throws at the use site.
/// </summary>
public class CommandIdJsonTests
{
    [Fact]
    public void CommandId_serializes_as_a_bare_JSON_string()
    {
        JsonSerializer.Serialize(new CommandId("cmd-1")).Should().Be("\"cmd-1\"");
    }

    [Fact]
    public void CommandId_reads_back_the_id_it_wrote()
    {
        var restored = JsonSerializer.Deserialize<CommandId>(JsonSerializer.Serialize(new CommandId("cmd-1")));

        restored.Value.Should().Be("cmd-1");
    }

    [Fact]
    public void SubmitAndWaitResult_reads_back_the_command_id_and_offset_it_wrote()
    {
        var result = new SubmitAndWaitResult(new CommandId("cmd-1"), "update-1", LedgerOffset.At(6057));

        var restored = JsonSerializer.Deserialize<SubmitAndWaitResult>(JsonSerializer.Serialize(result));

        restored.Should().Be(result);
    }

    [Fact]
    public void SubmitAndWaitResult_writes_both_scalars_as_the_bare_values_they_wrap()
    {
        var result = new SubmitAndWaitResult(new CommandId("cmd-1"), "update-1", LedgerOffset.At(6057));

        JsonSerializer.Serialize(result).Should()
            .Be("{\"CommandId\":\"cmd-1\",\"UpdateId\":\"update-1\",\"CompletionOffset\":6057}");
    }

    [Fact]
    public void CommandId_rejects_a_non_string_token()
    {
        var act = () => JsonSerializer.Deserialize<CommandId>("1");

        act.Should().Throw<JsonException>().WithMessage("*CommandId*Number*");
    }

    [Fact]
    public void CommandId_rejects_an_empty_id()
    {
        var act = () => JsonSerializer.Deserialize<CommandId>("\"\"");

        act.Should().Throw<JsonException>().WithMessage("*CommandId*empty*");
    }

    [Fact]
    public void CommandId_rejects_a_whitespace_only_id()
    {
        var act = () => JsonSerializer.Deserialize<CommandId>("\"   \"");

        act.Should().Throw<JsonException>().WithMessage("*CommandId*whitespace*");
    }

    [Fact]
    public void CommandId_rejects_a_null_on_a_non_nullable_member()
    {
        var json = "{\"CommandId\":null,\"UpdateId\":\"update-1\",\"CompletionOffset\":6057}";

        var act = () => JsonSerializer.Deserialize<SubmitAndWaitResult>(json);

        act.Should().Throw<JsonException>().WithMessage("*CommandId*Null*");
    }
}
