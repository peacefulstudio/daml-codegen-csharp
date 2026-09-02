// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Codegen.CSharp;

/// <summary>
/// Thrown when the C# code generator encounters a Daml construct it cannot emit
/// correct code for. Generation fails loudly at emit time instead of writing a
/// silent placeholder (such as a <c>default!</c> expression or an empty stub
/// record) into shipped generated code.
/// </summary>
public sealed class CodegenException : Exception
{
    /// <summary>Creates the exception without a message.</summary>
    public CodegenException()
    {
    }

    /// <summary>Creates the exception with a message describing the unmappable construct.</summary>
    public CodegenException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message describing the unmappable construct and the underlying cause.</summary>
    public CodegenException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
