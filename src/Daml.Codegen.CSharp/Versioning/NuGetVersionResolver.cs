// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.Model;

namespace Daml.Codegen.CSharp.Versioning;

/// <summary>
/// Entry point for the 4-part <c>M.m.p.r</c> NuGet versioning scheme. Composes
/// a DAR-intrinsic <see cref="Version"/> (segments 1–3, from the package metadata)
/// with the emitter counter (segment 4) derived from the supplied
/// <see cref="JsonReleaseCounterStore"/>. Intended to be called by the NuGet packing
/// step once per package being packed.
/// </summary>
internal static class NuGetVersionResolver
{
    /// <summary>
    /// Computes the 4-part NuGet version for one package being packed. The
    /// <paramref name="counterStore"/> is mutated and persisted in-place per the
    /// semantics in <see cref="JsonReleaseCounterStore.ResolveRevision"/>.
    /// </summary>
    public static FourPartPackageVersion Compute(
        string packageName,
        Version intrinsicVersion,
        string contentHash,
        JsonReleaseCounterStore counterStore)
    {
        ArgumentNullException.ThrowIfNull(counterStore);

        var revision = counterStore.ResolveRevision(packageName, intrinsicVersion, contentHash);
        return FourPartPackageVersion.FromIntrinsic(intrinsicVersion, revision);
    }
}
