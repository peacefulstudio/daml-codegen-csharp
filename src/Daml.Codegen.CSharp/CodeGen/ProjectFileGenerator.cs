using System.Text;
using System.Text.RegularExpressions;
using Daml.Codegen.CSharp.DarReader;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Generates .csproj project files for Daml-generated C# code.
/// </summary>
public sealed class ProjectFileGenerator
{
    private readonly CodeGenOptions _options;

    // Matches the partial-property syntax emitted by WriteKeyProperty, e.g.
    // `public partial string Key { get; }` or `public partial Foo.Bar Key { get; }`.
    // The character class `[^;{=]` excludes `{`, `;`, `=` so it can't span across the
    // type declaration's own opening brace and into the property body.
    private static readonly Regex PartialKeyPropertyPattern =
        new(@"\bpartial\s+[^;{=]+?\s+Key\s*\{\s*get;\s*\}", RegexOptions.Compiled);

    public ProjectFileGenerator(CodeGenOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Returns true if the given emitted file's content contains the
    /// codegen-emitted partial-property <c>Key</c> declaration that requires
    /// C# 13 (see <c>WriteKeyProperty</c> / daml-codegen-csharp#65). Used by
    /// the MSBuild integration target to decide whether to bump the consumer
    /// project's <c>&lt;LangVersion&gt;</c>.
    /// </summary>
    internal static bool ContentRequiresCSharp13(string content) =>
        PartialKeyPropertyPattern.IsMatch(content);

    /// <summary>
    /// Generates a .csproj file for the given package.
    /// </summary>
    /// <param name="package">The main package being generated.</param>
    /// <param name="externalReferences">
    /// Other DAR-level packages whose types are referenced by the generated code.
    /// Each becomes a NuGet PackageReference in the generated csproj.
    /// </param>
    /// <param name="emittedFiles">
    /// The .cs files actually emitted into the same project (after RootFilter and
    /// IncludeDependencies). When provided, scanned for partial-property syntax to
    /// decide whether to pin <c>&lt;LangVersion&gt;13&lt;/LangVersion&gt;</c> — the
    /// most precise answer because it tracks what actually goes into the project
    /// (a key-bearing dependency added via <c>IncludeDependencies</c> still needs
    /// the pin; a keyed template excluded by <c>RootFilter</c> doesn't). When
    /// <c>null</c> (back-compat for older callers that don't have access to the
    /// post-filter emission set), falls back to scanning <paramref name="package"/>'s
    /// modules for any key-bearing template — less precise but still functionally
    /// safe for the simple case (no <c>IncludeDependencies</c>, no <c>RootFilter</c>).
    /// </param>
    public GeneratedFile GenerateProjectFile(
        DamlPackage package,
        IReadOnlyList<DamlPackage>? externalReferences = null,
        IReadOnlyList<GeneratedFile>? emittedFiles = null)
    {
        var packageName = SanitizePackageName(package.Name);
        var projectFileName = $"{packageName}.csproj";

        var content = GenerateProjectFileContent(
            package,
            packageName,
            externalReferences ?? [],
            emittedFiles);

        return new GeneratedFile(projectFileName, content);
    }

    private string GenerateProjectFileContent(
        DamlPackage package,
        string packageName,
        IReadOnlyList<DamlPackage> externalReferences,
        IReadOnlyList<GeneratedFile>? emittedFiles)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
        sb.AppendLine();
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine($"    <TargetFramework>{_options.TargetFramework}</TargetFramework>");
        // Pin C# 13 only when the project's actual emitted code contains the
        // partial-property syntax from WriteKeyProperty (see daml-codegen-csharp#65).
        // Anchored to the emission set (not to package.Modules) so that:
        //   - a key-bearing dependency added via `IncludeDependencies` still pins
        //     LangVersion (otherwise its emitted partial-property would fail to parse);
        //   - a keyed template excluded by `RootFilter` does NOT pin LangVersion
        //     (the syntax never makes it into this project, so raising the SDK floor
        //     for every consumer is unnecessary).
        // When pinned, build machines need .NET 9 SDK or later — documented in the
        // CHANGELOG migration note.
        if (RequiresCSharp13(package, emittedFiles))
        {
            sb.AppendLine("    <LangVersion>13</LangVersion>");
        }
        sb.AppendLine("    <ImplicitUsings>enable</ImplicitUsings>");

        if (_options.EnableNullableReferenceTypes)
        {
            sb.AppendLine("    <Nullable>enable</Nullable>");
        }

        sb.AppendLine($"    <PackageId>{packageName}</PackageId>");
        sb.AppendLine($"    <Version>{package.Version}</Version>");
        sb.AppendLine($"    <Description>C# bindings for Daml package {package.Name}</Description>");
        sb.AppendLine("    <Authors>Generated by Daml.Codegen.CSharp</Authors>");
        sb.AppendLine("  </PropertyGroup>");
        sb.AppendLine();

        // Add package references
        sb.AppendLine("  <ItemGroup>");

        // Runtime package reference
        var runtimeVersion = _options.RuntimePackageVersion ?? "*";
        sb.AppendLine($"    <PackageReference Include=\"Daml.Runtime\" Version=\"{runtimeVersion}\" />");
        // Ledger-abstractions package reference. Required by the codegen-emitted
        // `<Choice>Async` extension methods (which take `ILedgerClient`). Emitted
        // unconditionally: the package is interface-only and lockstep-versioned
        // with Daml.Runtime, so pure-projector consumers absorb it at zero
        // transitive weight.
        sb.AppendLine($"    <PackageReference Include=\"Daml.Ledger.Abstractions\" Version=\"{runtimeVersion}\" />");

        // Add cross-DAR package references for any external types referenced in generated code.
        // Stdlib packages (daml-prim/daml-stdlib) are filtered out by the generator before this
        // point — those types come from Daml.Runtime.Stdlib.
        foreach (var dep in externalReferences.OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            var depPackageName = SanitizePackageName(dep.Name);
            sb.AppendLine($"    <PackageReference Include=\"{depPackageName}\" Version=\"{dep.Version}\" />");
        }

        sb.AppendLine("  </ItemGroup>");

        sb.AppendLine();
        sb.AppendLine("</Project>");

        return sb.ToString();
    }

