// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

namespace Daml.Codegen.CSharp.Model;

/// <summary>
/// Emitter input contract — a main package plus dependency packages, with
/// per-package-id lookup. Implemented by both the parser-direct
/// <c>DarArchive</c> (in <c>Daml.Codegen.DarParser</c>) and the
/// proto-direct <see cref="DarModel"/> produced by
/// <c>IntermediateDarReader</c>. Keeps the emitter library decoupled from
/// the DAR-parsing project per ADR 0003.
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

    /// <summary>
    /// Resolves dependency-reference name/version metadata against the
    /// loaded packages. Idempotent. Implementations that don't carry
    /// dependency references (e.g. the proto-direct path) may no-op.
    /// </summary>
    void ResolveAllDependencyReferences();
}
