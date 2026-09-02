// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;
using Microsoft.Extensions.Logging;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Immutable per-package value the C# emitter threads through its emit methods: the
/// root namespace, the <see cref="TypeReferenceQualifier"/>, the per-package
/// data-type lookup, and the local enum / variant / interface / choice-argument
/// name sets. Built once per package by
/// <see cref="ForPackage"/>; read-only during emission.
/// </summary>
internal sealed partial class PackageEmitContext
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
    /// every template plus every record/enum/variant, excluding the records LF declares
    /// alongside a same-named interface (they are replaced by the marker itself, so counting
    /// them would falsely self-disambiguate) and choice-argument records (they are emitted nested inside
    /// their parent template, not at the top level). The package's C# namespace is flat
    /// across all its modules, so this set has two consumers: it is the reserved-name
    /// input <see cref="Identifiers.InterfaceMarkerName"/> disambiguates interface marker
    /// names against, and it is passed to <see cref="Qualifier"/> so a package-declared
    /// type that collides with an imported runtime/BCL name (e.g. a Daml <c>enum Unit</c>)
    /// is qualified with <c>global::</c> instead of silently shadowing it.
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
    /// Maps a choice-argument type's module-qualified (<c>Module:Name</c>) name to its
    /// parent template name, for qualifying nested choice-argument types declared in this
    /// package. Module-qualified because Daml allows the same simple name in multiple
    /// modules — keying on the simple name alone would let one module's choice-arg type
    /// silently shadow another's and mis-resolve cross-references.
    /// </summary>
    public IReadOnlyDictionary<string, string> LocalChoiceArgToTemplate { get; }

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
        DamlPackage package,
        string rootNamespace,
        TypeReferenceQualifier qualifier,
        IReadOnlyDictionary<string, DamlDataType> dataTypes,
        IReadOnlySet<string> localReservedTypeNames,
        IReadOnlyDictionary<string, string> localInterfaceMarkerNames,
        IReadOnlySet<string> localEnumQualifiedNames,
        IReadOnlySet<string> localVariantQualifiedNames,
        IReadOnlySet<string> localInterfaceQualifiedNames,
        IReadOnlyDictionary<string, string> localChoiceArgToTemplate,
        IReadOnlyDictionary<string, string> localViewRecordMarkerNames)
    {
        Package = package;
        RootNamespace = rootNamespace;
        Qualifier = qualifier;
        DataTypes = dataTypes;
        LocalReservedTypeNames = localReservedTypeNames;
        LocalInterfaceMarkerNames = localInterfaceMarkerNames;
        LocalEnumQualifiedNames = localEnumQualifiedNames;
        LocalVariantQualifiedNames = localVariantQualifiedNames;
        LocalInterfaceQualifiedNames = localInterfaceQualifiedNames;
        LocalChoiceArgToTemplate = localChoiceArgToTemplate;
        LocalViewRecordMarkerNames = localViewRecordMarkerNames;
    }

    /// <summary>
    /// Scans <paramref name="package"/> and returns a fully-populated immutable context:
    /// derives the root namespace (honouring <see cref="CodeGenOptions.RootNamespace"/>),
    /// builds the global data-type lookup, and populates the local enum / variant /
    /// interface / choice-argument name sets. When two templates in the
    /// package map the same module-qualified choice-argument type, <paramref name="logger"/>
    /// (when supplied) receives a warning and the first-seen mapping is kept.
    /// </summary>
    public static PackageEmitContext ForPackage(
        DamlPackage package,
        CodeGenOptions options,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(options);

        var rootNamespace = options.RootNamespace ?? Identifiers.DeriveNamespace(package.Name);
        var localReservedTypeNames = ReservedTopLevelTypeNames(package);
        var qualifier = new TypeReferenceQualifier([rootNamespace], localReservedTypeNames);

        var dataTypes = new Dictionary<string, DamlDataType>();
        var localEnumQualifiedNames = new HashSet<string>();
        var localVariantQualifiedNames = new HashSet<string>();
        var localInterfaceQualifiedNames = new HashSet<string>();
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
                    localInterfaceQualifiedNames.Add($"{module.Name}:{dataType.Name}");
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
                                if (logger is not null)
                                {
                                    LogAmbiguousLocalChoiceArgument(logger, key, existingTemplate, template.Name);
                                }
                                continue;
                            }
                            localChoiceArgToTemplate[key] = template.Name;
                        }
                    }
                }
            }
        }

        var localInterfaceMarkerNames = InterfaceMarkerNames(package, localReservedTypeNames);
        var localViewRecordMarkerNames = ViewRecordMarkerNames(
            package, dataTypes, localInterfaceQualifiedNames, localInterfaceMarkerNames);

        return new PackageEmitContext(
            package,
            rootNamespace,
            qualifier,
            dataTypes,
            localReservedTypeNames,
            localInterfaceMarkerNames,
            localEnumQualifiedNames,
            localVariantQualifiedNames,
            localInterfaceQualifiedNames,
            localChoiceArgToTemplate,
            localViewRecordMarkerNames);
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
    internal static IReadOnlySet<string> ReservedTopLevelTypeNames(DamlPackage package)
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
                if (localInterfaceQualifiedNames.Contains(qualifiedName)
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

    [LoggerMessage(
        EventId = 1200,
        Level = LogLevel.Warning,
        Message = "Choice-argument type {ChoiceArgumentKey} is used by both templates {KeptTemplate} and {IgnoredTemplate} in the same package; keeping {KeptTemplate} and ignoring {IgnoredTemplate}. Rename one choice-argument type to disambiguate.")]
    private static partial void LogAmbiguousLocalChoiceArgument(ILogger logger, string choiceArgumentKey, string keptTemplate, string ignoredTemplate);
}
