// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;

namespace Daml.Codegen.CSharp.Model;

/// <summary>
/// Parses a Daml package version string into a <see cref="Version"/>. The
/// DAR-intrinsic version is a 3-part <c>Major.Minor.Patch</c> shape per
/// <a href="../../docs/adr/0002-splice-nuget-versioning.md">ADR 0002</a>;
/// the optional 4th segment used by Canton.Splice NuGet packaging is added
/// downstream at pack time. Used by both the proto-direct
/// <c>IntermediateDarReader</c> and the parser-direct <c>DalfReader</c>
/// so the two paths report identical versions for the same DAR.
/// </summary>
public static class PackageVersionParser
{
    private static readonly Regex ThreePart = new(@"^(\d+)\.(\d+)\.(\d+)", RegexOptions.Compiled);

    /// <summary>
    /// Parses a Daml package version. Accepts <c>Major.Minor.Patch</c>
    /// (any trailing characters are ignored). Returns
    /// <see cref="Version">Version(0, 0, 0)</see> when the input does not
    /// match the 3-part shape or any segment overflows <see cref="int"/>.
    /// </summary>
    public static Version Parse(string? raw)
    {
        if (raw is null) return new Version(0, 0, 0);
        var match = ThreePart.Match(raw);
        if (!match.Success) return new Version(0, 0, 0);
        if (!int.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(match.Groups[2].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var minor)
            || !int.TryParse(match.Groups[3].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var patch))
        {
            return new Version(0, 0, 0);
        }
        return new Version(major, minor, patch);
    }
}
