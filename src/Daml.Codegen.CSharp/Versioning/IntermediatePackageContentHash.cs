// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using Daml.Codegen.Intermediate;
using Google.Protobuf;

namespace Daml.Codegen.CSharp.Versioning;

/// <summary>
/// Computes a stable hex SHA-256 over the deterministic protobuf encoding of an
/// <see cref="IntermediatePackage"/>. This hash is audit/logging-only: it no
/// longer drives the 4th NuGet version segment, which is instead the shared
/// per-source generation ordinal resolved by
/// <see cref="JsonReleaseCounterStore.ResolveGeneration"/> from the codegen
/// tool's own version.
/// </summary>
internal static class IntermediatePackageContentHash
{
    /// <summary>
    /// Returns the lowercase hex SHA-256 of the package's deterministic proto bytes.
    /// </summary>
    public static string Compute(IntermediatePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        using var buffer = new MemoryStream();
        using (var output = new CodedOutputStream(buffer, leaveOpen: true))
        {
            output.Deterministic = true;
            package.WriteTo(output);
        }

        var hash = SHA256.HashData(buffer.ToArray());
        return Convert.ToHexStringLower(hash);
    }
}
