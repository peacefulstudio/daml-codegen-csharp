// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

namespace Daml.Codegen.CSharp;

/// <summary>
/// Verbosity-aware console logger used by <c>CSharpCodeGenerator</c> and
/// the CLI. Writes to <see cref="Console"/> directly (no Spectre or other
/// CLI-only dependency) so the emitter library remains free of UI
/// dependencies. Verbosity levels: 0 = errors only, 1 = warnings, 2 = info,
/// 3 = debug.
/// </summary>
public sealed class ConsoleLogger(int verbosity) : ICodegenLogger
{
    public void Error(string message) =>
        Console.Error.WriteLine($"ERROR: {message}");

    public void Warning(string message)
    {
        if (verbosity >= 1)
        {
            Console.Error.WriteLine($"WARN: {message}");
        }
    }

    public void Info(string message)
    {
        if (verbosity >= 2)
        {
            Console.WriteLine($"INFO: {message}");
        }
    }

    public void Debug(string message)
    {
        if (verbosity >= 3)
        {
            Console.WriteLine($"DEBUG: {message}");
        }
    }
}
