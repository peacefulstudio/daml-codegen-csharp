// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.RegularExpressions;
using Daml.Codegen.CSharp.Model;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Generates C# code from Daml packages.
/// </summary>
public sealed partial class CSharpCodeGenerator(CodeGenOptions options, ICodegenLogger logger)
{
    private readonly Regex? _rootFilter = options.RootFilter is not null
        ? new Regex(options.RootFilter, RegexOptions.Compiled)
        : null;

    private IDarSource? _currentArchive;
    private DamlPackage? _currentPackage;
    private readonly HashSet<string> _externalPackageIds = [];

    // Module-qualified enum type names for the package currently being generated.
    // Required because Daml allows the same simple name in multiple modules — e.g.
    // splice-amulet defines both `Splice.Amulet:Amulet` (record/template) and
    // `Splice.AmuletConfig:Amulet` (enum). A name-only lookup would dispatch every
    // `Amulet` reference through *Extensions.FromDamlEnum, breaking the record case.
    private readonly HashSet<string> _localEnumQualifiedNames = [];

    // Module-qualified names of variants declared in the package being generated. A
    // variant ref must route through the variant's ToVariant/FromVariant (it serializes
    // as DamlVariant), never the record-shaped ToRecord/FromRecord catch-all.
    private readonly HashSet<string> _localVariantQualifiedNames = [];

    // Module-qualified names of records that exist purely as the C# placeholder for a
    // Daml interface declaration. Daml-LF emits one record per interface (with the
    // same name and an empty fields list) so that `ContractId I` has a phantom type
    // parameter — see C# code emitted for splice-api-token-holding-v1's `Holding`
    // record alongside the `IHolding` interface. Those placeholder records are
    // generated as `: ITemplate` with throwing static metadata so `ContractId<T>`
    // (which constrains `T : ITemplate`) keeps compile-time safety while still
    // accommodating Daml interface-typed contract ids.
    private readonly HashSet<string> _interfacePlaceholderQualifiedNames = [];

    private readonly Dictionary<string, string> _localChoiceArgToTemplate = [];
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _foreignChoiceArgCache = [];

    private TypeReferenceQualifier _qualifier = new([]);
    private string _currentNamespace = string.Empty;

    /// <summary>
    /// Generates C# code for all types in the DAR.
    /// </summary>
    public IReadOnlyList<GeneratedFile> Generate(IDarSource dar)
    {
        var files = new List<GeneratedFile>();

        _currentArchive = dar;
        _currentPackage = dar.MainPackage;
        _externalPackageIds.Clear();

        // Generate code for the main package
        files.AddRange(GeneratePackage(dar.MainPackage));

        // Optionally generate code for dependencies
        if (options.IncludeDependencies)
        {
            foreach (var dep in dar.Dependencies)
            {
                logger.Debug($"Generating code for dependency: {dep.Name}");
                _currentPackage = dep;
                files.AddRange(GeneratePackage(dep));
            }
            _currentPackage = dar.MainPackage;
        }

        // Generate project file if requested
        if (options.GenerateProjectFile)
        {
            // Resolve every cross-package id encountered during codegen against the DAR
            // archive. If a foreign package is genuinely missing from the DAR (i.e.
            // GetPackageById returns null), warn — silently dropping it would emit a
            // csproj that references types whose NuGet package never gets a
            // <PackageReference>, surfacing later as opaque CS0246 at consumer
            // build time.
            var externalRefs = new List<DamlPackage>();
            foreach (var id in _externalPackageIds)
            {
                var pkg = dar.GetPackageById(id);
                if (pkg is null)
                {
                    logger.Warning($"External package id {id[..Math.Min(16, id.Length)]}… is not present in the DAR — no <PackageReference> will be emitted for it. Generated code that references it will fail to compile.");
                    continue;
                }
                if (IsStdlibPackage(pkg.Name) || IsPlaceholderPackageName(pkg.Name))
                {
                    continue;
                }
                externalRefs.Add(pkg);
            }
            var projectGenerator = new ProjectFileGenerator(options);
            // Pass the actually-emitted file set (post-RootFilter, post-IncludeDependencies)
            // so the LangVersion pin tracks what's in this project, not what's in the
            // unfiltered main package — see ProjectFileGenerator.RequiresCSharp13.
            files.Add(projectGenerator.GenerateProjectFile(dar.MainPackage, externalRefs, files));
        }

        var requiresCSharp13 = files.Any(f =>
            f.RelativePath.EndsWith(".cs", StringComparison.Ordinal)
            && ProjectFileGenerator.ContentRequiresCSharp13(f.Content));
        files.Add(new GeneratedFile(
            ".daml-langversion",
            requiresCSharp13 ? "13\n" : string.Empty));

        return files;
    }

    /// <summary>
    /// Returns true for packages whose types are baked into Daml.Runtime
    /// (daml-prim, daml-stdlib) and therefore must not be emitted as a
    /// PackageReference. Cross-package refs into these packages are mapped
    /// to Daml.Runtime.Stdlib.* types.
    /// </summary>
    private static bool IsStdlibPackage(string packageName) =>
        packageName.StartsWith("daml-prim", StringComparison.Ordinal)
        || packageName.StartsWith("daml-stdlib", StringComparison.Ordinal)
        || packageName.StartsWith("ghc-stdlib", StringComparison.Ordinal);

    private static bool IsPlaceholderPackageName(string packageName) =>
        string.IsNullOrEmpty(packageName)
        || packageName == "-no-package-metadata"
        || packageName == "UnknownPackage";

