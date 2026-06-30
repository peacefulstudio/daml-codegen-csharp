// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.Model;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Production <see cref="ICrossPackageResolver"/> that resolves type refs against an
/// <see cref="IDarSource"/>. The foreign-choice-argument memo and the discovered
/// external-package-id set are DAR-scoped — they live for the resolver's lifetime,
/// not per package.
/// </summary>
public sealed class DarCrossPackageResolver : ICrossPackageResolver
{
    private readonly IDarSource _dar;
    private readonly ICodegenLogger _logger;
    private readonly HashSet<string> _discoveredExternalPackageIds = [];
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _foreignChoiceArgCache = [];
    private readonly Dictionary<string, IReadOnlySet<string>> _foreignInterfaceCache = [];

    /// <summary>Creates a resolver scoped to a single <see cref="IDarSource"/>.</summary>
    public DarCrossPackageResolver(IDarSource dar, ICodegenLogger logger)
    {
        ArgumentNullException.ThrowIfNull(dar);
        ArgumentNullException.ThrowIfNull(logger);
        _dar = dar;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlySet<string> DiscoveredExternalPackageIds => _discoveredExternalPackageIds;

    /// <inheritdoc />
    public DamlPackage? LookupPackage(string packageId) => _dar.GetPackageById(packageId);

    /// <inheritdoc />
    public string Resolve(DamlTypeRef typeRef, PackageEmitContext context)
    {
        ArgumentNullException.ThrowIfNull(typeRef);
        ArgumentNullException.ThrowIfNull(context);

        var sanitized = Identifiers.Sanitize(typeRef.Name);

        if (context.IsLocalRef(typeRef))
        {
            if (context.InterfacePlaceholderQualifiedNames.Contains($"{typeRef.Module}:{typeRef.Name}"))
            {
                return Identifiers.InterfaceMarkerName(typeRef.Name);
            }
            if (context.LocalChoiceArgToTemplate.TryGetValue($"{typeRef.Module}:{typeRef.Name}", out var parentTemplate))
            {
                return $"{Identifiers.Sanitize(parentTemplate)}.{sanitized}";
            }
            return sanitized;
        }

        var foreignPkg = _dar.GetPackageById(typeRef.PackageId);
        if (foreignPkg is null)
        {
            throw new InvalidOperationException(
                $"Cross-package type ref {typeRef.Module}:{typeRef.Name} points at package {typeRef.PackageId[..Math.Min(16, typeRef.PackageId.Length)]}… which is not present in the DAR. Rebuild the DAR with the missing package included, or pass a multi-DAR input that resolves it.");
        }

        if (StdlibPackages.IsStdlibPackage(foreignPkg.Name) || StdlibPackages.IsPlaceholderPackageName(foreignPkg.Name))
        {
            var mapped = StdlibPackages.MapStdlibType(typeRef.Module, typeRef.Name);
            if (mapped is not null)
            {
                return context.Qualifier.Qualify(mapped, context.RootNamespace);
            }
            _logger.Warning($"Unmapped stdlib type {foreignPkg.Name}:{typeRef.Module}:{typeRef.Name} — generated code will not compile (no stdlib mapping for this type yet)");
            return sanitized;
        }

        _discoveredExternalPackageIds.Add(typeRef.PackageId);
        var foreignNs = Identifiers.DeriveNamespace(foreignPkg.Name);
        if (ForeignInterfaceQualifiedNames(foreignPkg).Contains($"{typeRef.Module}:{typeRef.Name}"))
        {
            return $"{foreignNs}.{Identifiers.InterfaceMarkerName(typeRef.Name)}";
        }
        if (!_foreignChoiceArgCache.TryGetValue(typeRef.PackageId, out var foreignChoiceArgMap))
        {
            foreignChoiceArgMap = BuildForeignChoiceArgToTemplate(foreignPkg);
            _foreignChoiceArgCache[typeRef.PackageId] = foreignChoiceArgMap;
        }
        if (foreignChoiceArgMap.TryGetValue($"{typeRef.Module}:{typeRef.Name}", out var foreignParentTemplate))
        {
            return $"{foreignNs}.{Identifiers.Sanitize(foreignParentTemplate)}.{sanitized}";
        }
        return $"{foreignNs}.{sanitized}";
    }

    private IReadOnlySet<string> ForeignInterfaceQualifiedNames(DamlPackage pkg)
    {
        if (!_foreignInterfaceCache.TryGetValue(pkg.PackageId, out var qualifiedNames))
        {
            qualifiedNames = pkg.Modules
                .SelectMany(module => module.Interfaces.Select(iface => $"{module.Name}:{iface.Name}"))
                .ToHashSet();
            _foreignInterfaceCache[pkg.PackageId] = qualifiedNames;
        }
        return qualifiedNames;
    }

    /// <summary>
    /// Builds a mapping of choice-argument type's module-qualified (<c>Module:Name</c>)
    /// name to parent template name for the given package, used to qualify cross-package
    /// refs that point at a type nested inside a foreign template. Module-qualified so a
    /// simple name reused across modules cannot collide. When two templates in the
    /// package map the same module-qualified choice-argument type, warns and keeps the
    /// first-seen mapping.
    /// </summary>
    private IReadOnlyDictionary<string, string> BuildForeignChoiceArgToTemplate(DamlPackage pkg)
    {
        var allTypeNames = pkg.Modules
            .SelectMany(m => m.DataTypes)
            .Select(dt => dt.Name)
            .ToHashSet();

        var result = new Dictionary<string, string>();
        foreach (var module in pkg.Modules)
        {
            foreach (var template in module.Templates)
            {
                foreach (var choice in template.Choices)
                {
                    if (choice.ArgumentType is DamlTypeRef typeRef && allTypeNames.Contains(typeRef.Name))
                    {
                        var key = $"{typeRef.Module}:{typeRef.Name}";
                        if (result.TryGetValue(key, out var existingTemplate)
                            && existingTemplate != template.Name)
                        {
                            _logger.Warning(
                                $"Choice-argument type {key} in package {pkg.Name} is used by both templates {existingTemplate} and {template.Name} in the same package; keeping {existingTemplate} and ignoring {template.Name}. Rename one choice-argument type to disambiguate.");
                            continue;
                        }
                        result[key] = template.Name;
                    }
                }
            }
        }
        return result;
    }
}
