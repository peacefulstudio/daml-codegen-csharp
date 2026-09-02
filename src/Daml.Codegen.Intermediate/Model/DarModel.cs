// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Codegen.Intermediate.Model;

/// <summary>
/// In-memory representation of an <c>IntermediateDar</c> graph: a main
/// package and its dependencies. Mirrors the proto contract
/// (<c>proto/intermediate_dar.proto</c>); produced by
/// <c>IntermediateDarReader</c> and consumed by the emitter.
/// </summary>
public sealed class DarModel : IDarSource
{
    /// <inheritdoc />
    public required DamlPackage MainPackage { get; init; }

    /// <inheritdoc />
    public required IReadOnlyList<DamlPackage> Dependencies { get; init; }

    /// <inheritdoc />
    public DamlPackage? GetPackageById(string packageId) => IDarSource.FindPackageById(this, packageId);
}
