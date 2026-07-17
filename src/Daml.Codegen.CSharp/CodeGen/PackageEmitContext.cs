// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.Model;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Immutable per-package value the C# emitter threads through its emit methods: the
/// root namespace, the <see cref="TypeReferenceQualifier"/>, the per-package
/// data-type lookup, and the local enum / variant / interface-placeholder /
/// choice-argument name sets. Built once per package by
/// <see cref="ForPackage"/>; read-only during emission.
/// </summary>
internal sealed class PackageEmitContext
{
    /// <summary>The Daml package this context was built for.</summary>
    public DamlPackage Package { get; }

    /// <summary>Root C# namespace every emitted type in the package lives in.</summary>
    public string RootNamespace { get; }

    /// <summary>Qualifier scoped to the package's generated namespaces.</summary>
    public TypeReferenceQualifier Qualifier { get; }

    /// <summary>
    /// Lookup of every data type across all modules, keyed by module-qualified
    /// (<c>Module:Name</c>) name. Module-qualified because Daml allows the same simple
    /// name in multiple modules — keying on the simple name alone would let one module's
    /// data type silently shadow another's and emit the wrong field list.
    /// </summary>
    public IReadOnlyDictionary<string, DamlDataType> DataTypes { get; }

    /// <summary>
    /// Sanitised C# names of every top-level type declared anywhere in the package —
    /// every template plus every record/enum/variant, excluding interface-placeholder
    /// records (they are replaced by the marker itself, so counting them would falsely
    /// self-disambiguate) and choice-argument records (they are emitted nested inside
    /// their parent template, not at the top level). The package's C# namespace is flat
    /// across all its modules, so this is the reserved-name input
    /// <see cref="Identifiers.InterfaceMarkerName"/> disambiguates interface marker
    /// names against.
    /// </summary>
    public IReadOnlySet<string> LocalReservedTypeNames { get; }

    /// <summary>
    /// Every interface declared in the package, keyed by its module-qualified
    /// (<c>Module:Name</c>) name, mapped to its final disambiguated C# marker name. See
    /// <see cref="InterfaceMarkerNames"/> for how the assignment is made deterministic.
    /// Callers that need an interface's marker name — the interface emitter, the
    /// generated file-path builder, and the cross-package resolver's local-ref path —
    /// must look it up here rather than recomputing it ad hoc, so every reference to a
    /// given interface agrees on the same marker.
    /// </summary>
    public IReadOnlyDictionary<string, string> LocalInterfaceMarkerNames { get; }

    /// <summary>
    /// Module-qualified (<c>Module:Name</c>) names of enums declared in the package.
    /// Required because Daml allows the same simple name in multiple modules.
    /// </summary>
    public IReadOnlySet<string> LocalEnumQualifiedNames { get; }

    /// <summary>Module-qualified names of variants declared in the package.</summary>
    public IReadOnlySet<string> LocalVariantQualifiedNames { get; }

    /// <summary>
    /// Module-qualified names of records that exist purely as the C# placeholder for a
    /// Daml interface declaration.
    /// </summary>
    public IReadOnlySet<string> InterfacePlaceholderQualifiedNames { get; }

    /// <summary>
    /// Maps a choice-argument type's module-qualified (<c>Module:Name</c>) name to its
    /// parent template name, for qualifying nested choice-argument types declared in this
    /// package. Module-qualified because Daml allows the same simple name in multiple
    /// modules — keying on the simple name alone would let one module's choice-arg type
    /// silently shadow another's and mis-resolve cross-references.
    /// </summary>
    public IReadOnlyDictionary<string, string> LocalChoiceArgToTemplate { get; }

    /// <summary>
    /// Returns true when <paramref name="typeRef"/> points at a type declared in this
    /// package — either an empty package id (self-reference) or a matching package id.
    /// </summary>
    public bool IsLocalRef(DamlTypeRef typeRef) =>
        string.IsNullOrEmpty(typeRef.PackageId)
        || typeRef.PackageId == Package.PackageId;

