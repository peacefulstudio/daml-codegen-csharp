// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;
using Microsoft.Extensions.Logging;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// The template a choice-argument record is emitted nested inside, identified by the
/// module that declares the template and the template's Daml name. The record's own
/// declaring module may differ, so a reference to the record must be qualified with the
/// template's namespace, not the record's.
/// </summary>
/// <param name="Module">The Daml module declaring the template.</param>
/// <param name="Name">The template's Daml name, before sanitisation.</param>
internal sealed record NestingTemplate(string Module, string Name);

/// <summary>
/// Immutable value the C# emitter threads through its emit methods. Built once per package
/// by <see cref="ForPackage"/>, which hands back one context per module: the package-wide
/// scan — the data-type lookup, the reserved and marker names, the choice-argument homes —
/// is shared by all of them, while <see cref="Module"/>, <see cref="Namespace"/> and
/// <see cref="Qualifier"/> belong to the module being emitted. Read-only during emission.
/// </summary>
internal sealed partial class PackageEmitContext
{
    /// <summary>The Daml package this context was built for.</summary>
    public DamlPackage Package { get; }

    /// <summary>The module of <see cref="Package"/> whose types this context emits.</summary>
    public DamlModule Module { get; }

    /// <summary>
    /// The C# namespace <see cref="Module"/>'s types are emitted into — the module name
    /// itself, prefixed by <see cref="CodeGenOptions.NamespacePrefix"/> on the main package
    /// (see <see cref="Identifiers.ModuleNamespace"/>).
    /// </summary>
    public string Namespace { get; }

    /// <summary>
    /// The C# namespace of every module in <see cref="Package"/>, keyed by module name.
    /// Identical across the package's module contexts; consulted when a reference from one
    /// module names a type declared in another, which must then be qualified with the
    /// declaring module's namespace.
    /// </summary>
    public IReadOnlyDictionary<string, string> ModuleNamespaces { get; }

    /// <summary>Qualifier scoped to <see cref="Namespace"/>.</summary>
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
    /// every template plus every record/enum/variant, excluding the records LF declares
    /// alongside a same-named interface (they are replaced by the marker itself, so counting
    /// them would falsely self-disambiguate) and choice-argument records (they are emitted nested inside
    /// their parent template, not at the top level). This is the reserved-name input
    /// <see cref="Identifiers.InterfaceMarkerName"/> disambiguates interface marker names
    /// against: reserving across the whole package rather than per namespace keeps the marker
    /// assignment independent of how modules map to namespaces, so every reference to an
    /// interface — local or foreign — derives the same marker. The shadow set handed to
    /// <see cref="Qualifier"/> is narrower: only the types declared in <see cref="Namespace"/>
    /// or one of its ancestor namespaces can shadow an imported name there.
    /// </summary>
    public IReadOnlySet<string> LocalReservedTypeNames { get; }

    /// <summary>
    /// Sanitised C# names of the top-level types emitted for <see cref="Module"/>: its
    /// templates, its records/enums/variants other than interface placeholders and
    /// choice-argument records, and its interface markers. The input the namespace guards
    /// compare against every emitted namespace.
    /// </summary>
    public IReadOnlySet<string> TopLevelTypeNames { get; }

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
    /// Module-qualified (<c>Module:Name</c>) names of the records LF declares alongside an
    /// interface of the same name in the same module. They are not emitted: the marker
    /// carries the interface's identity, and <c>ContractId&lt;IMarker&gt;</c> serves the
    /// contract-id fields and choice extensions that would otherwise need a record. The set
    /// is therefore the emitter's "this local name is an interface, not a record" oracle —
    /// read by the record emitter to skip the declaration, by the cross-package resolver and
    /// the choice-created-slot walker to resolve a local ref to its marker, and by
    /// <see cref="LocalViewRecord(DamlTypeRef)"/> to reject a view naming an interface.
    /// </summary>
    public IReadOnlySet<string> LocalInterfaceQualifiedNames { get; }

