// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.RegularExpressions;
using Daml.Codegen.CSharp.Model;
using RuntimeNamespaces = Daml.Runtime.RuntimeNamespaces;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Generates C# code from Daml packages.
/// </summary>
public sealed partial class CSharpCodeGenerator(CodeGenOptions options, ICodegenLogger logger)
{
    private readonly Regex? _rootFilter = options.RootFilter is not null
        ? new Regex(options.RootFilter, RegexOptions.Compiled)
        : null;

    private readonly PartyAnalysis _party = new();

    /// <summary>
    /// Generates C# code for all types in the DAR.
    /// </summary>
    public IReadOnlyList<GeneratedFile> Generate(IDarSource dar)
    {
        var files = new List<GeneratedFile>();

        var resolver = new DarCrossPackageResolver(dar, logger);

        // Generate code for the main package
        files.AddRange(GeneratePackage(resolver, dar.MainPackage));

        // Optionally generate code for dependencies
        if (options.IncludeDependencies)
        {
            foreach (var dep in dar.Dependencies)
            {
                logger.Debug($"Generating code for dependency: {dep.Name}");
                files.AddRange(GeneratePackage(resolver, dep));
            }
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
            foreach (var id in resolver.DiscoveredExternalPackageIds)
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
            files.Add(projectGenerator.GenerateReadme(dar.MainPackage));
            files.Add(projectGenerator.GenerateIcon());
        }

        var requiresCSharp13 = files.Any(f =>
            f.RelativePath.EndsWith(".cs", StringComparison.Ordinal)
            && ProjectFileGenerator.ContentRequiresCSharp13(f.Content));
        files.Add(GeneratedFile.Text(
            ".daml-langversion",
            requiresCSharp13 ? "13\n" : string.Empty));

        return files;
    }

    private static bool IsStdlibPackage(string packageName) => StdlibPackages.IsStdlibPackage(packageName);

    private static bool IsPlaceholderPackageName(string packageName) => StdlibPackages.IsPlaceholderPackageName(packageName);

