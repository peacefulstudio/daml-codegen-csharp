// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Codegen.CSharp;

/// <summary>
/// Logging contract that <see cref="CodeGen.CSharpCodeGenerator"/> writes
/// progress, warnings, and errors against. Implementations may surface
/// messages to a console (<see cref="ConsoleLogger"/>), capture them in
/// memory for tests, route them to a host application's logging
/// framework, or discard them entirely. Severity ordering and
/// verbosity-gating semantics are at the implementation's discretion.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "Error/Warning/Info/Debug are the established Daml.Codegen.CSharp logging vocabulary and are not invokable as members from VB.NET hosts in practice.")]
public interface ICodegenLogger
{
    /// <summary>Logs an error. Always surfaced.</summary>
    void Error(string message);

    /// <summary>Logs a warning. Surfaced unless the implementation gates on verbosity.</summary>
    void Warning(string message);

    /// <summary>Logs an informational message.</summary>
    void Info(string message);

    /// <summary>Logs a debug-level message; the noisiest tier.</summary>
    void Debug(string message);
}