    /// <summary>
    /// Builds a mapping of choice-argument type name to parent template name for the
    /// given package. Used by <see cref="ResolveTypeRefName"/> to qualify cross-package
    /// refs that point at a type nested inside a foreign template.
    /// </summary>
    private static IReadOnlyDictionary<string, string> BuildForeignChoiceArgToTemplate(DamlPackage pkg)
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
                        result[typeRef.Name] = template.Name;
                    }
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Maps a stdlib type reference to its simple Daml.Runtime.Stdlib type name (no namespace),
    /// or null if there is no known mapping (callers route a non-null result through the central
    /// qualifier and require <c>using Daml.Runtime.Stdlib;</c>; a null result drives the
    /// build-fails-loudly path so the gap is visible).
    /// </summary>
    private static string? MapStdlibType(string module, string typeName) => (module, typeName) switch
    {
        ("DA.Time.Types", "RelTime") => "RelTime",
        ("DA.Types", "Tuple2") => "Tuple2",
        ("DA.Types", "Tuple3") => "Tuple3",
        ("DA.Types", "Either") => "Either",
        ("DA.Set.Types", "Set") => "Set",
        ("DA.NonEmpty.Types", "NonEmpty") => "NonEmpty",
        ("DA.Map.Types", "Map") => "Map",
        ("DA.Internal.Map", "Map") => "Map",
        _ => null,
    };

    /// <summary>
    /// Returns true if a stdlib reference points at one of the parameterised
    /// stdlib types whose <c>ToRecord</c>/<c>FromRecord</c> need caller-supplied
    /// converters per type argument (delegate-based round-trip).
    /// </summary>
    private static bool IsParametricStdlibType(string module, string typeName) => (module, typeName) switch
    {
        ("DA.Types", "Tuple2") or ("DA.Types", "Tuple3") => true,
        ("DA.Types", "Either") => true,
        ("DA.Set.Types", "Set") => true,
        ("DA.NonEmpty.Types", "NonEmpty") => true,
        ("DA.Map.Types", "Map") or ("DA.Internal.Map", "Map") => true,
        _ => false,
    };

    /// <summary>
    /// Returns true if <paramref name="typeRef"/> resolves to a known stdlib type
    /// in a known stdlib package. Gating on package id matters because user
    /// packages can legally define a module named e.g. <c>DA.Types</c> with
    /// a type <c>Tuple2</c>; without the package check the codegen would
    /// route those through <c>Daml.Runtime.Stdlib.*</c> and emit broken code.
    /// </summary>
    private bool IsStdlibTypeRef(DamlTypeRef typeRef, bool parametric) =>
        IsInStdlibPackage(typeRef)
        && (parametric
            ? IsParametricStdlibType(typeRef.Module, typeRef.Name)
            : MapStdlibType(typeRef.Module, typeRef.Name) is not null);

    private bool IsInStdlibPackage(DamlTypeRef typeRef)
    {
        if (string.IsNullOrEmpty(typeRef.PackageId) || _currentArchive is null)
        {
            return false;
        }
        var foreignPkg = _currentArchive.GetPackageById(typeRef.PackageId);
        return foreignPkg is not null
            && (IsStdlibPackage(foreignPkg.Name) || IsPlaceholderPackageName(foreignPkg.Name));
    }

    private bool IsLocalTypeRef(DamlTypeRef typeRef) =>
        string.IsNullOrEmpty(typeRef.PackageId)
        || _currentPackage is null
        || typeRef.PackageId == _currentPackage.PackageId;

    private bool IsLocalEnumTypeRef(DamlTypeRef typeRef) =>
        IsLocalTypeRef(typeRef)
        && _localEnumQualifiedNames.Contains($"{typeRef.Module}:{typeRef.Name}");

    private bool IsCrossPackageEnumTypeRef(DamlTypeRef typeRef) =>
        !IsLocalTypeRef(typeRef)
        && (_currentArchive?.GetPackageById(typeRef.PackageId)?.Modules
            .Where(m => m.Name == typeRef.Module)
            .SelectMany(m => m.DataTypes)
            .Any(dt => dt.Name == typeRef.Name && dt.Definition is DamlEnumDefinition)
            ?? false);

    private bool IsEnumTypeRef(DamlTypeRef typeRef) =>
        IsLocalEnumTypeRef(typeRef) || IsCrossPackageEnumTypeRef(typeRef);

    private bool IsLocalVariantTypeRef(DamlTypeRef typeRef) =>
        IsLocalTypeRef(typeRef)
        && _localVariantQualifiedNames.Contains($"{typeRef.Module}:{typeRef.Name}");

    private bool IsCrossPackageVariantTypeRef(DamlTypeRef typeRef) =>
        !IsLocalTypeRef(typeRef)
        && (_currentArchive?.GetPackageById(typeRef.PackageId)?.Modules
            .Where(m => m.Name == typeRef.Module)
            .SelectMany(m => m.DataTypes)
            .Any(dt => dt.Name == typeRef.Name && dt.Definition is DamlVariantDefinition)
            ?? false);

    private bool IsVariantTypeRef(DamlTypeRef typeRef) =>
        IsLocalVariantTypeRef(typeRef) || IsCrossPackageVariantTypeRef(typeRef);

    private string QualifiedEnumExtensionsCall(DamlTypeRef typeRef, string method, string argument) =>
        $"{ResolveTypeRefName(typeRef)}Extensions.{method}({argument})";

    /// <summary>
    /// Resolves a DamlTypeRef to a C# identifier or fully qualified name.
    /// Local refs return the bare sanitized name (qualified with the parent template
    /// name when the type is a nested choice-argument type); cross-package refs return
    /// a fully qualified name and record the package id so a PackageReference can be
    /// emitted for it.
    /// </summary>
    private string ResolveTypeRefName(DamlTypeRef typeRef)
    {
        var sanitized = SanitizeIdentifier(typeRef.Name);

        if (IsLocalTypeRef(typeRef))
        {
            if (_localChoiceArgToTemplate.TryGetValue(typeRef.Name, out var parentTemplate))
            {
                return $"{SanitizeIdentifier(parentTemplate)}.{sanitized}";
            }
            return sanitized;
        }

        if (_currentArchive is null)
        {
            throw new InvalidOperationException(
                $"Cross-package type ref {typeRef.Module}:{typeRef.Name} (package {typeRef.PackageId[..Math.Min(16, typeRef.PackageId.Length)]}…) cannot be resolved — no archive context. Codegen requires a DarArchive that includes every transitively-referenced package.");
        }

        var foreignPkg = _currentArchive.GetPackageById(typeRef.PackageId);
        if (foreignPkg is null)
        {
            throw new InvalidOperationException(
                $"Cross-package type ref {typeRef.Module}:{typeRef.Name} points at package {typeRef.PackageId[..Math.Min(16, typeRef.PackageId.Length)]}… which is not present in the DAR. Rebuild the DAR with the missing package included, or pass a multi-DAR input that resolves it.");
        }

        if (IsStdlibPackage(foreignPkg.Name) || IsPlaceholderPackageName(foreignPkg.Name))
        {
            var mapped = MapStdlibType(typeRef.Module, typeRef.Name);
            if (mapped is not null)
            {
                return _qualifier.Qualify(mapped, _currentNamespace);
            }
            // Unknown stdlib type — leave unqualified so the build error points at the gap.
            logger.Warning($"Unmapped stdlib type {foreignPkg.Name}:{typeRef.Module}:{typeRef.Name} — generated code will not compile.");
            return sanitized;
        }

        _externalPackageIds.Add(typeRef.PackageId);
        var foreignNs = DeriveNamespace(foreignPkg.Name);
        if (!_foreignChoiceArgCache.TryGetValue(typeRef.PackageId, out var foreignChoiceArgMap))
        {
            foreignChoiceArgMap = BuildForeignChoiceArgToTemplate(foreignPkg);
            _foreignChoiceArgCache[typeRef.PackageId] = foreignChoiceArgMap;
        }
        if (foreignChoiceArgMap.TryGetValue(typeRef.Name, out var foreignParentTemplate))
        {
            return $"{foreignNs}.{SanitizeIdentifier(foreignParentTemplate)}.{sanitized}";
        }
        return $"{foreignNs}.{sanitized}";
    }

    /// <summary>
    /// Generates C# code for a single package.
    /// </summary>
    private IEnumerable<GeneratedFile> GeneratePackage(DamlPackage package)
    {
        // The root namespace comes from the package name (e.g., cats-markets -> Cats.Markets)
        // All types from all modules go into this single namespace
        var rootNamespace = options.RootNamespace ?? DeriveNamespace(package.Name);
        _currentNamespace = rootNamespace;
        _qualifier = new TypeReferenceQualifier([rootNamespace]);

        // Build a global lookup of all data types across all modules for cross-module references
        var globalDataTypes = new Dictionary<string, (DamlModule Module, DamlDataType DataType)>();
        foreach (var module in package.Modules)
        {
            foreach (var dataType in module.DataTypes)
            {
                globalDataTypes[dataType.Name] = (module, dataType);
            }
        }

        // Collect all data types and template names from all modules.
        // Daml allows the same simple type name in different modules within one package
        // (e.g. splice-amulet defines `Amulet` in multiple modules), so this dictionary
        // is built defensively as last-wins instead of via ToDictionary, which would throw
        // on the first collision.
        var allDataTypesInGroup = new Dictionary<string, DamlDataType>();
        _localEnumQualifiedNames.Clear();
        _localVariantQualifiedNames.Clear();
        _interfacePlaceholderQualifiedNames.Clear();
        _localChoiceArgToTemplate.Clear();
        _foreignChoiceArgCache.Clear();
        foreach (var module in package.Modules)
        {
            // Every Daml interface in the module declares a same-named record placeholder
            // in its data types; flag those names so the record emitter can produce the
            // ITemplate-with-throwing-stubs shape. The check is module-local because a
            // package may have an interface `Foo` in one module and an unrelated record
            // `Foo` in another.
            var interfaceNames = module.Interfaces.Select(i => i.Name).ToHashSet();

            foreach (var dataType in module.DataTypes)
            {
                allDataTypesInGroup[dataType.Name] = dataType;
                if (dataType.Definition is DamlEnumDefinition)
                {
                    _localEnumQualifiedNames.Add($"{module.Name}:{dataType.Name}");
                }
                if (dataType.Definition is DamlVariantDefinition)
                {
                    _localVariantQualifiedNames.Add($"{module.Name}:{dataType.Name}");
                }
                if (interfaceNames.Contains(dataType.Name))
                {
                    _interfacePlaceholderQualifiedNames.Add($"{module.Name}:{dataType.Name}");
                }
            }
        }

        var allTemplateNames = package.Modules
            .SelectMany(m => m.Templates)
            .Select(t => t.Name)
            .ToHashSet();

        foreach (var module in package.Modules)
        {
            foreach (var template in module.Templates)
            {
                foreach (var choice in template.Choices)
                {
                    if (choice.ArgumentType is DamlTypeRef typeRef && allDataTypesInGroup.ContainsKey(typeRef.Name))
                    {
                        _localChoiceArgToTemplate[typeRef.Name] = template.Name;
                    }
                }
            }
        }

        foreach (var module in package.Modules)
        {
            // Build a lookup of data types by name for populating template fields
            var dataTypesByName = module.DataTypes
                .Where(dt => dt.Definition is DamlRecordDefinition)
                .ToDictionary(dt => dt.Name, dt => (DamlRecordDefinition)dt.Definition!);

            // Generate templates
            foreach (var template in module.Templates)
            {
                if (_rootFilter is not null && !_rootFilter.IsMatch($"{module.Name}:{template.Name}"))
                {
                    logger.Debug($"Skipping template {module.Name}:{template.Name} (filtered)");
                    continue;
                }

                // Get fields from corresponding data type if template has none
                var fields = template.Fields.Count > 0
                    ? template.Fields
                    : (dataTypesByName.TryGetValue(template.Name, out var recordDef) ? recordDef.Fields : []);

                var code = GenerateTemplate(package, module, template, fields, allDataTypesInGroup, rootNamespace);
                var path = Path.Combine(
                    rootNamespace.Replace(".", Path.DirectorySeparatorChar.ToString()),
                    $"{SanitizeIdentifier(template.Name)}.cs");

                yield return new GeneratedFile(path, code);
            }

            // Generate data types (skip templates and choice argument types - they're generated as nested types)
            foreach (var dataType in module.DataTypes)
            {
                // Skip data types that correspond to templates - they're generated as part of the template
                if (allTemplateNames.Contains(dataType.Name))
                {
                    continue;
                }

                // Skip data types that are choice argument types - they're generated as nested types in the template
                if (_localChoiceArgToTemplate.ContainsKey(dataType.Name))
                {
                    continue;
                }

                var code = GenerateDataType(package, module, dataType, rootNamespace, allDataTypesInGroup);
                var path = Path.Combine(
                    rootNamespace.Replace(".", Path.DirectorySeparatorChar.ToString()),
                    $"{SanitizeIdentifier(dataType.Name)}.cs");

                yield return new GeneratedFile(path, code);
            }

            // Generate choice argument types as nested classes in partial template files
            foreach (var template in module.Templates)
            {
                if (_rootFilter is not null && !_rootFilter.IsMatch($"{module.Name}:{template.Name}"))
                {
                    continue;
                }

                foreach (var choice in template.Choices)
                {
                    // Check if this choice's argument type is a data type that should be nested
                    if (choice.ArgumentType is DamlTypeRef typeRef &&
                        allDataTypesInGroup.TryGetValue(typeRef.Name, out var argDataType) &&
                        argDataType.Definition is DamlRecordDefinition)
                    {
                        var code = GenerateNestedChoiceArgumentType(
                            package, module, template, choice, argDataType, rootNamespace, allDataTypesInGroup);
                        var path = Path.Combine(
                            rootNamespace.Replace(".", Path.DirectorySeparatorChar.ToString()),
                            $"{SanitizeIdentifier(template.Name)}.{SanitizeIdentifier(choice.Name)}.cs");

                        yield return new GeneratedFile(path, code);
                    }
                }
            }

            // Generate interfaces
            foreach (var iface in module.Interfaces)
            {
                if (_rootFilter is not null && !_rootFilter.IsMatch($"{module.Name}:{iface.Name}"))
                {
                    logger.Debug($"Skipping interface {module.Name}:{iface.Name} (filtered)");
                    continue;
                }

                var code = GenerateInterface(package, module, iface, allDataTypesInGroup, rootNamespace);
                var path = Path.Combine(
                    rootNamespace.Replace(".", Path.DirectorySeparatorChar.ToString()),
                    $"I{SanitizeIdentifier(iface.Name)}.cs");

                yield return new GeneratedFile(path, code);
            }
        }

        // Generate ContractIdentifiers helper class if enabled
        if (options.GenerateContractIdentifiers)
        {
            var allTemplates = package.Modules
                .SelectMany(m => m.Templates.Select(t => (Module: m, Template: t)))
                .Where(x => _rootFilter is null || _rootFilter.IsMatch($"{x.Module.Name}:{x.Template.Name}"))
                .ToList();

            if (allTemplates.Count > 0)
            {
                var identifiersFile = GenerateContractIdentifiersFile(package, allTemplates, rootNamespace);
                yield return identifiersFile;
            }
        }
    }

    /// <summary>
    /// Gets the base module name (first component) from a full module name.
    /// e.g., "Markets.MarketMembershipRequest" -> "Markets"
    /// </summary>
    private static string GetBaseModuleName(string moduleName)
    {
        var dotIndex = moduleName.IndexOf('.');
        return dotIndex > 0 ? moduleName[..dotIndex] : moduleName;
    }

    /// <summary>
    /// Generates C# code for a template.
    /// </summary>
    private string GenerateTemplate(
        DamlPackage package,
        DamlModule module,
        DamlTemplate template,
        IReadOnlyList<DamlField> fields,
        IReadOnlyDictionary<string, DamlDataType> dataTypes,
        string moduleNamespace)
    {
        var bodySb = new StringBuilder();
        var indent = new IndentWriter(bodySb);

        RequireCommonNamespaces(indent);

        if (options.UseFileScopedNamespaces)
        {
            indent.AppendLine($"namespace {moduleNamespace};");
            indent.AppendLine();
        }
        else
        {
            indent.AppendLine($"namespace {moduleNamespace}");
            indent.AppendLine("{");
            indent.Indent();
        }

        // Template class documentation
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Generated from Daml template {module.Name}:{template.Name}");
            indent.AppendLine("/// </summary>");
        }

        // Template record
        var className = SanitizeIdentifier(template.Name);
        indent.CurrentTypeName = className;

        // Determine interfaces to implement
        var keyType = template.Key is not null ? MapDamlTypeToCSharp(template.Key) : null;
        var interfacesList = new List<string> { _qualifier.Qualify("ITemplate", _currentNamespace) };
        if (keyType is not null)
            interfacesList.Add($"{_qualifier.Qualify("IHasKey", _currentNamespace)}<{keyType}>");
        if (package.UpgradedPackageId is not null)
            interfacesList.Add(_qualifier.Qualify("IUpgradeable", _currentNamespace));
        var interfaces = string.Join(", ", interfacesList);

        if (options.UseRecordTypes && options.UsePrimaryConstructors && fields.Count > 0)
        {
            // Record with primary constructor - use partial for nested types in separate files
            indent.Append($"public sealed partial record {className}(");
            WriteRecordParameters(indent, fields);
            indent.AppendLine($") : {interfaces}");
        }
        else if (options.UseRecordTypes)
        {
            indent.AppendLine($"public sealed partial record {className} : {interfaces}");
        }
        else
        {
            indent.AppendLine($"public sealed partial class {className} : {interfaces}");
        }

        indent.AppendLine("{");
        indent.Indent();

        // Static template metadata
        WriteTemplateMetadata(indent, package, module, template);

        // Key property (if template has a key)
        if (template.Key is not null)
        {
            WriteKeyProperty(indent, template.Key);
        }

        // Properties (if not using primary constructor)
        if (!options.UsePrimaryConstructors || !options.UseRecordTypes)
        {
            WriteProperties(indent, fields);
        }

        // ToRecord method
        WriteToRecordMethod(indent, fields);

        // FromRecord method
        WriteFromRecordMethod(indent, className, fields, dataTypes);

        // Choice argument types and methods
        foreach (var choice in template.Choices)
        {
            WriteChoiceArgumentType(indent, choice, dataTypes);
            WriteChoiceMethod(indent, choice, dataTypes);
        }

        // ContractId nested class
        WriteContractIdClass(indent, className);

        // Contract nested class
        WriteContractClass(indent, className);

        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine();

        // Typed <Choice>Result structs + FromCreatedContracts projectors are
        // emitted at namespace level (sibling of the template) so the static
        // <TemplateName>Extensions class below can reference them by their
        // unqualified name. See CSharpCodeGenerator.ChoiceResults.cs.
        WriteChoiceResultStructs(indent, template, moduleNamespace);

        // Static `<TemplateName>Extensions` class with one `<Choice>Async`
        // method per create-bearing choice. Lives at the namespace level so
        // `using` directives bring the extension methods into scope for
        // `ContractId<TemplateName>` receivers. The exerciser threads the
        // typed-controller analysis (named Party params per controller when
        // statically resolvable) and the union of template-level + choice-level
        // observers (contributed to SubmitterInfo.readAs) into each method.
        WriteChoiceAsyncExercisersClass(indent, template, className, fields, dataTypes);

        // Named-submitter extensions: per-template static class that exposes
        // CreateAsync and a <Choice>Async per choice, with one Party parameter
        // per Daml signatory/controller that the static analyzer couldn't
        // derive from the payload. Lives at the file's top level (sibling to
        // the template record) so the extensions resolve with a single using
        // of this namespace. See CSharpCodeGenerator.NamedSubmitters.cs.
        TryWriteNamedSubmitterExtensions(indent, template, fields, dataTypes);

        // Typed exerciser wrappers for choices whose return type is *not* a bare
        // ContractId T (Decimal, records, lists, Unit, etc.). Emitted at the
        // file's top level (sibling to the template class, not nested) so the
        // extension methods are accessible with a single using of this
        // namespace. See CSharpCodeGenerator.NonContractChoiceWrappers.cs for
        // the emission rules.
        TryWriteNonContractChoiceExtensions(indent, template, dataTypes);

        if (!options.UseFileScopedNamespaces)
        {
            indent.Dedent();
            indent.AppendLine("}");
        }

        return BuildFileContent(indent, bodySb);
    }

    /// <summary>
    /// Generates C# code for a data type.
    /// </summary>
    private string GenerateDataType(
        DamlPackage package,
        DamlModule module,
        DamlDataType dataType,
        string moduleNamespace,
        IReadOnlyDictionary<string, DamlDataType>? allDataTypes = null)
    {
        var bodySb = new StringBuilder();
        var indent = new IndentWriter(bodySb);

        RequireCommonNamespaces(indent);

        if (options.UseFileScopedNamespaces)
        {
            indent.AppendLine($"namespace {moduleNamespace};");
            indent.AppendLine();
        }
        else
        {
            indent.AppendLine($"namespace {moduleNamespace}");
            indent.AppendLine("{");
            indent.Indent();
        }

        switch (dataType.Definition)
        {
            case DamlRecordDefinition record:
                WriteRecordType(indent, module, dataType, record, allDataTypes);
                break;
            case DamlVariantDefinition variant:
                WriteVariantType(indent, dataType, variant, allDataTypes);
                break;
            case DamlEnumDefinition enumDef:
                WriteEnumType(indent, dataType, enumDef);
                break;
        }

        if (!options.UseFileScopedNamespaces)
        {
            indent.Dedent();
            indent.AppendLine("}");
        }

        return BuildFileContent(indent, bodySb);
    }

    /// <summary>
    /// Generates C# code for a Daml interface.
    /// </summary>
    private string GenerateInterface(
        DamlPackage package,
        DamlModule module,
        DamlInterface iface,
        IReadOnlyDictionary<string, DamlDataType> dataTypes,
        string moduleNamespace)
    {
        var bodySb = new StringBuilder();
        var indent = new IndentWriter(bodySb);

        RequireCommonNamespaces(indent);

        // Namespace
        if (options.UseFileScopedNamespaces)
        {
            indent.AppendLine($"namespace {moduleNamespace};");
            indent.AppendLine();
        }
        else
        {
            indent.AppendLine($"namespace {moduleNamespace}");
            indent.AppendLine("{");
            indent.Indent();
        }

        // Interface documentation
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Generated from Daml interface {module.Name}:{iface.Name}");
            indent.AppendLine("/// </summary>");
        }

        // Interface declaration
        var interfaceName = $"I{SanitizeIdentifier(iface.Name)}";
        indent.CurrentTypeName = interfaceName;

        // Determine view interface
        var viewType = iface.ViewType is not null ? MapDamlTypeToCSharp(iface.ViewType) : null;
        var interfaces = viewType is not null
            ? $"{_qualifier.Qualify("IDamlInterface", _currentNamespace)}, {_qualifier.Qualify("IHasView", _currentNamespace)}<{viewType}>"
            : _qualifier.Qualify("IDamlInterface", _currentNamespace);

        indent.AppendLine($"public interface {interfaceName} : {interfaces}");
        indent.AppendLine("{");
        indent.Indent();

        // Static interface metadata
        WriteInterfaceMetadata(indent, package, module, iface);

        // Generate method signatures for each choice
        foreach (var method in iface.Choices)
        {
            WriteInterfaceMethod(indent, method, dataTypes);
        }

        indent.Dedent();
        indent.AppendLine("}");

        // Sibling static class hosting the typed interface-choice exercisers.
        // Mirrors the shape Daml expects: `cid.exercise (toInterfaceContractId @I)
        // Choice with ...` becomes `await cid.ChoiceAsync(client, ...)` in C#.
        // The `cid` here is a `ContractId<I>` (interface marker), so the wire-level
        // `template_id` slot carries the interface id per Canton's gRPC semantics —
        // see ExerciseCommand.ForInterface in Daml.Runtime.
        if (iface.Choices.Count > 0)
        {
            indent.AppendLine();
            WriteInterfaceChoiceExtensions(indent, package, module, iface, interfaceName, dataTypes);
        }

        if (!options.UseFileScopedNamespaces)
        {
            indent.Dedent();
            indent.AppendLine("}");
        }

        return BuildFileContent(indent, bodySb);
    }

    private void WriteInterfaceChoiceExtensions(
        IndentWriter indent,
        DamlPackage package,
        DamlModule module,
        DamlInterface iface,
        string interfaceName,
        IReadOnlyDictionary<string, DamlDataType> dataTypes)
    {
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Static <c>&lt;Choice&gt;Async</c> extension methods for the <c>{iface.Name}</c> Daml interface.");
            indent.AppendLine("/// One method per choice; each submits an interface-typed");
            indent.AppendLine($"/// <see cref=\"global::Daml.Runtime.Commands.ExerciseCommand\"/> built via");
            indent.AppendLine($"/// <see cref=\"global::Daml.Runtime.Commands.ExerciseCommand.ForInterface{{TInterface}}(global::Daml.Runtime.Contracts.ContractId{{TInterface}},global::Daml.Runtime.Commands.ChoiceName,global::Daml.Runtime.Data.DamlValue)\"/>");
            indent.AppendLine("/// through <see cref=\"global::Daml.Ledger.Abstractions.ILedgerClient.TrySubmitAndWaitForTransactionAsync\"/>");
            indent.AppendLine($"/// and surfaces the raw <see cref=\"global::Daml.Runtime.Outcomes.ExerciseOutcome{{TransactionResult}}\"/> —");
            indent.AppendLine("/// interface choices have no typed <c>&lt;Choice&gt;Result</c> projection because the");
            indent.AppendLine("/// implementing template (and therefore the produced contracts' shapes) is unknown");
            indent.AppendLine("/// at the call site.");
            indent.AppendLine("/// </summary>");
        }

        var extensionsClassName = $"{interfaceName}Extensions";

        var emittable = iface.Choices.ToList();

        if (emittable.Count == 0)
        {
            return;
        }

        RequireAsyncExerciserNamespaces(indent);

        indent.AppendLine($"public static class {extensionsClassName}");
        indent.AppendLine("{");
        indent.Indent();

        for (var i = 0; i < emittable.Count; i++)
        {
            if (i > 0)
            {
                indent.AppendLine();
            }
            WriteInterfaceChoiceExtensionMethod(indent, emittable[i], interfaceName, dataTypes);
        }

        indent.Dedent();
        indent.AppendLine("}");
    }

    private void WriteInterfaceChoiceExtensionMethod(
        IndentWriter indent,
        DamlChoice choice,
        string interfaceName,
        IReadOnlyDictionary<string, DamlDataType> dataTypes)
    {
        var methodName = $"{SanitizeIdentifier(choice.Name)}Async";
        var (argTypeName, hasArg) = ResolveInterfaceChoiceArgType(choice);

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Exercises the <c>{choice.Name}</c> interface choice on this contract id.");
            indent.AppendLine("/// The wire-level <c>template_id</c> slot carries the interface id — Canton's");
            indent.AppendLine("/// ledger API resolves the concrete implementing template at the participant.");
            indent.AppendLine("/// </summary>");
            indent.AppendLine("/// <param name=\"contractId\">The interface-typed contract id to exercise on.</param>");
            indent.AppendLine("/// <param name=\"client\">The ledger client.</param>");
            if (hasArg)
            {
                indent.AppendLine("/// <param name=\"argument\">The choice argument.</param>");
            }
            indent.AppendLine("/// <param name=\"actAs\">The party submitting the command.</param>");
            indent.AppendLine("/// <param name=\"workflowId\">Optional workflow id; passed through to the ledger when supplied. No default — workflow IDs are correlation keys, and a per-choice default would bucket every submission of the same choice under one ID.</param>");
            indent.AppendLine("/// <param name=\"cancellationToken\">Cancellation token.</param>");
        }

        // Method signature mirrors the concrete-template <Choice>Async shape,
        // but skips the typed <Choice>Result projection: interface choices do not know
        // the implementing template at the call site, so the most useful return shape
        // is the raw ExerciseOutcome<TransactionResult> the ledger client surfaces.
        indent.AppendLine($"public static async Task<{_qualifier.Qualify("ExerciseOutcome", _currentNamespace)}<{_qualifier.Qualify("TransactionResult", _currentNamespace)}>> {methodName}(");
        indent.Indent();
        indent.AppendLine($"this {_qualifier.Qualify("ContractId", _currentNamespace)}<{interfaceName}> contractId,");
        indent.AppendLine($"{_qualifier.Qualify("ILedgerClient", _currentNamespace)} client,");
        if (hasArg)
        {
            indent.AppendLine($"{argTypeName} argument,");
        }
        indent.AppendLine($"{_qualifier.Qualify("Party", _currentNamespace)} actAs,");
        indent.AppendLine("string? workflowId = null,");
        indent.AppendLine("CancellationToken cancellationToken = default)");
        indent.Dedent();
        indent.AppendLine("{");
        indent.Indent();

        indent.AppendLine("ArgumentNullException.ThrowIfNull(contractId);");
        indent.AppendLine("ArgumentNullException.ThrowIfNull(client);");
        if (hasArg && choice.ArgumentType is DamlTypeRef)
        {
            indent.AppendLine("ArgumentNullException.ThrowIfNull(argument);");
        }

        var argExpr = hasArg
            ? GetToValueConversion(choice.ArgumentType, "argument")
            : $"{_qualifier.Qualify("DamlUnit", _currentNamespace)}.Instance";
        indent.AppendLine($"var command = {_qualifier.Qualify("ExerciseCommand", _currentNamespace)}.ForInterface<{interfaceName}>(contractId, new {_qualifier.Qualify("ChoiceName", _currentNamespace)}(\"{choice.Name}\"), {argExpr});");
        indent.AppendLine();
        indent.AppendLine($"var submission = {_qualifier.Qualify("CommandsSubmission", _currentNamespace)}.Single(command)");
        indent.Indent();
        indent.AppendLine(".WithActAs(actAs)");
        indent.AppendLine($".WithCommandId(new {_qualifier.Qualify("CommandId", _currentNamespace)}(Guid.NewGuid().ToString()));");
        indent.Dedent();
        indent.AppendLine("if (!string.IsNullOrEmpty(workflowId))");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine($"submission = submission.WithWorkflowId(new {_qualifier.Qualify("WorkflowId", _currentNamespace)}(workflowId));");
        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine();
        indent.AppendLine("return await client.TrySubmitAndWaitForTransactionAsync(submission, cancellationToken).ConfigureAwait(false);");

        indent.Dedent();
        indent.AppendLine("}");
    }

    private (string TypeName, bool HasArg) ResolveInterfaceChoiceArgType(DamlChoice choice)
    {
        if (choice.ArgumentType is DamlPrimitiveType { Primitive: DamlPrimitive.Unit })
        {
            return ("DamlUnit", false);
        }
        if (choice.ArgumentType is DamlTypeRef { Name: "Archive", Module: "DA.Internal.Template" } archiveRef
            && !string.IsNullOrEmpty(archiveRef.PackageId)
            && _currentArchive is not null
            && _currentArchive.GetPackageById(archiveRef.PackageId) is { } archivePkg
            && (IsStdlibPackage(archivePkg.Name) || IsPlaceholderPackageName(archivePkg.Name)))
        {
            return ("DamlUnit", false);
        }
        return (MapDamlTypeToCSharp(choice.ArgumentType), true);
    }

    /// <summary>
    /// Generates the ContractIdentifiers helper class with fully qualified identifiers for all templates.
    /// </summary>
    private GeneratedFile GenerateContractIdentifiersFile(
        DamlPackage package,
        IReadOnlyList<(DamlModule Module, DamlTemplate Template)> templates,
        string moduleNamespace)
    {
        var bodySb = new StringBuilder();
        var indent = new IndentWriter(bodySb);

        indent.Require("Daml.Runtime.Contracts");
        indent.Require("static Daml.Runtime.Contracts.TemplateExtensions");

        // Namespace
        if (options.UseFileScopedNamespaces)
        {
            indent.AppendLine($"namespace {moduleNamespace};");
            indent.AppendLine();
        }
        else
        {
            indent.AppendLine($"namespace {moduleNamespace}");
            indent.AppendLine("{");
            indent.Indent();
        }

        // Class documentation
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine("/// Provides fully qualified contract identifiers for all templates in this package.");
            indent.AppendLine("/// These identifiers can be used for PQS queries.");
            indent.AppendLine("/// </summary>");
        }

        indent.AppendLine("public static class ContractIdentifiers");
        indent.AppendLine("{");
        indent.Indent();

        // Generate a property for each template
        for (int i = 0; i < templates.Count; i++)
        {
            var (module, template) = templates[i];
            var templateClassName = SanitizeIdentifier(template.Name);

            if (options.GenerateXmlDocs)
            {
                indent.AppendLine("/// <summary>");
                indent.AppendLine($"/// Gets the fully qualified template identifier for {template.Name} contracts.");
                indent.AppendLine($"/// Format: {{packageName}}:{module.Name}:{template.Name}");
                indent.AppendLine("/// </summary>");
            }

            indent.AppendLine($"public static string {templateClassName} {{ get; }} = GetTemplateId<{templateClassName}>();");

            // Add blank line between properties, but not after the last one
            if (i < templates.Count - 1)
            {
                indent.AppendLine();
            }
        }

        indent.Dedent();
        indent.AppendLine("}");

        if (!options.UseFileScopedNamespaces)
        {
            indent.Dedent();
            indent.AppendLine("}");
        }

        // Place the file one level above the package folder (beside it, not inside)
        var namespacePath = moduleNamespace.Replace(".", Path.DirectorySeparatorChar.ToString());
        var parentPath = Path.GetDirectoryName(namespacePath) ?? string.Empty;
        var path = Path.Combine(parentPath, "ContractIdentifiers.cs");

        return new GeneratedFile(path, BuildFileContent(indent, bodySb));
    }

    /// <summary>
    /// Generates a partial file with the choice argument type nested inside the template.
    /// </summary>
    private string GenerateNestedChoiceArgumentType(
        DamlPackage package,
        DamlModule module,
        DamlTemplate template,
        DamlChoice choice,
        DamlDataType argDataType,
        string moduleNamespace,
        IReadOnlyDictionary<string, DamlDataType> dataTypes)
    {
        var bodySb = new StringBuilder();
        var indent = new IndentWriter(bodySb);

        RequireCommonNamespaces(indent);

        if (options.UseFileScopedNamespaces)
        {
            indent.AppendLine($"namespace {moduleNamespace};");
            indent.AppendLine();
        }
        else
        {
            indent.AppendLine($"namespace {moduleNamespace}");
            indent.AppendLine("{");
            indent.Indent();
        }

        // Partial template class containing the nested choice argument type
        var templateClassName = SanitizeIdentifier(template.Name);
        indent.AppendLine($"public sealed partial record {templateClassName}");
        indent.AppendLine("{");
        indent.Indent();

        // Write the nested choice argument record type
        if (argDataType.Definition is DamlRecordDefinition record)
        {
            var choiceTypeName = SanitizeIdentifier(choice.Name);

            if (options.GenerateXmlDocs)
            {
                indent.AppendLine("/// <summary>");
                indent.AppendLine($"/// Choice argument type for {choice.Name}.");
                indent.AppendLine("/// </summary>");
            }

            if (options.UseRecordTypes && options.UsePrimaryConstructors && record.Fields.Count > 0)
            {
                indent.Append($"public sealed record {choiceTypeName}(");
                WriteRecordParameters(indent, record.Fields);
                indent.AppendLine($") : {_qualifier.Qualify("IDamlRecord", _currentNamespace)}");
            }
            else
            {
                indent.AppendLine($"public sealed record {choiceTypeName} : {_qualifier.Qualify("IDamlRecord", _currentNamespace)}");
            }

            indent.AppendLine("{");
            indent.Indent();

            WriteToRecordMethod(indent, record.Fields);
            WriteFromRecordMethod(indent, choiceTypeName, record.Fields, dataTypes);

            indent.Dedent();
            indent.AppendLine("}");
        }

        indent.Dedent();
        indent.AppendLine("}");

        if (!options.UseFileScopedNamespaces)
        {
            indent.Dedent();
            indent.AppendLine("}");
        }

        return BuildFileContent(indent, bodySb);
    }

    private void WriteInterfaceMetadata(
        IndentWriter indent,
        DamlPackage package,
        DamlModule module,
        DamlInterface iface)
    {
        indent.Require("System");
        indent.Require("Daml.Runtime.Contracts");
        indent.AppendLine($"/// <summary>Gets the interface identifier.</summary>");
        indent.AppendLine($"static {_qualifier.Qualify("Identifier", _currentNamespace)} {_qualifier.Qualify("IDamlInterface", _currentNamespace)}.InterfaceId => new(\"{package.PackageId}\", \"{module.Name}\", \"{iface.Name}\");");
        indent.AppendLine();

        indent.AppendLine($"/// <summary>Gets the package ID.</summary>");
        indent.AppendLine($"static string {_qualifier.Qualify("IDamlInterface", _currentNamespace)}.PackageId => \"{package.PackageId}\";");
        indent.AppendLine();

        indent.AppendLine($"/// <summary>Gets the package name.</summary>");
        indent.AppendLine($"static string {_qualifier.Qualify("IDamlInterface", _currentNamespace)}.PackageName => \"{package.Name}\";");
        indent.AppendLine();

        indent.AppendLine($"/// <summary>Gets the package version.</summary>");
        indent.AppendLine($"static Version {_qualifier.Qualify("IDamlInterface", _currentNamespace)}.PackageVersion => new({package.Version.Major}, {package.Version.Minor}, {package.Version.Build});");
        indent.AppendLine();
    }

    private void WriteInterfaceMethod(IndentWriter indent, DamlChoice method, IReadOnlyDictionary<string, DamlDataType> dataTypes)
    {
        var methodName = SanitizeIdentifier(method.Name);
        var returnType = MapDamlTypeToCSharp(method.ReturnType);
        var (argTypeName, _, _, _) = GetChoiceArgumentInfo(method, dataTypes);

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Interface method {method.Name}.");
            indent.AppendLine("/// </summary>");
        }

        // Generate method signature
        if (argTypeName == "DamlUnit")
        {
            indent.AppendLine($"// Choice {method.Name}() -> {returnType}");
        }
        else
        {
            indent.AppendLine($"// Choice {method.Name}({argTypeName}) -> {returnType}");
        }
    }

    private void WriteFileHeader(IndentWriter indent)
    {
        indent.AppendLine("// <auto-generated>");
        indent.AppendLine("// This code was generated by daml-codegen-csharp.");
        indent.AppendLine("// Do not edit this file manually.");
        indent.AppendLine("// </auto-generated>");
        indent.AppendLine();

        if (options.EnableNullableReferenceTypes)
        {
            indent.AppendLine("#nullable enable");
            indent.AppendLine();
        }
    }

    private static void WriteTrackedUsings(IndentWriter headerIndent, IndentWriter bodyIndent)
    {
        foreach (var ns in bodyIndent.RequiredUsings)
        {
            headerIndent.AppendLine($"using {ns};");
        }
        headerIndent.AppendLine();
    }

    private string BuildFileContent(IndentWriter bodyIndent, StringBuilder bodySb)
    {
        var headerSb = new StringBuilder();
        var headerIndent = new IndentWriter(headerSb);
        WriteFileHeader(headerIndent);
        WriteTrackedUsings(headerIndent, bodyIndent);
        headerSb.Append(bodySb);
        return headerSb.ToString();
    }

    private void RequireCommonNamespaces(IndentWriter indent)
    {
        indent.Require("Daml.Runtime.Data");
    }

    private static void RequireAsyncExerciserNamespaces(IndentWriter indent)
    {
        indent.Require("System");
        indent.Require("System.Threading");
        indent.Require("System.Threading.Tasks");
        indent.Require("Daml.Ledger.Abstractions");
        indent.Require("Daml.Runtime.Commands");
        indent.Require("Daml.Runtime.Contracts");
        indent.Require("Daml.Runtime.Outcomes");
    }

    internal void RequireForFieldType(IndentWriter indent, DamlType type)
    {
        switch (type)
        {
            case DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.List } } app:
                indent.Require("System.Collections.Generic");
                indent.Require("System.Linq");
                RequireForFieldType(indent, app.Arguments[0]);
                break;
            case DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional } } app:
                RequireForFieldType(indent, app.Arguments[0]);
                break;
            case DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.TextMap } } app:
                indent.Require("System.Collections.Generic");
                indent.Require("System.Linq");
                RequireForFieldType(indent, app.Arguments[0]);
                break;
            case DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.GenMap } } app:
                indent.Require("System.Collections.Generic");
                indent.Require("System.Linq");
                RequireForFieldType(indent, app.Arguments[0]);
                RequireForFieldType(indent, app.Arguments[1]);
                break;
            case DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.ContractId } }:
                indent.Require("Daml.Runtime.Contracts");
                break;
            case DamlPrimitiveType { Primitive: DamlPrimitive.Date }:
            case DamlPrimitiveType { Primitive: DamlPrimitive.Timestamp }:
                indent.Require("System");
                break;
            case DamlPrimitiveType { Primitive: DamlPrimitive.Unit }:
                indent.Require("Daml.Runtime.Stdlib");
                break;
            case DamlTypeApp { Base: DamlTypeRef typeRef } app
                when IsStdlibTypeRef(typeRef, parametric: true):
                indent.Require("Daml.Runtime.Stdlib");
                foreach (var arg in app.Arguments)
                {
                    RequireForFieldType(indent, arg);
                }
                break;
            case DamlTypeRef typeRef when IsStdlibTypeRef(typeRef, parametric: false):
                indent.Require("Daml.Runtime.Stdlib");
                break;
            case DamlTypeVar:
                indent.Require("Daml.Runtime.Stdlib");
                break;
            case DamlTypeApp app:
                foreach (var argument in app.Arguments)
                {
                    if (argument is DamlTypeVar)
                    {
                        continue;
                    }
                    RequireForFieldType(indent, argument);
                }
                break;
        }
    }

    private void WriteTemplateMetadata(
        IndentWriter indent,
        DamlPackage package,
        DamlModule module,
        DamlTemplate template)
    {
        indent.Require("System");
        var templateId = $"{module.Name}:{template.Name}";

        indent.AppendLine($"/// <summary>Gets the template identifier.</summary>");
        indent.AppendLine($"public static {_qualifier.Qualify("Identifier", _currentNamespace)} TemplateId {{ get; }} = new(\"{package.PackageId}\", \"{module.Name}\", \"{template.Name}\");");
        indent.AppendLine();

        indent.AppendLine($"/// <summary>Gets the package ID.</summary>");
        indent.AppendLine($"public static string PackageId => \"{package.PackageId}\";");
        indent.AppendLine();

        indent.AppendLine($"/// <summary>Gets the package name.</summary>");
        indent.AppendLine($"public static string PackageName => \"{package.Name}\";");
        indent.AppendLine();

        indent.AppendLine($"/// <summary>Gets the package version.</summary>");
        indent.AppendLine($"public static Version PackageVersion {{ get; }} = new({package.Version.Major}, {package.Version.Minor}, {package.Version.Build});");
        indent.AppendLine();

        // Add upgraded package ID if this is an upgrade
        if (package.UpgradedPackageId is not null)
        {
            indent.AppendLine($"/// <summary>Gets the package ID that this package upgrades.</summary>");
            indent.AppendLine($"public static string? UpgradedPackageId => \"{package.UpgradedPackageId}\";");
            indent.AppendLine();
        }
    }

    private static string ToCrefTypeArgument(string csharpType) =>
        csharpType
            .Replace("global::", string.Empty, StringComparison.Ordinal)
            .Replace('<', '{')
            .Replace('>', '}');

    private void WriteKeyProperty(IndentWriter indent, DamlType keyType)
    {
        indent.Require("Daml.Runtime.Contracts");
        RequireForFieldType(indent, keyType);
        var csharpKeyType = MapDamlTypeToCSharp(keyType);
        var crefKeyType = ToCrefTypeArgument(csharpKeyType);

        // Full DALF key-expression analysis (mapping the template's `key` Daml
        // expression back to template fields, including tuple/record builders) is
        // not yet implemented. Until that lands, two
        // options exist for the codegen-emitted Key accessor:
        //   (1) emit a body that throws NotImplementedException — silent at compile,
        //       loud at runtime;
        //   (2) emit a partial property declaration that REQUIRES the consumer to
        //       supply an implementing partial — loud at compile (CS9248
        //       "Partial property must have an implementation part") and impossible
        //       to ship to production unnoticed.
        // We pick (2). Trade-off: consumers must use C# 13+ (partial-property
        // syntax) and add a hand-rolled partial declaration alongside the generated
        // template, satisfying the IHasKey<TKey> contract.
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Gets the contract key of type <c>{crefKeyType}</c>, satisfying <see cref=\"global::Daml.Runtime.Contracts.IHasKey{{TKey}}\"/>.");
            indent.AppendLine("/// </summary>");
            indent.AppendLine("/// <remarks>");
            indent.AppendLine("/// The body is supplied by a hand-rolled <c>partial</c> declaration in the");
            indent.AppendLine("/// consuming project until the full DALF key-expression analysis lands.");
            indent.AppendLine("/// Failure to supply the implementing partial is a compile-time error");
            indent.AppendLine("/// (Roslyn <c>CS9248</c>) — that is intentional and indicates the consuming");
            indent.AppendLine("/// project must opt in. Requires C# 13 or later on the consumer side.");
            indent.AppendLine("/// </remarks>");
        }
        indent.AppendLine($"public partial {csharpKeyType} Key {{ get; }}");
        indent.AppendLine();
    }

    private void WriteRecordParameters(IndentWriter indent, IReadOnlyList<DamlField> fields)
    {
        var first = true;
        foreach (var field in fields)
        {
            if (!first)
            {
                indent.Append(", ");
            }
            first = false;

            var csharpType = MapDamlTypeToCSharp(field.Type);
            var fieldName = ToPascalCase(SanitizeIdentifier(field.Name));
            RequireForFieldType(indent, field.Type);
            indent.Append($"{csharpType} {fieldName}");
        }
    }

    private void WriteProperties(IndentWriter indent, IReadOnlyList<DamlField> fields)
    {
        foreach (var field in fields)
        {
            var csharpType = MapDamlTypeToCSharp(field.Type);
            var fieldName = ToPascalCase(SanitizeIdentifier(field.Name));
            RequireForFieldType(indent, field.Type);

            if (options.GenerateXmlDocs)
            {
                indent.AppendLine($"/// <summary>Gets the {field.Name} field.</summary>");
            }

            indent.AppendLine($"public required {csharpType} {fieldName} {{ get; init; }}");
            indent.AppendLine();
        }
    }

    private void WriteToRecordMethod(IndentWriter indent, IReadOnlyList<DamlField> fields)
    {
        // Wording is "this value" rather than "this template" because the same
        // method is emitted for templates, plain records, and choice argument
        // records — the noun has to fit all three subjects.
        indent.AppendLine("/// <summary>Converts this value to a DamlRecord.</summary>");

        if (fields.Count == 0)
        {
            indent.AppendLine($"public {_qualifier.Qualify("DamlRecord", _currentNamespace)} ToRecord() => {_qualifier.Qualify("DamlRecord", _currentNamespace)}.Create();");
            indent.AppendLine();
            return;
        }

        // Use expression-bodied member with inline DamlRecord.Create
        indent.AppendLine($"public {_qualifier.Qualify("DamlRecord", _currentNamespace)} ToRecord() => {_qualifier.Qualify("DamlRecord", _currentNamespace)}.Create(");
        indent.Indent();

        for (int i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            var fieldName = ToPascalCase(SanitizeIdentifier(field.Name));
            var conversion = GetToValueConversion(field.Type, fieldName);
            var comma = i < fields.Count - 1 ? "," : "";
            RequireForFieldType(indent, field.Type);

            indent.AppendLine($"{_qualifier.Qualify("DamlField", _currentNamespace)}.Create(\"{field.Name}\", {conversion}){comma}");
        }

        indent.Dedent();
        indent.AppendLine(");");
        indent.AppendLine();
    }

    private void WriteFromRecordMethod(IndentWriter indent, string className, IReadOnlyList<DamlField> fields, IReadOnlyDictionary<string, DamlDataType>? dataTypes = null)
    {
        indent.AppendLine("/// <summary>Creates an instance from a DamlRecord.</summary>");

        if (fields.Count == 0)
        {
            indent.AppendLine($"public static {className} FromRecord({_qualifier.Qualify("DamlRecord", _currentNamespace)} record) => new {className}();");
            indent.AppendLine();
            return;
        }

        foreach (var field in fields)
        {
            RequireForFieldType(indent, field.Type);
        }

        if (options.UseRecordTypes && options.UsePrimaryConstructors)
        {
            indent.AppendLine($"public static {className} FromRecord({_qualifier.Qualify("DamlRecord", _currentNamespace)} record) => new {className}(");
            indent.Indent();

            for (int i = 0; i < fields.Count; i++)
            {
                var field = fields[i];
                var fieldName = ToPascalCase(SanitizeIdentifier(field.Name));
                var conversion = GetFromValueConversion(field.Type, $"record.GetRequiredField(\"{field.Name}\")", dataTypes);
                var comma = i < fields.Count - 1 ? "," : "";

                indent.AppendLine($"{fieldName}: {conversion}{comma}");
            }

            indent.Dedent();
            indent.AppendLine(");");
        }
        else
        {
            indent.AppendLine($"public static {className} FromRecord({_qualifier.Qualify("DamlRecord", _currentNamespace)} record)");
            indent.AppendLine("{");
            indent.Indent();

            indent.AppendLine($"return new {className}");
            indent.AppendLine("{");
            indent.Indent();

            foreach (var field in fields)
            {
                var fieldName = ToPascalCase(SanitizeIdentifier(field.Name));
                var conversion = GetFromValueConversion(field.Type, $"record.GetRequiredField(\"{field.Name}\")", dataTypes);
                indent.AppendLine($"{fieldName} = {conversion},");
            }

            indent.Dedent();
            indent.AppendLine("};");

            indent.Dedent();
            indent.AppendLine("}");
        }
        indent.AppendLine();
    }

    private (string TypeName, IReadOnlyList<DamlField>? Fields, bool IsFallback, bool IsNestedTemplateArg) GetChoiceArgumentInfo(
        DamlChoice choice,
        IReadOnlyDictionary<string, DamlDataType> dataTypes)
    {
        if (choice.ArgumentType is DamlTypeRef typeRef
            && IsLocalTypeRef(typeRef)
            && dataTypes.TryGetValue(typeRef.Name, out var dataType))
        {
            var fields = dataType.Definition is DamlRecordDefinition recordDef ? recordDef.Fields : null;
            return (SanitizeIdentifier(choice.Name), fields, false, true);
        }

        if (choice.ArgumentType is DamlPrimitiveType { Primitive: DamlPrimitive.Unit })
        {
            return ("DamlUnit", null, false, false);
        }

        if (choice.ArgumentType is DamlTypeRef externalRef)
        {
            if (externalRef is { Name: "Archive", Module: "DA.Internal.Template" }
                && !string.IsNullOrEmpty(externalRef.PackageId)
                && _currentArchive is not null
                && _currentArchive.GetPackageById(externalRef.PackageId) is { } archivePkg
                && (IsStdlibPackage(archivePkg.Name) || IsPlaceholderPackageName(archivePkg.Name)))
            {
                return ("DamlUnit", null, false, false);
            }
            return (ResolveTypeRefName(externalRef), null, false, false);
        }

        return ($"{SanitizeIdentifier(choice.Name)}Arg", null, true, true);
    }

    private void WriteChoiceArgumentType(IndentWriter indent, DamlChoice choice, IReadOnlyDictionary<string, DamlDataType> dataTypes)
    {
        var (_, _, isFallback, _) = GetChoiceArgumentInfo(choice, dataTypes);

        if (!isFallback)
        {
            return;
        }

        var choiceName = SanitizeIdentifier(choice.Name);
        indent.AppendLine($"/// <summary>Arguments for the {choice.Name} choice.</summary>");
        indent.AppendLine($"public sealed record {choiceName}Arg");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine("// TODO: Extract fields from argument type");
        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine();
    }

    private void WriteChoiceMethod(IndentWriter indent, DamlChoice choice, IReadOnlyDictionary<string, DamlDataType> dataTypes)
    {
        var choiceName = SanitizeIdentifier(choice.Name);
        var returnType = MapDamlTypeToCSharp(choice.ReturnType);
        var (argTypeName, _, isFallback, _) = GetChoiceArgumentInfo(choice, dataTypes);

        if (isFallback)
        {
            return;
        }

        indent.Require("Daml.Runtime.Commands");
        RequireForFieldType(indent, choice.ReturnType);

        indent.AppendLine("/// <summary>");
        indent.AppendLine($"/// Exercise the {choice.Name} choice.");
        if (choice.Consuming)
        {
            indent.AppendLine("/// This choice is consuming and will archive the contract.");
        }
        indent.AppendLine("/// </summary>");

        var argTypeRef = argTypeName == "DamlUnit"
            ? _qualifier.Qualify("DamlUnit", _currentNamespace)
            : argTypeName;
        indent.AppendLine($"public static {_qualifier.Qualify("Choice", _currentNamespace)}<{indent.CurrentTypeName}, {argTypeRef}, {returnType}> Choice{choiceName} {{ get; }} = new()");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine($"Name = new {_qualifier.Qualify("ChoiceName", _currentNamespace)}(\"{choice.Name}\"),");
        indent.AppendLine($"Consuming = {(choice.Consuming ? "true" : "false")},");

        if (argTypeName == "DamlUnit")
        {
            indent.AppendLine($"ArgumentEncoder = _ => {_qualifier.Qualify("DamlUnit", _currentNamespace)}.Instance,");
        }
        else
        {
            indent.AppendLine("ArgumentEncoder = arg => arg.ToRecord(),");
        }

        // Generate ResultDecoder
        WriteResultDecoder(indent, choice.ReturnType, returnType, dataTypes);

        indent.Dedent();
        indent.AppendLine("};");
        indent.AppendLine();
    }

    private void WriteResultDecoder(IndentWriter indent, DamlType returnType, string csharpReturnType, IReadOnlyDictionary<string, DamlDataType>? dataTypes)
    {
        // Keep the canonical short forms for trivial cases (Unit and the primitive
        // ContractId form) where the call-site reads more naturally than the helper's
        // output. Every other case — including type-refs (record/variant/enum) —
        // delegates to GetFromValueConversion so the result decoder picks up the same
        // module-qualified enum dispatch and TextMap/GenMap/Optional/List handling
        // that field deserialization uses. Earlier hand-rolled paths here used a
        // simple-name enum check that diverged from the module-qualified version,
        // and would silently route an enum return through DamlRecord.As<>() when a
        // same-named record existed in another module of the same package.
        switch (returnType)
        {
            case DamlPrimitiveType { Primitive: DamlPrimitive.Unit }:
                indent.AppendLine($"ResultDecoder = _ => {_qualifier.Qualify("DamlUnit", _currentNamespace)}.Instance");
                return;
            case DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.ContractId }, Arguments: [var arg] }:
                var contractType = MapDamlTypeToCSharp(arg);
                indent.AppendLine($"ResultDecoder = val => new {_qualifier.Qualify("ContractId", _currentNamespace)}<{contractType}>(val.As<{_qualifier.Qualify("DamlContractId", _currentNamespace)}>().Value)");
                return;
        }

        var expr = GetFromValueConversion(returnType, "val", dataTypes);
        indent.AppendLine($"ResultDecoder = val => {expr}");
    }

    private void WriteContractIdClass(IndentWriter indent, string className)
    {
        indent.Require("Daml.Runtime.Commands");
        indent.Require("Daml.Runtime.Contracts");
        indent.AppendLine($"/// <summary>Contract ID for {className}.</summary>");
        indent.AppendLine($"public sealed record ContractId(string Value) : {_qualifier.Qualify("ContractId", _currentNamespace)}<{className}>(Value), {_qualifier.Qualify("IExercises", _currentNamespace)}<{className}>");
        indent.AppendLine("{");
        indent.Indent();

        indent.AppendLine($"{_qualifier.Qualify("ContractId", _currentNamespace)}<{className}> {_qualifier.Qualify("IExercises", _currentNamespace)}<{className}>.ContractId => this;");

        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine();
    }

    private void WriteContractClass(IndentWriter indent, string className)
    {
        indent.Require("Daml.Runtime.Contracts");
        indent.AppendLine($"/// <summary>Active contract for {className}.</summary>");
        indent.AppendLine($"public sealed record Contract(ContractId Id, {className} Data) : {_qualifier.Qualify("IContract", _currentNamespace)}<ContractId, {className}>");
        indent.AppendLine("{");
        indent.Indent();

        indent.AppendLine("/// <summary>Creates a Contract from a CreatedEvent.</summary>");
        indent.AppendLine($"public static Contract FromCreatedEvent({_qualifier.Qualify("CreatedEvent", _currentNamespace)} @event) =>");
        indent.Indent();
        indent.AppendLine($"new(new ContractId(@event.ContractId), {className}.FromRecord(@event.CreateArguments));");
        indent.Dedent();

        indent.Dedent();
        indent.AppendLine("}");
    }

    private void WriteRecordType(IndentWriter indent, DamlModule module, DamlDataType dataType, DamlRecordDefinition record, IReadOnlyDictionary<string, DamlDataType>? dataTypes = null)
    {
        if (_interfacePlaceholderQualifiedNames.Contains($"{module.Name}:{dataType.Name}"))
        {
            WriteInterfacePlaceholderRecord(indent, module, dataType);
            return;
        }

        var className = SanitizeIdentifier(dataType.Name);
        var typeParams = GetTypeParametersDeclaration(dataType.TypeParams);
        var fullClassName = $"{className}{typeParams}";

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Generated from Daml record {dataType.Name}");
            if (dataType.TypeParams.Count > 0)
            {
                indent.AppendLine($"/// Type parameters: {string.Join(", ", dataType.TypeParams)}");
            }
            indent.AppendLine("/// </summary>");
            WriteTypeParamDocs(indent, dataType.TypeParams);
        }

        if (options.UseRecordTypes && options.UsePrimaryConstructors && record.Fields.Count > 0)
        {
            indent.Append($"public sealed record {fullClassName}(");
            WriteRecordParameters(indent, record.Fields);
            indent.AppendLine($") : {_qualifier.Qualify("IDamlRecord", _currentNamespace)}");
        }
        else
        {
            indent.AppendLine($"public sealed record {fullClassName} : {_qualifier.Qualify("IDamlRecord", _currentNamespace)}");
        }

        indent.AppendLine("{");
        indent.Indent();

        WriteToRecordMethod(indent, record.Fields);
        WriteFromRecordMethod(indent, fullClassName, record.Fields, dataTypes);

        indent.Dedent();
        indent.AppendLine("}");
    }

    /// <summary>
    /// Emits the C# placeholder for a Daml interface declaration. The Daml-LF emits a
    /// same-named empty record alongside every <c>interface I where ...</c> so that
    /// <c>ContractId I</c> can be expressed at the type level. We surface that record
    /// as a sealed record implementing <see cref="ITemplate"/> with throwing static
    /// metadata: it lets <c>ContractId&lt;I&gt;</c> compile (the runtime constraint is
    /// <c>where T : ITemplate</c>) but loudly fails any code path that tries to read
    /// <c>I.TemplateId</c> directly — which would be a logic error, since interface
    /// placeholders carry no template identity. Coerce the contract id to the
    /// underlying template type before reading metadata or constructing commands.
    /// </summary>
    private void WriteInterfacePlaceholderRecord(IndentWriter indent, DamlModule module, DamlDataType dataType)
    {
        indent.Require("System");
        indent.Require("Daml.Runtime.Contracts");
        var className = SanitizeIdentifier(dataType.Name);
        var qualifiedDamlName = $"{module.Name}:{dataType.Name}";
        var throwMessage =
            $"'{className}' is the C# placeholder for the Daml interface "
            + $"'{qualifiedDamlName}' and carries no template metadata. "
            + "Coerce ContractId<" + className + "> to a typed ContractId<TConcrete> before reading template metadata or exercising commands.";

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Phantom placeholder for the Daml interface <c>{qualifiedDamlName}</c>.");
            indent.AppendLine("/// Implements <see cref=\"ITemplate\"/> so that <c>ContractId&lt;" + className + "&gt;</c>");
            indent.AppendLine("/// satisfies its <c>where T : ITemplate</c> constraint, but every static");
            indent.AppendLine("/// metadata accessor throws — interface placeholders carry no template identity.");
            indent.AppendLine("/// </summary>");
        }

        indent.AppendLine($"public sealed record {className} : {_qualifier.Qualify("ITemplate", _currentNamespace)}");
        indent.AppendLine("{");
        indent.Indent();

        // Static metadata required by ITemplate. All four throw — they're never the
        // right thing to call on an interface placeholder, and a runtime exception
        // here is the cleanest signal that the caller should have coerced first.
        indent.AppendLine($"public static {_qualifier.Qualify("Identifier", _currentNamespace)} TemplateId =>");
        indent.Indent();
        indent.AppendLine($"throw new InvalidOperationException(\"{throwMessage}\");");
        indent.Dedent();
        indent.AppendLine();
        indent.AppendLine("public static string PackageId =>");
        indent.Indent();
        indent.AppendLine($"throw new InvalidOperationException(\"{throwMessage}\");");
        indent.Dedent();
        indent.AppendLine();
        indent.AppendLine("public static string PackageName =>");
        indent.Indent();
        indent.AppendLine($"throw new InvalidOperationException(\"{throwMessage}\");");
        indent.Dedent();
        indent.AppendLine();
        indent.AppendLine("public static Version PackageVersion =>");
        indent.Indent();
        indent.AppendLine($"throw new InvalidOperationException(\"{throwMessage}\");");
        indent.Dedent();
        indent.AppendLine();

        indent.AppendLine($"public {_qualifier.Qualify("DamlRecord", _currentNamespace)} ToRecord() => {_qualifier.Qualify("DamlRecord", _currentNamespace)}.Create();");
        indent.AppendLine();
        indent.AppendLine($"public static {className} FromRecord({_qualifier.Qualify("DamlRecord", _currentNamespace)} record) => new {className}();");

        indent.Dedent();
        indent.AppendLine("}");
    }

    private void WriteVariantType(
        IndentWriter indent,
        DamlDataType dataType,
        DamlVariantDefinition variant,
        IReadOnlyDictionary<string, DamlDataType>? dataTypes)
    {
        indent.Require("System");
        var className = SanitizeIdentifier(dataType.Name);
        var typeParams = GetTypeParametersDeclaration(dataType.TypeParams);
        var fullClassName = $"{className}{typeParams}";

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Generated from Daml variant {dataType.Name}");
            if (dataType.TypeParams.Count > 0)
            {
                indent.AppendLine($"/// Type parameters: {string.Join(", ", dataType.TypeParams)}");
            }
            indent.AppendLine("/// </summary>");
            WriteTypeParamDocs(indent, dataType.TypeParams);
        }

        // Base abstract record
        indent.AppendLine($"public abstract record {fullClassName} : {_qualifier.Qualify("IDamlVariant", _currentNamespace)}");
        indent.AppendLine("{");
        indent.Indent();

        indent.AppendLine("/// <summary>Gets the variant constructor name.</summary>");
        indent.AppendLine("public abstract string Tag { get; }");
        indent.AppendLine();

        indent.AppendLine("/// <summary>Converts to a DamlVariant.</summary>");
        indent.AppendLine($"public abstract {_qualifier.Qualify("DamlVariant", _currentNamespace)} ToVariant();");
        indent.AppendLine();

        indent.AppendLine($"/// <summary>Reconstructs {IndefiniteArticleFor(className)} {className} by dispatching on the DamlVariant constructor tag.</summary>");
        indent.AppendLine($"public static {fullClassName} FromVariant({_qualifier.Qualify("DamlVariant", _currentNamespace)} variant) =>");
        indent.Indent();
        indent.AppendLine("variant.Constructor switch");
        indent.AppendLine("{");
        indent.Indent();
        foreach (var ctor in variant.Constructors)
        {
            var ctorName = SanitizeIdentifier(ctor.Name);
            if (HasVariantPayload(ctor))
            {
                indent.AppendLine($"\"{ctor.Name}\" => new {ctorName}({GetFromValueConversion(ctor.ArgumentType!, "variant.Value", dataTypes)}),");
            }
            else
            {
                indent.AppendLine($"\"{ctor.Name}\" => new {ctorName}(),");
            }
        }
        indent.AppendLine($"_ => throw new ArgumentOutOfRangeException(nameof(variant), variant.Constructor, \"Unknown {className} constructor\")");
        indent.Dedent();
        indent.AppendLine("};");
        indent.Dedent();
        indent.AppendLine();

        // Generate derived types for each constructor
        foreach (var ctor in variant.Constructors)
        {
            var ctorName = SanitizeIdentifier(ctor.Name);
            var hasArg = HasVariantPayload(ctor);
            var argType = hasArg ? MapDamlTypeToCSharp(ctor.ArgumentType!) : null;

            if (argType is not null)
            {
                RequireForFieldType(indent, ctor.ArgumentType!);
                indent.AppendLine($"/// <summary>{ctor.Name} constructor.</summary>");
                indent.AppendLine($"public sealed record {ctorName}({argType} Value) : {fullClassName}");
            }
            else
            {
                indent.AppendLine($"/// <summary>{ctor.Name} constructor (no arguments).</summary>");
                indent.AppendLine($"public sealed record {ctorName}() : {fullClassName}");
            }

            indent.AppendLine("{");
            indent.Indent();

            indent.AppendLine($"public override string Tag => \"{ctor.Name}\";");
            indent.AppendLine();
            var payload = hasArg
                ? GetToValueConversion(ctor.ArgumentType!, "Value")
                : $"{_qualifier.Qualify("DamlUnit", _currentNamespace)}.Instance";
            indent.AppendLine($"public override {_qualifier.Qualify("DamlVariant", _currentNamespace)} ToVariant() => {_qualifier.Qualify("DamlVariant", _currentNamespace)}.Create(\"{ctor.Name}\", {payload});");

            indent.Dedent();
            indent.AppendLine("}");
            indent.AppendLine();
        }

        indent.Dedent();
        indent.AppendLine("}");
    }

    private static bool HasVariantPayload(DamlVariantConstructor ctor) =>
        ctor.ArgumentType is not null
        && ctor.ArgumentType is not DamlPrimitiveType { Primitive: DamlPrimitive.Unit };

    private void WriteEnumType(IndentWriter indent, DamlDataType dataType, DamlEnumDefinition enumDef)
    {
        indent.Require("System");
        var enumName = SanitizeIdentifier(dataType.Name);

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Generated from Daml enum {dataType.Name}");
            indent.AppendLine("/// </summary>");
        }

        indent.AppendLine($"public enum {enumName}");
        indent.AppendLine("{");
        indent.Indent();

        foreach (var ctor in enumDef.Constructors)
        {
            indent.AppendLine($"{SanitizeIdentifier(ctor)},");
        }

        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine();

        // Generate extension methods for enum serialization
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Extension methods for {enumName} serialization.");
            indent.AppendLine("/// </summary>");
        }
        indent.AppendLine($"public static class {enumName}Extensions");
        indent.AppendLine("{");
        indent.Indent();

        // ToDamlEnum method (returns DamlEnum)
        indent.AppendLine($"/// <summary>Converts to a DamlEnum value.</summary>");
        indent.AppendLine($"public static {_qualifier.Qualify("DamlEnum", _currentNamespace)} ToDamlEnum(this {enumName} value)");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine("return value switch");
        indent.AppendLine("{");
        indent.Indent();
        foreach (var ctor in enumDef.Constructors)
        {
            indent.AppendLine($"{enumName}.{SanitizeIdentifier(ctor)} => {_qualifier.Qualify("DamlEnum", _currentNamespace)}.Create(\"{ctor}\"),");
        }
        indent.AppendLine($"_ => throw new ArgumentOutOfRangeException(nameof(value), value, null)");
        indent.Dedent();
        indent.AppendLine("};");
        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine();

        // FromDamlEnum static method
        indent.AppendLine($"/// <summary>Creates an instance from a DamlEnum value.</summary>");
        indent.AppendLine($"public static {enumName} FromDamlEnum({_qualifier.Qualify("DamlEnum", _currentNamespace)} value)");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine("return value.Constructor switch");
        indent.AppendLine("{");
        indent.Indent();
        foreach (var ctor in enumDef.Constructors)
        {
            indent.AppendLine($"\"{ctor}\" => {enumName}.{SanitizeIdentifier(ctor)},");
        }
        indent.AppendLine($"_ => throw new ArgumentOutOfRangeException(nameof(value), value.Constructor, null)");
        indent.Dedent();
        indent.AppendLine("};");
        indent.Dedent();
        indent.AppendLine("}");

        indent.Dedent();
        indent.AppendLine("}");
    }


    // Type mapping helpers
    private string MapDamlTypeToCSharp(DamlType type) => type switch
    {
        DamlPrimitiveType primitive => MapBarePrimitiveToCSharp(primitive.Primitive),
        // Numeric with scale argument (Numeric n) - maps to decimal
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Numeric } } => "decimal",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.ContractId }, Arguments: [var arg] } =>
            $"{_qualifier.Qualify("ContractId", _currentNamespace)}<{MapDamlTypeToCSharp(arg)}>",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional },
                      Arguments: [DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional } }] } =>
            throw new NotSupportedException("Codegen does not support nested Optional types (Optional (Optional t)). C# nullable syntax cannot represent the Some Nothing / Nothing distinction without a wrapper type. Refactor the Daml signature, or open a feature request to introduce a representable CLR model."),
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional }, Arguments: [var arg] } =>
            $"{MapDamlTypeToCSharp(arg)}?",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.List }, Arguments: [var arg] } =>
            $"{_qualifier.Qualify("IReadOnlyList", _currentNamespace)}<{MapDamlTypeToCSharp(arg)}>",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.TextMap }, Arguments: [var arg] } =>
            $"{_qualifier.Qualify("IReadOnlyDictionary", _currentNamespace)}<string, {MapDamlTypeToCSharp(arg)}>",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.GenMap }, Arguments: [var keyArg, var valueArg] } =>
            $"{_qualifier.Qualify("IReadOnlyDictionary", _currentNamespace)}<{MapDamlTypeToCSharp(keyArg)}, {MapDamlTypeToCSharp(valueArg)}>",
        // Parameterized references — e.g. `Set X`, `Map k v` or any user-defined
        // generic record/variant. Emit `BaseName<arg1, arg2>` so the C# type stays
        // structurally faithful. If the base name resolves to a hand-coded stdlib
        // stub, keeping the parameter list lets that stub still be generic.
        DamlTypeApp { Base: DamlTypeRef typeRef } app =>
            app.Arguments.Count > 0
                ? $"{ResolveTypeRefName(typeRef)}<{string.Join(", ", app.Arguments.Select(MapDamlTypeToCSharp))}>"
                : ResolveTypeRefName(typeRef),
        DamlTypeRef typeRef => ResolveTypeRefName(typeRef),
        DamlTypeVar typeVar => $"T{ToPascalCase(SanitizeIdentifier(typeVar.Name))}",
        _ => "object"
    };

    // CS8524 disabled (not CS8509): the no-default switch covers every named
    // DamlPrimitive so an out-of-range cast is the only uncovered input. CS8509
    // (a newly-added named member left unhandled) stays an error — that is the
    // compiler-enforced checklist for adding a Daml primitive.