    /// <summary>
    /// Generates C# code for a single package.
    /// </summary>
    private IEnumerable<GeneratedFile> GeneratePackage(ICrossPackageResolver resolver, DamlPackage package)
    {
        var context = PackageEmitContext.ForPackage(package, options, logger);
        var mapper = new DamlTypeMapper(context, resolver);
        var choiceEmitter = new ChoiceEmitter(context, resolver, options, mapper, _party);
        var rootNamespace = context.RootNamespace;
        var submissionExtensions = new SubmissionExtensionsEmitter(context, options, _party);

        var allTemplateNames = package.Modules
            .SelectMany(m => m.Templates)
            .Select(t => t.Name)
            .ToHashSet();

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

                var code = GenerateTemplate(context, submissionExtensions, resolver, mapper, choiceEmitter, package, module, template, fields);
                var path = RelativeFilePath(rootNamespace, $"{SanitizeIdentifier(template.Name)}.cs");

                yield return GeneratedFile.Text(path, code);
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
                if (context.LocalChoiceArgToTemplate.ContainsKey($"{module.Name}:{dataType.Name}"))
                {
                    continue;
                }

                var code = GenerateDataType(context, resolver, mapper, module, dataType);
                var path = RelativeFilePath(rootNamespace, $"{SanitizeIdentifier(dataType.Name)}.cs");

                yield return GeneratedFile.Text(path, code);
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
                        context.DataTypes.TryGetValue(typeRef.Name, out var argDataType) &&
                        argDataType.Definition is DamlRecordDefinition)
                    {
                        var code = GenerateNestedChoiceArgumentType(context, resolver, mapper,
                            template, choice, argDataType);
                        var path = RelativeFilePath(
                            rootNamespace,
                            $"{SanitizeIdentifier(template.Name)}.{SanitizeIdentifier(choice.Name)}.cs");

                        yield return GeneratedFile.Text(path, code);
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

                var code = GenerateInterface(context, resolver, mapper, choiceEmitter, package, module, iface);
                var path = RelativeFilePath(rootNamespace, $"I{SanitizeIdentifier(iface.Name)}.cs");

                yield return GeneratedFile.Text(path, code);
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
                var identifiersFile = GenerateContractIdentifiersFile(allTemplates, rootNamespace);
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
        PackageEmitContext context, SubmissionExtensionsEmitter submissionExtensions,
        ICrossPackageResolver resolver, DamlTypeMapper mapper,
        ChoiceEmitter choiceEmitter,
        DamlPackage package,
        DamlModule module,
        DamlTemplate template,
        IReadOnlyList<DamlFieldDefinition> fields)
    {
        var moduleNamespace = context.RootNamespace;
        var dataTypes = context.DataTypes;
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
        var keyType = template.Key is not null ? mapper.MapType(template.Key) : null;
        var interfacesList = new List<string> { context.Qualifier.Qualify(RuntimeTypeNames.ITemplate, context.RootNamespace) };
        if (keyType is not null)
            interfacesList.Add($"{context.Qualifier.Qualify(RuntimeTypeNames.IHasKey, context.RootNamespace)}<{keyType}>");
        if (package.UpgradedPackageId is not null)
            interfacesList.Add(context.Qualifier.Qualify(RuntimeTypeNames.IUpgradeable, context.RootNamespace));
        var interfaces = string.Join(", ", interfacesList);

        if (options.UseRecordTypes && options.UsePrimaryConstructors && fields.Count > 0)
        {
            // Record with primary constructor - use partial for nested types in separate files
            indent.Append($"public sealed partial record {className}(");
            WriteRecordParameters(context, resolver, mapper, indent, fields);
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
        WriteTemplateMetadata(context, indent, package, module, template);

        // Key property (if template has a key)
        if (template.Key is not null)
        {
            WriteKeyProperty(context, resolver, mapper, indent, template.Key);
        }

        // Properties (if not using primary constructor)
        if (!options.UsePrimaryConstructors || !options.UseRecordTypes)
        {
            WriteProperties(context, resolver, mapper, indent, fields);
        }

        // ToRecord method
        WriteToRecordMethod(context, resolver, mapper, indent, fields);

        // FromRecord method
        WriteFromRecordMethod(context, resolver, mapper, indent, className, fields);

        // Choice argument types and methods
        choiceEmitter.WriteChoiceDescriptors(indent, template);

        // ContractId nested class
        WriteContractIdClass(context, indent, className);

        // Contract nested class
        WriteContractClass(context, indent, className);

        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine();

        // Typed <Choice>Result structs + FromCreatedContracts projectors are
        // emitted at namespace level (sibling of the template) so the static
        // <TemplateName>Extensions class below can reference them by their
        // unqualified name. See ChoiceEmitter.ContractIdExercisers.cs.
        choiceEmitter.WriteChoiceResultStructs(indent, template, moduleNamespace);

        // Static `<TemplateName>Extensions` class with one `<Choice>Async`
        // method per create-bearing choice. Lives at the namespace level so
        // `using` directives bring the extension methods into scope for
        // `ContractId<TemplateName>` receivers. The exerciser threads the
        // typed-controller analysis (named Party params per controller when
        // statically resolvable) and the union of template-level + choice-level
        // observers (contributed to SubmitterInfo.readAs) into each method.
        choiceEmitter.WriteChoiceAsyncExercisersClass(indent, template, className, fields, dataTypes);

        // Submission extensions: per-template static class exposing CreateAsync
        // and the optional Observers(payload) helper. Lives at the file's top
        // level (sibling to the template record) so the extensions resolve with
        // a single using of this namespace. See SubmissionExtensionsEmitter.cs.
        submissionExtensions.TryWriteSubmissionExtensions(indent, template, fields);

        // Typed exerciser wrappers for choices whose return type is *not* a bare
        // ContractId T (Decimal, records, lists, Unit, etc.). Emitted at the
        // file's top level (sibling to the template class, not nested) so the
        // extension methods are accessible with a single using of this
        // namespace. See ChoiceEmitter.NonContractExercisers.cs for the
        // emission rules.
        choiceEmitter.TryWriteNonContractChoiceExtensions(indent, template, dataTypes);

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
        PackageEmitContext context, ICrossPackageResolver resolver, DamlTypeMapper mapper,
        DamlModule module,
        DamlDataType dataType)
    {
        var moduleNamespace = context.RootNamespace;
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
                WriteRecordType(context, resolver, mapper, indent, module, dataType, record);
                break;
            case DamlVariantDefinition variant:
                WriteVariantType(context, resolver, mapper, indent, dataType, variant);
                break;
            case DamlEnumDefinition enumDef:
                WriteEnumType(context, indent, dataType, enumDef);
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
        PackageEmitContext context, ICrossPackageResolver resolver, DamlTypeMapper mapper,
        ChoiceEmitter choiceEmitter,
        DamlPackage package,
        DamlModule module,
        DamlInterface iface)
    {
        var moduleNamespace = context.RootNamespace;
        var dataTypes = context.DataTypes;
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
        var viewType = iface.ViewType is not null ? mapper.MapType(iface.ViewType) : null;
        var interfaces = viewType is not null
            ? $"{context.Qualifier.Qualify(RuntimeTypeNames.IDamlInterface, context.RootNamespace)}, {context.Qualifier.Qualify(RuntimeTypeNames.IHasView, context.RootNamespace)}<{viewType}>"
            : context.Qualifier.Qualify(RuntimeTypeNames.IDamlInterface, context.RootNamespace);

        indent.AppendLine($"public interface {interfaceName} : {interfaces}");
        indent.AppendLine("{");
        indent.Indent();

        // Static interface metadata
        WriteInterfaceMetadata(context, indent, package, module, iface);

        // Generate method signatures for each choice
        foreach (var method in iface.Choices)
        {
            choiceEmitter.WriteInterfaceMethod(indent, method, dataTypes);
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
            choiceEmitter.WriteInterfaceChoiceExtensions(indent, iface, interfaceName);
        }

        if (!options.UseFileScopedNamespaces)
        {
            indent.Dedent();
            indent.AppendLine("}");
        }

        return BuildFileContent(indent, bodySb);
    }

    /// <summary>
    /// Generates the ContractIdentifiers helper class with fully qualified identifiers for all templates.
    /// </summary>
    private GeneratedFile GenerateContractIdentifiersFile(
        IReadOnlyList<(DamlModule Module, DamlTemplate Template)> templates,
        string moduleNamespace)
    {
        var bodySb = new StringBuilder();
        var indent = new IndentWriter(bodySb);

        indent.Require(RuntimeNamespaces.Contracts);
        indent.Require($"static {RuntimeNamespaces.Contracts}.TemplateExtensions");

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

        var lastDot = moduleNamespace.LastIndexOf('.');
        var namespaceBesidePackageFolder = lastDot < 0 ? string.Empty : moduleNamespace[..lastDot];
        var path = RelativeFilePath(namespaceBesidePackageFolder, "ContractIdentifiers.cs");

        return GeneratedFile.Text(path, BuildFileContent(indent, bodySb));
    }

    private static string RelativeFilePath(string dottedNamespace, string fileName) =>
        dottedNamespace.Length == 0 ? fileName : $"{dottedNamespace.Replace('.', '/')}/{fileName}";

    /// <summary>
    /// Generates a partial file with the choice argument type nested inside the template.
    /// </summary>
    private string GenerateNestedChoiceArgumentType(
        PackageEmitContext context, ICrossPackageResolver resolver, DamlTypeMapper mapper,
        DamlTemplate template,
        DamlChoice choice,
        DamlDataType argDataType)
    {
        var moduleNamespace = context.RootNamespace;
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
            indent.CurrentTypeName = choiceTypeName;

            if (options.GenerateXmlDocs)
            {
                indent.AppendLine("/// <summary>");
                indent.AppendLine($"/// Choice argument type for {choice.Name}.");
                indent.AppendLine("/// </summary>");
            }

            if (options.UseRecordTypes && options.UsePrimaryConstructors && record.Fields.Count > 0)
            {
                indent.Append($"public sealed record {choiceTypeName}(");
                WriteRecordParameters(context, resolver, mapper, indent, record.Fields);
                indent.AppendLine($") : {context.Qualifier.Qualify(RuntimeTypeNames.IDamlRecord, context.RootNamespace)}");
            }
            else
            {
                indent.AppendLine($"public sealed record {choiceTypeName} : {context.Qualifier.Qualify(RuntimeTypeNames.IDamlRecord, context.RootNamespace)}");
            }

            indent.AppendLine("{");
            indent.Indent();

            WriteToRecordMethod(context, resolver, mapper, indent, record.Fields);
            WriteFromRecordMethod(context, resolver, mapper, indent, choiceTypeName, record.Fields);

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
        PackageEmitContext context,
        IndentWriter indent,
        DamlPackage package,
        DamlModule module,
        DamlInterface iface)
    {
        indent.Require("System");
        indent.Require(RuntimeNamespaces.Contracts);
        indent.AppendLine($"/// <summary>Gets the interface identifier.</summary>");
        indent.AppendLine($"static {context.Qualifier.Qualify(RuntimeTypeNames.Identifier, context.RootNamespace)} {context.Qualifier.Qualify(RuntimeTypeNames.IDamlInterface, context.RootNamespace)}.InterfaceId => new(\"{package.PackageId}\", \"{module.Name}\", \"{iface.Name}\");");
        indent.AppendLine();

        indent.AppendLine($"/// <summary>Gets the package ID.</summary>");
        indent.AppendLine($"static string {context.Qualifier.Qualify(RuntimeTypeNames.IDamlInterface, context.RootNamespace)}.{nameof(Daml.Runtime.Contracts.IDamlInterface.PackageId)} => \"{package.PackageId}\";");
        indent.AppendLine();

        indent.AppendLine($"/// <summary>Gets the package name.</summary>");
        indent.AppendLine($"static string {context.Qualifier.Qualify(RuntimeTypeNames.IDamlInterface, context.RootNamespace)}.{nameof(Daml.Runtime.Contracts.IDamlInterface.PackageName)} => \"{package.Name}\";");
        indent.AppendLine();

        indent.AppendLine($"/// <summary>Gets the package version.</summary>");
        indent.AppendLine($"static Version {context.Qualifier.Qualify(RuntimeTypeNames.IDamlInterface, context.RootNamespace)}.{nameof(Daml.Runtime.Contracts.IDamlInterface.PackageVersion)} => new({package.Version.Major}, {package.Version.Minor}, {package.Version.Build});");
        indent.AppendLine();
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
        indent.Require(RuntimeNamespaces.Data);
    }

    private void WriteTemplateMetadata(
        PackageEmitContext context,
        IndentWriter indent,
        DamlPackage package,
        DamlModule module,
        DamlTemplate template)
    {
        indent.Require("System");
        var templateId = $"{module.Name}:{template.Name}";

        indent.AppendLine($"/// <summary>Gets the template identifier.</summary>");
        indent.AppendLine($"public static {context.Qualifier.Qualify(RuntimeTypeNames.Identifier, context.RootNamespace)} TemplateId {{ get; }} = new(\"{package.PackageId}\", \"{module.Name}\", \"{template.Name}\");");
        indent.AppendLine();

        indent.AppendLine($"/// <summary>Gets the package ID.</summary>");
        indent.AppendLine($"public static string {nameof(Daml.Runtime.Contracts.ITemplate.PackageId)} => \"{package.PackageId}\";");
        indent.AppendLine();

        indent.AppendLine($"/// <summary>Gets the package name.</summary>");
        indent.AppendLine($"public static string {nameof(Daml.Runtime.Contracts.ITemplate.PackageName)} => \"{package.Name}\";");
        indent.AppendLine();

        indent.AppendLine($"/// <summary>Gets the package version.</summary>");
        indent.AppendLine($"public static Version {nameof(Daml.Runtime.Contracts.ITemplate.PackageVersion)} {{ get; }} = new({package.Version.Major}, {package.Version.Minor}, {package.Version.Build});");
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

    private void WriteKeyProperty(PackageEmitContext context, ICrossPackageResolver resolver, DamlTypeMapper mapper, IndentWriter indent, DamlType keyType)
    {
        indent.Require(RuntimeNamespaces.Contracts);
        StdlibPackages.RequireForFieldType(resolver, indent, keyType);
        var csharpKeyType = mapper.MapType(keyType);
        var crefKeyType = ToCrefTypeArgument(csharpKeyType);

        // Translating the template's `key` Daml expression to a C# projection is
        // tracked in peacefulstudio/daml-codegen-csharp#64; the intermediate model
        // carries only the key type, not the expression. Until #64 (or an upstream
        // Daml-LF projection) lands, the accessor throws. ADR 0013 records why this
        // reverts the partial-property contract from #65 (the CS9248 compile gate
        // blocked the automated DAR publish pipeline, which has no human to supply
        // an implementing partial).
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Gets the contract key of type <c>{crefKeyType}</c>, satisfying <see cref=\"global::Daml.Runtime.Contracts.IHasKey{{TKey}}\"/>.");
            indent.AppendLine("/// </summary>");
            indent.AppendLine("/// <remarks>");
            indent.AppendLine("/// Throws <see cref=\"global::System.NotImplementedException\"/>: the codegen does not");
            indent.AppendLine("/// yet translate the template's <c>key</c> expression into a C# projection");
            indent.AppendLine("/// (see <see href=\"https://github.com/peacefulstudio/daml-codegen-csharp/issues/64\">daml-codegen-csharp#64</see>).");
            indent.AppendLine("/// The key type is fully generated and serializable — construct a key value");
            indent.AppendLine("/// explicitly for key-based ledger operations rather than reading it here.");
            indent.AppendLine("/// </remarks>");
        }
        indent.AppendLine($"public {csharpKeyType} Key => throw new global::System.NotImplementedException(");
        indent.AppendLine($"    \"Contract-key projection is not generated yet (daml-codegen-csharp#64); construct the {csharpKeyType} key value explicitly for key-based ledger operations.\");");
        indent.AppendLine();
    }

