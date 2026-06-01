// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

namespace Daml.Codegen.CSharp.Model;

/// <summary>
/// In-memory representation of an <c>IntermediateDar</c> graph: a main
/// package and its dependencies. Mirrors the proto contract from #146
/// (<c>proto/intermediate_dar.proto</c>); produced by
/// <c>IntermediateDarReader</c> and consumed by the emitter.
/// </summary>
public sealed class DarModel : IDarSource
{
    public required DamlPackage MainPackage { get; init; }
    public required IReadOnlyList<DamlPackage> Dependencies { get; init; }

    private Dictionary<string, DamlPackage>? _index;

    public DamlPackage? GetPackageById(string packageId)
    {
        _index ??= ((IDarSource)this).AllPackages.ToDictionary(p => p.PackageId);
        return _index.GetValueOrDefault(packageId);
    }

    public void ResolveAllDependencyReferences()
    {
    }
}