#pragma warning disable CS8524
    private string MapBarePrimitiveToCSharp(DamlPrimitive primitive) => primitive switch
    {
        DamlPrimitive.Unit => _qualifier.Qualify("DamlUnit", _currentNamespace),
        DamlPrimitive.Bool => "bool",
        DamlPrimitive.Int64 => "long",
        DamlPrimitive.Numeric => "decimal",
        DamlPrimitive.Text => "string",
        DamlPrimitive.Date => "DateOnly",
        DamlPrimitive.Timestamp => "DateTimeOffset",
        DamlPrimitive.Party => _qualifier.Qualify("Party", _currentNamespace),
        DamlPrimitive.ContractId
            or DamlPrimitive.List
            or DamlPrimitive.Optional
            or DamlPrimitive.TextMap
            or DamlPrimitive.GenMap =>
            throw new NotSupportedException(
                $"Daml primitive '{primitive}' is a type constructor and cannot appear bare — it must be applied to argument types (handled by the DamlTypeApp arms of MapDamlTypeToCSharp)."),
    };
#pragma warning restore CS8524

    private string GetToValueConversion(DamlType type, string fieldName) => type switch
    {
        DamlPrimitiveType primitive => GetBarePrimitiveToValueConversion(primitive.Primitive, fieldName),
        // Numeric with scale argument
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Numeric } } =>
            $"new {_qualifier.Qualify("DamlNumeric", _currentNamespace)}({fieldName})",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.ContractId } } =>
            $"{fieldName}.ToDamlValue()",
        // The TrimStart('@') here protects against an Optional field whose name is a
        // C# keyword (e.g. `lock`, `class`, `event`). SanitizeIdentifier escapes those
        // by prepending '@', which is valid as a property name but invalid as the
        // local-variable name produced by `is { } __<name>` below. Without the trim,
        // a Daml `Optional <something>` field called `lock` produces `__@lock`, which
        // does not parse. The trim is local-variable-only — the property reference
        // (`{fieldName}`) keeps its `@` escape so the original record property is
        // still addressable.
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional } } app =>
            $"{fieldName} is {{ }} __{fieldName.TrimStart('@')} ? new {_qualifier.Qualify("DamlOptional", _currentNamespace)}({GetToValueConversion(app.Arguments[0], $"__{fieldName.TrimStart('@')}")}) : {_qualifier.Qualify("DamlOptional", _currentNamespace)}.None",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.List } } app =>
            $"new {_qualifier.Qualify("DamlList", _currentNamespace)}({fieldName}.Select(x => ({_qualifier.Qualify("DamlValue", _currentNamespace)}){GetToValueConversion(app.Arguments[0], "x")}).ToList())",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.TextMap } } app =>
            $"new {_qualifier.Qualify("DamlTextMap", _currentNamespace)}({fieldName}.ToDictionary(kv => kv.Key, kv => ({_qualifier.Qualify("DamlValue", _currentNamespace)}){GetToValueConversion(app.Arguments[0], "kv.Value")}))",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.GenMap } } app =>
            $"new {_qualifier.Qualify("DamlGenMap", _currentNamespace)}({fieldName}.Select(kv => (({_qualifier.Qualify("DamlValue", _currentNamespace)}){GetToValueConversion(app.Arguments[0], "kv.Key")}, ({_qualifier.Qualify("DamlValue", _currentNamespace)}){GetToValueConversion(app.Arguments[1], "kv.Value")})).ToList())",
        // Parametric stdlib types — Tuple2/3, Set, NonEmpty, Map. These all live in
        // Daml.Runtime.Stdlib and round-trip via delegate-based ToRecord/FromRecord
        // because their generic arguments may be CLR primitives (long, string, Party)
        // rather than IDamlRecord. The codegen knows the concrete arg types here so
        // it inlines a conversion lambda per arg.
        DamlTypeApp { Base: DamlTypeRef typeRef } app
            when IsStdlibTypeRef(typeRef, parametric: true) =>
            EmitParametricStdlibToValue(typeRef, app.Arguments, fieldName),
        DamlTypeRef typeRef when IsLocalEnumTypeRef(typeRef) =>
            $"{fieldName}.ToDamlEnum()",
        DamlTypeRef typeRef when IsCrossPackageEnumTypeRef(typeRef) =>
            QualifiedEnumExtensionsCall(typeRef, "ToDamlEnum", fieldName),
        DamlTypeRef typeRef when IsVariantTypeRef(typeRef) =>
            $"{fieldName}.ToVariant()",
        // Daml type variables (parametric polymorphism). The codegen has no way to
        // dispatch ToRecord through a bare T at compile time, so we emit a runtime
        // stub: the type compiles, but serializing an actual generic instance throws.
        DamlTypeVar => $"{_qualifier.Qualify("GenericStub", _currentNamespace)}.NotImplemented<{_qualifier.Qualify("DamlValue", _currentNamespace)}>(\"{fieldName}\")",
        _ => $"{fieldName}.ToRecord()"
    };

    // See MapBarePrimitiveToCSharp for the CS8524-vs-CS8509 rationale.
