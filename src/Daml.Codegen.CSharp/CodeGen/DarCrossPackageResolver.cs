// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Production <see cref="ICrossPackageResolver"/> that resolves type refs against an
/// <see cref="IDarSource"/>. The foreign-choice-argument memo and the discovered
/// external-package-id set are DAR-scoped — they live for the resolver's lifetime,
/// not per package.
/// </summary>
internal sealed partial class DarCrossPackageResolver : ICrossPackageResolver
{
    private readonly IDarSource _dar;
    private readonly ILogger _logger;
    private readonly HashSet<string> _discoveredExternalPackageIds = [];
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _foreignChoiceArgCache = [];
    private readonly Dictionary<string, IReadOnlySet<string>> _foreignInterfaceCache = [];
    private readonly Dictionary<string, IReadOnlySet<string>> _foreignReservedTypeNameCache = [];
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _foreignInterfaceMarkerNameCache = [];

    /// <summary>Creates a resolver scoped to a single <see cref="IDarSource"/>.</summary>
    /// <param name="dar">The archive type refs are resolved against.</param>
    /// <param name="logger">Where cross-package warnings go; omit it and the resolver stays silent.</param>
    public DarCrossPackageResolver(IDarSource dar, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(dar);
        _dar = dar;
        _logger = logger ?? NullLogger.Instance;
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
            if (context.LocalInterfaceQualifiedNames.Contains($"{typeRef.Module}:{typeRef.Name}"))
            {
                return context.LocalInterfaceMarkerNames[$"{typeRef.Module}:{typeRef.Name}"];
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
            LogUnmappedStdlibType(_logger, foreignPkg.Name, typeRef.Module, typeRef.Name);
            return sanitized;
        }

        _discoveredExternalPackageIds.Add(typeRef.PackageId);
        var foreignNs = Identifiers.DeriveNamespace(foreignPkg.Name);
        if (ForeignInterfaceQualifiedNames(foreignPkg).Contains($"{typeRef.Module}:{typeRef.Name}"))
        {
            return $"{foreignNs}.{ForeignInterfaceMarkerNames(foreignPkg)[$"{typeRef.Module}:{typeRef.Name}"]}";
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
    /// Sanitised C# names of every top-level type declared in <paramref name="pkg"/>,
    /// mirroring <see cref="PackageEmitContext.LocalReservedTypeNames"/> for a foreign
    /// package — the seed <see cref="ForeignInterfaceMarkerNames"/> disambiguates against
    /// so a marker referenced across packages agrees with the reserved set the declaring
    /// package's own emission used.
    /// </summary>
    private IReadOnlySet<string> ForeignReservedTypeNames(DamlPackage pkg)
    {
        if (!_foreignReservedTypeNameCache.TryGetValue(pkg.PackageId, out var reservedTypeNames))
        {
            reservedTypeNames = PackageEmitContext.ReservedTopLevelTypeNames(pkg);
            _foreignReservedTypeNameCache[pkg.PackageId] = reservedTypeNames;
        }
        return reservedTypeNames;
    }

    /// <summary>
    /// The precomputed interface-marker map for <paramref name="pkg"/>, mirroring
    /// <see cref="PackageEmitContext.LocalInterfaceMarkerNames"/> for a foreign package —
    /// so a marker referenced across packages agrees with the same deterministic
    /// assignment the declaring package's own emission used.
    /// </summary>
    private IReadOnlyDictionary<string, string> ForeignInterfaceMarkerNames(DamlPackage pkg)
    {
        if (!_foreignInterfaceMarkerNameCache.TryGetValue(pkg.PackageId, out var markerNames))
        {
            markerNames = PackageEmitContext.InterfaceMarkerNames(pkg, ForeignReservedTypeNames(pkg));
            _foreignInterfaceMarkerNameCache[pkg.PackageId] = markerNames;
        }
        return markerNames;
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
                            LogAmbiguousForeignChoiceArgument(_logger, key, pkg.Name, existingTemplate, template.Name);
                            continue;
                        }
                        result[key] = template.Name;
                    }
                }
            }
        }
        return result;
    }

    [LoggerMessage(
        EventId = 1100,
        Level = LogLevel.Warning,
        Message = "Unmapped stdlib type {PackageName}:{ModuleName}:{TypeName} \u2014 generated code will not compile (no stdlib mapping for this type yet)")]
    private static partial void LogUnmappedStdlibType(ILogger logger, string packageName, string moduleName, string typeName);

    [LoggerMessage(
        EventId = 1101,
        Level = LogLevel.Warning,
        Message = "Choice-argument type {ChoiceArgumentKey} in package {PackageName} is used by both templates {KeptTemplate} and {IgnoredTemplate} in the same package; keeping {KeptTemplate} and ignoring {IgnoredTemplate}. Rename one choice-argument type to disambiguate.")]
    private static partial void LogAmbiguousForeignChoiceArgument(ILogger logger, string choiceArgumentKey, string packageName, string keptTemplate, string ignoredTemplate);
}
