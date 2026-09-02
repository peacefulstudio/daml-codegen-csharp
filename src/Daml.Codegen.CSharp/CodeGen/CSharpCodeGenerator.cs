// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using Daml.Codegen.Intermediate.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RuntimeNamespaces = Daml.Runtime.RuntimeNamespaces;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Generates C# code from Daml packages.
/// </summary>
/// <param name="options">Emission options.</param>
/// <param name="logger">
/// Where progress and warnings go. Omit it — or pass <c>null</c> — and the generator stays silent.
/// </param>
public sealed partial class CSharpCodeGenerator(CodeGenOptions options, ILogger<CSharpCodeGenerator>? logger = null)
{
    private readonly ILogger _log = logger ?? NullLogger<CSharpCodeGenerator>.Instance;

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

        var resolver = new DarCrossPackageResolver(dar, _log);

        files.AddRange(GeneratePackage(resolver, dar.MainPackage));

        if (options.IncludeDependencies)
        {
            foreach (var dep in dar.Dependencies)
            {
                LogGeneratingDependency(_log, dep.Name);
                files.AddRange(GeneratePackage(resolver, dep));
            }
        }

        if (options.GenerateProjectFile)
        {
            var externalRefs = new List<DamlPackage>();
            foreach (var id in resolver.DiscoveredExternalPackageIds)
            {
                var pkg = dar.GetPackageById(id);
                if (pkg is null)
                {
                    LogExternalPackageMissing(_log, id[..Math.Min(16, id.Length)]);
                    continue;
                }
                if (IsStdlibPackage(pkg.Name) || IsPlaceholderPackageName(pkg.Name))
                {
                    continue;
                }
                externalRefs.Add(pkg);
            }
            var projectGenerator = new ProjectFileGenerator(options);
            files.Add(projectGenerator.GenerateProjectFile(dar.MainPackage, externalRefs));
            files.Add(projectGenerator.GenerateReadme(dar.MainPackage));
            files.Add(projectGenerator.GenerateIcon());
        }

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
        var interfaceEmitter = new InterfaceEmitter(context, mapper, resolver, choiceEmitter, options);
        var rootNamespace = context.RootNamespace;
        var submissionExtensions = new SubmissionExtensionsEmitter(context, options, _party);
        var templateEmitter = new TemplateEmitter(context, resolver, recordSerialization, choiceEmitter, submissionExtensions, options, logger);

        var allTemplateNames = package.Modules
            .SelectMany(m => m.Templates)
            .Select(t => t.Name)
            .ToHashSet();

        foreach (var module in package.Modules)
        {
            var dataTypesByName = module.DataTypes
                .Where(dt => dt.Definition is DamlRecordDefinition)
                .ToDictionary(dt => dt.Name, dt => (DamlRecordDefinition)dt.Definition!);

            foreach (var template in module.Templates)
            {
                if (_rootFilter is not null && !_rootFilter.IsMatch($"{module.Name}:{template.Name}"))
                {
                    LogSkippingTemplate(_log, module.Name, template.Name);
                    continue;
                }

                if (!dataTypesByName.TryGetValue(template.Name, out var recordDef))
                {
                    var sameNamed = module.DataTypes.FirstOrDefault(dt => dt.Name == template.Name);
                    var cause = sameNamed is null
                        ? "no data type of that name exists in the module"
                        : $"the same-named data type is a {sameNamed.Definition.GetType().Name}";

                    throw new CodegenException(
                        $"Template '{module.Name}:{template.Name}' has no same-named record definition in its module: {cause}. " +
                        "An LF template payload is always a same-named record carrying the template's fields, so " +
                        "this means the model is malformed and no payload type can be emitted.");
                }

                var code = GenerateTemplate(context, templateEmitter, package, module, template, recordDef.Fields);
                var path = RelativeFilePath(rootNamespace, $"{EmitterHelpers.SanitizeIdentifier(template.Name)}.cs");

                yield return GeneratedFile.Text(path, code);
            }

            foreach (var dataType in module.DataTypes)
            {
                if (allTemplateNames.Contains(dataType.Name))
                {
                    continue;
                }

                if (context.LocalInterfaceQualifiedNames.Contains($"{module.Name}:{dataType.Name}"))
                {
                    continue;
                }

                if (context.LocalChoiceArgToTemplate.ContainsKey($"{module.Name}:{dataType.Name}"))
                {
                    continue;
                }

                var code = GenerateDataType(context, recordEmitter, enumEmitter, variantEmitter, module, dataType);
                var path = RelativeFilePath(rootNamespace, $"{EmitterHelpers.SanitizeIdentifier(dataType.Name)}.cs");

                yield return GeneratedFile.Text(path, code);
            }

            foreach (var template in module.Templates)
            {
                if (_rootFilter is not null && !_rootFilter.IsMatch($"{module.Name}:{template.Name}"))
                {
                    continue;
                }

                foreach (var choice in template.Choices)
                {
                    if (choice.ArgumentType is DamlTypeRef typeRef &&
                        context.DataTypes.TryGetValue($"{typeRef.Module}:{typeRef.Name}", out var argDataType) &&
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

            foreach (var iface in module.Interfaces)
            {
                if (_rootFilter is not null && !_rootFilter.IsMatch($"{module.Name}:{iface.Name}"))
                {
                    LogSkippingInterface(_log, module.Name, iface.Name);
                    continue;
                }

                var code = GenerateInterface(context, interfaceEmitter, package, module, iface);
                var path = RelativeFilePath(rootNamespace, $"{context.LocalInterfaceMarkerNames[$"{module.Name}:{iface.Name}"]}.cs");

                yield return GeneratedFile.Text(path, code);
            }
        }

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

    [LoggerMessage(EventId = 1000, Level = LogLevel.Debug, Message = "Generating code for dependency: {PackageName}")]
    private static partial void LogGeneratingDependency(ILogger logger, string packageName);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "External package id {PackageIdPrefix}\u2026 is not present in the DAR \u2014 no <PackageReference> will be emitted for it. Generated code that references it will fail to compile.")]
    private static partial void LogExternalPackageMissing(ILogger logger, string packageIdPrefix);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Debug, Message = "Skipping template {ModuleName}:{TemplateName} (filtered)")]
    private static partial void LogSkippingTemplate(ILogger logger, string moduleName, string templateName);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Debug, Message = "Skipping interface {ModuleName}:{InterfaceName} (filtered)")]
    private static partial void LogSkippingInterface(ILogger logger, string moduleName, string interfaceName);
}
