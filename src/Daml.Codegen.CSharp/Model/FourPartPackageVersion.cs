// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;

namespace Daml.Codegen.CSharp.Model;

/// <summary>
/// 4-part NuGet version <c>Major.Minor.Patch.Generation</c>, optionally carrying a
/// SemVer prerelease suffix (e.g. <c>0.1.6.1-preview.2</c>).
/// Segments 1–3 are the DAR-intrinsic version; segment 4 (<see cref="Generation"/>) is
/// the codegen-generation ordinal — a uniform, DAR-independent value shared by every
/// package produced by one codegen version, incremented only when the codegen version
/// changes.
/// <see cref="PrereleaseSuffix"/> is stored without the leading dash; an empty,
/// null, or whitespace value means no suffix.
/// </summary>
internal readonly record struct FourPartPackageVersion(
    int Major,
    int Minor,
    int Patch,
    int Generation,
    string? PrereleaseSuffix = null)
{
    /// <summary>
    /// Lifts a 3-part DAR-intrinsic <see cref="Version"/> (as produced by
    /// <see cref="PackageVersionParser.Parse"/>) into a 4-part version by
    /// attaching the supplied <paramref name="generation"/> as segment 4 and,
    /// when supplied, the SemVer <paramref name="prereleaseSuffix"/> (without
    /// a leading dash).
    /// </summary>
    public static FourPartPackageVersion FromIntrinsic(Version intrinsic, int generation, string? prereleaseSuffix = null)
    {
        ArgumentNullException.ThrowIfNull(intrinsic);
        ArgumentOutOfRangeException.ThrowIfNegative(generation);
        var patch = Math.Max(0, intrinsic.Build);
        return new FourPartPackageVersion(intrinsic.Major, intrinsic.Minor, patch, generation, NormalizeSuffix(prereleaseSuffix));
    }

    /// <summary>
    /// Parses a version string <c>M.m.p.g</c>, optionally followed by a SemVer
    /// prerelease suffix <c>-suffix</c>. The trailing generation segment <c>g</c> is
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

        var generation = 0;
        if (segments.Length == 4 && !TryParseSegment(segments[3], out generation))
        {
            return false;
        }

        version = new FourPartPackageVersion(major, minor, patch, generation, suffix);
        return true;
    }

    /// <summary>
    /// Returns the canonical <c>"M.m.p.g"</c> string form, where segment 4 is the
    /// generation ordinal, appending <c>"-{suffix}"</c> when a prerelease suffix is present.
    /// </summary>
    public override string ToString() =>
        string.IsNullOrWhiteSpace(PrereleaseSuffix)
            ? string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}.{Generation}")
            : string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}.{Generation}-{PrereleaseSuffix}");

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
