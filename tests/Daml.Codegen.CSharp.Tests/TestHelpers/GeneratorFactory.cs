// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.CodeGen;

namespace Daml.Codegen.CSharp.Tests.TestHelpers;

/// <summary>
/// Shared factory for the <see cref="CSharpCodeGenerator"/> used across the
/// codegen test suite. Centralizes the default <see cref="CodeGenOptions"/>
/// (nullable reference types, file-scoped namespaces, record types, primary
/// constructors) and a silent logger so individual test classes import this
/// via <c>using static</c> and call <see cref="CreateGenerator"/> directly.
/// </summary>
public static class GeneratorFactory
{
    /// <summary>
    /// Creates a <see cref="CSharpCodeGenerator"/> with a silent logger. When
    /// <paramref name="options"/> is <c>null</c>, the suite-wide default options
    /// are used.
    /// </summary>
    public static CSharpCodeGenerator CreateGenerator(CodeGenOptions? options = null) =>
        new(options ?? DefaultOptions(), new ConsoleLogger(0));

    private static CodeGenOptions DefaultOptions() =>
        new()
        {
            EnableNullableReferenceTypes = true,
            UseFileScopedNamespaces = true,
            UseRecordTypes = true,
            UsePrimaryConstructors = true,
            GenerateXmlDocs = true,
            GenerateContractIdentifiers = true,
        };
}
