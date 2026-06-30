// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

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
    /// <summary>Writes an ERROR line to stderr; always shown regardless of verbosity.</summary>
    public void Error(string message) =>
        Console.Error.WriteLine($"ERROR: {message}");

    /// <summary>Writes a WARN line to stderr when verbosity is 1 or higher.</summary>
    public void Warning(string message)
    {
        if (verbosity >= 1)
        {
            Console.Error.WriteLine($"WARN: {message}");
        }
    }

    /// <summary>Writes an INFO line to stdout when verbosity is 2 or higher.</summary>
    public void Info(string message)
    {
        if (verbosity >= 2)
        {
            Console.WriteLine($"INFO: {message}");
        }
    }

    /// <summary>Writes a DEBUG line to stdout when verbosity is 3 or higher.</summary>
    public void Debug(string message)
    {
        if (verbosity >= 3)
        {
            Console.WriteLine($"DEBUG: {message}");
        }
    }
}
