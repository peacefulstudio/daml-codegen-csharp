// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Codegen.CSharp.Model;

/// <summary>
/// Emitter input contract — a main package plus dependency packages, with
/// per-package-id lookup. Implemented by the proto-direct
/// <see cref="DarModel"/> produced by <c>IntermediateDarReader</c>. Keeps the
/// emitter library decoupled from how the DAR is decoded.
/// </summary>
public interface IDarSource
{
    /// <summary>The main package being generated.</summary>
    DamlPackage MainPackage { get; }

    /// <summary>The dependency packages (used for cross-package type resolution).</summary>
    IReadOnlyList<DamlPackage> Dependencies { get; }

    /// <summary>All packages — main first, then dependencies.</summary>
    IEnumerable<DamlPackage> AllPackages => Dependencies.Prepend(MainPackage);

    /// <summary>Returns the package with the given id, or null if absent.</summary>
    DamlPackage? GetPackageById(string packageId);
}