    private void WriteRecordParameters(PackageEmitContext context, ICrossPackageResolver resolver, DamlTypeMapper mapper, IndentWriter indent, IReadOnlyList<DamlFieldDefinition> fields)
    {
        var first = true;
        foreach (var field in fields)
        {
            if (!first)
            {
                indent.Append(", ");
            }
            first = false;

            var csharpType = mapper.MapType(field.Type);
            var fieldName = MemberName(field.Name, indent.CurrentTypeName);
            StdlibPackages.RequireForFieldType(resolver, indent, field.Type);
            indent.Append($"{csharpType} {fieldName}");
        }
    }

    private void WriteProperties(PackageEmitContext context, ICrossPackageResolver resolver, DamlTypeMapper mapper, IndentWriter indent, IReadOnlyList<DamlFieldDefinition> fields)
    {
        foreach (var field in fields)
        {
            var csharpType = mapper.MapType(field.Type);
            var fieldName = MemberName(field.Name, indent.CurrentTypeName);
            StdlibPackages.RequireForFieldType(resolver, indent, field.Type);

            if (options.GenerateXmlDocs)
            {
                indent.AppendLine($"/// <summary>Gets the {field.Name} field.</summary>");
            }

            indent.AppendLine($"public required {csharpType} {fieldName} {{ get; init; }}");
            indent.AppendLine();
        }
    }

