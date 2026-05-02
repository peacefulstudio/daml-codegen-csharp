using System.Text;
using System.Text.RegularExpressions;
using Daml.Codegen.CSharp.DarReader;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Generates C# code from Daml packages.
/// </summary>
internal sealed partial class CSharpCodeGenerator(CodeGenOptions options, ConsoleLogger logger)
{
    private readonly Regex? _rootFilter = options.RootFilter is not null
        ? new Regex(options.RootFilter, RegexOptions.Compiled)
        : null;

    private DarArchive? _currentArchive;
    private DamlPackage? _currentPackage;
    private readonly HashSet<string> _externalPackageIds = [];

    // Module-qualified enum type names for the package currently being generated.
    // Required because Daml allows the same simple name in multiple modules — e.g.
    // splice-amulet defines both `Splice.Amulet:Amulet` (record/template) and
    // `Splice.AmuletConfig:Amulet` (enum). A name-only lookup would dispatch every
    // `Amulet` reference through *Extensions.FromRecord, breaking the record case.
    private readonly HashSet<string> _localEnumQualifiedNames = [];

    // Module-qualified names of records that exist purely as the C# placeholder for a
    // Daml interface declaration. Daml-LF emits one record per interface (with the
    // same name and an empty fields list) so that `ContractId I` has a phantom type
    // parameter — see C# code emitted for splice-api-token-holding-v1's `Holding`
    // record alongside the `IHolding` interface. Those placeholder records are
    // generated as `: ITemplate` with throwing static metadata so `ContractId<T>`
    // (which constrains `T : ITemplate`) keeps compile-time safety while still
    // accommodating Daml interface-typed contract ids.
    private readonly HashSet<string> _interfacePlaceholderQualifiedNames = [];

    /// <summary>
    /// Generates C# code for all types in the DAR.
    /// </summary>
    public IReadOnlyList<GeneratedFile> Generate(DarArchive dar)
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
                if (IsStdlibPackage(pkg.Name))
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

        // Emit a sentinel marker file when the generated code contains the
        // partial-property `Key` syntax (which requires C# 13). The MSBuild
        // integration package (Daml.Codegen.CSharp.MSBuild) checks for this marker
        // in `DamlPinLangVersionForKeyBearing` and bumps the consumer project's
        // <LangVersion> to 13 — without this, consumers using the MSBuild
        // integration (rather than --generate-project) would silently inherit
        // their project's default LangVersion and fail to compile the emitted
        // partial-property syntax. The marker has no .cs extension so MSBuild's
        // <Compile Include="**/*.cs"> glob doesn't pick it up.
        if (files.Any(f =>
                f.RelativePath.EndsWith(".cs", StringComparison.Ordinal)
                && ProjectFileGenerator.ContentRequiresCSharp13(f.Content)))
        {
            files.Add(new GeneratedFile(
                ".daml-needs-csharp13",
                "Sentinel: this directory's generated code contains C#-13 partial-property syntax. "
                + "The Daml.Codegen.CSharp.MSBuild targets check for this file and bump <LangVersion> "
                + "to 13. See daml-codegen-csharp#65.\n"));
        }

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

    /// <summary>
    /// Maps a stdlib type reference to its hand-coded Daml.Runtime.Stdlib equivalent,
    /// or null if there is no known mapping (the codegen will fall back to an unqualified
    /// name and the build will fail loudly so the gap is visible).
    /// </summary>
    private static string? MapStdlibType(string module, string typeName) => (module, typeName) switch
    {
        ("DA.Time.Types", "RelTime") => "Daml.Runtime.Stdlib.RelTime",
        _ => null,
    };