    private PackageEmitContext(
        DamlPackage package,
        string rootNamespace,
        TypeReferenceQualifier qualifier,
        IReadOnlyDictionary<string, DamlDataType> dataTypes,
        IReadOnlySet<string> localReservedTypeNames,
        IReadOnlyDictionary<string, string> localInterfaceMarkerNames,
        IReadOnlySet<string> localEnumQualifiedNames,
        IReadOnlySet<string> localVariantQualifiedNames,
        IReadOnlySet<string> interfacePlaceholderQualifiedNames,
        IReadOnlyDictionary<string, string> localChoiceArgToTemplate)
    {
        Package = package;
        RootNamespace = rootNamespace;
        Qualifier = qualifier;
        DataTypes = dataTypes;
        LocalReservedTypeNames = localReservedTypeNames;
        LocalInterfaceMarkerNames = localInterfaceMarkerNames;
        LocalEnumQualifiedNames = localEnumQualifiedNames;
        LocalVariantQualifiedNames = localVariantQualifiedNames;
        InterfacePlaceholderQualifiedNames = interfacePlaceholderQualifiedNames;
        LocalChoiceArgToTemplate = localChoiceArgToTemplate;
    }

    /// <summary>
    /// Scans <paramref name="package"/> and returns a fully-populated immutable context:
    /// derives the root namespace (honouring <see cref="CodeGenOptions.RootNamespace"/>),
    /// builds the global data-type lookup, and populates the local enum / variant /
    /// interface-placeholder / choice-argument name sets. When two templates in the
    /// package map the same module-qualified choice-argument type, <paramref name="logger"/>
    /// (when supplied) receives a warning and the first-seen mapping is kept.
    /// </summary>
    public static PackageEmitContext ForPackage(
        DamlPackage package,
        CodeGenOptions options,
        ICodegenLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(options);

        var rootNamespace = options.RootNamespace ?? Identifiers.DeriveNamespace(package.Name);
        var qualifier = new TypeReferenceQualifier([rootNamespace]);

        var dataTypes = new Dictionary<string, DamlDataType>();
        var localEnumQualifiedNames = new HashSet<string>();
        var localVariantQualifiedNames = new HashSet<string>();
        var interfacePlaceholderQualifiedNames = new HashSet<string>();
        foreach (var module in package.Modules)
        {
            var interfaceNames = module.Interfaces.Select(i => i.Name).ToHashSet();

            foreach (var dataType in module.DataTypes)
            {
                dataTypes[$"{module.Name}:{dataType.Name}"] = dataType;
                if (dataType.Definition is DamlEnumDefinition)
                {
                    localEnumQualifiedNames.Add($"{module.Name}:{dataType.Name}");
                }
                if (dataType.Definition is DamlVariantDefinition)
                {
                    localVariantQualifiedNames.Add($"{module.Name}:{dataType.Name}");
                }
                if (interfaceNames.Contains(dataType.Name))
                {
                    interfacePlaceholderQualifiedNames.Add($"{module.Name}:{dataType.Name}");
                }
            }
        }

        var localChoiceArgToTemplate = new Dictionary<string, string>();
        foreach (var module in package.Modules)
        {
            foreach (var template in module.Templates)
            {
                foreach (var choice in template.Choices)
                {
                    if (choice.ArgumentType is DamlTypeRef typeRef)
                    {
                        var key = $"{typeRef.Module}:{typeRef.Name}";
                        if (dataTypes.ContainsKey(key))
                        {
                            if (localChoiceArgToTemplate.TryGetValue(key, out var existingTemplate)
                                && existingTemplate != template.Name)
                            {
                                logger?.Warning(
                                    $"Choice-argument type {key} is used by both templates {existingTemplate} and {template.Name} in the same package; keeping {existingTemplate} and ignoring {template.Name}. Rename one choice-argument type to disambiguate.");
                                continue;
                            }
                            localChoiceArgToTemplate[key] = template.Name;
                        }
                    }
                }
            }
        }

        var localReservedTypeNames = ReservedTopLevelTypeNames(package);
        var localInterfaceMarkerNames = InterfaceMarkerNames(package, localReservedTypeNames);

        return new PackageEmitContext(
            package,
            rootNamespace,
            qualifier,
            dataTypes,
            localReservedTypeNames,
            localInterfaceMarkerNames,
            localEnumQualifiedNames,
            localVariantQualifiedNames,
            interfacePlaceholderQualifiedNames,
            localChoiceArgToTemplate);
    }