    private static bool RequiresCSharp13(DamlPackage package, IReadOnlyList<GeneratedFile>? emittedFiles)
    {
        // When the caller provides the post-filter emission set, scan it directly —
        // most precise: handles IncludeDependencies (key-bearing dep gets pinned)
        // and RootFilter (filtered-out keyed template doesn't force the pin).
        if (emittedFiles is not null)
        {
            return emittedFiles.Any(f =>
                f.RelativePath.EndsWith(".cs", StringComparison.Ordinal)
                && ContentRequiresCSharp13(f.Content));
        }

        // Back-compat fallback: older callers that pass only `package`. Scan the
        // package's templates for any key-bearing one — less precise (doesn't see
        // IncludeDependencies or RootFilter) but still functionally safe for the
        // simple case. Without this fallback, an old caller with a key-bearing
        // package would silently produce a `.csproj` lacking <LangVersion>13</> and
        // the build would fail on a syntax error in the emitted partial property.
        return package.Modules.Any(m => m.Templates.Any(t => t.Key is not null));
    }

    /// <summary>
    /// Sanitizes a Daml package name to be a valid .NET package/project name.
    /// Converts to PascalCase and joins with dots (e.g., "cats-markets" -> "Cats.Markets").
    /// </summary>
    private static string SanitizePackageName(string name)
    {
        // Split on hyphens and underscores, convert each part to PascalCase
        var parts = name.Split('-', '_')
            .Select(ToPascalCase)
            .Select(SanitizeIdentifier);
        return string.Join(".", parts);
    }

    private static string ToPascalCase(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return s;
        }

        // Capitalize first letter
        return char.ToUpperInvariant(s[0]) + s[1..];
    }

    private static string SanitizeIdentifier(string name)
    {
        // Replace invalid characters with underscores
        var sanitized = string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_'));

        // Ensure it doesn't start with a number
        if (sanitized.Length > 0 && char.IsDigit(sanitized[0]))
        {
            sanitized = "_" + sanitized;
        }

        return sanitized;
    }
}
