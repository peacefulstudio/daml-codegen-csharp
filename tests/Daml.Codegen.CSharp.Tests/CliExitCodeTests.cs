// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Daml.Codegen.CSharp.Cli;
using FluentAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class CliExitCodeTests
{
    [Fact]
    public async Task no_args_returns_nonzero_exit_code()
    {
        var exit = await Program.Main([]);
        exit.Should().NotBe(0,
            "the CLI must fail loudly when given neither --intermediate nor any positional dar-files (review #6)");
    }

    [Fact]
    public async Task only_unknown_args_returns_nonzero_exit_code()
    {
        var exit = await Program.Main(["--unknown-flag"]);
        exit.Should().NotBe(0,
            "System.CommandLine should surface a parse error and the action must not paper over it (review #6)");
    }

    [Fact]
    public async Task no_args_returns_exit_code_one()
    {
        var exit = await Program.Main([]);
        exit.Should().Be(1,
            "RunCodegen returns 1 on the missing-args path; an exact-value assertion catches SetAction overload misbinding (Task<int> -> Task) that the looser !=0 check would miss if the int got coerced to 0");
    }
}