    /// <summary>
    /// Computes the sanitised C# name of every top-level type declared anywhere in
    /// <paramref name="package"/> — every template plus every record/enum/variant,
    /// excluding interface-placeholder records (replaced by the marker itself) and
    /// choice-argument records (emitted nested inside their parent template, not at the
    /// top level) — the single source of the reserved-name set
    /// <see cref="Identifiers.InterfaceMarkerName"/> disambiguates against, shared by
    /// <see cref="ForPackage"/> (for the emitting package) and the cross-package
    /// resolver (for foreign packages) so both sides derive the same marker name.
    /// </summary>
    internal static IReadOnlySet<string> ReservedTopLevelTypeNames(DamlPackage package)
    {
        var interfacePlaceholderQualifiedNames = new HashSet<string>();
        var dataTypeNames = new HashSet<string>();
        foreach (var module in package.Modules)
        {
            var interfaceNames = module.Interfaces.Select(i => i.Name).ToHashSet();
            foreach (var dataType in module.DataTypes)
            {
                dataTypeNames.Add(dataType.Name);
                if (interfaceNames.Contains(dataType.Name))
                {
                    interfacePlaceholderQualifiedNames.Add($"{module.Name}:{dataType.Name}");
                }
            }
        }

        var choiceArgumentQualifiedNames = new HashSet<string>();
        foreach (var module in package.Modules)
        {
            foreach (var template in module.Templates)
            {
                foreach (var choice in template.Choices)
                {
                    if (choice.ArgumentType is DamlTypeRef typeRef && dataTypeNames.Contains(typeRef.Name))
                    {
                        choiceArgumentQualifiedNames.Add($"{typeRef.Module}:{typeRef.Name}");
                    }
                }
            }
        }

        var reserved = new HashSet<string>();
        foreach (var module in package.Modules)
        {
            foreach (var template in module.Templates)
            {
                reserved.Add(Identifiers.Sanitize(template.Name));
            }
            foreach (var dataType in module.DataTypes)
            {
                var qualifiedName = $"{module.Name}:{dataType.Name}";
                if (interfacePlaceholderQualifiedNames.Contains(qualifiedName)
                    || choiceArgumentQualifiedNames.Contains(qualifiedName))
                {
                    continue;
                }
                reserved.Add(Identifiers.Sanitize(dataType.Name));
            }
        }
        return reserved;
    }

    /// <summary>
    /// Precomputes the final disambiguated C# marker name for every interface in
    /// <paramref name="package"/>, keyed by module-qualified (<c>Module:Name</c>) name,
    /// seeded from <paramref name="reservedTypeNames"/> (see
    /// <see cref="ReservedTopLevelTypeNames"/>). Interfaces are processed in a stable
    /// ordinal sort over their qualified name — not module declaration order — so that
    /// when two interfaces in different modules sanitise to the same marker, the same one
    /// deterministically wins the unsuffixed name on every run; each assigned marker is
    /// threaded into the reserved set before the next interface is disambiguated, so the
    /// loser picks up the trailing <c>_</c>. Shared by <see cref="ForPackage"/> (for the
    /// emitting package) and the cross-package resolver (for foreign packages) so both
    /// sides derive the same marker assignment.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> InterfaceMarkerNames(
        DamlPackage package, IReadOnlySet<string> reservedTypeNames)
    {
        var reserved = new HashSet<string>(reservedTypeNames);
        var markers = new Dictionary<string, string>();

        var interfaces = package.Modules
            .SelectMany(module => module.Interfaces.Select(iface => (Module: module.Name, Interface: iface)))
            .OrderBy(x => $"{x.Module}:{x.Interface.Name}", StringComparer.Ordinal);

        foreach (var (moduleName, iface) in interfaces)
        {
            var marker = Identifiers.InterfaceMarkerName(iface.Name, reserved);
            reserved.Add(marker);
            markers[$"{moduleName}:{iface.Name}"] = marker;
        }

        return markers;
    }
}
