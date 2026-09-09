// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Runtime.Tests;

internal sealed record MarkdownCodeFence(int LineNumber, string Language, string Code, string? ExclusionReason)
{
    private const string Delimiter = "```";
    private const string ExclusionMarker = "<!-- snippet: not-compiled";
    private const string ExclusionMarkerWithReason = ExclusionMarker + ":";
    private const string MarkerClose = "-->";

    internal static IReadOnlyList<MarkdownCodeFence> ReadFrom(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var fences = new List<MarkdownCodeFence>();

        for (var index = 0; index < lines.Length; index++)
        {
            var opening = lines[index].TrimStart();
            if (!opening.StartsWith(Delimiter, StringComparison.Ordinal))
            {
                continue;
            }

            var closing = index + 1;
            while (closing < lines.Length
                && !lines[closing].TrimStart().StartsWith(Delimiter, StringComparison.Ordinal))
            {
                closing++;
            }

            if (closing == lines.Length)
            {
                throw new InvalidDataException(
                    $"The fence opened at line {index + 1} is never closed.");
            }

            fences.Add(new MarkdownCodeFence(
                LineNumber: index + 1,
                Language: opening[Delimiter.Length..].Trim(),
                Code: string.Join("\n", lines[(index + 1)..closing]),
                ExclusionReason: ExclusionReasonAbove(lines, index)));

            index = closing;
        }

        return fences;
    }

    private static string? ExclusionReasonAbove(string[] lines, int fenceIndex)
    {
        if (fenceIndex == 0)
        {
            return null;
        }

        var marker = lines[fenceIndex - 1].Trim();
        if (!marker.StartsWith(ExclusionMarker, StringComparison.Ordinal))
        {
            return null;
        }

        var reason = marker.StartsWith(ExclusionMarkerWithReason, StringComparison.Ordinal)
            && marker.EndsWith(MarkerClose, StringComparison.Ordinal)
                ? marker[ExclusionMarkerWithReason.Length..^MarkerClose.Length].Trim()
                : string.Empty;

        return reason.Length > 0
            ? reason
            : throw new InvalidDataException(
                $"The exclusion marker at line {fenceIndex} states no reason. Write it as "
                + $"\"{ExclusionMarkerWithReason} <why this snippet cannot compile> {MarkerClose}\" so a reader "
                + "of the shipped README can see what was skipped and why.");
    }
}
