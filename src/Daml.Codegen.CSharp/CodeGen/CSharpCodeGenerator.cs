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
        // (usually not needed as they should have their own codegen output)

        return files;
    }

    /// <summary>
    /// Generates C# code for a single package.
    /// </summary>
    private IEnumerable<GeneratedFile> GeneratePackage(DamlPackage package)
    {
        var rootNamespace = options.RootNamespace ?? DeriveNamespace(package.Name);

        foreach (var module in package.Modules)
        {
            var moduleNamespace = $"{rootNamespace}.{SanitizeIdentifier(module.Name.Replace(".", "."))}";

            // Generate templates
            foreach (var template in module.Templates)
            {
                if (_rootFilter is not null && !_rootFilter.IsMatch($"{module.Name}:{template.Name}"))
                {
                    logger.Debug($"Skipping template {module.Name}:{template.Name} (filtered)");
                    continue;
                }

                var code = GenerateTemplate(package, module, template, moduleNamespace);
                var path = Path.Combine(
                    moduleNamespace.Replace(".", Path.DirectorySeparatorChar.ToString()),
                    $"{SanitizeIdentifier(template.Name)}.cs");

                yield return new GeneratedFile(path, code);
            }

            // Generate data types
            foreach (var dataType in module.DataTypes)
            {
                var code = GenerateDataType(package, module, dataType, moduleNamespace);
                var path = Path.Combine(
                    moduleNamespace.Replace(".", Path.DirectorySeparatorChar.ToString()),
                    $"{SanitizeIdentifier(dataType.Name)}.cs");

                yield return new GeneratedFile(path, code);
            }
        }
    }

    /// <summary>
    /// Generates C# code for a template.
    /// </summary>
    private string GenerateTemplate(
        DamlPackage package,
        DamlModule module,
        DamlTemplate template,
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

        if (options.UseRecordTypes && options.UsePrimaryConstructors && template.Fields.Count > 0)
        {
            // Record with primary constructor
            indent.Append($"public sealed record {className}(");
            WriteRecordParameters(indent, template.Fields);
            indent.AppendLine(") : ITemplate");
        }
        else if (options.UseRecordTypes)
        {
            indent.AppendLine($"public sealed record {className} : ITemplate");
        }
        else
        {
            indent.AppendLine($"public sealed class {className} : ITemplate");
        }

        indent.AppendLine("{");
        indent.Indent();

        // Static template metadata
        WriteTemplateMetadata(indent, package, module, template);

        // Properties (if not using primary constructor)
        if (!options.UsePrimaryConstructors || !options.UseRecordTypes)
        {
            WriteProperties(indent, template.Fields);
        }

        // ToRecord method
        WriteToRecordMethod(indent, template.Fields);

        // FromRecord method
        WriteFromRecordMethod(indent, className, template.Fields);

        // Choice argument types and methods
        foreach (var choice in template.Choices)
        {
            WriteChoiceArgumentType(indent, choice);
            WriteChoiceMethod(indent, choice);
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
        string moduleNamespace)
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
                WriteRecordType(indent, dataType, record);
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
        indent.AppendLine("public DamlRecord ToRecord()");
        indent.AppendLine("{");
        indent.Indent();

        indent.AppendLine("return DamlRecord.Create(");
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

        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine();
    }

    private void WriteFromRecordMethod(IndentWriter indent, string className, IReadOnlyList<DamlField> fields)
    {
        indent.AppendLine("/// <summary>Creates an instance from a DamlRecord.</summary>");
        indent.AppendLine($"public static {className} FromRecord(DamlRecord record)");
        indent.AppendLine("{");
        indent.Indent();

        indent.AppendLine($"return new {className}");

        if (options.UseRecordTypes && options.UsePrimaryConstructors)
        {
            indent.AppendLine("(");
            indent.Indent();

            for (int i = 0; i < fields.Count; i++)
            {
                var field = fields[i];
                var fieldName = ToPascalCase(SanitizeIdentifier(field.Name));
                var conversion = GetFromValueConversion(field.Type, $"record.GetRequiredField(\"{field.Name}\")");
                var comma = i < fields.Count - 1 ? "," : "";

                indent.AppendLine($"{fieldName}: {conversion}{comma}");
            }

            indent.Dedent();
            indent.AppendLine(");");
        }
        else
        {
            indent.AppendLine("{");
            indent.Indent();

            foreach (var field in fields)
            {
                var fieldName = ToPascalCase(SanitizeIdentifier(field.Name));
                var conversion = GetFromValueConversion(field.Type, $"record.GetRequiredField(\"{field.Name}\")");
                indent.AppendLine($"{fieldName} = {conversion},");
            }

            indent.Dedent();
            indent.AppendLine("};");
        }

        indent.Dedent();
        indent.AppendLine("}");
        indent.AppendLine();
    }

    private void WriteChoiceArgumentType(IndentWriter indent, DamlChoice choice)
    {
        var choiceName = SanitizeIdentifier(choice.Name);

        indent.AppendLine($"/// <summary>Arguments for the {choice.Name} choice.</summary>");

        if (choice.ArgumentType is DamlTypeRef)
        {
            // Complex argument type - generate nested record
            indent.AppendLine($"public sealed record {choiceName}Argument");
            indent.AppendLine("{");
            indent.Indent();
            // Would extract fields from ArgumentType
            indent.Dedent();
            indent.AppendLine("}");
        }

        indent.AppendLine();
    }

    private void WriteChoiceMethod(IndentWriter indent, DamlChoice choice)
    {
        var choiceName = SanitizeIdentifier(choice.Name);
        var returnType = MapDamlTypeToCSharp(choice.ReturnType);

        indent.AppendLine($"/// <summary>");
        indent.AppendLine($"/// Exercise the {choice.Name} choice.");
        if (choice.Consuming)
        {
            indent.AppendLine($"/// This choice is consuming and will archive the contract.");
        }
        indent.AppendLine($"/// </summary>");

        // We'll generate a method that returns a Choice descriptor
        indent.AppendLine($"public static Choice<{indent.CurrentTypeName}, {choiceName}Argument, {returnType}> Choice{choiceName} {{ get; }} = new()");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine($"Name = \"{choice.Name}\",");
        indent.AppendLine($"Consuming = {(choice.Consuming ? "true" : "false")},");
        indent.AppendLine("ArgumentEncoder = arg => DamlUnit.Instance, // TODO: Implement");
        indent.AppendLine("ResultDecoder = val => default! // TODO: Implement");
        indent.Dedent();
        indent.AppendLine("};");
        indent.AppendLine();
    }

    private void WriteContractIdClass(IndentWriter indent, string className)
    {
        indent.AppendLine($"/// <summary>Contract ID for {className}.</summary>");
        indent.AppendLine($"public new sealed record ContractId(string Value) : ContractId<{className}>(Value), IExercises<{className}>");
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
        indent.AppendLine($"public new sealed record Contract(ContractId Id, {className} Data) : Contract<{className}>(Id, Data)");
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

    private void WriteRecordType(IndentWriter indent, DamlDataType dataType, DamlRecordDefinition record)
    {
        var className = SanitizeIdentifier(dataType.Name);

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Generated from Daml record {dataType.Name}");
            indent.AppendLine("/// </summary>");
        }

        if (options.UseRecordTypes && options.UsePrimaryConstructors && record.Fields.Count > 0)
        {
            indent.Append($"public sealed record {className}(");
            WriteRecordParameters(indent, record.Fields);
            indent.AppendLine(") : IDamlValue");
        }
        else
        {
            indent.AppendLine($"public sealed record {className} : IDamlValue");
        }

        indent.AppendLine("{");
        indent.Indent();

        WriteToRecordMethod(indent, record.Fields);
        WriteFromRecordMethod(indent, className, record.Fields);

        indent.Dedent();
        indent.AppendLine("}");
    }

    private void WriteVariantType(IndentWriter indent, DamlDataType dataType, DamlVariantDefinition variant)
    {
        var className = SanitizeIdentifier(dataType.Name);

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Generated from Daml variant {dataType.Name}");
            indent.AppendLine("/// </summary>");
        }

        // Base abstract record
        indent.AppendLine($"public abstract record {className} : IDamlValue");
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
                indent.AppendLine($"public sealed record {ctorName}({argType} Value) : {className}");
            }
            else
            {
                indent.AppendLine($"/// <summary>{ctor.Name} constructor (no arguments).</summary>");
                indent.AppendLine($"public sealed record {ctorName}() : {className}");
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
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.ContractId }, Arguments: [var arg] } =>
            $"ContractId<{MapDamlTypeToCSharp(arg)}>",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional }, Arguments: [var arg] } =>
            $"{MapDamlTypeToCSharp(arg)}?",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.List }, Arguments: [var arg] } =>
            $"IReadOnlyList<{MapDamlTypeToCSharp(arg)}>",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.TextMap }, Arguments: [var arg] } =>
            $"IReadOnlyDictionary<string, {MapDamlTypeToCSharp(arg)}>",
        DamlTypeRef typeRef => SanitizeIdentifier(typeRef.Name),
        DamlTypeVar typeVar => typeVar.Name,
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
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.ContractId } } =>
            $"{fieldName}.ToDamlValue()",
        DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional } } =>
            $"{fieldName} is not null ? new DamlOptional({GetToValueConversion(((DamlTypeApp)type).Arguments[0], fieldName)}) : DamlOptional.None",
        _ => $"{fieldName}.ToRecord()"
    };

    private static string GetFromValueConversion(DamlType type, string valueName) => type switch
    {
        DamlPrimitiveType { Primitive: DamlPrimitive.Bool } => $"(({valueName}).As<DamlBool>()).Value",
        DamlPrimitiveType { Primitive: DamlPrimitive.Int64 } => $"(({valueName}).As<DamlInt64>()).Value",
        DamlPrimitiveType { Primitive: DamlPrimitive.Numeric } => $"(({valueName}).As<DamlNumeric>()).Value",
        DamlPrimitiveType { Primitive: DamlPrimitive.Text } => $"(({valueName}).As<DamlText>()).Value",
        DamlPrimitiveType { Primitive: DamlPrimitive.Date } => $"(({valueName}).As<DamlDate>()).Value",
        DamlPrimitiveType { Primitive: DamlPrimitive.Timestamp } => $"(({valueName}).As<DamlTimestamp>()).Value",
        DamlPrimitiveType { Primitive: DamlPrimitive.Party } => $"(({valueName}).As<DamlParty>()).Value",
        _ => $"default! /* TODO: Implement deserialization for {type} */"
    };

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

        return sb.ToString();
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

/// <summary>
/// Helper for writing indented code.
/// </summary>
internal sealed class IndentWriter(StringBuilder sb)
{
    private int _indentLevel;
    private const string IndentString = "    ";

    public string CurrentTypeName { get; set; } = "";

    public void Indent() => _indentLevel++;
    public void Dedent() => _indentLevel = Math.Max(0, _indentLevel - 1);

    public void Append(string text) => sb.Append(text);

    public void AppendLine(string? line = null)
    {
        if (line is not null)
        {
            for (int i = 0; i < _indentLevel; i++)
            {
                sb.Append(IndentString);
            }
            sb.AppendLine(line);
        }
        else
        {
            sb.AppendLine();
        }
    }
}
