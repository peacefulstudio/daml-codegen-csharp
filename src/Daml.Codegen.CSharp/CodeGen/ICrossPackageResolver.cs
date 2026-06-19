// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.Model;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// DAR-scoped resolution of a <see cref="DamlTypeRef"/> to a C# name. Owns the
/// archive lookup, the foreign-choice-argument memo, and the set of external
/// package ids it has discovered while resolving — read after emission to emit a
/// <c>&lt;PackageReference&gt;</c> per id. Lives for one
/// <see cref="CSharpCodeGenerator.Generate"/> call.
/// </summary>
public interface ICrossPackageResolver
{
    /// <summary>
    /// Resolves <paramref name="typeRef"/> to a C# identifier or fully qualified name.
    /// Local refs return the bare sanitized name (qualified with the parent template
    /// name when the type is a nested choice-argument type); cross-package refs return
    /// a fully qualified name and record the package id so a
    /// <c>&lt;PackageReference&gt;</c> can be emitted for it.
    /// </summary>
    string Resolve(DamlTypeRef typeRef, PackageEmitContext context);

    /// <summary>The external package ids encountered during resolution so far.</summary>
    IReadOnlySet<string> DiscoveredExternalPackageIds { get; }

    /// <summary>
    /// Returns the package with the given id from the DAR, or <c>null</c> if absent.
    /// Lets the emitter classify a type ref (local / stdlib / cross-package) without
    /// holding the archive itself.
    /// </summary>
    DamlPackage? LookupPackage(string packageId);
}
