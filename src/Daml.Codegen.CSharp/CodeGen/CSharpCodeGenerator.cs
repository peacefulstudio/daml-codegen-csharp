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

    /// <summary>
    /// Generates C# code for all types in the DAR.
    /// </summary>
    public IReadOnlyList<GeneratedFile> Generate(DarArchive dar)
    {
        var files = new List<GeneratedFile>();

        // Generate code for the main package
        files.AddRange(GeneratePackage(dar.MainPackage));

        // Optionally generate code for dependencies
        if (options.IncludeDependencies)
        {
            foreach (var dep in dar.Dependencies)
            {
                logger.Debug($"Generating code for dependency: {dep.Name}");
                files.AddRange(GeneratePackage(dep));
            }
        }

        // Generate project file if requested
        if (options.GenerateProjectFile)
        {
            var projectGenerator = new ProjectFileGenerator(options);
            files.Add(projectGenerator.GenerateProjectFile(dar.MainPackage));
        }

        return files;
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

        // Collect all data types and template names from all modules
        var allDataTypesInGroup = package.Modules
            .SelectMany(m => m.DataTypes)
            .ToDictionary(dt => dt.Name, dt => dt);

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
            WriteKeyProperty(indent, template.Key, fields);
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
                WriteRecordType(indent, dataType, record, allDataTypes);
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
        indent.AppendLine("using Daml.Codegen.CSharp.Runtime.Contracts;");
        indent.AppendLine("using static Daml.Codegen.CSharp.Runtime.Contracts.TemplateExtensions;");
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
                indent.AppendLine($"/// Format: {{packageId}}:{module.Name}:{template.Name}");
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
        var (argTypeName, _, _) = GetChoiceArgumentInfo(method, dataTypes);

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
        indent.AppendLine("using Daml.Codegen.CSharp.Runtime.Commands;");
        indent.AppendLine("using Daml.Codegen.CSharp.Runtime.Contracts;");
        indent.AppendLine("using Daml.Codegen.CSharp.Runtime.Data;");

        if (options.GenerateJsonSupport)
        {
            indent.AppendLine("using Daml.Codegen.CSharp.Runtime.Serialization;");
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

    private void WriteKeyProperty(IndentWriter indent, DamlType keyType, IReadOnlyList<DamlField> fields)
    {
        var csharpKeyType = MapDamlTypeToCSharp(keyType);

        indent.AppendLine("/// <summary>Gets the contract key.</summary>");

        // Check if key type matches a single field (simple key)
        // Or if it's a tuple/record type (compound key)
        switch (keyType)
        {
            case DamlPrimitiveType:
            case DamlTypeApp { Base: DamlPrimitiveType }:
                // Simple key - try to find corresponding field
                // For now, generate a key construction from fields
                indent.AppendLine($"public {csharpKeyType} Key => GetKey();");
                indent.AppendLine();
                indent.AppendLine($"private {csharpKeyType} GetKey()");
                indent.AppendLine("{");
                indent.Indent();
                indent.AppendLine($"// TODO: Implement key extraction based on template key definition");
                indent.AppendLine($"throw new NotImplementedException(\"Contract key extraction not yet implemented\");");
                indent.Dedent();
                indent.AppendLine("}");
                break;

            case DamlTypeRef typeRef:
                // Key is a record type - generate construction from fields
                indent.AppendLine($"public {csharpKeyType} Key => new {csharpKeyType}");
                indent.AppendLine("{");
                indent.Indent();
                indent.AppendLine($"// Key fields should be mapped from template fields");
                indent.AppendLine($"// This requires key expression analysis from DALF");
                indent.Dedent();
                indent.AppendLine("};");
                break;

            default:
                // Tuple or complex key - generate a property that builds the tuple
                indent.AppendLine($"public {csharpKeyType} Key");
                indent.AppendLine("{");
                indent.Indent();
                indent.AppendLine("get");
                indent.AppendLine("{");
                indent.Indent();
                indent.AppendLine("// Complex key - requires key expression from DALF");
                indent.AppendLine("throw new NotImplementedException(\"Complex contract key not yet implemented\");");
                indent.Dedent();
                indent.AppendLine("}");
                indent.Dedent();
                indent.AppendLine("}");
                break;
        }

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
        indent.AppendLine("/// <summary>Converts this template to a DamlRecord.</summary>");

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
    private static (string TypeName, IReadOnlyList<DamlField>? Fields, bool IsExternalRef) GetChoiceArgumentInfo(
        DamlChoice choice,
        IReadOnlyDictionary<string, DamlDataType> dataTypes)
    {
        // Check if the argument type is a reference to a data type in the same module
        // These are now generated as nested types inside the template using the choice name
        if (choice.ArgumentType is DamlTypeRef typeRef && dataTypes.TryGetValue(typeRef.Name, out var dataType))
        {
            var fields = dataType.Definition is DamlRecordDefinition recordDef ? recordDef.Fields : null;
            // Use the choice name since that's the nested type name we generate
            return (SanitizeIdentifier(choice.Name), fields, false);
        }

        // Check for Unit type (no arguments)
        if (choice.ArgumentType is DamlPrimitiveType { Primitive: DamlPrimitive.Unit })
        {
            return ("DamlUnit", null, false);
        }

        // Check for external type references (like DA.Internal.Template:Archive)
        // These are typically empty argument records, so we use DamlUnit
        if (choice.ArgumentType is DamlTypeRef externalRef)
        {
            // Standard Archive choice from daml-prim uses an empty record
            if (externalRef.Name == "Archive")
            {
                return ("DamlUnit", null, true);
            }
            // Other external references - fallback to DamlUnit as safe default
            return ("DamlUnit", null, true);
        }

        // Fallback to generating a nested argument type
        return ($"{SanitizeIdentifier(choice.Name)}Arg", null, false);
    }

    private void WriteChoiceArgumentType(IndentWriter indent, DamlChoice choice, IReadOnlyDictionary<string, DamlDataType> dataTypes)
    {
        var (argTypeName, _, isExternalRef) = GetChoiceArgumentInfo(choice, dataTypes);

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
        var (argTypeName, argFields, isExternalRef) = GetChoiceArgumentInfo(choice, dataTypes);

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
        WriteResultDecoder(indent, choice.ReturnType, returnType);

        indent.Dedent();
        indent.AppendLine("};");
        indent.AppendLine();
    }

    private void WriteResultDecoder(IndentWriter indent, DamlType returnType, string csharpReturnType)
    {
        // Generate appropriate decoder based on return type
        switch (returnType)
        {
            case DamlPrimitiveType { Primitive: DamlPrimitive.Unit }:
                indent.AppendLine("ResultDecoder = _ => DamlUnit.Instance");
                break;
            case DamlPrimitiveType { Primitive: DamlPrimitive.Bool }:
                indent.AppendLine("ResultDecoder = val => val.As<DamlBool>().Value");
                break;
            case DamlPrimitiveType { Primitive: DamlPrimitive.Int64 }:
                indent.AppendLine("ResultDecoder = val => val.As<DamlInt64>().Value");
                break;
            case DamlPrimitiveType { Primitive: DamlPrimitive.Text }:
                indent.AppendLine("ResultDecoder = val => val.As<DamlText>().Value");
                break;
            case DamlPrimitiveType { Primitive: DamlPrimitive.Party }:
                indent.AppendLine("ResultDecoder = val => val.As<DamlParty>().Value");
                break;
            case DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.ContractId }, Arguments: [var arg] }:
                var contractType = MapDamlTypeToCSharp(arg);
                indent.AppendLine($"ResultDecoder = val => new ContractId<{contractType}>(val.As<DamlContractId>().Value)");
                break;
            case DamlTypeRef:
                // Reference to a data type - use FromRecord
                indent.AppendLine($"ResultDecoder = val => {csharpReturnType}.FromRecord(val.As<DamlRecord>())");
                break;
            default:
                // Fallback for complex types
                indent.AppendLine($"ResultDecoder = val => {csharpReturnType}.FromRecord(val.As<DamlRecord>())");
                break;
        }
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

    private void WriteRecordType(IndentWriter indent, DamlDataType dataType, DamlRecordDefinition record, IReadOnlyDictionary<string, DamlDataType>? dataTypes = null)
    {
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
    private static string MapDamlTypeToCSharp(DamlType type) => type switch
    {
        DamlPrimitiveType { Primitive: DamlPrimitive.Unit } => "DamlUnit",
        DamlPrimitiveType { Primitive: DamlPrimitive.Bool } => "bool",
        DamlPrimitiveType { Primitive: DamlPrimitive.Int64 } => "long",
        DamlPrimitiveType { Primitive: DamlPrimitive.Numeric } => "decimal",
        DamlPrimitiveType { Primitive: DamlPrimitive.Text } => "string",
        DamlPrimitiveType { Primitive: DamlPrimitive.Date } => "DateOnly",
        DamlPrimitiveType { Primitive: DamlPrimitive.Timestamp } => "DateTimeOffset",
        DamlPrimitiveType { Primitive: DamlPrimitive.Party } => "string",
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
        DamlTypeRef typeRef => SanitizeIdentifier(typeRef.Name),
        DamlTypeVar typeVar => $"T{ToPascalCase(SanitizeIdentifier(typeVar.Name))}",
        _ => "object"
    };

    private static string GetToValueConversion(DamlType type, string fieldName) => type switch
    {
        DamlPrimitiveType { Primitive: DamlPrimitive.Unit } => "DamlUnit.Instance",
        DamlPrimitiveType { Primitive: DamlPrimitive.Bool } => $"new DamlBool({fieldName})",
        DamlPrimitiveType { Primitive: DamlPrimitive.Int64 } => $"new DamlInt64({fieldName})",
        DamlPrimitiveType { Primitive: DamlPrimitive.Numeric } => $"new DamlNumeric({fieldName})",
        DamlPrimitiveType { Primitive: DamlPrimitive.Text } => $"new DamlText({fieldName})",
        DamlPrimitiveType { Primitive: DamlPrimitive.Date } => $"new DamlDate({fieldName})",
        DamlPrimitiveType { Primitive: DamlPrimitive.Timestamp } => $"new DamlTimestamp({fieldName})",
        DamlPrimitiveType { Primitive: DamlPrimitive.Party } => $"new DamlParty({fieldName})",
        // Numeric with scale argument
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Numeric } } =>
            $"new DamlNumeric({fieldName})",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.ContractId } } =>
            $"{fieldName}.ToDamlValue()",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional } } app =>
            $"{fieldName} is not null ? new DamlOptional({GetToValueConversion(app.Arguments[0], fieldName)}) : DamlOptional.None",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.List } } app =>
            $"new DamlList({fieldName}.Select(x => {GetToValueConversion(app.Arguments[0], "x")}))",
        _ => $"{fieldName}.ToRecord()"
    };

    private static string GetFromValueConversion(DamlType type, string valueName, IReadOnlyDictionary<string, DamlDataType>? dataTypes = null) => type switch
    {
        DamlPrimitiveType { Primitive: DamlPrimitive.Bool } => $"(({valueName}).As<DamlBool>()).Value",
        DamlPrimitiveType { Primitive: DamlPrimitive.Int64 } => $"(({valueName}).As<DamlInt64>()).Value",
        DamlPrimitiveType { Primitive: DamlPrimitive.Numeric } => $"(({valueName}).As<DamlNumeric>()).Value",
        DamlPrimitiveType { Primitive: DamlPrimitive.Text } => $"(({valueName}).As<DamlText>()).Value",
        DamlPrimitiveType { Primitive: DamlPrimitive.Date } => $"(({valueName}).As<DamlDate>()).Value",
        DamlPrimitiveType { Primitive: DamlPrimitive.Timestamp } => $"(({valueName}).As<DamlTimestamp>()).Value",
        DamlPrimitiveType { Primitive: DamlPrimitive.Party } => $"(({valueName}).As<DamlParty>()).Value",
        // Numeric with scale argument
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Numeric } } =>
            $"(({valueName}).As<DamlNumeric>()).Value",
        // Optional type
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional }, Arguments: [var arg] } =>
            $"(({valueName}).As<DamlOptional>()).HasValue ? {GetFromValueConversion(arg, $"({valueName}).As<DamlOptional>().Value!", dataTypes)} : null",
        // List type
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.List }, Arguments: [var arg] } =>
            $"(({valueName}).As<DamlList>()).Select(x => {GetFromValueConversion(arg, "x", dataTypes)}).ToList()",
        // ContractId type
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.ContractId }, Arguments: [var arg] } =>
            $"new ContractId<{MapDamlTypeToCSharp(arg)}>((({valueName}).As<DamlContractId>()).Value)",
        // Type reference - check if it's an enum
        DamlTypeRef typeRef when dataTypes?.TryGetValue(typeRef.Name, out var dt) == true && dt.Definition is DamlEnumDefinition =>
            $"{SanitizeIdentifier(typeRef.Name)}Extensions.FromRecord(({valueName}).As<DamlEnum>())",
        // Type reference (record/variant types)
        DamlTypeRef typeRef => $"{SanitizeIdentifier(typeRef.Name)}.FromRecord(({valueName}).As<DamlRecord>())",
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
