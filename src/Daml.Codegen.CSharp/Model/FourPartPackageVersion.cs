// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;

namespace Daml.Codegen.CSharp.Model;

/// <summary>
/// 4-part NuGet version <c>Major.Minor.Patch.Revision</c>, optionally carrying a
/// SemVer prerelease suffix (e.g. <c>0.1.6.1-preview.2</c>).
/// Segments 1–3 are the DAR-intrinsic version; segment 4 (<see cref="Revision"/>) is
/// the monotonic emitter counter that disambiguates content-identical re-emissions
/// of the same DAR-intrinsic version under different emitter versions.
/// <see cref="PrereleaseSuffix"/> is stored without the leading dash; an empty,
/// null, or whitespace value means no suffix.
/// </summary>
internal readonly record struct FourPartPackageVersion(
    int Major,
    int Minor,
    int Patch,
    int Revision,
    string? PrereleaseSuffix = null)
{
    /// <summary>
    /// Lifts a 3-part DAR-intrinsic <see cref="Version"/> (as produced by
    /// <see cref="PackageVersionParser.Parse"/>) into a 4-part version by
    /// attaching the supplied <paramref name="revision"/> as segment 4 and,
    /// when supplied, the SemVer <paramref name="prereleaseSuffix"/> (without
    /// a leading dash).
    /// </summary>
    public static FourPartPackageVersion FromIntrinsic(Version intrinsic, int revision, string? prereleaseSuffix = null)
    {
        ArgumentNullException.ThrowIfNull(intrinsic);
        ArgumentOutOfRangeException.ThrowIfNegative(revision);
        var patch = Math.Max(0, intrinsic.Build);
        return new FourPartPackageVersion(intrinsic.Major, intrinsic.Minor, patch, revision, NormalizeSuffix(prereleaseSuffix));
    }

    /// <summary>
    /// Parses a version string <c>M.m.p.r</c>, optionally followed by a SemVer
    /// prerelease suffix <c>-suffix</c>. The trailing <c>r</c> segment is
    /// optional and defaults to <c>0</c> when absent (so <c>"0.1.17"</c> ≡
    /// <c>"0.1.17.0"</c>). The numeric core's segments must be non-negative
    /// <see cref="int"/> values; the suffix, when present, must be a non-empty
    /// dot-separated sequence of <c>[0-9A-Za-z-]+</c> identifiers. Anything else
    /// fails the parse.
    /// </summary>
    public static bool TryParse(string? raw, out FourPartPackageVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var dashIndex = raw.IndexOf('-', StringComparison.Ordinal);
        var core = dashIndex < 0 ? raw : raw[..dashIndex];
        string? suffix = null;
        if (dashIndex >= 0)
        {
            suffix = raw[(dashIndex + 1)..];
            if (!IsValidPrereleaseSuffix(suffix)) return false;
        }

        var segments = core.Split('.');
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

        version = new FourPartPackageVersion(major, minor, patch, revision, suffix);
        return true;
    }

    /// <summary>
    /// Returns the canonical <c>"M.m.p.r"</c> string form, appending
    /// <c>"-{suffix}"</c> when a prerelease suffix is present.
    /// </summary>
    public override string ToString() =>
        string.IsNullOrWhiteSpace(PrereleaseSuffix)
            ? string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}.{Revision}")
            : string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}.{Revision}-{PrereleaseSuffix}");

    private static string? NormalizeSuffix(string? suffix) =>
        string.IsNullOrWhiteSpace(suffix) ? null : suffix;

    /// <summary>
    /// Returns true when <paramref name="suffix"/> is a non-empty dot-separated
    /// sequence of <c>[0-9A-Za-z-]+</c> SemVer prerelease identifiers (no empty
    /// identifiers). The leading dash is not part of the suffix.
    /// </summary>
    internal static bool IsValidPrereleaseSuffix(string suffix)
    {
        if (suffix.Length == 0) return false;
        var identifiers = suffix.Split('.');
        return identifiers.All(static identifier =>
            identifier.Length > 0
            && identifier.All(static c => char.IsAsciiLetterOrDigit(c) || c == '-'));
    }

    private static bool TryParseSegment(string segment, out int value) =>
        int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
