// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Codegen.Testing.Conformance;

/// <summary>
/// Access to the conformance corpus DAR embedded in this package. Consumers
/// upload this DAR to a participant before creating contracts of the corpus
/// templates.
/// </summary>
public static class ConformanceCorpus
{
    /// <summary>
    /// Opens a stream over the embedded conformance DAR. The caller owns the
    /// returned stream and must dispose it.
    /// </summary>
    public static Stream OpenDar()
    {
        var assembly = typeof(ConformanceCorpus).Assembly;
        const string resourceName = "richtypes.dar";
        return assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded conformance DAR resource '{resourceName}' was not found.");
    }
}
