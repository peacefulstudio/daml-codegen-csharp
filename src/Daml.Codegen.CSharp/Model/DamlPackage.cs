// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Codegen.CSharp.Model;

/// <summary>
/// Represents a reference to a package dependency.
/// </summary>
public sealed class DamlPackageReference
{
    /// <summary>
    /// Gets the package ID (content hash) of the referenced package.
    /// </summary>
    public required string PackageId { get; init; }

    /// <summary>
    /// Gets the package name if known (resolved from dependencies); <c>null</c>
    /// when the referenced package is not bundled in the source DAR and must be
    /// provided externally (e.g. by the participant's package store).
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Gets the package version if known (resolved from dependencies); <c>null</c>
    /// when the referenced package is not bundled in the source DAR and must be
    /// provided externally (e.g. by the participant's package store).
    /// </summary>
    public Version? Version { get; init; }
}

/// <summary>
/// Represents a compiled Daml package (DALF).
/// </summary>
public sealed class DamlPackage
{
    /// <summary>
    /// Gets the package ID (content hash).
    /// </summary>
    public required string PackageId { get; init; }

    /// <summary>
    /// Gets the package name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the package version.
    /// </summary>
    public required Version Version { get; init; }

    /// <summary>
    /// Gets the Daml-LF version.
    /// </summary>
    public required string LfVersion { get; init; }

    /// <summary>
    /// Gets the modules in this package.
    /// </summary>
    public required IReadOnlyList<DamlModule> Modules { get; init; }

    /// <summary>
    /// Gets the package IDs that this package depends on.
    /// </summary>
    public required IReadOnlyList<DamlPackageReference> DependencyReferences { get; init; }

    /// <summary>
    /// Gets the package ID of the package that this package upgrades, if any.
    /// This is used for Daml's package upgrade mechanism.
    /// </summary>
    public string? UpgradedPackageId { get; init; }
}

/// <summary>
/// Represents a Daml module within a package.
/// </summary>
public sealed class DamlModule
{
    /// <summary>
    /// Gets the fully qualified module name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the templates defined in this module.
    /// </summary>
    public required IReadOnlyList<DamlTemplate> Templates { get; init; }

    /// <summary>
    /// Gets the data types defined in this module.
    /// </summary>
    public required IReadOnlyList<DamlDataType> DataTypes { get; init; }

    /// <summary>
    /// Gets the interfaces defined in this module.
    /// </summary>
    public required IReadOnlyList<DamlInterface> Interfaces { get; init; }
}
