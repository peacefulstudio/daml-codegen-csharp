// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using Daml.Codegen.Intermediate;
using Google.Protobuf;

namespace Daml.Codegen.CSharp.Versioning;

/// <summary>
/// Computes a stable hex SHA-256 over the deterministic protobuf encoding of an
/// <see cref="IntermediatePackage"/>. This is the content-stability signal fed into
/// <see cref="JsonReleaseCounterStore.ResolveRevision"/>: two emissions whose
/// <c>IntermediatePackage</c> serializes byte-for-byte the same will resolve to the
/// same 4th-segment revision; any difference bumps the revision.
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
