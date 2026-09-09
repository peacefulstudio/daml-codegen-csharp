// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Production <see cref="ICrossPackageResolver"/> that resolves type refs against an
/// <see cref="IDarSource"/>. Every namespace it spells comes from
/// <see cref="Identifiers.ModuleNamespace"/> — the same function the emitter names its
/// files and namespaces with — so a reference always lands on a namespace that is emitted.
/// The foreign-package memos and the discovered external-package-id set are DAR-scoped —
/// they live for the resolver's lifetime, not per package.
/// </summary>
internal sealed partial class DarCrossPackageResolver : ICrossPackageResolver
{
    private readonly IDarSource _dar;
    private readonly CodeGenOptions _options;
    private readonly ILogger _logger;
    private readonly HashSet<string> _discoveredExternalPackageIds = [];
    private readonly Dictionary<string, IReadOnlyDictionary<string, NestingTemplate>> _foreignChoiceArgCache = [];
    private readonly Dictionary<string, IReadOnlySet<string>> _foreignInterfaceCache = [];
    private readonly Dictionary<string, IReadOnlySet<string>> _foreignReservedTypeNameCache = [];
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _foreignInterfaceMarkerNameCache = [];
    private readonly Dictionary<string, ILookup<(string Module, string Name), DamlDataTypeDefinition>> _foreignDataTypeCache = [];

    /// <summary>Creates a resolver scoped to a single <see cref="IDarSource"/>.</summary>
    /// <param name="dar">The archive type refs are resolved against.</param>
    /// <param name="options">
    /// The emission options, read for <see cref="CodeGenOptions.NamespacePrefix"/>: a reference
    /// into the main package is spelled under the same prefix the main package is emitted with.
    /// </param>
    /// <param name="logger">Where cross-package warnings go; omit it and the resolver stays silent.</param>
    public DarCrossPackageResolver(IDarSource dar, CodeGenOptions options, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(dar);
        ArgumentNullException.ThrowIfNull(options);
        _dar = dar;
        _options = options;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <inheritdoc />
    public IReadOnlySet<string> DiscoveredExternalPackageIds => _discoveredExternalPackageIds;

    /// <inheritdoc />
    public DamlPackage? LookupPackage(string packageId) => _dar.GetPackageById(packageId);

    /// <inheritdoc />
    public ILookup<(string Module, string Name), DamlDataTypeDefinition> DataTypeDefinitions(string packageId)
    {
        if (!_foreignDataTypeCache.TryGetValue(packageId, out var definitions))
        {
            definitions = ICrossPackageResolver.IndexDataTypeDefinitions(LookupPackage(packageId));
            _foreignDataTypeCache[packageId] = definitions;
        }
        return definitions;
    }

    /// <inheritdoc />
    public string Resolve(DamlTypeRef typeRef, PackageEmitContext context)
    {
        ArgumentNullException.ThrowIfNull(typeRef);
        ArgumentNullException.ThrowIfNull(context);

        var sanitized = Identifiers.Sanitize(typeRef.Name);
        var qualifiedName = $"{typeRef.Module}:{typeRef.Name}";

        if (context.IsLocalRef(typeRef))
        {
            return ResolveLocal(typeRef, context, sanitized, qualifiedName);
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
                return context.Qualifier.Qualify(mapped);
            }
            LogUnmappedStdlibType(_logger, foreignPkg.Name, typeRef.Module, typeRef.Name);
            return sanitized;
        }

        _discoveredExternalPackageIds.Add(typeRef.PackageId);
        if (ForeignInterfaceQualifiedNames(foreignPkg).Contains(qualifiedName))
        {
            return Identifiers.GlobalQualified(ForeignNamespace(foreignPkg, typeRef.Module), ForeignInterfaceMarkerNames(foreignPkg)[qualifiedName]);
        }
        if (ForeignChoiceArgToTemplate(foreignPkg).TryGetValue(qualifiedName, out var nestingTemplate))
        {
            return $"{Identifiers.GlobalPrefix}{ForeignNamespace(foreignPkg, nestingTemplate.Module)}.{Identifiers.Sanitize(nestingTemplate.Name)}.{sanitized}";
        }
        return Identifiers.GlobalQualified(ForeignNamespace(foreignPkg, typeRef.Module), sanitized);
    }

    /// <summary>
    /// A same-package reference is bare when the referent lives in the emitting module's
    /// namespace and <c>global::</c>-qualified with the referent module's namespace
    /// otherwise — a relative spelling could bind to a same-named nested namespace of the
    /// emitting one. A choice-argument record is homed on the template that nests it, which
    /// may sit in a different module than the record's own declaration.
    /// </summary>
    private static string ResolveLocal(DamlTypeRef typeRef, PackageEmitContext context, string sanitized, string qualifiedName)
    {
        var (homeModule, name) =
            context.LocalInterfaceQualifiedNames.Contains(qualifiedName)
                ? (typeRef.Module, context.LocalInterfaceMarkerNames[qualifiedName])
                : context.LocalChoiceArgToTemplate.TryGetValue(qualifiedName, out var nestingTemplate)
                    ? (nestingTemplate.Module, $"{Identifiers.Sanitize(nestingTemplate.Name)}.{sanitized}")
                    : (typeRef.Module, sanitized);

        var homeNamespace = context.NamespaceOf(homeModule);
        return homeNamespace == context.Namespace ? name : Identifiers.GlobalQualified(homeNamespace, name);
    }

    private string ForeignNamespace(DamlPackage foreignPkg, string moduleName) =>
        Identifiers.ModuleNamespace(moduleName, foreignPkg.PackageId == _dar.MainPackage.PackageId, _options);

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

    private IReadOnlyDictionary<string, NestingTemplate> ForeignChoiceArgToTemplate(DamlPackage pkg)
    {
        if (!_foreignChoiceArgCache.TryGetValue(pkg.PackageId, out var choiceArgToTemplate))
        {
            choiceArgToTemplate = BuildForeignChoiceArgToTemplate(pkg);
            _foreignChoiceArgCache[pkg.PackageId] = choiceArgToTemplate;
        }
        return choiceArgToTemplate;
    }

    /// <summary>
    /// Builds a mapping of choice-argument type's module-qualified (<c>Module:Name</c>)
    /// name to the template nesting it for the given package, used to qualify cross-package
    /// refs that point at a type nested inside a foreign template. Module-qualified so a
    /// simple name reused across modules cannot collide. When two templates in the
    /// package map the same module-qualified choice-argument type, warns and keeps the
    /// first-seen mapping.
    /// </summary>
    private IReadOnlyDictionary<string, NestingTemplate> BuildForeignChoiceArgToTemplate(DamlPackage pkg)
    {
        var allTypeNames = pkg.Modules
            .SelectMany(m => m.DataTypes)
            .Select(dt => dt.Name)
            .ToHashSet();

        var result = new Dictionary<string, NestingTemplate>();
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
                            && existingTemplate.Name != template.Name)
                        {
                            LogAmbiguousForeignChoiceArgument(_logger, key, pkg.Name, existingTemplate.Name, template.Name);
                            continue;
                        }
                        result[key] = new NestingTemplate(module.Name, template.Name);
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