#pragma warning disable CS8524
    private string GetBarePrimitiveToValueConversion(DamlPrimitive primitive, string fieldName) => primitive switch
    {
        DamlPrimitive.Unit => $"{_qualifier.Qualify("DamlUnit", _currentNamespace)}.Instance",
        DamlPrimitive.Bool => $"new {_qualifier.Qualify("DamlBool", _currentNamespace)}({fieldName})",
        DamlPrimitive.Int64 => $"new {_qualifier.Qualify("DamlInt64", _currentNamespace)}({fieldName})",
        DamlPrimitive.Numeric => $"new {_qualifier.Qualify("DamlNumeric", _currentNamespace)}({fieldName})",
        DamlPrimitive.Text => $"new {_qualifier.Qualify("DamlText", _currentNamespace)}({fieldName})",
        DamlPrimitive.Date => $"new {_qualifier.Qualify("DamlDate", _currentNamespace)}({fieldName})",
        DamlPrimitive.Timestamp => $"new {_qualifier.Qualify("DamlTimestamp", _currentNamespace)}({fieldName})",
        DamlPrimitive.Party => $"{fieldName}.ToDamlValue()",
        DamlPrimitive.ContractId
            or DamlPrimitive.List
            or DamlPrimitive.Optional
            or DamlPrimitive.TextMap
            or DamlPrimitive.GenMap =>
            throw new NotSupportedException(
                $"Daml primitive '{primitive}' is a type constructor and cannot appear bare — it must be applied to argument types (handled by the DamlTypeApp arms of GetToValueConversion)."),
    };