    private void WriteToRecordMethod(PackageEmitContext context, ICrossPackageResolver resolver, DamlTypeMapper mapper, IndentWriter indent, IReadOnlyList<DamlFieldDefinition> fields)
    {
        // Wording is "this value" rather than "this template" because the same
        // method is emitted for templates, plain records, and choice argument
        // records — the noun has to fit all three subjects.
        indent.AppendLine("/// <summary>Converts this value to a DamlRecord.</summary>");

        if (fields.Count == 0)
        {
            indent.AppendLine($"public {context.Qualifier.Qualify(RuntimeTypeNames.DamlRecord, context.RootNamespace)} ToRecord() => {context.Qualifier.Qualify(RuntimeTypeNames.DamlRecord, context.RootNamespace)}.Create();");
            indent.AppendLine();
            return;
        }

        // Use expression-bodied member with inline DamlRecord.Create
        indent.AppendLine($"public {context.Qualifier.Qualify(RuntimeTypeNames.DamlRecord, context.RootNamespace)} ToRecord() => {context.Qualifier.Qualify(RuntimeTypeNames.DamlRecord, context.RootNamespace)}.Create(");
        indent.Indent();

        for (int i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            var fieldName = MemberName(field.Name, indent.CurrentTypeName);
            var conversion = mapper.ToValue(field.Type, fieldName);
            var comma = i < fields.Count - 1 ? "," : "";
            StdlibPackages.RequireForFieldType(resolver, indent, field.Type);

            indent.AppendLine($"{context.Qualifier.Qualify(RuntimeTypeNames.DamlField, context.RootNamespace)}.Create(\"{field.Name}\", {conversion}){comma}");
        }

        indent.Dedent();
        indent.AppendLine(");");
        indent.AppendLine();
    }

