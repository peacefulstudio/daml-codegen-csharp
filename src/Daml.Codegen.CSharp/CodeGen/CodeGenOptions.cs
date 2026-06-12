// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Configuration options for the C# code generator.
/// </summary>
public sealed class CodeGenOptions
{
    /// <summary>
    /// Gets or sets the root namespace for generated code.
    /// If null, the namespace is derived from the package name.
    /// </summary>
    public string? RootNamespace { get; init; }

    /// <summary>
    /// Gets or sets a regex pattern to filter which templates to generate.
    /// Default is ".*" (all templates).
    /// </summary>
    public string? RootFilter { get; init; }

    /// <summary>
    /// Gets or sets whether to enable nullable reference types.
    /// </summary>
    public bool EnableNullableReferenceTypes { get; init; } = true;

    /// <summary>
    /// Gets or sets whether to generate XML documentation comments.
    /// </summary>
    public bool GenerateXmlDocs { get; init; } = true;

    /// <summary>
    /// Gets or sets whether to use file-scoped namespaces.
    /// </summary>
    public bool UseFileScopedNamespaces { get; init; } = true;

    /// <summary>
    /// Gets or sets whether to generate record types instead of classes.
    /// </summary>
    public bool UseRecordTypes { get; init; } = true;

    /// <summary>
    /// Gets or sets whether to generate primary constructors.
    /// </summary>
    public bool UsePrimaryConstructors { get; init; } = true;

    /// <summary>
    /// Gets or sets whether to generate a .csproj project file.
    /// </summary>
    public bool GenerateProjectFile { get; init; }

    /// <summary>
    /// Gets or sets whether to include dependency packages in code generation.
    /// </summary>
    public bool IncludeDependencies { get; init; }

    /// <summary>
    /// Gets or sets the target framework for the generated project (e.g., "net10.0").
    /// </summary>
    public string TargetFramework { get; init; } = "net10.0";

    /// <summary>
    /// Gets or sets the version of the Daml.Runtime package to reference.
    /// When null, the generated project pins the Daml.Runtime version that is
    /// lockstep-versioned with this emitter build.
    /// </summary>
    public string? RuntimePackageVersion { get; init; }

    /// <summary>
    /// Gets or sets whether to generate a ContractIdentifiers helper class.
    /// </summary>
    public bool GenerateContractIdentifiers { get; init; } = true;

    /// <summary>
    /// Gets or sets the 4th-segment emitter counter for the generated NuGet
    /// version. The Daml package supplies segments 1–3
    /// (<c>Major.Minor.Patch</c>); segment 4 is a
    /// monotonic counter keyed to the emitter version that produced this
    /// build. Defaults to <c>0</c> for the first emission of a given content.
    /// A release pipeline supplies a monotonic counter to distinguish
    /// republished builds of the same source; local-dev codegen invocations
    /// leave the default in place.
    /// </summary>
    public int EmitterCounter { get; init; }

    /// <summary>
    /// Gets or sets the SPDX license expression emitted in the generated
    /// <c>.csproj</c>'s <c>&lt;PackageLicenseExpression&gt;</c>. Defaults to
    /// <c>Apache-2.0</c>, correct for any DAR whose own licensing is
    /// Apache-2.0. Consumers running the
    /// emitter against a proprietary or differently-licensed DAR should
    /// pass the right SPDX identifier here (or via <c>--package-license</c>
    /// on the CLI) so the published <c>.nuspec</c> doesn't misrepresent
    /// the source DAR's license.
    /// </summary>
    public string PackageLicenseExpression { get; init; } = "Apache-2.0";
}

/// <summary>
/// Represents a generated source file.
/// </summary>
/// <param name="RelativePath">The path relative to the output directory, '/'-separated on every platform.</param>
/// <param name="Content">The file content.</param>
public sealed record GeneratedFile(string RelativePath, string Content);
