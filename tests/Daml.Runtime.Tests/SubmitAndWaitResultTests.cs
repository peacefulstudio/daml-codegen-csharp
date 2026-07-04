// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class SubmitAndWaitResultTests
{
    [Fact]
    public void SubmitAndWaitResult_carries_command_id_update_id_and_completion_offset()
    {
        var result = new SubmitAndWaitResult(new CommandId("cmd-1"), "update-1", 42L);

        result.CommandId.Value.Should().Be("cmd-1");
        result.UpdateId.Should().Be("update-1");
        result.CompletionOffset.Should().Be(42L);
    }
}
