// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// DAR-scoped resolution of a <see cref="DamlTypeRef"/> to a C# name. Owns the
/// archive lookup, the foreign-choice-argument memo, and the set of external
/// package ids it has discovered while resolving — read after emission to emit a
/// <c>&lt;PackageReference&gt;</c> per id. Lives for one
/// <see cref="CSharpCodeGenerator.Generate"/> call.
/// </summary>
internal interface ICrossPackageResolver
{
    /// <summary>
    /// Resolves <paramref name="typeRef"/> to a C# identifier or fully qualified name.
    /// A local ref declared in the emitting module's namespace returns the bare sanitized
    /// name (qualified with the parent template name when the type is a nested
    /// choice-argument type); a local ref into another module of the same package returns
    /// the <c>global::</c>-rooted name under that module's namespace; cross-package refs
    /// return a fully qualified name under the referent module's namespace and record the
    /// package id so a <c>&lt;PackageReference&gt;</c> can be emitted for it.
    /// </summary>
    string Resolve(DamlTypeRef typeRef, PackageEmitContext context);

    /// <summary>The external package ids encountered during resolution so far.</summary>
    IReadOnlySet<string> DiscoveredExternalPackageIds { get; }

    /// <summary>
    /// Returns the package with the given id from the DAR, or <c>null</c> if absent.
    /// Lets the emitter classify a type ref (local / stdlib / cross-package) without
    /// holding the archive itself.
    /// </summary>
    DamlPackage? LookupPackage(string packageId);

    /// <summary>
    /// The data-type definitions the package with the given id declares, indexed by the
    /// declaring module's name and the type's own name, so classifying a cross-package ref
    /// costs a lookup instead of a walk of every module. Empty when the package is absent
    /// from the DAR; a name declared under the same key more than once keeps every
    /// definition, so asking whether any of them is a record, a variant or an enum answers
    /// what a walk would have answered.
    /// </summary>
    ILookup<(string Module, string Name), DamlDataTypeDefinition> DataTypeDefinitions(string packageId) =>
        IndexDataTypeDefinitions(LookupPackage(packageId));

    /// <summary>Builds the <see cref="DataTypeDefinitions"/> index of <paramref name="package"/>.</summary>
    static ILookup<(string Module, string Name), DamlDataTypeDefinition> IndexDataTypeDefinitions(DamlPackage? package) =>
        (package?.Modules ?? [])
            .SelectMany(module => module.DataTypes.Select(dataType =>
                (Key: (Module: module.Name, Name: dataType.Name), dataType.Definition)))
            .ToLookup(entry => entry.Key, entry => entry.Definition);
}
