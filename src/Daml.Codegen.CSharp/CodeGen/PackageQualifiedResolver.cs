// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Decorates an <see cref="ICrossPackageResolver"/> so every in-package resolution comes
/// back <c>global::</c>-qualified with the emitting package's root namespace, while
/// cross-package and stdlib resolutions pass through untouched. Emitting a type reference
/// into a body that declares nearer members or nested types of the same spelling — the
/// active contract's <c>Id</c> / <c>Data</c> / <c>Key</c> members and its nested
/// <c>Contract</c> / <c>ContractId</c> records — needs the qualification applied at
/// resolution, so that it survives the composition a rendered name goes through: an
/// <c>Optional</c> becomes <c>Name?</c> and a list becomes
/// <c>IReadOnlyList&lt;Name&gt;</c>, neither of which a rendered-name comparison matches.
/// </summary>
internal sealed class PackageQualifiedResolver(ICrossPackageResolver inner) : ICrossPackageResolver
{
    /// <inheritdoc />
    public IReadOnlySet<string> DiscoveredExternalPackageIds => inner.DiscoveredExternalPackageIds;

    /// <inheritdoc />
    public DamlPackage? LookupPackage(string packageId) => inner.LookupPackage(packageId);

    /// <inheritdoc />
    public string Resolve(DamlTypeRef typeRef, PackageEmitContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var resolved = inner.Resolve(typeRef, context);
        return context.IsLocalRef(typeRef) ? $"global::{context.RootNamespace}.{resolved}" : resolved;
    }
}