    /// <summary>
    /// Resolves a DamlTypeRef to a C# identifier or fully qualified name.
    /// Local refs return the bare sanitized name; cross-package refs return a
    /// fully qualified name and record the package id so a PackageReference can be
    /// emitted for it.
    /// </summary>
    private string ResolveTypeRefName(DamlTypeRef typeRef)
    {
        var sanitized = SanitizeIdentifier(typeRef.Name);

        if (string.IsNullOrEmpty(typeRef.PackageId)
            || _currentPackage is null
            || typeRef.PackageId == _currentPackage.PackageId)
        {
            return sanitized;
        }

        // Each remaining fallback returns the unqualified name. That fails loudly at
        // C# compile time (CS0246), but only after a long detour through `dotnet
        // build` output. Warn here so the gap surfaces at codegen time too — without
        // a warning each fallback was previously silent, hiding the cause behind a
        // generic "type or namespace not found" further downstream.
        if (_currentArchive is null)
        {
            logger.Warning($"Cross-package type ref {typeRef.Module}:{typeRef.Name} (package {typeRef.PackageId[..Math.Min(16, typeRef.PackageId.Length)]}…) cannot be resolved — no archive context. Generated code will not compile.");
            return sanitized;
        }

        var foreignPkg = _currentArchive.GetPackageById(typeRef.PackageId);
        if (foreignPkg is null)
        {
            logger.Warning($"Cross-package type ref {typeRef.Module}:{typeRef.Name} points at package {typeRef.PackageId[..Math.Min(16, typeRef.PackageId.Length)]}… which is not present in the DAR. Generated code will not compile.");
            return sanitized;
        }

        if (IsStdlibPackage(foreignPkg.Name))
        {
            var mapped = MapStdlibType(typeRef.Module, typeRef.Name);
            if (mapped is not null)
            {
                return mapped;
            }
            // Unknown stdlib type — leave unqualified so the build error points at the gap.
            // Tracked in https://github.com/peacefulstudio/daml-codegen-csharp/issues/57.
            logger.Warning($"Unmapped stdlib type {foreignPkg.Name}:{typeRef.Module}:{typeRef.Name} — generated code will not compile (see issue #57)");
            return sanitized;
        }

        _externalPackageIds.Add(typeRef.PackageId);
        var foreignNs = DeriveNamespace(foreignPkg.Name);
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
        _interfacePlaceholderQualifiedNames.Clear();
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

        // Build a mapping of choice argument type names to their parent template
        // This maps choiceArgTypeName -> (templateName, choiceName)
        var choiceArgTypeToTemplate = new Dictionary<string, (string TemplateName, string ChoiceName)>();
        foreach (var module in package.Modules)
        {
            foreach (var template in module.Templates)
            {
                foreach (var choice in template.Choices)
                {
                    if (choice.ArgumentType is DamlTypeRef typeRef && allDataTypesInGroup.ContainsKey(typeRef.Name))
                    {
                        // This choice's argument type is a data type that should be nested
                        choiceArgTypeToTemplate[typeRef.Name] = (template.Name, choice.Name);
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
                if (choiceArgTypeToTemplate.ContainsKey(dataType.Name))
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
        var sb = new StringBuilder();
        var indent = new IndentWriter(sb);

        // File header
        WriteFileHeader(indent);

        // Usings
        WriteUsings(indent);

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
        var interfacesList = new List<string> { "ITemplate" };
        if (keyType is not null)
            interfacesList.Add($"IHasKey<{keyType}>");
        if (package.UpgradedPackageId is not null)
            interfacesList.Add("IUpgradeable");
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
        // `ContractId<TemplateName>` receivers.
        WriteChoiceAsyncExercisersClass(indent, template, className, dataTypes);

        if (!options.UseFileScopedNamespaces)
        {
            indent.Dedent();
            indent.AppendLine("}");
        }

        return sb.ToString();
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
        var sb = new StringBuilder();
        var indent = new IndentWriter(sb);

        WriteFileHeader(indent);
        WriteUsings(indent);

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
                WriteVariantType(indent, dataType, variant);
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

        return sb.ToString();
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
        var sb = new StringBuilder();
        var indent = new IndentWriter(sb);

        // File header
        WriteFileHeader(indent);

        // Usings
        WriteUsings(indent);

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
            ? $"IDamlInterface, IHasView<{viewType}>"
            : "IDamlInterface";

        indent.AppendLine($"public interface {interfaceName} : {interfaces}");
        indent.AppendLine("{");
        indent.Indent();

        // Static interface metadata
        WriteInterfaceMetadata(indent, package, module, iface);

        // Generate method signatures for each choice
        foreach (var method in iface.Methods)
        {
            WriteInterfaceMethod(indent, method, dataTypes);
        }

        indent.Dedent();
        indent.AppendLine("}");

        if (!options.UseFileScopedNamespaces)
        {
            indent.Dedent();
            indent.AppendLine("}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates the ContractIdentifiers helper class with fully qualified identifiers for all templates.
    /// </summary>
    private GeneratedFile GenerateContractIdentifiersFile(
        DamlPackage package,
        IReadOnlyList<(DamlModule Module, DamlTemplate Template)> templates,
        string moduleNamespace)
    {
        var sb = new StringBuilder();
        var indent = new IndentWriter(sb);

        // File header
        WriteFileHeader(indent);

        // Usings - include static import for TemplateExtensions.GetTemplateId
        indent.AppendLine("using Daml.Runtime.Contracts;");
        indent.AppendLine("using static Daml.Runtime.Contracts.TemplateExtensions;");
        indent.AppendLine();

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

        return new GeneratedFile(path, sb.ToString());
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
        var sb = new StringBuilder();
        var indent = new IndentWriter(sb);

        // File header
        WriteFileHeader(indent);

        // Usings
        WriteUsings(indent);

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
                indent.AppendLine(") : IDamlValue");
            }
            else
            {
                indent.AppendLine($"public sealed record {choiceTypeName} : IDamlValue");
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

        return sb.ToString();
    }

    private void WriteInterfaceMetadata(
        IndentWriter indent,
        DamlPackage package,
        DamlModule module,
        DamlInterface iface)
    {
        indent.AppendLine($"/// <summary>Gets the interface identifier.</summary>");
        indent.AppendLine($"static Identifier IDamlInterface.InterfaceId => new(\"{package.PackageId}\", \"{module.Name}\", \"{iface.Name}\");");
        indent.AppendLine();

        indent.AppendLine($"/// <summary>Gets the package ID.</summary>");
        indent.AppendLine($"static string IDamlInterface.PackageId => \"{package.PackageId}\";");
        indent.AppendLine();

        indent.AppendLine($"/// <summary>Gets the package name.</summary>");
        indent.AppendLine($"static string IDamlInterface.PackageName => \"{package.Name}\";");
        indent.AppendLine();

        indent.AppendLine($"/// <summary>Gets the package version.</summary>");
        indent.AppendLine($"static Version IDamlInterface.PackageVersion => new({package.Version.Major}, {package.Version.Minor}, {package.Version.Build});");
        indent.AppendLine();
    }

    private void WriteInterfaceMethod(IndentWriter indent, DamlChoice method, IReadOnlyDictionary<string, DamlDataType> dataTypes)
    {
        var methodName = SanitizeIdentifier(method.Name);
        var returnType = MapDamlTypeToCSharp(method.ReturnType);
        var (argTypeName, _, _, _) = GetChoiceArgumentInfo(method, dataTypes);

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine($"/// <summary>");
            indent.AppendLine($"/// Interface method {method.Name}.");
            indent.AppendLine($"/// </summary>");
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

    private void WriteUsings(IndentWriter indent)
    {
        // BCL usings emitted explicitly so generated code compiles even when a
        // consumer disables `<ImplicitUsings>` in their csproj. The codegen-emitted
        // csproj turns ImplicitUsings on, but generated source dropped into a project
        // without that flag (e.g. someone copying files into a hand-rolled lib) would
        // otherwise miss `System`, `Collections.Generic`, `Tasks`, and `Threading`.
        indent.AppendLine("using System;");
        indent.AppendLine("using System.Collections.Generic;");
        indent.AppendLine("using System.Linq;");
        indent.AppendLine("using System.Threading;");
        indent.AppendLine("using System.Threading.Tasks;");
        indent.AppendLine("using Daml.Ledger.Abstractions;");
        indent.AppendLine("using Daml.Runtime.Commands;");
        indent.AppendLine("using Daml.Runtime.Contracts;");
        indent.AppendLine("using Daml.Runtime.Data;");
        // Outcomes namespace covers ExerciseOutcome<T> referenced by emitted
        // <Choice>Result.FromCreatedContracts projectors and <Choice>Async
        // exercisers. Always emitted — even for templates without create-bearing
        // choices, the cost is one unused-using lint suppression at most.
        indent.AppendLine("using Daml.Runtime.Outcomes;");

        if (options.GenerateJsonSupport)
        {
            indent.AppendLine("using Daml.Runtime.Serialization;");
        }

        indent.AppendLine();
    }

    private void WriteTemplateMetadata(
        IndentWriter indent,
        DamlPackage package,
        DamlModule module,
        DamlTemplate template)
    {
        var templateId = $"{module.Name}:{template.Name}";

        indent.AppendLine($"/// <summary>Gets the template identifier.</summary>");
        indent.AppendLine($"public static Identifier TemplateId {{ get; }} = new(\"{package.PackageId}\", \"{module.Name}\", \"{template.Name}\");");
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

    private void WriteKeyProperty(IndentWriter indent, DamlType keyType)
    {
        var csharpKeyType = MapDamlTypeToCSharp(keyType);
        // cref attribute syntax escapes generic angle brackets as { }. Apply the
        // same transform to the rendered key type so generic key types don't
        // produce an invalid cref (CS1574 under <GenerateDocumentationFile>true</>).
        var crefKeyType = csharpKeyType.Replace('<', '{').Replace('>', '}');

        // Full DALF key-expression analysis (mapping the template's `key` Daml
        // expression back to template fields, including tuple/record builders) is
        // tracked in peacefulstudio/daml-codegen-csharp#64. Until that lands, two
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
            indent.AppendLine($"/// Gets the contract key, satisfying <see cref=\"global::Daml.Runtime.Contracts.IHasKey{{{crefKeyType}}}\"/>.");
            indent.AppendLine("/// </summary>");
            indent.AppendLine("/// <remarks>");
            indent.AppendLine("/// The body is supplied by a hand-rolled <c>partial</c> declaration in the");
            indent.AppendLine("/// consuming project until the full DALF key-expression analysis lands");
            indent.AppendLine("/// (see <see href=\"https://github.com/peacefulstudio/daml-codegen-csharp/issues/64\">daml-codegen-csharp#64</see>).");
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
            indent.Append($"{csharpType} {fieldName}");
        }
    }

    private void WriteProperties(IndentWriter indent, IReadOnlyList<DamlField> fields)
    {
        foreach (var field in fields)
        {
            var csharpType = MapDamlTypeToCSharp(field.Type);
            var fieldName = ToPascalCase(SanitizeIdentifier(field.Name));

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
            indent.AppendLine("public DamlRecord ToRecord() => DamlRecord.Create();");
            indent.AppendLine();
            return;
        }

        // Use expression-bodied member with inline DamlRecord.Create
        indent.AppendLine("public DamlRecord ToRecord() => DamlRecord.Create(");
        indent.Indent();

        for (int i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            var fieldName = ToPascalCase(SanitizeIdentifier(field.Name));
            var conversion = GetToValueConversion(field.Type, fieldName);
            var comma = i < fields.Count - 1 ? "," : "";

            indent.AppendLine($"DamlField.Create(\"{field.Name}\", {conversion}){comma}");
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
            indent.AppendLine($"public static {className} FromRecord(DamlRecord record) => new {className}();");
            indent.AppendLine();
            return;
        }

        if (options.UseRecordTypes && options.UsePrimaryConstructors)
        {
            indent.AppendLine($"public static {className} FromRecord(DamlRecord record) => new {className}(");
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
            indent.AppendLine($"public static {className} FromRecord(DamlRecord record)");
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

    /// <summary>
    /// Gets the argument type info for a choice, including the C# type name and whether it's a data type reference.
    /// Choice argument types that reference data types are now generated as nested types inside the template,
    /// so we use the choice name (which becomes the nested type name) rather than the data type name.
    /// </summary>
    /// <returns>
    /// A 4-tuple: <c>(TypeName, Fields, IsExternalRef, IsFallback)</c>. <c>IsFallback</c> is
    /// <c>true</c> when the codegen could not resolve the argument to any of (same-module
    /// record, Unit, external ref) and is emitting a field-less <c>&lt;Choice&gt;Arg</c>
    /// stub. Callers that need to emit code referencing <c>argument.ToRecord()</c> must
    /// skip those choices — the stub doesn't define <c>ToRecord</c>.
    /// </returns>
    private static (string TypeName, IReadOnlyList<DamlField>? Fields, bool IsExternalRef, bool IsFallback) GetChoiceArgumentInfo(
        DamlChoice choice,
        IReadOnlyDictionary<string, DamlDataType> dataTypes)
    {
        // Check if the argument type is a reference to a data type in the same module
        // These are now generated as nested types inside the template using the choice name
        if (choice.ArgumentType is DamlTypeRef typeRef && dataTypes.TryGetValue(typeRef.Name, out var dataType))
        {
            var fields = dataType.Definition is DamlRecordDefinition recordDef ? recordDef.Fields : null;
            // Use the choice name since that's the nested type name we generate
            return (SanitizeIdentifier(choice.Name), fields, false, false);
        }

        // Check for Unit type (no arguments)
        if (choice.ArgumentType is DamlPrimitiveType { Primitive: DamlPrimitive.Unit })
        {
            return ("DamlUnit", null, false, false);
        }

        // Check for external type references (like DA.Internal.Template:Archive)
        // These are typically empty argument records, so we use DamlUnit
        if (choice.ArgumentType is DamlTypeRef externalRef)
        {
            // Standard Archive choice from daml-prim uses an empty record
            if (externalRef.Name == "Archive")
            {
                return ("DamlUnit", null, true, false);
            }
            // Other external references - fallback to DamlUnit as safe default
            return ("DamlUnit", null, true, false);
        }

        // Fallback to generating a nested argument type
        return ($"{SanitizeIdentifier(choice.Name)}Arg", null, false, true);
    }

    private void WriteChoiceArgumentType(IndentWriter indent, DamlChoice choice, IReadOnlyDictionary<string, DamlDataType> dataTypes)
    {
        var (argTypeName, _, isExternalRef, _) = GetChoiceArgumentInfo(choice, dataTypes);

        // If the argument type is a reference to an existing data type, don't generate a nested class
        // The data type is already generated separately
        if (choice.ArgumentType is DamlTypeRef typeRef && dataTypes.ContainsKey(typeRef.Name))
        {
            // No nested class needed - we'll reference the existing data type
            return;
        }

        // If it's Unit or external reference (like Archive), no argument type needed
        if (choice.ArgumentType is DamlPrimitiveType { Primitive: DamlPrimitive.Unit } || isExternalRef)
        {
            return;
        }

        // For other cases (shouldn't happen often), generate a simple argument type
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
        var (argTypeName, argFields, isExternalRef, isFallback) = GetChoiceArgumentInfo(choice, dataTypes);

        // B3: skip choices whose argument type went through the codegen fallback
        // path (no recognised record / Unit / external ref). The fallback emits a
        // field-less <Choice>Arg stub with no ToRecord(), so the static
        // Choice<T,A,R> field's `ArgumentEncoder = arg => arg.ToRecord()` would
        // not compile in consumer output. Mirrors the gate applied to
        // <Choice>Async emission in #77 (see WriteChoiceAsyncExercisersClass).
        // Fixes #78.
        if (isFallback)
        {
            return;
        }

        indent.AppendLine($"/// <summary>");
        indent.AppendLine($"/// Exercise the {choice.Name} choice.");
        if (choice.Consuming)
        {
            indent.AppendLine($"/// This choice is consuming and will archive the contract.");
        }
        indent.AppendLine($"/// </summary>");

        // Generate the Choice property with proper encoder/decoder
        indent.AppendLine($"public static Choice<{indent.CurrentTypeName}, {argTypeName}, {returnType}> Choice{choiceName} {{ get; }} = new()");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine($"Name = \"{choice.Name}\",");
        indent.AppendLine($"Consuming = {(choice.Consuming ? "true" : "false")},");

        // Generate ArgumentEncoder
        if (argTypeName == "DamlUnit" || isExternalRef)
        {
            indent.AppendLine("ArgumentEncoder = _ => DamlUnit.Instance,");
        }
        else
        {
            // The argument type has ToRecord() method
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
                indent.AppendLine("ResultDecoder = _ => DamlUnit.Instance");
                return;
            case DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.ContractId }, Arguments: [var arg] }:
                var contractType = MapDamlTypeToCSharp(arg);
                indent.AppendLine($"ResultDecoder = val => new ContractId<{contractType}>(val.As<DamlContractId>().Value)");
                return;
        }

        var expr = GetFromValueConversion(returnType, "val", dataTypes);
        indent.AppendLine($"ResultDecoder = val => {expr}");
    }

    private void WriteContractIdClass(IndentWriter indent, string className)
    {
        indent.AppendLine($"/// <summary>Contract ID for {className}.</summary>");
        indent.AppendLine($"public sealed record ContractId(string Value) : ContractId<{className}>(Value), IExercises<{className}>");
        indent.AppendLine("{");
        indent.Indent();

        indent.AppendLine($"ContractId<{className}> IExercises<{className}>.ContractId => this;");

        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine();
    }

    private void WriteContractClass(IndentWriter indent, string className)
    {
        indent.AppendLine($"/// <summary>Active contract for {className}.</summary>");
        indent.AppendLine($"public sealed record Contract(ContractId Id, {className} Data) : IContract<ContractId, {className}>");
        indent.AppendLine("{");
        indent.Indent();

        indent.AppendLine("/// <summary>Creates a Contract from a CreatedEvent.</summary>");
        indent.AppendLine("public static Contract FromCreatedEvent(CreatedEvent @event) =>");
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
            indent.AppendLine(") : IDamlValue");
        }
        else
        {
            indent.AppendLine($"public sealed record {fullClassName} : IDamlValue");
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

        indent.AppendLine($"public sealed record {className} : ITemplate");
        indent.AppendLine("{");
        indent.Indent();

        // Static metadata required by ITemplate. All four throw — they're never the
        // right thing to call on an interface placeholder, and a runtime exception
        // here is the cleanest signal that the caller should have coerced first.
        indent.AppendLine($"public static Identifier TemplateId =>");
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

        // IDamlValue requirements. ToRecord returns an empty record (matches the LF
        // shape — interface placeholders have no fields). FromRecord round-trips
        // back to an empty instance; no information is lost because none was ever
        // carried.
        indent.AppendLine("public DamlRecord ToRecord() => DamlRecord.Create();");
        indent.AppendLine();
        indent.AppendLine($"public static {className} FromRecord(DamlRecord record) => new {className}();");

        indent.Dedent();
        indent.AppendLine("}");
    }

    private void WriteVariantType(IndentWriter indent, DamlDataType dataType, DamlVariantDefinition variant)
    {
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
        indent.AppendLine($"public abstract record {fullClassName} : IDamlValue");
        indent.AppendLine("{");
        indent.Indent();

        indent.AppendLine("/// <summary>Gets the variant constructor name.</summary>");
        indent.AppendLine("public abstract string Tag { get; }");
        indent.AppendLine();

        indent.AppendLine("/// <summary>Converts to a DamlRecord.</summary>");
        indent.AppendLine("public abstract DamlRecord ToRecord();");
        indent.AppendLine();

        // Variant FromRecord stub. Variants serialize to DamlVariant on the wire, not
        // DamlRecord; full round-trip support is tracked in
        // https://github.com/peacefulstudio/daml-codegen-csharp/issues/57. This stub
        // exists so generated parent records that hold a variant field still compile
        // — runtime deserialization of the variant itself will throw until proper
        // variant codegen lands.
        indent.AppendLine($"/// <summary>Reconstructs a {className} from a DamlRecord. Stub: throws until variant deserialization is implemented (see issue #57).</summary>");
        indent.AppendLine($"public static {fullClassName} FromRecord(DamlRecord record) =>");
        indent.Indent();
        indent.AppendLine($"throw new NotImplementedException(\"Variant deserialization for {dataType.Name} is not implemented (variants serialize as DamlVariant, not DamlRecord). Tracking issue: https://github.com/peacefulstudio/daml-codegen-csharp/issues/57\");");
        indent.Dedent();
        indent.AppendLine();

        // Generate derived types for each constructor
        foreach (var ctor in variant.Constructors)
        {
            var ctorName = SanitizeIdentifier(ctor.Name);
            var argType = ctor.ArgumentType is not null ? MapDamlTypeToCSharp(ctor.ArgumentType) : null;

            if (argType is not null)
            {
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
            indent.AppendLine("public override DamlRecord ToRecord() => DamlRecord.Create();");

            indent.Dedent();
            indent.AppendLine("}");
            indent.AppendLine();
        }

        indent.Dedent();
        indent.AppendLine("}");
    }

    private void WriteEnumType(IndentWriter indent, DamlDataType dataType, DamlEnumDefinition enumDef)
    {
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

        // ToRecord method (returns DamlEnum)
        indent.AppendLine($"/// <summary>Converts to a DamlEnum value.</summary>");
        indent.AppendLine($"public static DamlEnum ToRecord(this {enumName} value)");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine("return value switch");
        indent.AppendLine("{");
        indent.Indent();
        foreach (var ctor in enumDef.Constructors)
        {
            indent.AppendLine($"{enumName}.{SanitizeIdentifier(ctor)} => DamlEnum.Create(\"{ctor}\"),");
        }
        indent.AppendLine($"_ => throw new ArgumentOutOfRangeException(nameof(value), value, null)");
        indent.Dedent();
        indent.AppendLine("};");
        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine();

        // FromRecord static method
        indent.AppendLine($"/// <summary>Creates an instance from a DamlEnum value.</summary>");
        indent.AppendLine($"public static {enumName} FromRecord(DamlEnum value)");
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
        DamlPrimitiveType { Primitive: DamlPrimitive.Unit } => "DamlUnit",
        DamlPrimitiveType { Primitive: DamlPrimitive.Bool } => "bool",
        DamlPrimitiveType { Primitive: DamlPrimitive.Int64 } => "long",
        DamlPrimitiveType { Primitive: DamlPrimitive.Numeric } => "decimal",
        DamlPrimitiveType { Primitive: DamlPrimitive.Text } => "string",
        DamlPrimitiveType { Primitive: DamlPrimitive.Date } => "DateOnly",
        DamlPrimitiveType { Primitive: DamlPrimitive.Timestamp } => "DateTimeOffset",
        DamlPrimitiveType { Primitive: DamlPrimitive.Party } => "Party",
        // Numeric with scale argument (Numeric n) - maps to decimal
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Numeric } } => "decimal",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.ContractId }, Arguments: [var arg] } =>
            $"ContractId<{MapDamlTypeToCSharp(arg)}>",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional }, Arguments: [var arg] } =>
            $"{MapDamlTypeToCSharp(arg)}?",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.List }, Arguments: [var arg] } =>
            $"IReadOnlyList<{MapDamlTypeToCSharp(arg)}>",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.TextMap }, Arguments: [var arg] } =>
            $"IReadOnlyDictionary<string, {MapDamlTypeToCSharp(arg)}>",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.GenMap }, Arguments: [var keyArg, var valueArg] } =>
            $"IReadOnlyDictionary<{MapDamlTypeToCSharp(keyArg)}, {MapDamlTypeToCSharp(valueArg)}>",
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

    private string GetToValueConversion(DamlType type, string fieldName) => type switch
    {
        DamlPrimitiveType { Primitive: DamlPrimitive.Unit } => "DamlUnit.Instance",
        DamlPrimitiveType { Primitive: DamlPrimitive.Bool } => $"new DamlBool({fieldName})",
        DamlPrimitiveType { Primitive: DamlPrimitive.Int64 } => $"new DamlInt64({fieldName})",
        DamlPrimitiveType { Primitive: DamlPrimitive.Numeric } => $"new DamlNumeric({fieldName})",
        DamlPrimitiveType { Primitive: DamlPrimitive.Text } => $"new DamlText({fieldName})",
        DamlPrimitiveType { Primitive: DamlPrimitive.Date } => $"new DamlDate({fieldName})",
        DamlPrimitiveType { Primitive: DamlPrimitive.Timestamp } => $"new DamlTimestamp({fieldName})",
        DamlPrimitiveType { Primitive: DamlPrimitive.Party } => $"{fieldName}.ToDamlValue()",
        // Numeric with scale argument
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Numeric } } =>
            $"new DamlNumeric({fieldName})",
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
            $"{fieldName} is {{ }} __{fieldName.TrimStart('@')} ? new DamlOptional({GetToValueConversion(app.Arguments[0], $"__{fieldName.TrimStart('@')}")}) : DamlOptional.None",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.List } } app =>
            $"new DamlList({fieldName}.Select(x => (DamlValue){GetToValueConversion(app.Arguments[0], "x")}).ToList())",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.TextMap } } app =>
            $"new DamlTextMap({fieldName}.ToDictionary(kv => kv.Key, kv => (DamlValue){GetToValueConversion(app.Arguments[0], "kv.Value")}))",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.GenMap } } app =>
            $"new DamlGenMap({fieldName}.Select(kv => ((DamlValue){GetToValueConversion(app.Arguments[0], "kv.Key")}, (DamlValue){GetToValueConversion(app.Arguments[1], "kv.Value")})).ToList())",
        // Daml type variables (parametric polymorphism). The codegen has no way to
        // dispatch ToRecord through a bare T at compile time, so we emit a runtime
        // stub: the type compiles, but serializing an actual generic instance throws.
        DamlTypeVar => $"Daml.Runtime.Stdlib.GenericStub.NotImplemented<DamlValue>(\"{fieldName}\")",
        _ => $"{fieldName}.ToRecord()"
    };

    private string GetFromValueConversion(DamlType type, string valueName, IReadOnlyDictionary<string, DamlDataType>? dataTypes = null) => type switch
    {
        DamlPrimitiveType { Primitive: DamlPrimitive.Bool } => $"{valueName}.As<DamlBool>().Value",
        DamlPrimitiveType { Primitive: DamlPrimitive.Int64 } => $"{valueName}.As<DamlInt64>().Value",
        DamlPrimitiveType { Primitive: DamlPrimitive.Numeric } => $"{valueName}.As<DamlNumeric>().Value",
        DamlPrimitiveType { Primitive: DamlPrimitive.Text } => $"{valueName}.As<DamlText>().Value",
        DamlPrimitiveType { Primitive: DamlPrimitive.Date } => $"{valueName}.As<DamlDate>().Value",
        DamlPrimitiveType { Primitive: DamlPrimitive.Timestamp } => $"{valueName}.As<DamlTimestamp>().Value",
        DamlPrimitiveType { Primitive: DamlPrimitive.Party } => $"Party.FromDamlValue({valueName}.As<DamlParty>())",
        // Numeric with scale argument
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Numeric } } =>
            $"{valueName}.As<DamlNumeric>().Value",
        // Optional type
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional }, Arguments: [var arg] } =>
            $"{valueName}.As<DamlOptional>().HasValue ? {GetFromValueConversion(arg, $"{valueName}.As<DamlOptional>().Value!", dataTypes)} : null",
        // List type
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.List }, Arguments: [var arg] } =>
            $"{valueName}.As<DamlList>().Values.Select(x => {GetFromValueConversion(arg, "x", dataTypes)}).ToList()",
        // TextMap type
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.TextMap }, Arguments: [var arg] } =>
            $"{valueName}.As<DamlTextMap>().Values.ToDictionary(kv => kv.Key, kv => {GetFromValueConversion(arg, "kv.Value", dataTypes)})",
        // GenMap type
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.GenMap }, Arguments: [var keyArg, var valueArg] } =>
            $"{valueName}.As<DamlGenMap>().Entries.ToDictionary(kv => {GetFromValueConversion(keyArg, "kv.Key", dataTypes)}, kv => {GetFromValueConversion(valueArg, "kv.Value", dataTypes)})",
        // ContractId type
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.ContractId }, Arguments: [var arg] } =>
            $"new ContractId<{MapDamlTypeToCSharp(arg)}>({valueName}.As<DamlContractId>().Value)",
        // Type reference — enum dispatch keyed by module:name so a same-name record in a
        // different module doesn't accidentally route through *Extensions.FromRecord.
        DamlTypeRef typeRef when _localEnumQualifiedNames.Contains($"{typeRef.Module}:{typeRef.Name}") =>
            $"{ResolveTypeRefName(typeRef)}Extensions.FromRecord({valueName}.As<DamlEnum>())",
        // Type reference (record/variant types)
        DamlTypeRef typeRef => $"{ResolveTypeRefName(typeRef)}.FromRecord({valueName}.As<DamlRecord>())",
        // Daml type variable — same treatment as ToValue: emit a runtime-throwing stub
        // typed as the generic placeholder so generics-bearing records still compile.
        DamlTypeVar typeVar => $"Daml.Runtime.Stdlib.GenericStub.NotImplemented<T{ToPascalCase(SanitizeIdentifier(typeVar.Name))}>(\"{typeVar.Name}\")",
        _ => $"default! /* TODO: Implement deserialization for {type} */"
    };

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
    private static string DeriveNamespace(string packageName)
    {
        var parts = packageName.Split('-', '_')
            .Select(ToPascalCase)
            .Select(SanitizeIdentifier);
        return string.Join(".", parts);
    }

    private static string SanitizeIdentifier(string name)
    {
        // Replace invalid characters
        var sanitized = IdentifierRegex().Replace(name, "_");

        // Ensure it doesn't start with a digit
        if (char.IsDigit(sanitized[0]))
        {
            sanitized = "_" + sanitized;
        }

        // Handle C# keywords
        if (CSharpKeywords.Contains(sanitized))
        {
            sanitized = "@" + sanitized;
        }

        return sanitized;
    }

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
