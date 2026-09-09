// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// One Daml module as the namespace guards see it: where it comes from, the C# namespace
/// it is emitted into, and the simple names of every top-level type that namespace will
/// declare for it.
/// </summary>
/// <param name="PackageName">Name of the Daml package declaring the module.</param>
/// <param name="ModuleName">The Daml module name, verbatim.</param>
/// <param name="Namespace">The C# namespace the module's types are emitted into.</param>
/// <param name="TopLevelTypeNames">Sanitised C# names of the module's top-level types.</param>
internal sealed record EmittedModule(
    string PackageName,
    string ModuleName,
    string Namespace,
    IReadOnlyCollection<string> TopLevelTypeNames);

/// <summary>
/// Fails an emit whose module-to-namespace map cannot produce compilable, complete output:
/// two or more modules sharing one namespace would overwrite each other's files and
/// collide on same-named types, and a namespace spelled like an emitted type's fully
/// qualified name is a C# error (CS0101). A namespace declaration implies every ancestor
/// namespace it is nested in, so the second check covers those too: module <c>A.B.C.D</c>
/// declares <c>A.B.C</c> along the way and collides with a type <c>C</c> of module
/// <c>A.B</c>. Both checks span every package the run emits, because a prefix on the main
/// package can make one of its modules coincide with a dependency's, and a dependency's
/// namespace can spell a main-package type's name.
/// </summary>
internal static class ModuleNamespaceGuards
{
    /// <summary>
    /// Throws a <see cref="CodegenException"/> when any two of <paramref name="modules"/> share a
    /// namespace, or when a namespace any module emits or implies as an ancestor equals the fully
    /// qualified name of a type another module emits. The first such collision is reported, naming
    /// every module involved in it, ordered by package and then by module so the same modules
    /// always produce the same message whatever order they reach the emitter in.
    /// </summary>
    public static void Check(IReadOnlyList<EmittedModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        RequireDistinctNamespaces(modules);
        RequireNoNamespaceSpelledLikeAType(modules);
    }

    private static void RequireDistinctNamespaces(IReadOnlyList<EmittedModule> modules)
    {
        var shared = modules
            .GroupBy(module => module.Namespace, StringComparer.Ordinal)
            .Where(sharing => sharing.Count() > 1)
            .OrderBy(sharing => sharing.Key, StringComparer.Ordinal)
            .FirstOrDefault();
        if (shared is null)
        {
            return;
        }

        throw new CodegenException(
            $"Daml modules {JoinWithAnd(InDiagnosticOrder(shared).Select(Describe))} map to the same C# namespace '{shared.Key}'. " +
            "Modules cannot share a namespace: their same-named types would collide and their files would overwrite each other. " +
            "Rename the modules apart, or choose a --namespace prefix that no longer makes them coincide.");
    }

    private static void RequireNoNamespaceSpelledLikeAType(IReadOnlyList<EmittedModule> modules)
    {
        var declarersByNamespace = DeclarersByNamespace(modules);
        foreach (var declaring in InDiagnosticOrder(modules))
        {
            foreach (var typeName in declaring.TopLevelTypeNames.Order(StringComparer.Ordinal))
            {
                var fullyQualifiedTypeName = $"{declaring.Namespace}.{typeName}";
                if (!declarersByNamespace.Contains(fullyQualifiedTypeName))
                {
                    continue;
                }

                var declarations = declarersByNamespace[fullyQualifiedTypeName]
                    .Select(declarer => DescribeDeclaration(declarer, fullyQualifiedTypeName));
                throw new CodegenException(
                    $"The C# namespace '{fullyQualifiedTypeName}' {JoinWithAnd(declarations)} is also the fully qualified name of type " +
                    $"{typeName} declared by Daml module {Describe(declaring)} (namespace '{declaring.Namespace}'). " +
                    "C# does not allow a namespace and a type to share a fully qualified name (CS0101). Rename the module or the type.");
            }
        }
    }

    private static ILookup<string, EmittedModule> DeclarersByNamespace(IReadOnlyList<EmittedModule> modules) =>
        InDiagnosticOrder(modules)
            .SelectMany(
                module => Identifiers.NamespaceWithAncestors(module.Namespace),
                (module, declaredNamespace) => (Namespace: declaredNamespace, Module: module))
            .ToLookup(declaration => declaration.Namespace, declaration => declaration.Module, StringComparer.Ordinal);

    private static IEnumerable<EmittedModule> InDiagnosticOrder(IEnumerable<EmittedModule> modules) =>
        modules
            .OrderBy(module => module.PackageName, StringComparer.Ordinal)
            .ThenBy(module => module.ModuleName, StringComparer.Ordinal);

    private static string JoinWithAnd(IEnumerable<string> parts)
    {
        var ordered = parts.ToList();
        return ordered.Count switch
        {
            0 => throw new ArgumentException("A collision names at least one module.", nameof(parts)),
            1 => ordered[0],
            _ => $"{string.Join(", ", ordered[..^1])} and {ordered[^1]}",
        };
    }

    private static string DescribeDeclaration(EmittedModule module, string declaredNamespace) =>
        declaredNamespace == module.Namespace
            ? $"emitted for Daml module {Describe(module)}"
            : $"implied by the namespace '{module.Namespace}' emitted for Daml module {Describe(module)}";

    private static string Describe(EmittedModule module) => $"{module.PackageName}:{module.ModuleName}";
}