    /// <summary>
    /// Maps a choice-argument type's module-qualified (<c>Module:Name</c>) name to the
    /// template it is emitted nested inside, for qualifying nested choice-argument types
    /// declared in this package. Module-qualified because Daml allows the same simple name in
    /// multiple modules — keying on the simple name alone would let one module's choice-arg
    /// type silently shadow another's and mis-resolve cross-references. The value carries the
    /// template's module because the argument record may be declared in a different module
    /// than the template that nests it.
    /// </summary>
    public IReadOnlyDictionary<string, NestingTemplate> LocalChoiceArgToTemplate { get; }

    /// <summary>
    /// Maps a record's module-qualified (<c>Module:Name</c>) name to the C# marker name
    /// of the single local interface declaring that record as its view type. Only
    /// package-local, non-generic record views with exactly one viewing interface and no
    /// field that mirrors onto the marker under a different name or over a member the
    /// marker already declares are mapped: a dependency package is emitted without
    /// knowledge of its dependents, so a foreign view record cannot be stamped with this
    /// package's markers; a record stamped with two markers would inherit two explicit
    /// implementations of the same identity statics — no most specific implementation, a
    /// compile error; and a field whose two mirror names disagree would leave the marker
    /// declaring a member the record never implements. The record emitter stamps the
    /// marker into the view record's base list, and the interface emitter mirrors the
    /// view's fields onto the marker for the same set, so both degrade together.
    /// </summary>
    public IReadOnlyDictionary<string, string> LocalViewRecordMarkerNames { get; }

    /// <summary>
    /// Returns true when <paramref name="typeRef"/> points at a type declared in this
    /// package — either an empty package id (self-reference) or a matching package id.
    /// </summary>
    public bool IsLocalRef(DamlTypeRef typeRef) =>
        string.IsNullOrEmpty(typeRef.PackageId)
        || typeRef.PackageId == Package.PackageId;

    /// <summary>
    /// Returns the C# namespace of <paramref name="moduleName"/>, which must be a module of
    /// <see cref="Package"/>; a local reference into a module the package does not declare
    /// is a malformed model and fails the emit rather than being spelled as a name nothing
    /// declares.
    /// </summary>
    public string NamespaceOf(string moduleName) =>
        ModuleNamespaces.TryGetValue(moduleName, out var moduleNamespace)
            ? moduleNamespace
            : throw new CodegenException(
                $"Module '{moduleName}' is referenced as local to package '{Package.Name}', which declares no such module. " +
                "The Daml model is malformed: a same-package reference must name a module of that package.");

    /// <summary>
    /// Spells a type name so it binds to the type even where a nearer member or nested type
    /// of the same spelling would otherwise capture it: a bare name — a type declared in
    /// <see cref="Module"/>'s own namespace — comes back <c>global::</c>-rooted under
    /// <see cref="Namespace"/>; a name that already carries a dot was spelled with its own
    /// qualifier by the resolver, another module's or another package's namespace, and is
    /// returned unchanged.
    /// </summary>
    public string QualifyInModule(string typeName) =>
        typeName.Contains('.', StringComparison.Ordinal)
            ? typeName
            : Identifiers.GlobalQualified(Namespace, typeName);

    /// <summary>
    /// Returns true when <paramref name="iface"/>'s view type can stand as the
    /// <c>TView</c> of a <see cref="Daml.Runtime.Contracts.ViewDescriptor{TInterface, TView}"/>,
    /// whose <c>TView : IDamlRecord&lt;TView&gt;</c> constraint admits only a non-generic
    /// record. A local view reference must resolve to one here; a foreign reference is
    /// taken as such, since a dependency package emits its own non-generic records with
    /// that facet. A view naming a variant, an enum, a generic record, an interface, or a
    /// type this package does not declare therefore degrades to the
    /// bare <see cref="Daml.Runtime.Contracts.IHasView{TView}"/> facet rather than an
    /// uncompilable witness.
    /// </summary>
    public bool HasWitnessableViewRecord(DamlInterface iface) =>
        iface.ViewType is DamlTypeRef viewRef
        && (!IsLocalRef(viewRef) || LocalViewRecord(viewRef) is not null);