#pragma warning restore CS8524

    private string EmitParametricStdlibFromValue(
        DamlTypeRef typeRef,
        IReadOnlyList<DamlType> arguments,
        string valueName,
        IReadOnlyDictionary<string, DamlDataType>? dataTypes)
    {
        var stdlibName = _qualifier.Qualify(
            MapStdlibType(typeRef.Module, typeRef.Name)
                ?? throw new InvalidOperationException($"No stdlib mapping for {typeRef.Module}:{typeRef.Name}"),
            _currentNamespace);
        var typeArgs = string.Join(", ", arguments.Select(MapDamlTypeToCSharp));
        var lambdas = arguments.Select((arg, i) =>
            $"__v{i} => {GetFromValueConversion(arg, $"__v{i}", dataTypes)}");
        return (typeRef.Module, typeRef.Name) switch
        {
            // Set/NonEmpty take a single converter (over the element type).
            ("DA.Set.Types", "Set") =>
                $"{stdlibName}<{typeArgs}>.FromRecord({valueName}.As<{_qualifier.Qualify("DamlRecord", _currentNamespace)}>(), {lambdas.First()})",
            ("DA.NonEmpty.Types", "NonEmpty") =>
                $"{stdlibName}<{typeArgs}>.FromRecord({valueName}.As<{_qualifier.Qualify("DamlRecord", _currentNamespace)}>(), {lambdas.First()})",
            ("DA.Types", "Either") =>
                $"{stdlibName}<{typeArgs}>.FromValue({valueName}, {string.Join(", ", lambdas)})",
            // Tuple2/3 and Map take one converter per generic argument.
            _ => $"{stdlibName}<{typeArgs}>.FromRecord({valueName}.As<{_qualifier.Qualify("DamlRecord", _currentNamespace)}>(), {string.Join(", ", lambdas)})",
        };
    }

    private string EmitParametricStdlibToValue(DamlTypeRef typeRef, IReadOnlyList<DamlType> arguments, string fieldName)
    {
        // Each conversion lambda must yield a DamlValue. We always cast through
        // (DamlValue) because some inner expressions (e.g. `new DamlBool(...)`) are
        // typed as a more specific subtype that the helper signature won't accept
        // implicitly.
        var converters = arguments.Select((arg, i) =>
            $"({_qualifier.Qualify("DamlValue", _currentNamespace)})({GetToValueConversion(arg, $"__t{i}")})").ToList();
        var lambdas = arguments.Select((_, i) =>
            $"__t{i} => {converters[i]}");
        return (typeRef.Module, typeRef.Name) switch
        {
            ("DA.Set.Types", "Set") =>
                $"{fieldName}.ToRecord({lambdas.First()})",
            ("DA.NonEmpty.Types", "NonEmpty") =>
                $"{fieldName}.ToRecord({lambdas.First()})",
            ("DA.Types", "Either") =>
                $"{fieldName}.ToValue({string.Join(", ", lambdas)})",
            _ => $"{fieldName}.ToRecord({string.Join(", ", lambdas)})",
        };
    }

    private string GetFromValueConversion(DamlType type, string valueName, IReadOnlyDictionary<string, DamlDataType>? dataTypes = null) => type switch
    {
        DamlPrimitiveType primitive => GetBarePrimitiveFromValueConversion(primitive.Primitive, valueName),
        // Numeric with scale argument
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Numeric } } =>
            $"{valueName}.As<{_qualifier.Qualify("DamlNumeric", _currentNamespace)}>().Value",
        // Optional type
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional }, Arguments: [var arg] } =>
            $"{valueName}.AsOptional().HasValue ? {GetFromValueConversion(arg, $"{valueName}.AsOptional().Value!", dataTypes)} : null",
        // List type
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.List }, Arguments: [var arg] } =>
            $"({_qualifier.Qualify("IReadOnlyList", _currentNamespace)}<{MapDamlTypeToCSharp(arg)}>){valueName}.As<{_qualifier.Qualify("DamlList", _currentNamespace)}>().Values.Select(x => {GetFromValueConversion(arg, "x", dataTypes)}).ToList()",
        // TextMap type
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.TextMap }, Arguments: [var arg] } =>
            $"{valueName}.As<{_qualifier.Qualify("DamlTextMap", _currentNamespace)}>().Values.ToDictionary(kv => kv.Key, kv => {GetFromValueConversion(arg, "kv.Value", dataTypes)})",
        // GenMap type
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.GenMap }, Arguments: [var keyArg, var valueArg] } =>
            $"{valueName}.As<{_qualifier.Qualify("DamlGenMap", _currentNamespace)}>().Entries.ToDictionary(kv => {GetFromValueConversion(keyArg, "kv.Key", dataTypes)}, kv => {GetFromValueConversion(valueArg, "kv.Value", dataTypes)})",
        // ContractId type
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.ContractId }, Arguments: [var arg] } =>
            $"new {_qualifier.Qualify("ContractId", _currentNamespace)}<{MapDamlTypeToCSharp(arg)}>({valueName}.As<{_qualifier.Qualify("DamlContractId", _currentNamespace)}>().Value)",
        // Parametric stdlib types — see GetToValueConversion for the matching
        // serialization arm. The conversion lambdas decode each generic arg from a
        // DamlValue back into its CLR shape.
        DamlTypeApp { Base: DamlTypeRef typeRef } app
            when IsStdlibTypeRef(typeRef, parametric: true) =>
            EmitParametricStdlibFromValue(typeRef, app.Arguments, valueName, dataTypes),
        DamlTypeRef typeRef when IsEnumTypeRef(typeRef) =>
            QualifiedEnumExtensionsCall(typeRef, "FromDamlEnum", $"{valueName}.As<{_qualifier.Qualify("DamlEnum", _currentNamespace)}>()"),
        DamlTypeRef typeRef when IsVariantTypeRef(typeRef) =>
            $"{ResolveTypeRefName(typeRef)}.FromVariant({valueName}.As<{_qualifier.Qualify("DamlVariant", _currentNamespace)}>())",
        // Type reference (record types)
        DamlTypeRef typeRef => $"{ResolveTypeRefName(typeRef)}.FromRecord({valueName}.As<{_qualifier.Qualify("DamlRecord", _currentNamespace)}>())",
        // Daml type variable — same treatment as ToValue: emit a runtime-throwing stub
        // typed as the generic placeholder so generics-bearing records still compile.
        DamlTypeVar typeVar => $"{_qualifier.Qualify("GenericStub", _currentNamespace)}.NotImplemented<T{ToPascalCase(SanitizeIdentifier(typeVar.Name))}>(\"{typeVar.Name}\")",
        _ => $"default! /* TODO: Implement deserialization for {type} */"
    };

    // See MapBarePrimitiveToCSharp for the CS8524-vs-CS8509 rationale.
