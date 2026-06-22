// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Model;
using FluentAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class ICodegenLoggerTests
{
    [Fact]
    public void code_generator_accepts_icodegenlogger_implementation()
    {
        var captured = new CapturingLogger();
        var options = new CodeGenOptions();

        var generator = new CSharpCodeGenerator(options, captured);
        generator.Should().NotBeNull("CSharpCodeGenerator's constructor must accept any ICodegenLogger, not only ConsoleLogger (review #13)");
    }

    [Fact]
    public void console_logger_implements_icodegen_logger()
    {
        var logger = new ConsoleLogger(0);
        logger.Should().BeAssignableTo<ICodegenLogger>(
            "ConsoleLogger keeps its existing role as the CLI logger but is now plug-compatible via ICodegenLogger (review #13)");
    }

    private sealed class CapturingLogger : ICodegenLogger
    {
        public List<string> Errors { get; } = [];
        public List<string> Warnings { get; } = [];
        public List<string> Infos { get; } = [];
        public List<string> Debugs { get; } = [];

        public void Error(string message) => Errors.Add(message);
        public void Warning(string message) => Warnings.Add(message);
        public void Info(string message) => Infos.Add(message);
        public void Debug(string message) => Debugs.Add(message);
    }
}
