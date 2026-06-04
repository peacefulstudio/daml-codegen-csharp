// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;

namespace Daml.Codegen.CSharp.Model;

/// <summary>
/// 4-part NuGet version <c>Major.Minor.Patch.Revision</c> per
/// <a href="../../docs/adr/0002-splice-nuget-versioning.md">ADR 0002</a>.
/// Segments 1–3 are the DAR-intrinsic version; segment 4 (<see cref="Revision"/>) is
/// the monotonic emitter counter that disambiguates content-identical re-emissions
/// of the same DAR-intrinsic version under different emitter versions.
/// </summary>
public readonly record struct FourPartPackageVersion(int Major, int Minor, int Patch, int Revision)
{
    /// <summary>
    /// Lifts a 3-part DAR-intrinsic <see cref="Version"/> (as produced by
    /// <see cref="PackageVersionParser.Parse"/>) into a 4-part version by
    /// attaching the supplied <paramref name="revision"/> as segment 4.
    /// </summary>
    public static FourPartPackageVersion FromIntrinsic(Version intrinsic, int revision)
    {
        ArgumentNullException.ThrowIfNull(intrinsic);
        ArgumentOutOfRangeException.ThrowIfNegative(revision);
        var patch = Math.Max(0, intrinsic.Build);
        return new FourPartPackageVersion(intrinsic.Major, intrinsic.Minor, patch, revision);
    }

    /// <summary>
    /// Parses a 4-part version string <c>M.m.p.r</c>. The trailing <c>r</c> segment
    /// is optional and defaults to <c>0</c> when absent (so <c>"0.1.17"</c> ≡
    /// <c>"0.1.17.0"</c>). All segments must be non-negative <see cref="int"/>
    /// values; anything else fails the parse.
    /// </summary>
    public static bool TryParse(string? raw, out FourPartPackageVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var segments = raw.Split('.');
        if (segments.Length is < 3 or > 4) return false;

        if (!TryParseSegment(segments[0], out var major)
            || !TryParseSegment(segments[1], out var minor)
            || !TryParseSegment(segments[2], out var patch))
        {
            return false;
        }

        var revision = 0;
        if (segments.Length == 4 && !TryParseSegment(segments[3], out revision))
        {
            return false;
        }

        version = new FourPartPackageVersion(major, minor, patch, revision);
        return true;
    }

    /// <summary>
    /// Returns the canonical <c>"M.m.p.r"</c> string form.
    /// </summary>
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Major}.{Minor}.{Patch}.{Revision}");

    private static bool TryParseSegment(string segment, out int value) =>
        int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