    /// <summary>
    /// Returns the non-generic record definition <paramref name="viewRef"/> names in this
    /// package, or <c>null</c> when it names an interface, a generic record, a non-record
    /// definition, or a type this package does not declare. Callers that also
    /// care about locality must test <see cref="IsLocalRef"/> first — the lookup key
    /// carries no package id, so a foreign reference can otherwise collide with a
    /// same-named local declaration.
    /// </summary>
    public DamlRecordDefinition? LocalViewRecord(DamlTypeRef viewRef) =>
        LocalViewRecord(viewRef, DataTypes, LocalInterfaceQualifiedNames);

    private static DamlRecordDefinition? LocalViewRecord(
        DamlTypeRef viewRef,
        IReadOnlyDictionary<string, DamlDataType> dataTypes,
        IReadOnlySet<string> localInterfaceQualifiedNames)
    {
        ArgumentNullException.ThrowIfNull(viewRef);
        var viewKey = $"{viewRef.Module}:{viewRef.Name}";
        return !localInterfaceQualifiedNames.Contains(viewKey)
            && dataTypes.TryGetValue(viewKey, out var viewDataType)
            && viewDataType.TypeParams.Count == 0
            && viewDataType.Definition is DamlRecordDefinition viewRecord
                ? viewRecord
                : null;
    }

    private PackageEmitContext(
        PackageScan scan,
        DamlModule module,
        string moduleNamespace,
        TypeReferenceQualifier qualifier,
        IReadOnlySet<string> topLevelTypeNames)
    {
        Package = scan.Package;
        Module = module;
        Namespace = moduleNamespace;
        ModuleNamespaces = scan.ModuleNamespaces;
        Qualifier = qualifier;
        TopLevelTypeNames = topLevelTypeNames;
        DataTypes = scan.DataTypes;
        LocalReservedTypeNames = scan.ReservedTypeNames;
        LocalInterfaceMarkerNames = scan.InterfaceMarkerNames;
        LocalEnumQualifiedNames = scan.EnumQualifiedNames;
        LocalVariantQualifiedNames = scan.VariantQualifiedNames;
        LocalInterfaceQualifiedNames = scan.InterfaceQualifiedNames;
        LocalChoiceArgToTemplate = scan.ChoiceArgToTemplate;
        LocalViewRecordMarkerNames = scan.ViewRecordMarkerNames;
    }

    private sealed record PackageScan(
        DamlPackage Package,
        IReadOnlyDictionary<string, string> ModuleNamespaces,
        IReadOnlyDictionary<string, DamlDataType> DataTypes,
        IReadOnlyDictionary<string, IReadOnlySet<string>> ReservedTypeNamesByModule,
        IReadOnlySet<string> ReservedTypeNames,
        IReadOnlyDictionary<string, string> InterfaceMarkerNames,
        IReadOnlySet<string> EnumQualifiedNames,
        IReadOnlySet<string> VariantQualifiedNames,
        IReadOnlySet<string> InterfaceQualifiedNames,
        IReadOnlyDictionary<string, NestingTemplate> ChoiceArgToTemplate,
        IReadOnlyDictionary<string, string> ViewRecordMarkerNames);

    /// <summary>
    /// Scans <paramref name="package"/> once and returns one fully-populated immutable
    /// context per module, in declaration order: maps every module to its namespace
    /// (honouring <see cref="CodeGenOptions.NamespacePrefix"/> when
    /// <paramref name="isMainPackage"/>), builds the package-wide data-type lookup and the
    /// local enum / variant / interface / choice-argument name sets, and scopes each
    /// context's <see cref="Qualifier"/> to its module's namespace. When two templates in
    /// the package map the same module-qualified choice-argument type,
    /// <paramref name="logger"/> (when supplied) receives a warning and the first-seen mapping
    /// is kept.
    /// </summary>
    public static IReadOnlyList<PackageEmitContext> ForPackage(
        DamlPackage package,
        CodeGenOptions options,
        bool isMainPackage,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(options);

        var scan = Scan(package, NamespacesByModule(package, options, isMainPackage), logger);

        return package.Modules
            .Select(module => new PackageEmitContext(
                scan,
                module,
                scan.ModuleNamespaces[module.Name],
                new TypeReferenceQualifier(scan.ModuleNamespaces[module.Name], ShadowingTypeNames(scan, module)),
                TopLevelTypeNamesOf(scan, module)))
            .ToList();
    }

