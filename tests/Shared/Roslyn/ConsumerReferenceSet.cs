// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Daml.Testing.Roslyn;

/// <summary>
/// The Roslyn reference set every compile guard in the repo compiles against: the whole
/// target framework, plus <c>Daml.Runtime</c> and <c>Daml.Ledger.Abstractions</c> — exactly
/// what a consumer of the published packages references, and nothing the test host happens
/// to have loaded beside it.
/// </summary>
internal static class ConsumerReferenceSet
{
    private const string TrustedPlatformAssembliesKey = "TRUSTED_PLATFORM_ASSEMBLIES";

    private static readonly Lazy<ImmutableArray<MetadataReference>> Lazy = new(Build);

    internal static ImmutableArray<MetadataReference> Assemblies => Lazy.Value;

    private static ImmutableArray<MetadataReference> Build()
    {
        var publishedPackages = new[]
        {
            typeof(Daml.Runtime.Contracts.ITemplate).Assembly.Location,
            typeof(Daml.Ledger.Abstractions.ILedgerClient).Assembly.Location,
        };

        return
        [
            .. TargetFramework().Concat(publishedPackages)
                .Distinct(StringComparer.Ordinal)
                .Select(location => (MetadataReference)MetadataReference.CreateFromFile(location)),
        ];
    }

    private static IEnumerable<string> TargetFramework()
    {
        var frameworkDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)
            ?? throw new InvalidOperationException(
                $"Cannot determine the shared framework directory from {typeof(object).Assembly.Location}: "
                + "the path has no directory component, so the target-framework filter would return nothing");

        return TrustedPlatformAssemblies()
            .Where(location => Path.GetDirectoryName(location) == frameworkDirectory);
    }

    private static IEnumerable<string> TrustedPlatformAssemblies() =>
        (AppContext.GetData(TrustedPlatformAssembliesKey) as string
            ?? throw new InvalidOperationException(
                $"{TrustedPlatformAssembliesKey} is unset, so the target framework cannot be resolved "
                + "and the compile guards would silently narrow to whatever this host has loaded"))
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
}
