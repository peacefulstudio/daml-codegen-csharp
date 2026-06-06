// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

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

    [Fact]
    public async Task empty_target_framework_returns_nonzero_exit_code()
    {
        var exit = await Program.Main(["--target-framework", ""]);
        exit.Should().NotBe(0,
            "--target-framework rejects empty/whitespace-only strings at the CLI boundary; an empty TFM produces a broken .csproj that fails late with a confusing dotnet error");
    }

    [Fact]
    public async Task whitespace_target_framework_returns_nonzero_exit_code()
    {
        var exit = await Program.Main(["--target-framework", "   "]);
        exit.Should().NotBe(0,
            "--target-framework rejects whitespace-only values at the CLI boundary");
    }

    [Fact]
    public async Task empty_runtime_version_returns_nonzero_exit_code()
    {
        var exit = await Program.Main(["--runtime-version", ""]);
        exit.Should().NotBe(0,
            "--runtime-version rejects empty/whitespace-only strings when explicitly supplied; an empty version string breaks the generated PackageReference attribute");
    }

    [Fact]
    public async Task whitespace_runtime_version_returns_nonzero_exit_code()
    {
        var exit = await Program.Main(["--runtime-version", "   "]);
        exit.Should().NotBe(0,
            "--runtime-version rejects whitespace-only values when explicitly supplied");
    }
}