    private static IReadOnlyDictionary<string, string> NamespacesByModule(
        DamlPackage package, CodeGenOptions options, bool isMainPackage)
    {
        var namespaces = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var module in package.Modules)
        {
            if (!namespaces.TryAdd(module.Name, Identifiers.ModuleNamespace(module.Name, isMainPackage, options)))
            {
                throw new CodegenException(
                    $"Package '{package.Name}' declares module '{module.Name}' more than once. " +
                    "The Daml model is malformed: module names are unique within a package.");
            }
        }
        return namespaces;
    }

    /// <summary>
    /// The top-level type names that shadow an imported runtime/BCL name inside
    /// <paramref name="module"/>'s namespace: C# binds a simple name by walking the
    /// enclosing namespaces outward before consulting <c>using</c> directives, so a type
    /// declared in the module's own namespace or in any ancestor namespace binds first,
    /// while a type in a sibling namespace does not.
    /// </summary>
    private static IReadOnlySet<string> ShadowingTypeNames(PackageScan scan, DamlModule module)
    {
        var emittingNamespace = scan.ModuleNamespaces[module.Name];
        var shadowing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (moduleName, moduleNamespace) in scan.ModuleNamespaces)
        {
            if (Identifiers.StartsWithSegments(emittingNamespace, moduleNamespace))
            {
                shadowing.UnionWith(scan.ReservedTypeNamesByModule[moduleName]);
            }
        }
        return shadowing;
    }

    private static IReadOnlySet<string> TopLevelTypeNamesOf(PackageScan scan, DamlModule module)
    {
        var names = new HashSet<string>(scan.ReservedTypeNamesByModule[module.Name], StringComparer.Ordinal);
        foreach (var iface in module.Interfaces)
        {
            names.Add(scan.InterfaceMarkerNames[$"{module.Name}:{iface.Name}"]);
        }
        return names;
    }

    private static PackageScan Scan(
        DamlPackage package,
        IReadOnlyDictionary<string, string> moduleNamespaces,
        ILogger? logger)
    {
        var reservedTypeNamesByModule = ReservedTopLevelTypeNamesByModule(package);
        var reservedTypeNames = Union(reservedTypeNamesByModule.Values);

        var dataTypes = new Dictionary<string, DamlDataType>();
        var enumQualifiedNames = new HashSet<string>();
        var variantQualifiedNames = new HashSet<string>();
        var interfaceQualifiedNames = new HashSet<string>();
        foreach (var module in package.Modules)
        {
            var interfaceNames = module.Interfaces.Select(i => i.Name).ToHashSet();

            foreach (var dataType in module.DataTypes)
            {
                dataTypes[$"{module.Name}:{dataType.Name}"] = dataType;
                if (dataType.Definition is DamlEnumDefinition)
                {
                    enumQualifiedNames.Add($"{module.Name}:{dataType.Name}");
                }
                if (dataType.Definition is DamlVariantDefinition)
                {
                    variantQualifiedNames.Add($"{module.Name}:{dataType.Name}");
                }
                if (interfaceNames.Contains(dataType.Name))
                {
                    interfaceQualifiedNames.Add($"{module.Name}:{dataType.Name}");
                }
            }
        }

        var choiceArgToTemplate = new Dictionary<string, NestingTemplate>();
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
                            if (choiceArgToTemplate.TryGetValue(key, out var existingTemplate)
                                && existingTemplate.Name != template.Name)
                            {
                                if (logger is not null)
                                {
                                    LogAmbiguousLocalChoiceArgument(logger, key, existingTemplate.Name, template.Name);
                                }
                                continue;
                            }
                            choiceArgToTemplate[key] = new NestingTemplate(module.Name, template.Name);
                        }
                    }
                }
            }
        }

        var interfaceMarkerNames = InterfaceMarkerNames(package, reservedTypeNames);
        var viewRecordMarkerNames = ViewRecordMarkerNames(
            package, dataTypes, interfaceQualifiedNames, interfaceMarkerNames);

        return new PackageScan(
            package,
            moduleNamespaces,
            dataTypes,
            reservedTypeNamesByModule,
            reservedTypeNames,
            interfaceMarkerNames,
            enumQualifiedNames,
            variantQualifiedNames,
            interfaceQualifiedNames,
            choiceArgToTemplate,
            viewRecordMarkerNames);
    }

    /// <summary>
    /// Maps every package-local, non-generic view record with exactly one viewing
    /// interface and a clean field mirror to that interface's marker name — the source of
    /// <see cref="LocalViewRecordMarkerNames"/>. Foreign view references, references to
    /// types this package does not declare, generic records, non-record definitions, and
    /// the records LF declares alongside a same-named interface are all excluded (only a
    /// record this package emits itself can be stamped with a marker); a record viewed by more than one interface is
    /// excluded because the stamp would inherit two explicit implementations of the same
    /// identity statics — no most specific implementation, a compile error; and a record
    /// failing <see cref="ViewFieldsMirrorCleanly"/> is excluded because the marker would
    /// declare a member the stamped record does not implement.
    /// </summary>
    private static IReadOnlyDictionary<string, string> ViewRecordMarkerNames(
        DamlPackage package,
        IReadOnlyDictionary<string, DamlDataType> dataTypes,
        IReadOnlySet<string> localInterfaceQualifiedNames,
        IReadOnlyDictionary<string, string> localInterfaceMarkerNames)
    {
        var markersByViewRecord = new Dictionary<string, SortedSet<string>>();
        foreach (var module in package.Modules)
        {
            foreach (var iface in module.Interfaces)
            {
                if (iface.ViewType is not DamlTypeRef viewRef
                    || (!string.IsNullOrEmpty(viewRef.PackageId) && viewRef.PackageId != package.PackageId)
                    || LocalViewRecord(viewRef, dataTypes, localInterfaceQualifiedNames) is null)
                {
                    continue;
                }

                var viewKey = $"{viewRef.Module}:{viewRef.Name}";
                if (!markersByViewRecord.TryGetValue(viewKey, out var markers))
                {
                    markers = new SortedSet<string>(StringComparer.Ordinal);
                    markersByViewRecord[viewKey] = markers;
                }
                markers.Add(localInterfaceMarkerNames[$"{module.Name}:{iface.Name}"]);
            }
        }

        return markersByViewRecord
            .Where(entry => entry.Value.Count == 1)
            .Select(entry => (ViewKey: entry.Key, Marker: entry.Value.Single()))
            .Where(pair => ViewFieldsMirrorCleanly(dataTypes[pair.ViewKey], pair.Marker))
            .ToDictionary(pair => pair.ViewKey, pair => pair.Marker);
    }

    /// <summary>
    /// Members a generated interface marker declares in its own right, which a mirrored
    /// view field must not shadow: the <c>View</c> witness and the <c>InterfaceId</c>
    /// identity re-declaration (CS0102).
    /// </summary>
    private static readonly IReadOnlySet<string> MarkerDeclaredMemberNames =
        new HashSet<string>(StringComparer.Ordinal) { "View", "InterfaceId" };

    /// <summary>
    /// Returns true when every field of <paramref name="viewRecord"/> mirrors onto
    /// <paramref name="markerName"/> under the same C# member name the record itself emits
    /// for it, and under no name the marker already declares. The two sides derive their
    /// member names independently, each disambiguating only against its own enclosing type
    /// (CS0542), so a field PascalCasing to the record's name is emitted as <c>Name_</c>
    /// there and <c>Name</c> on the marker — and vice versa for a field PascalCasing to the
    /// marker's name — leaving the record short of a marker member (CS0535). A field
    /// PascalCasing to <c>View</c> or <c>InterfaceId</c> would instead redeclare a member
    /// the marker already owns (CS0102). Either way the pair is ineligible and the record
    /// stays un-stamped beside an un-enriched marker.
    /// </summary>
    private static bool ViewFieldsMirrorCleanly(DamlDataType viewRecord, string markerName)
    {
        if (viewRecord.Definition is not DamlRecordDefinition record)
        {
            return false;
        }

        var recordClassName = Identifiers.Sanitize(viewRecord.Name);
        return record.Fields.All(field =>
        {
            var markerMemberName = Identifiers.MemberName(field.Name, markerName);
            return markerMemberName == Identifiers.MemberName(field.Name, recordClassName)
                && !MarkerDeclaredMemberNames.Contains(markerMemberName);
        });
    }

    /// <summary>
    /// Computes the sanitised C# name of every top-level type declared anywhere in
    /// <paramref name="package"/> — every template plus every record/enum/variant,
    /// excluding the records LF declares alongside a same-named interface (replaced by the
    /// marker itself) and choice-argument records (emitted nested inside their parent
    /// template, not at the top level) — the single source of the reserved-name set
    /// <see cref="Identifiers.InterfaceMarkerName"/> disambiguates against, shared by
    /// <see cref="ForPackage"/> (for the emitting package) and the cross-package
    /// resolver (for foreign packages) so both sides derive the same marker name.
    /// </summary>
    internal static IReadOnlySet<string> ReservedTopLevelTypeNames(DamlPackage package) =>
        Union(ReservedTopLevelTypeNamesByModule(package).Values);

    /// <summary>
    /// The per-module breakdown of <see cref="ReservedTopLevelTypeNames"/>, keyed by module
    /// name: the source of each module's <see cref="TopLevelTypeNames"/> and of the shadow
    /// set its <see cref="Qualifier"/> is scoped to.
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlySet<string>> ReservedTopLevelTypeNamesByModule(DamlPackage package)
    {
        var localInterfaceQualifiedNames = new HashSet<string>();
        var dataTypeNames = new HashSet<string>();
        foreach (var module in package.Modules)
        {
            var interfaceNames = module.Interfaces.Select(i => i.Name).ToHashSet();
            foreach (var dataType in module.DataTypes)
            {
                dataTypeNames.Add(dataType.Name);
                if (interfaceNames.Contains(dataType.Name))
                {
                    localInterfaceQualifiedNames.Add($"{module.Name}:{dataType.Name}");
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

        var reservedByModule = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        foreach (var module in package.Modules)
        {
            var reserved = new HashSet<string>(StringComparer.Ordinal);
            foreach (var template in module.Templates)
            {
                reserved.Add(Identifiers.Sanitize(template.Name));
            }
            foreach (var dataType in module.DataTypes)
            {
                var qualifiedName = $"{module.Name}:{dataType.Name}";
                if (localInterfaceQualifiedNames.Contains(qualifiedName)
                    || choiceArgumentQualifiedNames.Contains(qualifiedName))
                {
                    continue;
                }
                reserved.Add(Identifiers.Sanitize(dataType.Name));
            }
            reservedByModule[module.Name] = reserved;
        }
        return reservedByModule;
    }

    private static IReadOnlySet<string> Union(IEnumerable<IReadOnlySet<string>> sets)
    {
        var union = new HashSet<string>(StringComparer.Ordinal);
        foreach (var set in sets)
        {
            union.UnionWith(set);
        }
        return union;
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

    [LoggerMessage(
        EventId = 1200,
        Level = LogLevel.Warning,
        Message = "Choice-argument type {ChoiceArgumentKey} is used by both templates {KeptTemplate} and {IgnoredTemplate} in the same package; keeping {KeptTemplate} and ignoring {IgnoredTemplate}. Rename one choice-argument type to disambiguate.")]
    private static partial void LogAmbiguousLocalChoiceArgument(ILogger logger, string choiceArgumentKey, string keptTemplate, string ignoredTemplate);
}
