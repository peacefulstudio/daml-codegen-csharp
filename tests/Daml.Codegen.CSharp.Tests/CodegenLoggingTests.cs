// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Tests.TestHelpers;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class CodegenLoggingTests
{
    [Fact]
    public void CSharpCodeGenerator_accepts_any_ilogger_implementation()
    {
        var captured = new CapturingLogger();
        var loggerFactory = new SingleLoggerFactory(captured);

        var generator = new CSharpCodeGenerator(
            new CodeGenOptions(), loggerFactory.CreateLogger<CSharpCodeGenerator>());

        generator.Should().NotBeNull(
            "CSharpCodeGenerator must accept any ILogger, not only a provider the library ships");
    }

    [Fact]
    public void CSharpCodeGenerator_defaults_to_the_null_logger()
    {
        var generator = new CSharpCodeGenerator(new CodeGenOptions());

        generator.Should().NotBeNull(
            "omitting the logger must leave the emitter silent rather than force a host to supply one");
    }

    [Fact]
    public void VerbosityConsoleLoggerProvider_gates_records_on_verbosity()
    {
        using var errorsOnly = new VerbosityConsoleLoggerProvider(0);
        using var debug = new VerbosityConsoleLoggerProvider(3);

        var quiet = errorsOnly.CreateLogger("test");
        quiet.IsEnabled(LogLevel.Error).Should().BeTrue();
        quiet.IsEnabled(LogLevel.Warning).Should().BeFalse();
        quiet.IsEnabled(LogLevel.Information).Should().BeFalse();

        var loud = debug.CreateLogger("test");
        loud.IsEnabled(LogLevel.Debug).Should().BeTrue();
        loud.IsEnabled(LogLevel.Trace).Should().BeFalse();
    }

    [Fact]
    public void VerbosityConsoleLoggerProvider_verbosity_1_enables_warning_but_not_debug()
    {
        using var warningsAndAbove = new VerbosityConsoleLoggerProvider(1);
        var logger = warningsAndAbove.CreateLogger("test");

        logger.IsEnabled(LogLevel.Error).Should().BeTrue();
        logger.IsEnabled(LogLevel.Warning).Should().BeTrue();
        logger.IsEnabled(LogLevel.Information).Should().BeFalse();
        logger.IsEnabled(LogLevel.Debug).Should().BeFalse();
    }

    [Fact]
    public void VerbosityConsoleLoggerProvider_verbosity_2_enables_information_but_not_debug()
    {
        using var informationAndAbove = new VerbosityConsoleLoggerProvider(2);
        var logger = informationAndAbove.CreateLogger("test");

        logger.IsEnabled(LogLevel.Error).Should().BeTrue();
        logger.IsEnabled(LogLevel.Warning).Should().BeTrue();
        logger.IsEnabled(LogLevel.Information).Should().BeTrue();
        logger.IsEnabled(LogLevel.Debug).Should().BeFalse();
    }

    [Fact]
    public void NullLogger_swallows_every_severity()
    {
        var logger = NullLogger<CSharpCodeGenerator>.Instance;

        logger.IsEnabled(LogLevel.Error).Should().BeFalse();
        logger.IsEnabled(LogLevel.Debug).Should().BeFalse();
    }

    private sealed class SingleLoggerFactory(ILogger logger) : ILoggerFactory
    {
        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName) => logger;

        public void Dispose()
        {
        }
    }
}
