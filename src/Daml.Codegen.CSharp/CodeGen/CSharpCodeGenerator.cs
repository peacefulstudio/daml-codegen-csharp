// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

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
        var enumEmitter = new EnumEmitter(context, options);
        var variantEmitter = new VariantEmitter(context, resolver, options, mapper);
        var recordSerialization = new RecordSerializationEmitter(context, resolver, options, mapper);
        var recordEmitter = new RecordEmitter(context, options, recordSerialization);
        var interfaceEmitter = new InterfaceEmitter(context, mapper, choiceEmitter, options);
        var rootNamespace = context.RootNamespace;
        var submissionExtensions = new SubmissionExtensionsEmitter(context, options, _party);
        var templateEmitter = new TemplateEmitter(context, resolver, mapper, recordSerialization, choiceEmitter, submissionExtensions, options);

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

                var code = GenerateTemplate(context, templateEmitter, package, module, template, fields);
                var path = RelativeFilePath(rootNamespace, $"{EmitterHelpers.SanitizeIdentifier(template.Name)}.cs");

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

                var code = GenerateDataType(context, recordEmitter, enumEmitter, variantEmitter, module, dataType);
                var path = RelativeFilePath(rootNamespace, $"{EmitterHelpers.SanitizeIdentifier(dataType.Name)}.cs");

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
                        var code = GenerateNestedChoiceArgumentType(context, templateEmitter,
                            template, choice, argDataType);
                        var path = RelativeFilePath(
                            rootNamespace,
                            $"{EmitterHelpers.SanitizeIdentifier(template.Name)}.{EmitterHelpers.SanitizeIdentifier(choice.Name)}.cs");

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

                var code = GenerateInterface(context, interfaceEmitter, package, module, iface);
                var path = RelativeFilePath(rootNamespace, $"{Identifiers.InterfaceMarkerName(iface.Name)}.cs");

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
        PackageEmitContext context,
        TemplateEmitter templateEmitter,
        DamlPackage package,
        DamlModule module,
        DamlTemplate template,
        IReadOnlyList<DamlFieldDefinition> fields) =>
        EmitFile(context.RootNamespace, indent =>
        {
            RequireCommonNamespaces(indent);
            templateEmitter.WriteTemplateType(indent, package, module, template, fields);
        });

    /// <summary>
    /// Generates C# code for a data type.
    /// </summary>
    private string GenerateDataType(
        PackageEmitContext context,
        RecordEmitter recordEmitter,
        EnumEmitter enumEmitter,
        VariantEmitter variantEmitter,
        DamlModule module,
        DamlDataType dataType) =>
        EmitFile(context.RootNamespace, indent =>
        {
            RequireCommonNamespaces(indent);

            switch (dataType.Definition)
            {
                case DamlRecordDefinition record:
                    recordEmitter.WriteRecordType(indent, module, dataType, record);
                    break;
                case DamlVariantDefinition variant:
                    variantEmitter.WriteVariantType(indent, dataType, variant);
                    break;
                case DamlEnumDefinition enumDef:
                    enumEmitter.WriteEnumType(indent, dataType, enumDef);
                    break;
            }
        });

    /// <summary>
    /// Generates C# code for a Daml interface.
    /// </summary>
    private string GenerateInterface(
        PackageEmitContext context,
        InterfaceEmitter interfaceEmitter,
        DamlPackage package,
        DamlModule module,
        DamlInterface iface) =>
        EmitFile(context.RootNamespace, indent =>
        {
            RequireCommonNamespaces(indent);
            interfaceEmitter.WriteInterfaceType(indent, package, module, iface);
        });

    /// <summary>
    /// Generates the ContractIdentifiers helper class with fully qualified identifiers for all templates.
    /// </summary>
    private GeneratedFile GenerateContractIdentifiersFile(
        IReadOnlyList<(DamlModule Module, DamlTemplate Template)> templates,
        string moduleNamespace)
    {
        var content = EmitFile(moduleNamespace, indent =>
        {
            indent.Require(RuntimeNamespaces.Contracts);
            indent.Require($"static {RuntimeNamespaces.Contracts}.TemplateExtensions");

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
                var templateClassName = EmitterHelpers.SanitizeIdentifier(template.Name);

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
        });

        var lastDot = moduleNamespace.LastIndexOf('.');
        var namespaceBesidePackageFolder = lastDot < 0 ? string.Empty : moduleNamespace[..lastDot];
        var path = RelativeFilePath(namespaceBesidePackageFolder, "ContractIdentifiers.cs");

        return GeneratedFile.Text(path, content);
    }

    private static string RelativeFilePath(string dottedNamespace, string fileName) =>
        dottedNamespace.Length == 0 ? fileName : $"{dottedNamespace.Replace('.', '/')}/{fileName}";

    /// <summary>
    /// Generates a partial file with the choice argument type nested inside the template.
    /// </summary>
    private string GenerateNestedChoiceArgumentType(
        PackageEmitContext context,
        TemplateEmitter templateEmitter,
        DamlTemplate template,
        DamlChoice choice,
        DamlDataType argDataType) =>
        EmitFile(context.RootNamespace, indent =>
        {
            RequireCommonNamespaces(indent);
            templateEmitter.WriteNestedChoiceArgumentType(indent, template, choice, argDataType);
        });
}