#pragma warning disable CS8524
    private string GetBarePrimitiveFromValueConversion(DamlPrimitive primitive, string valueName) => primitive switch
    {
        DamlPrimitive.Bool => $"{valueName}.As<{_qualifier.Qualify("DamlBool", _currentNamespace)}>().Value",
        DamlPrimitive.Int64 => $"{valueName}.As<{_qualifier.Qualify("DamlInt64", _currentNamespace)}>().Value",
        DamlPrimitive.Numeric => $"{valueName}.As<{_qualifier.Qualify("DamlNumeric", _currentNamespace)}>().Value",
        DamlPrimitive.Text => $"{valueName}.As<{_qualifier.Qualify("DamlText", _currentNamespace)}>().Value",
        DamlPrimitive.Date => $"{valueName}.As<{_qualifier.Qualify("DamlDate", _currentNamespace)}>().Value",
        DamlPrimitive.Timestamp => $"{valueName}.As<{_qualifier.Qualify("DamlTimestamp", _currentNamespace)}>().Value",
        DamlPrimitive.Party => $"{_qualifier.Qualify("Party", _currentNamespace)}.FromDamlValue({valueName}.As<{_qualifier.Qualify("DamlParty", _currentNamespace)}>())",
        // The wire-level DamlUnit.Instance is the single inhabitant; we surface it as
        // the field type DamlUnit. Without this arm, nested Unit shapes — Optional (),
        // [()], tuples containing () — fall through and produce wrong typed results at
        // runtime. The bare-`()` return is special-cased upstream; this covers nesting.
        DamlPrimitive.Unit => $"{valueName}.As<{_qualifier.Qualify("DamlUnit", _currentNamespace)}>()",
        DamlPrimitive.ContractId
            or DamlPrimitive.List
            or DamlPrimitive.Optional
            or DamlPrimitive.TextMap
            or DamlPrimitive.GenMap =>
            throw new NotSupportedException(
                $"Daml primitive '{primitive}' is a type constructor and cannot appear bare — it must be applied to argument types (handled by the DamlTypeApp arms of GetFromValueConversion)."),
    };