    private void WriteFromRecordMethod(PackageEmitContext context, ICrossPackageResolver resolver, DamlTypeMapper mapper, IndentWriter indent, string className, IReadOnlyList<DamlFieldDefinition> fields)
    {
        indent.AppendLine("/// <summary>Creates an instance from a DamlRecord.</summary>");

        if (fields.Count == 0)
        {
            indent.AppendLine($"public static {className} FromRecord({context.Qualifier.Qualify(RuntimeTypeNames.DamlRecord, context.RootNamespace)} record) => new {className}();");
            indent.AppendLine();
            return;
        }

        foreach (var field in fields)
        {
            StdlibPackages.RequireForFieldType(resolver, indent, field.Type);
        }

        if (options.UseRecordTypes && options.UsePrimaryConstructors)
        {
            indent.AppendLine($"public static {className} FromRecord({context.Qualifier.Qualify(RuntimeTypeNames.DamlRecord, context.RootNamespace)} record) => new {className}(");
            indent.Indent();

            for (int i = 0; i < fields.Count; i++)
            {
                var field = fields[i];
                var fieldName = MemberName(field.Name, indent.CurrentTypeName);
                var conversion = mapper.FromValue(field.Type, $"record.GetRequiredField(\"{field.Name}\")");
                var comma = i < fields.Count - 1 ? "," : "";

                indent.AppendLine($"{fieldName}: {conversion}{comma}");
            }

            indent.Dedent();
            indent.AppendLine(");");
        }
        else
        {
            indent.AppendLine($"public static {className} FromRecord({context.Qualifier.Qualify(RuntimeTypeNames.DamlRecord, context.RootNamespace)} record)");
            indent.AppendLine("{");
            indent.Indent();

            indent.AppendLine($"return new {className}");
            indent.AppendLine("{");
            indent.Indent();

            foreach (var field in fields)
            {
                var fieldName = MemberName(field.Name, indent.CurrentTypeName);
                var conversion = mapper.FromValue(field.Type, $"record.GetRequiredField(\"{field.Name}\")");
                indent.AppendLine($"{fieldName} = {conversion},");
            }

            indent.Dedent();
            indent.AppendLine("};");

            indent.Dedent();
            indent.AppendLine("}");
        }
        indent.AppendLine();
    }

