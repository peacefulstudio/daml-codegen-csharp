// Copyright (c) 2026 Peaceful Studio OÜ
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
public sealed class PackageEmitContext
{
    /// <summary>The Daml package this context was built for.</summary>
    public DamlPackage Package { get; }

    /// <summary>Root C# namespace every emitted type in the package lives in.</summary>
    public string RootNamespace { get; }

    /// <summary>Qualifier scoped to the package's generated namespaces.</summary>
    public TypeReferenceQualifier Qualifier { get; }

    /// <summary>Last-wins lookup of every data type across all modules, keyed by simple name.</summary>
    public IReadOnlyDictionary<string, DamlDataType> DataTypes { get; }

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
        IReadOnlySet<string> localEnumQualifiedNames,
        IReadOnlySet<string> localVariantQualifiedNames,
        IReadOnlySet<string> interfacePlaceholderQualifiedNames,
        IReadOnlyDictionary<string, string> localChoiceArgToTemplate)
    {
        Package = package;
        RootNamespace = rootNamespace;
        Qualifier = qualifier;
        DataTypes = dataTypes;
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
                dataTypes[dataType.Name] = dataType;
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
                    if (choice.ArgumentType is DamlTypeRef typeRef && dataTypes.ContainsKey(typeRef.Name))
                    {
                        var key = $"{typeRef.Module}:{typeRef.Name}";
                        if (localChoiceArgToTemplate.TryGetValue(key, out var existingTemplate)
                            && existingTemplate != template.Name)
                        {
                            logger?.Warning(
                                $"Choice-argument type {key} is used by both templates {existingTemplate} and {template.Name} in the same package; keeping {existingTemplate} and ignoring {template.Name}. Rename one choice-argument type to disambiguate (see issue #368).");
                            continue;
                        }
                        localChoiceArgToTemplate[key] = template.Name;
                    }
                }
            }
        }

        return new PackageEmitContext(
            package,
            rootNamespace,
            qualifier,
            dataTypes,
            localEnumQualifiedNames,
            localVariantQualifiedNames,
            interfacePlaceholderQualifiedNames,
            localChoiceArgToTemplate);
    }
}
