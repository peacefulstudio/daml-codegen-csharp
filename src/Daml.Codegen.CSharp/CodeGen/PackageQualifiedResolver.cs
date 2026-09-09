// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Decorates an <see cref="ICrossPackageResolver"/> so every in-package resolution comes
/// back <c>global::</c>-qualified with the referent module's namespace, while
/// cross-package and stdlib resolutions pass through untouched. Emitting a type reference
/// into a body that declares nearer members or nested types of the same spelling — the
/// active contract's <c>Id</c> / <c>Data</c> / <c>Key</c> members and its nested
/// <c>Contract</c> / <c>ContractId</c> records — needs the qualification applied at
/// resolution, so that it survives the composition a rendered name goes through: an
/// <c>Optional</c> becomes <c>Name?</c> and a list becomes
/// <c>IReadOnlyList&lt;Name&gt;</c>, neither of which a rendered-name comparison matches.
/// Unlike <see cref="PackageEmitContext.QualifyInModule"/>, an embedded dot is not the
/// discriminator here: a same-module choice-argument record resolves to
/// <c>Template.Argument</c>, dotted yet unqualified, so only a <c>global::</c> root marks a
/// resolution as already qualified.
/// </summary>
internal sealed class PackageQualifiedResolver(ICrossPackageResolver inner) : ICrossPackageResolver
{
    /// <inheritdoc />
    public IReadOnlySet<string> DiscoveredExternalPackageIds => inner.DiscoveredExternalPackageIds;

    /// <inheritdoc />
    public DamlPackage? LookupPackage(string packageId) => inner.LookupPackage(packageId);

    /// <inheritdoc />
    public ILookup<(string Module, string Name), DamlDataTypeDefinition> DataTypeDefinitions(string packageId) =>
        inner.DataTypeDefinitions(packageId);

    /// <inheritdoc />
    public string Resolve(DamlTypeRef typeRef, PackageEmitContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var resolved = inner.Resolve(typeRef, context);
        var alreadyQualified = resolved.StartsWith(Identifiers.GlobalPrefix, StringComparison.Ordinal);
        return context.IsLocalRef(typeRef) && !alreadyQualified
            ? Identifiers.GlobalQualified(context.Namespace, resolved)
            : resolved;
    }
}