    private void WriteContractIdClass(PackageEmitContext context, IndentWriter indent, string className)
    {
        indent.Require(RuntimeNamespaces.Commands);
        indent.Require(RuntimeNamespaces.Contracts);
        indent.AppendLine($"/// <summary>Contract ID for {className}.</summary>");
        indent.AppendLine($"public sealed record ContractId(string Value) : {context.Qualifier.Qualify(RuntimeTypeNames.ContractId, context.RootNamespace)}<{className}>(Value), {context.Qualifier.Qualify(RuntimeTypeNames.IExercises, context.RootNamespace)}<{className}>");
        indent.AppendLine("{");
        indent.Indent();

        indent.AppendLine($"{context.Qualifier.Qualify(RuntimeTypeNames.ContractId, context.RootNamespace)}<{className}> {context.Qualifier.Qualify(RuntimeTypeNames.IExercises, context.RootNamespace)}<{className}>.ContractId => this;");

        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine();
    }

    private void WriteContractClass(PackageEmitContext context, IndentWriter indent, string className)
    {
        indent.Require(RuntimeNamespaces.Contracts);
        indent.AppendLine($"/// <summary>Active contract for {className}.</summary>");
        indent.AppendLine($"public sealed record Contract(ContractId Id, {className} Data) : {context.Qualifier.Qualify(RuntimeTypeNames.IContract, context.RootNamespace)}<ContractId, {className}>");
        indent.AppendLine("{");
        indent.Indent();

        indent.AppendLine("/// <summary>Creates a Contract from a CreatedEvent.</summary>");
        indent.AppendLine($"public static Contract FromCreatedEvent({context.Qualifier.Qualify(RuntimeTypeNames.CreatedEvent, context.RootNamespace)} @event) =>");
        indent.Indent();
        indent.AppendLine($"new(new ContractId(@event.ContractId), {className}.FromRecord(@event.CreateArguments));");
        indent.Dedent();

        indent.Dedent();
        indent.AppendLine("}");
    }

