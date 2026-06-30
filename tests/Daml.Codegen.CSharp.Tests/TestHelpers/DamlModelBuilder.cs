// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.Model;

namespace Daml.Codegen.CSharp.Tests.TestHelpers;

/// <summary>
/// Fluent builder for the <see cref="DarModel"/> fixtures used across the codegen
/// test suite. Defaults mirror the canonical single-package test DAR
/// (<c>test-package-id</c> / <c>test-package</c> / <c>1.0.0</c> / LF <c>2.1</c>,
/// no dependencies, no upgrade). Package id, name, version, LF version, modules,
/// dependencies, and the upgraded-package id are all configurable;
/// <see cref="DamlPackage.DependencyReferences"/> is always set to empty, matching
/// every existing fixture. The static <see cref="CreateTestDar(DamlModule)"/>
/// overloads cover the most common shapes for <c>using static</c> call sites;
/// callers needing a custom package identity use the fluent instance API instead.
/// </summary>
public sealed class DamlModelBuilder
{
    private string _packageId = "test-package-id";
    private string _packageName = "test-package";
    private Version _version = new(1, 0, 0);
    private string _lfVersion = "2.1";
    private readonly List<DamlModule> _modules = [];
    private readonly List<DamlPackage> _dependencies = [];
    private string? _upgradedPackageId;

    /// <summary>Sets the main package id (content hash).</summary>
    public DamlModelBuilder WithPackageId(string packageId)
    {
        _packageId = packageId;
        return this;
    }

    /// <summary>Sets the main package name.</summary>
    public DamlModelBuilder WithPackageName(string packageName)
    {
        _packageName = packageName;
        return this;
    }

    /// <summary>Sets the main package version.</summary>
    public DamlModelBuilder WithVersion(Version version)
    {
        _version = version;
        return this;
    }

    /// <summary>Sets the Daml-LF version of the main package.</summary>
    public DamlModelBuilder WithLfVersion(string lfVersion)
    {
        _lfVersion = lfVersion;
        return this;
    }

    /// <summary>Appends a module to the main package.</summary>
    public DamlModelBuilder WithModule(DamlModule module)
    {
        _modules.Add(module);
        return this;
    }

    /// <summary>Appends modules to the main package.</summary>
    public DamlModelBuilder WithModules(params DamlModule[] modules)
    {
        _modules.AddRange(modules);
        return this;
    }

    /// <summary>Appends a dependency package to the DAR.</summary>
    public DamlModelBuilder WithDependency(DamlPackage dependency)
    {
        _dependencies.Add(dependency);
        return this;
    }

    /// <summary>Appends dependency packages to the DAR.</summary>
    public DamlModelBuilder WithDependencies(params DamlPackage[] dependencies)
    {
        _dependencies.AddRange(dependencies);
        return this;
    }

    /// <summary>Sets the id of the package that the main package upgrades.</summary>
    public DamlModelBuilder WithUpgradedPackageId(string? upgradedPackageId)
    {
        _upgradedPackageId = upgradedPackageId;
        return this;
    }

    /// <summary>Builds the configured main <see cref="DamlPackage"/>.</summary>
    public DamlPackage BuildPackage() =>
        new()
        {
            PackageId = _packageId,
            Name = _packageName,
            Version = _version,
            LfVersion = _lfVersion,
            Modules = _modules.ToList(),
            DependencyReferences = [],
            UpgradedPackageId = _upgradedPackageId,
        };

    /// <summary>Builds the configured <see cref="DarModel"/>.</summary>
    public DarModel Build() =>
        new()
        {
            MainPackage = BuildPackage(),
            Dependencies = _dependencies.ToList(),
        };

    /// <summary>
    /// Builds the canonical single-module test DAR: default package identity,
    /// no dependencies, no upgrade.
    /// </summary>
    public static DarModel CreateTestDar(DamlModule module) =>
        new DamlModelBuilder().WithModule(module).Build();

    /// <summary>
    /// Builds a single-module test DAR whose main package upgrades
    /// <paramref name="upgradedPackageId"/> (default identity, no dependencies).
    /// </summary>
    public static DarModel CreateTestDar(DamlModule module, string? upgradedPackageId) =>
        new DamlModelBuilder().WithModule(module).WithUpgradedPackageId(upgradedPackageId).Build();

    /// <summary>
    /// Builds a multi-module test DAR: default package identity, no dependencies.
    /// </summary>
    public static DarModel CreateTestDar(DamlModule[] modules) =>
        new DamlModelBuilder().WithModules(modules).Build();
}
