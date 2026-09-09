// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public sealed class MarkdownCodeFenceTests
{
    [Fact]
    public void MarkdownCodeFence_reads_each_fence_with_its_language_and_opening_line_number()
    {
        var fences = MarkdownCodeFence.ReadFrom(
            """
            # Title

            ```bash
            dotnet add package Daml.Runtime
            ```

            ```csharp
            var one = 1;
            ```
            """);

        fences.Should().BeEquivalentTo([
            new MarkdownCodeFence(3, "bash", "dotnet add package Daml.Runtime", null),
            new MarkdownCodeFence(7, "csharp", "var one = 1;", null),
        ]);
    }

    [Fact]
    public void MarkdownCodeFence_reads_a_fence_indented_under_a_list_item()
    {
        var fences = MarkdownCodeFence.ReadFrom(
            """
            1. Bind the alias:

               ```csharp
               using IouContract = Iou.Iou;
               ```
            """);

        fences.Should().ContainSingle()
            .Which.Language.Should().Be("csharp");
    }

    [Fact]
    public void MarkdownCodeFence_carries_the_reason_from_an_exclusion_marker_above_the_fence()
    {
        var fences = MarkdownCodeFence.ReadFrom(
            """
            <!-- snippet: not-compiled: it elides the surrounding method with an ellipsis -->
            ```csharp
            // ...
            ```
            """);

        fences.Should().ContainSingle()
            .Which.ExclusionReason.Should().Be("it elides the surrounding method with an ellipsis");
    }

    [Fact]
    public void MarkdownCodeFence_rejects_an_exclusion_marker_that_states_no_reason()
    {
        var readWithoutAReason = () => MarkdownCodeFence.ReadFrom(
            """
            <!-- snippet: not-compiled -->
            ```csharp
            // ...
            ```
            """);

        readWithoutAReason.Should().Throw<InvalidDataException>()
            .WithMessage("*line 1*")
            .WithMessage("*reason*");
    }
}