    private void WriteRecordType(PackageEmitContext context, ICrossPackageResolver resolver, DamlTypeMapper mapper, IndentWriter indent, DamlModule module, DamlDataType dataType, DamlRecordDefinition record)
    {
        if (context.InterfacePlaceholderQualifiedNames.Contains($"{module.Name}:{dataType.Name}"))
        {
            WriteInterfacePlaceholderRecord(context, indent, module, dataType);
            return;
        }

        var className = SanitizeIdentifier(dataType.Name);
        indent.CurrentTypeName = className;
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
            WriteRecordParameters(context, resolver, mapper, indent, record.Fields);
            indent.AppendLine($") : {context.Qualifier.Qualify(RuntimeTypeNames.IDamlRecord, context.RootNamespace)}");
        }
        else
        {
            indent.AppendLine($"public sealed record {fullClassName} : {context.Qualifier.Qualify(RuntimeTypeNames.IDamlRecord, context.RootNamespace)}");
        }

        indent.AppendLine("{");
        indent.Indent();

        WriteToRecordMethod(context, resolver, mapper, indent, record.Fields);
        WriteFromRecordMethod(context, resolver, mapper, indent, fullClassName, record.Fields);

        indent.Dedent();
        indent.AppendLine("}");
    }

    /// <summary>
    /// Emits the C# placeholder for a Daml interface declaration. The Daml-LF emits a
    /// same-named empty record alongside every <c>interface I where ...</c> so that
    /// <c>ContractId I</c> can be expressed at the type level. We surface that record
    /// as a sealed record implementing <see cref="Daml.Runtime.Contracts.ITemplate"/> with throwing static
    /// metadata: it lets <c>ContractId&lt;I&gt;</c> compile (the runtime constraint is
    /// <c>where T : ITemplate</c>) but loudly fails any code path that tries to read
    /// <c>I.TemplateId</c> directly — which would be a logic error, since interface
    /// placeholders carry no template identity. Coerce the contract id to the
    /// underlying template type before reading metadata or constructing commands.
    /// </summary>
    private void WriteInterfacePlaceholderRecord(PackageEmitContext context, IndentWriter indent, DamlModule module, DamlDataType dataType)
    {
        indent.Require("System");
        indent.Require(RuntimeNamespaces.Contracts);
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

        indent.AppendLine($"public sealed record {className} : {context.Qualifier.Qualify(RuntimeTypeNames.ITemplate, context.RootNamespace)}");
        indent.AppendLine("{");
        indent.Indent();

        // Static metadata required by ITemplate. All four throw — they're never the
        // right thing to call on an interface placeholder, and a runtime exception
        // here is the cleanest signal that the caller should have coerced first.
        indent.AppendLine($"public static {context.Qualifier.Qualify(RuntimeTypeNames.Identifier, context.RootNamespace)} TemplateId =>");
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

        indent.AppendLine($"public {context.Qualifier.Qualify(RuntimeTypeNames.DamlRecord, context.RootNamespace)} ToRecord() => {context.Qualifier.Qualify(RuntimeTypeNames.DamlRecord, context.RootNamespace)}.Create();");
        indent.AppendLine();
        indent.AppendLine($"public static {className} FromRecord({context.Qualifier.Qualify(RuntimeTypeNames.DamlRecord, context.RootNamespace)} record) => new {className}();");

        indent.Dedent();
        indent.AppendLine("}");
    }

    private void WriteVariantType(
        PackageEmitContext context, ICrossPackageResolver resolver, DamlTypeMapper mapper,
        IndentWriter indent,
        DamlDataType dataType,
        DamlVariantDefinition variant)
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
        indent.AppendLine($"public abstract record {fullClassName} : {context.Qualifier.Qualify(RuntimeTypeNames.IDamlVariant, context.RootNamespace)}");
        indent.AppendLine("{");
        indent.Indent();

        indent.AppendLine("/// <summary>Gets the variant constructor name.</summary>");
        indent.AppendLine("public abstract string Tag { get; }");
        indent.AppendLine();

        indent.AppendLine("/// <summary>Converts to a DamlVariant.</summary>");
        indent.AppendLine($"public abstract {context.Qualifier.Qualify(RuntimeTypeNames.DamlVariant, context.RootNamespace)} ToVariant();");
        indent.AppendLine();

        indent.AppendLine($"/// <summary>Reconstructs {IndefiniteArticleFor(className)} {className} by dispatching on the DamlVariant constructor tag.</summary>");
        indent.AppendLine($"public static {fullClassName} FromVariant({context.Qualifier.Qualify(RuntimeTypeNames.DamlVariant, context.RootNamespace)} variant) =>");
        indent.Indent();
        indent.AppendLine("variant.Constructor switch");
        indent.AppendLine("{");
        indent.Indent();
        foreach (var ctor in variant.Constructors)
        {
            var ctorName = VariantConstructorName(ctor.Name, className);
            if (HasVariantPayload(ctor))
            {
                indent.AppendLine($"\"{ctor.Name}\" => new {ctorName}({mapper.FromValue(ctor.ArgumentType!, "variant.Value")}),");
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
            var ctorName = VariantConstructorName(ctor.Name, className);
            var hasArg = HasVariantPayload(ctor);
            var argType = hasArg ? mapper.MapType(ctor.ArgumentType!) : null;

            if (argType is not null)
            {
                StdlibPackages.RequireForFieldType(resolver, indent, ctor.ArgumentType!);
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

            indent.AppendLine("/// <inheritdoc />");
            indent.AppendLine($"public override string Tag => \"{ctor.Name}\";");
            indent.AppendLine();
            var payload = hasArg
                ? mapper.ToValue(ctor.ArgumentType!, "Value")
                : $"{context.Qualifier.Qualify(RuntimeTypeNames.DamlUnit, context.RootNamespace)}.Instance";
            indent.AppendLine("/// <inheritdoc />");
            indent.AppendLine($"public override {context.Qualifier.Qualify(RuntimeTypeNames.DamlVariant, context.RootNamespace)} ToVariant() => {context.Qualifier.Qualify(RuntimeTypeNames.DamlVariant, context.RootNamespace)}.Create(\"{ctor.Name}\", {payload});");

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

    private void WriteEnumType(PackageEmitContext context, IndentWriter indent, DamlDataType dataType, DamlEnumDefinition enumDef)
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
        indent.AppendLine($"public static {context.Qualifier.Qualify(RuntimeTypeNames.DamlEnum, context.RootNamespace)} ToDamlEnum(this {enumName} value)");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine("return value switch");
        indent.AppendLine("{");
        indent.Indent();
        foreach (var ctor in enumDef.Constructors)
        {
            indent.AppendLine($"{enumName}.{SanitizeIdentifier(ctor)} => {context.Qualifier.Qualify(RuntimeTypeNames.DamlEnum, context.RootNamespace)}.Create(\"{ctor}\"),");
        }
        indent.AppendLine($"_ => throw new ArgumentOutOfRangeException(nameof(value), value, null)");
        indent.Dedent();
        indent.AppendLine("};");
        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine();

        // FromDamlEnum static method
        indent.AppendLine($"/// <summary>Creates an instance from a DamlEnum value.</summary>");
        indent.AppendLine($"public static {enumName} FromDamlEnum({context.Qualifier.Qualify(RuntimeTypeNames.DamlEnum, context.RootNamespace)} value)");
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
    private static string SanitizeIdentifier(string name) => Identifiers.Sanitize(name);

    private static string IndefiniteArticleFor(string name) =>
        name.Length > 0 && "aeiou".Contains(char.ToLowerInvariant(name[0])) ? "an" : "a";

    private static string ToPascalCase(string name) => Identifiers.ToPascalCase(name);

    private static string MemberName(string damlFieldName, string enclosingTypeName) =>
        Identifiers.MemberName(damlFieldName, enclosingTypeName);

    private static string VariantConstructorName(string ctorName, string enclosingTypeName) =>
        Identifiers.Disambiguate(SanitizeIdentifier(ctorName), enclosingTypeName);
}