#pragma warning restore CS8524

    // Type parameter helpers
    private static string GetTypeParametersDeclaration(IReadOnlyList<string> typeParams)
    {
        if (typeParams.Count == 0)
            return string.Empty;

        var sanitized = typeParams.Select(p => $"T{ToPascalCase(SanitizeIdentifier(p))}");
        return $"<{string.Join(", ", sanitized)}>";
    }

    private static void WriteTypeParamDocs(IndentWriter indent, IReadOnlyList<string> typeParams)
    {
        foreach (var param in typeParams)
        {
            var sanitized = $"T{ToPascalCase(SanitizeIdentifier(param))}";
            indent.AppendLine($"/// <typeparam name=\"{sanitized}\">Type parameter {param}</typeparam>");
        }
    }

    // Naming helpers
    private const string FallbackNamespace = "DamlGenerated";

    private static string DeriveNamespace(string packageName)
    {
        var parts = packageName.Split('-', '_')
            .Select(ToPascalCase)
            .Select(SanitizeIdentifier)
            .Where(segment => segment.Length > 0)
            .ToList();
        return parts.Count == 0 ? FallbackNamespace : string.Join(".", parts);
    }

    private static string SanitizeIdentifier(string name)
    {
        var sanitized = IdentifierRegex().Replace(name, "_");

        if (sanitized.Length == 0)
        {
            return sanitized;
        }

        if (char.IsDigit(sanitized[0]))
        {
            sanitized = "_" + sanitized;
        }

        if (CSharpKeywords.Contains(sanitized))
        {
            sanitized = "@" + sanitized;
        }

        return sanitized;
    }

    private static string IndefiniteArticleFor(string name) =>
        name.Length > 0 && "aeiou".Contains(char.ToLowerInvariant(name[0])) ? "an" : "a";

    private static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var sb = new StringBuilder();
        var capitalizeNext = true;

        foreach (var c in name)
        {
            if (c is '_' or '-' or '.')
            {
                capitalizeNext = true;
            }
            else if (capitalizeNext)
            {
                sb.Append(char.ToUpperInvariant(c));
                capitalizeNext = false;
            }
            else
            {
                sb.Append(c);
            }
        }

        var result = sb.ToString();

        // If the result starts with a digit (e.g., from "_1" becoming "1"),
        // prefix with underscore to make it a valid identifier
        if (result.Length > 0 && char.IsDigit(result[0]))
        {
            return "_" + result;
        }

        return result;
    }

    [GeneratedRegex("[^a-zA-Z0-9_]")]
    private static partial Regex IdentifierRegex();

    private static readonly HashSet<string> CSharpKeywords =
    [
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char",
        "checked", "class", "const", "continue", "decimal", "default", "delegate",
        "do", "double", "else", "enum", "event", "explicit", "extern", "false",
        "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit",
        "in", "int", "interface", "internal", "is", "lock", "long", "namespace",
        "new", "null", "object", "operator", "out", "override", "params", "private",
        "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
        "short", "sizeof", "stackalloc", "static", "string", "struct", "switch",
        "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked",
        "unsafe", "ushort", "using", "virtual", "void", "volatile", "while"
    ];
}
