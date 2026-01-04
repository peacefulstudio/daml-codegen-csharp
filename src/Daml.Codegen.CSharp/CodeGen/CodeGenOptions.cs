namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Configuration options for the C# code generator.
/// </summary>
public sealed class CodeGenOptions
{
    /// <summary>
    /// Gets or sets the output directory for generated files.
    /// </summary>
    public required string OutputDirectory { get; init; }

    /// <summary>
    /// Gets or sets the root namespace for generated code.
    /// If null, the namespace is derived from the package name.
    /// </summary>
    public string? RootNamespace { get; init; }

    /// <summary>
    /// Gets or sets a regex pattern to filter which templates to generate.
    /// Default is ".*" (all templates).
    /// </summary>
    public string? RootFilter { get; init; }

    /// <summary>
    /// Gets or sets whether to generate JSON serialization support.
    /// </summary>
    public bool GenerateJsonSupport { get; init; } = true;

    /// <summary>
    /// Gets or sets whether to enable nullable reference types.
    /// </summary>
    public bool EnableNullableReferenceTypes { get; init; } = true;

    /// <summary>
    /// Gets or sets the verbosity level for logging.
    /// </summary>
    public int Verbosity { get; init; } = 1;

    /// <summary>
    /// Gets or sets whether to generate XML documentation comments.
    /// </summary>
    public bool GenerateXmlDocs { get; init; } = true;

    /// <summary>
    /// Gets or sets whether to use file-scoped namespaces.
    /// </summary>
    public bool UseFileScopedNamespaces { get; init; } = true;

    /// <summary>
    /// Gets or sets whether to generate record types instead of classes.
    /// </summary>
    public bool UseRecordTypes { get; init; } = true;

    /// <summary>
    /// Gets or sets whether to generate primary constructors.
    /// </summary>
    public bool UsePrimaryConstructors { get; init; } = true;

    /// <summary>
    /// Gets or sets whether to generate a .csproj project file.
    /// </summary>
    public bool GenerateProjectFile { get; init; }

    /// <summary>
    /// Gets or sets whether to include dependency packages in code generation.
    /// </summary>
    public bool IncludeDependencies { get; init; }

    /// <summary>
    /// Gets or sets the target framework for the generated project (e.g., "net10.0").
    /// </summary>
    public string TargetFramework { get; init; } = "net10.0";

    /// <summary>
    /// Gets or sets the version of the Daml.Codegen.CSharp.Runtime package to reference.
    /// </summary>
    public string? RuntimePackageVersion { get; init; }
}

/// <summary>
/// Represents a generated source file.
/// </summary>
/// <param name="RelativePath">The path relative to the output directory.</param>
/// <param name="Content">The file content.</param>
public sealed record GeneratedFile(string RelativePath, string Content);
