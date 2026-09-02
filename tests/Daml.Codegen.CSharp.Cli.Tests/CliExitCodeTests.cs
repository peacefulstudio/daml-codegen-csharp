// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.Cli;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Cli.Tests;

[Collection("ConsoleRedirection")]
public class CliExitCodeTests
{
    [Fact]
    public async Task Program_only_unknown_args_returns_nonzero_exit_code()
    {
        var exit = await Program.Main(["--unknown-flag"]);
        exit.Should().NotBe(0,
            "System.CommandLine should surface a parse error and the action must not paper over it");
    }

    [Fact]
    public async Task Program_no_args_returns_exit_code_one()
    {
        var exit = await Program.Main([]);
        exit.Should().Be(1,
            "--intermediate is a required option, so a no-args invocation must fail at parse time; an exact-value assertion catches SetAction overload misbinding (Task<int> -> Task) that the looser !=0 check would miss if the int got coerced to 0");
    }

    [Theory]
    [InlineData("--target-framework", "", "--target-framework rejects empty/whitespace-only strings at the CLI boundary; an empty TFM produces a broken .csproj that fails late with a confusing dotnet error")]
    [InlineData("--target-framework", "   ", "--target-framework rejects whitespace-only values at the CLI boundary")]
    [InlineData("--runtime-version", "", "--runtime-version rejects empty/whitespace-only strings when explicitly supplied; an empty version string breaks the generated PackageReference attribute")]
    [InlineData("--runtime-version", "   ", "--runtime-version rejects whitespace-only values when explicitly supplied")]
    public async Task Program_blank_option_value_returns_nonzero_exit_code(
        string option, string value, string because)
    {
        var exit = await Program.Main([option, value]);
        exit.Should().NotBe(0, because);
    }

    [Fact]
    public void Program_is_not_part_of_the_public_API_surface()
    {
        typeof(Program).IsPublic.Should().BeFalse(
            "the CLI entry point is an executable, not a library surface; nothing outside the binary (and its test assemblies) may bind to it");
    }
}
