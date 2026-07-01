// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;

namespace Daml.Codegen.CSharp.Versioning;

/// <summary>
/// Resolves the codegen tool's own version — the key that
/// <see cref="JsonReleaseCounterStore.ResolveGeneration"/> mints a shared
/// per-source ordinal against. Every package emitted by one run of a given
/// codegen-tool version receives the same 4th NuGet version segment; the
/// segment advances only when this value changes between runs.
/// </summary>
internal static class EmitterVersion
{
    /// <summary>
    /// The emitter's informational version, with any source-revision
    /// (<c>+&lt;git-sha&gt;</c>) metadata stripped so the value only changes
    /// on an actual version bump rather than on every commit.
    /// </summary>
    public static string Current => LazyCurrent.Value;

    private static readonly Lazy<string> LazyCurrent = new(Resolve);

    private static string Resolve()
    {
        var informational = typeof(EmitterVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
        {
            throw new InvalidOperationException(
                "The Daml.Codegen.CSharp assembly carries no AssemblyInformationalVersionAttribute, " +
                "so the codegen tool's own version cannot be derived. Restore the assembly version metadata.");
        }
        var metadataSeparator = informational.IndexOf('+', StringComparison.Ordinal);
        return metadataSeparator >= 0 ? informational[..metadataSeparator] : informational;
    }
}
