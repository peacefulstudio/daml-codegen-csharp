// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.Cli;
using FluentAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Cli.Tests;

public class CliExitCodeTests
{
    [Fact]
    public async Task only_unknown_args_returns_nonzero_exit_code()
    {
        var exit = await Program.Main(["--unknown-flag"]);
        exit.Should().NotBe(0,
            "System.CommandLine should surface a parse error and the action must not paper over it");
    }

    [Fact]
    public async Task no_args_returns_exit_code_one()
    {
        var exit = await Program.Main([]);
        exit.Should().Be(1,
            "--intermediate is a required option, so a no-args invocation must fail at parse time; an exact-value assertion catches SetAction overload misbinding (Task<int> -> Task) that the looser !=0 check would miss if the int got coerced to 0");
    }

    [Fact]
    public void Program_is_not_part_of_the_public_API_surface()
    {
        typeof(Program).IsPublic.Should().BeFalse(
            "the CLI entry point is an executable, not a library surface; nothing outside the binary (and its test assemblies) may bind to it");
    }
}
